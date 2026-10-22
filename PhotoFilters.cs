using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Notch;

public static class PhotoFilters
{
    public static readonly string[] Names = { "Original", "Warm", "Mono", "Vintage", "Cool" };

    // Always start from the raw frame; switching filters never compounds the effect.
    public static BitmapSource Apply(BitmapSource source, int filter)
    {
        if (filter <= 0 || filter >= Names.Length) return source;
        var input = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = input.PixelWidth * 4;
        var pixels = new byte[stride * input.PixelHeight];
        input.CopyPixels(pixels, stride, 0);
        static byte Byte(double value) => (byte)Math.Clamp(value, 0, 255);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            double b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            double l = r * .299 + g * .587 + b * .114;
            (r, g, b) = filter switch
            {
                1 => (r * 1.07 + 6, g * 1.02 + 2, b * .91),
                2 => (l, l, l),
                3 => (l * .35 + r * .65 + 14, l * .25 + g * .75 + 5, l * .25 + b * .65 + 6),
                4 => (r * .92, g * 1.02 + 2, b * 1.08 + 5),
                _ => (r, g, b),
            };
            pixels[i] = Byte(b); pixels[i + 1] = Byte(g); pixels[i + 2] = Byte(r);
        }
        var result = BitmapSource.Create(input.PixelWidth, input.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }
}
