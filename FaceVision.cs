using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;

namespace Notch;

public readonly record struct VisionResult(bool Present, double X, double Y);

/// Local presence/position only; no recognition, recording, or network calls.
public sealed class FaceVision
{
    FaceDetector? _detector;
    DateTime _next;
    VisionResult? _last;
    int _misses;
    public bool Ready => DateTime.UtcNow >= _next;
    public void Reset() { _last = null; _misses = 0; _next = DateTime.MinValue; }
    public async Task<VisionResult?> Observe(BitmapSource frame)
    {
        if (DateTime.UtcNow < _next) return _last;
        _next = DateTime.UtcNow.AddMilliseconds(500);
        if (!FaceDetector.IsSupported || !FaceDetector.IsBitmapPixelFormatSupported(BitmapPixelFormat.Gray8)) return null;
        _detector ??= await FaceDetector.CreateAsync();
        var gray = new FormatConvertedBitmap(frame, PixelFormats.Gray8, null, 0);
        var pixels = new byte[gray.PixelWidth * gray.PixelHeight];
        gray.CopyPixels(pixels, gray.PixelWidth, 0);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Gray8, gray.PixelWidth, gray.PixelHeight);
        var faces = await _detector.DetectFacesAsync(bitmap);
        var largest = faces.OrderByDescending(f => (long)f.FaceBox.Width * f.FaceBox.Height).FirstOrDefault();
        if (largest == null)
        {
            // A brief missed frame or head turn should not trigger another greeting.
            if (++_misses < 3 && _last is { Present: true }) return _last;
            _last = new VisionResult(false, .5, .5);
        }
        else
        {
            _misses = 0;
            double x = (largest.FaceBox.X + largest.FaceBox.Width / 2.0) / gray.PixelWidth;
            double y = (largest.FaceBox.Y + largest.FaceBox.Height / 2.0) / gray.PixelHeight;
            _last = _last is { Present: true } previous
                ? new VisionResult(true, previous.X + (x - previous.X) * .45, previous.Y + (y - previous.Y) * .45)
                : new VisionResult(true, x, y);
        }
        return _last;
    }
}
