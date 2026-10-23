using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace Notch;

/// The webcam as a live WriteableBitmap, started explicitly by the booth or camera reactions.
sealed class Camera(int maxWidth = 1280, int minimumFrameMilliseconds = 0)
{
    MediaCapture? _cap;
    MediaFrameReader? _reader;
    sealed class CaptureFrames
    {
        public byte[] Pixels = Array.Empty<byte>();
        public int Pending;
        public long LastFrameAt;
    }
    readonly object _frameGate = new();
    CaptureFrames? _frames;
    readonly SemaphoreSlim _lifecycle = new(1);

    public WriteableBitmap? Frame { get; private set; }
    public bool On { get { lock (_frameGate) return _cap != null; } }
    public event Action? NewFrame; // the bitmap was (re)created: point the preview at it

    // Set up off the UI thread; MediaCapture is agile and frames arrive on worker threads anyway.
    public async Task Start()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (On) return;
            await Task.Run(InitializeCapture);
        }
        finally { _lifecycle.Release(); }
    }

    async Task InitializeCapture()
    {
        var cap = new MediaCapture();
        MediaFrameReader? reader = null;
        try
        {
            await cap.InitializeAsync(new MediaCaptureInitializationSettings { StreamingCaptureMode = StreamingCaptureMode.Video, MemoryPreference = MediaCaptureMemoryPreference.Cpu });
            var src = cap.FrameSources.Values.Where(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
                .OrderBy(s => s.Info.MediaStreamType == MediaStreamType.VideoPreview ? 0 : 1).FirstOrDefault()
                ?? throw new InvalidOperationException("no color camera");
            // up to 720p: sharp enough for a strip, cheap enough to copy every frame
            var fmt = src.SupportedFormats.Where(f => f.VideoFormat.Width >= 320 && f.VideoFormat.Width <= maxWidth)
                .OrderByDescending(f => f.VideoFormat.Width).ThenByDescending(f => f.FrameRate.Denominator == 0 ? 0 : (double)f.FrameRate.Numerator / f.FrameRate.Denominator)
                .FirstOrDefault();
            if (fmt != null) try { await src.SetFormatAsync(fmt); } catch (Exception) { } // keep the camera's default
            reader = await cap.CreateFrameReaderAsync(src, MediaEncodingSubtypes.Bgra8);
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            reader.FrameArrived += Arrived;
            if (await reader.StartAsync() != MediaFrameReaderStartStatus.Success) throw new InvalidOperationException("camera busy");
            lock (_frameGate)
            {
                _cap = cap;
                _reader = reader;
                _frames = new CaptureFrames();
            }
        }
        catch (Exception)
        {
            if (reader != null) { reader.FrameArrived -= Arrived; reader.Dispose(); }
            cap.Dispose();
            throw;
        }
    }

    void Arrived(MediaFrameReader r, MediaFrameArrivedEventArgs e)
    {
        lock (_frameGate)
        {
            var state = _frames;
            if (r != _reader || state == null) return;
            var now = Environment.TickCount64;
            if (now - state.LastFrameAt < minimumFrameMilliseconds || state.Pending != 0) return;
            state.LastFrameAt = now;
            state.Pending = 1; // keep one buffer until its UI upload finishes
            try
            {
                using var frame = r.TryAcquireLatestFrame();
                var sb = frame?.VideoMediaFrame?.SoftwareBitmap;
                if (sb == null) { state.Pending = 0; return; }
                using var converted = sb.BitmapPixelFormat == BitmapPixelFormat.Bgra8 ? null : SoftwareBitmap.Convert(sb, BitmapPixelFormat.Bgra8);
                var b = converted ?? sb;
                int w = b.PixelWidth, h = b.PixelHeight;
                if (state.Pixels.Length != w * h * 4) state.Pixels = new byte[w * h * 4];
                b.CopyToBuffer(state.Pixels.AsBuffer());
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    lock (_frameGate)
                    {
                        try
                        {
                            // An old callback must never upload into a restarted capture.
                            if (!ReferenceEquals(state, _frames)) return;
                            var created = false;
                            if (Frame == null || Frame.PixelWidth != w || Frame.PixelHeight != h)
                            {
                                Frame = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
                                created = true;
                            }
                            Frame.WritePixels(new Int32Rect(0, 0, w, h), state.Pixels, w * 4, 0);
                            if (created) NewFrame?.Invoke();
                        }
                        finally { state.Pending = 0; }
                    }
                }, DispatcherPriority.Render);
            }
            catch (Exception) { state.Pending = 0; }
        }
    }

    /// A still of what the preview shows right now.
    public BitmapSource? Snap()
    {
        if (Frame == null) return null;
        var still = Frame.Clone();
        still.Freeze();
        return still;
    }

    /// A small, independent still for filters/vision, without a full-resolution clone.
    public BitmapSource? Snap(int maximumSize) => Frame is { } frame ? Snapshot(frame, maximumSize) : null;

    internal static BitmapSource Snapshot(BitmapSource source, int maximumSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSize);
        var scale = Math.Min(1.0, (double)maximumSize / Math.Max(source.PixelWidth, source.PixelHeight));
        int width = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
        int height = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.LowQuality);
        using (var dc = visual.RenderOpen()) dc.DrawImage(source, new Rect(0, 0, width, height));
        var still = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        still.Render(visual);
        still.Freeze();
        return still;
    }

    public async Task Stop()
    {
        await _lifecycle.WaitAsync();
        try
        {
            MediaFrameReader? r;
            MediaCapture? c;
            lock (_frameGate)
            {
                (r, c) = (_reader, _cap);
                _reader = null;
                _cap = null;
                Frame = null;
                // Pending UI delegates retain only this tiny state, not the camera buffer.
                if (_frames != null) _frames.Pixels = Array.Empty<byte>();
                _frames = null;
            }
            if (r != null)
            {
                r.FrameArrived -= Arrived;
                try { await r.StopAsync(); } catch (Exception) { } // already gone (camera unplugged)
                r.Dispose();
            }
            c?.Dispose(); // the camera light goes off here
        }
        finally { _lifecycle.Release(); }
    }
}

/// Lays the shots out as a printed photo booth strip and saves it.
public static class Booth
{
    public static readonly string[] Frames = { "Kilo", "Film", "Paper" };
    const double W = 600;

    public static BitmapSource Compose(IReadOnlyList<BitmapSource> shots, int frame, DateTime when, string? place)
    {
        bool film = frame == 1, paper = frame == 2;
        double side = film ? 56 : 34, top = film ? 40 : 34, gap = film ? 26 : 18, pw = W - 2 * side, ph = pw * 3 / 4, footer = 172;
        var footTop = top + shots.Count * (ph + gap) - gap;
        var h = Math.Ceiling(footTop + footer);
        var v = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(v, BitmapScalingMode.HighQuality);
        using (var dc = v.RenderOpen())
        {
            var all = new Rect(0, 0, W, h);
            Color ink;
            if (film)
            {
                dc.DrawRectangle(Solid(Rgb(0x111113)), null, all);
                for (var y = 12.0; y + 22 < h; y += 36) // sprocket holes down both edges
                {
                    dc.DrawRoundedRectangle(Solid(Rgb(0xE9E2D2)), null, new Rect(18, y, 18, 22), 4, 4);
                    dc.DrawRoundedRectangle(Solid(Rgb(0xE9E2D2)), null, new Rect(W - 36, y, 18, 22), 4, 4);
                }
                ink = Rgb(0xF2A43A);
            }
            else if (paper)
            {
                dc.DrawRectangle(Solid(Rgb(0xFBF7EE)), null, all);
                ink = Rgb(0x2A2A2E);
            }
            else
            {
                dc.DrawRectangle(new LinearGradientBrush(Theme.A, Theme.B, new Point(0, 0), new Point(1, 1)), null, all);
                ink = (Theme.Luma(Theme.A) + Theme.Luma(Theme.B)) / 2 > 0.6 ? Rgb(0x111114) : Colors.White;
                // a few sparkles around the signature
                foreach (var (x, y, s, o) in new[] { (64.0, 46.0, 30.0, 0.85), (112.0, 112.0, 14.0, 0.6), (W - 74, 54.0, 24.0, 0.8), (W - 122, 124.0, 13.0, 0.55), (40.0, 140.0, 16.0, 0.5), (W - 40, 138.0, 12.0, 0.5) })
                    Text(dc, "✦", "Segoe UI Symbol", s, FontWeights.Normal, ink, o, x, footTop + y - s / 2, centered: true);
            }

            for (int i = 0; i < shots.Count; i++)
            {
                var r = new Rect(side, top + i * (ph + gap), pw, ph);
                var radius = film ? 3.0 : paper ? 6.0 : 22.0;
                if (!film) // a white (or paper-edge) border around each photo
                {
                    var edge = paper ? 1.5 : 5;
                    dc.DrawRoundedRectangle(Solid(paper ? Rgb(0xE3DACB) : Colors.White), null, new Rect(r.X - edge, r.Y - edge, r.Width + 2 * edge, r.Height + 2 * edge), radius + edge, radius + edge);
                }
                Photo(dc, shots[i], r, radius);
                if (film)
                {
                    Text(dc, $"▸ {i + 1}A", "Consolas", 12, FontWeights.Bold, ink, 0.9, r.X, r.Bottom + 5);
                    Text(dc, "KILO 400", "Consolas", 12, FontWeights.Bold, ink, 0.9, r.Right - 64, r.Bottom + 5);
                }
            }

            // the signature: Kilo winking at you, the booth's name, the date (and city)
            Color body = film ? ink : paper ? Rgb(0x16161A) : Colors.Black, eye = film ? Rgb(0x111113) : paper ? Rgb(0xFBF7EE) : Colors.White;
            Notch(dc, new Rect(W / 2 - 64, footTop + 22, 128, 40), body, eye);
            var (title, family, size, weight) = film ? ("K I L O   N O T C H", "Consolas", 26.0, FontWeights.Bold)
                : paper ? ("kilo booth ♡", "Segoe Print", 32.0, FontWeights.Normal)
                : ("kilo booth", "Segoe UI Variable Display", 34.0, FontWeights.Bold);
            Text(dc, title, family, size, weight, ink, 1, W / 2, footTop + 78, centered: true);
            var date = when.ToString("d MMM yyyy", CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(place) ? "" : " · " + place);
            Text(dc, film ? date.ToUpperInvariant() : date, paper ? "Segoe Print" : film ? "Consolas" : "Segoe UI Variable Display", 15, FontWeights.Normal, ink, 0.72, W / 2, footTop + 128, centered: true);
        }
        var bmp = new RenderTargetBitmap((int)W, (int)h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(v);
        bmp.Freeze();
        return bmp;
    }

    public static string Save(BitmapSource strip)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Kilo Booth");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"Kilo Booth {DateTime.Now:yyyy-MM-dd HH.mm.ss.fff}-{Guid.NewGuid().ToString("N")[..6]}.png");
        var png = new PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(strip));
        using var f = File.Create(path);
        png.Save(f);
        return path;
    }

    // The middle of the shot, cropped to the slot and mirrored like the preview (it's a selfie).
    static void Photo(DrawingContext dc, BitmapSource src, Rect r, double radius)
    {
        double sw = src.PixelWidth, sh = src.PixelHeight, want = r.Width / r.Height;
        var crop = sw / sh > want
            ? new Int32Rect((int)((sw - sh * want) / 2), 0, (int)(sh * want), (int)sh)
            : new Int32Rect(0, (int)((sh - sw / want) / 2), (int)sw, (int)(sw / want));
        dc.PushClip(new RectangleGeometry(r, radius, radius));
        dc.PushTransform(new ScaleTransform(-1, 1, r.X + r.Width / 2, 0));
        dc.DrawImage(new CroppedBitmap(src, crop), r);
        dc.Pop();
        dc.Pop();
    }

    // The notch logo with Kilo winking: one eye squeezed shut, the other wide open.
    static void Notch(DrawingContext dc, Rect r, Color body, Color eye)
    {
        double h = r.Height, e = h * 0.26, rad = h * 0.45;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(r.Left - e, r.Top), true, true);
            c.QuadraticBezierTo(new Point(r.Left, r.Top), new Point(r.Left, r.Top + e), true, true);
            c.LineTo(new Point(r.Left, r.Bottom - rad), true, true);
            c.ArcTo(new Point(r.Left + rad, r.Bottom), new Size(rad, rad), 0, false, SweepDirection.Counterclockwise, true, true);
            c.LineTo(new Point(r.Right - rad, r.Bottom), true, true);
            c.ArcTo(new Point(r.Right, r.Bottom - rad), new Size(rad, rad), 0, false, SweepDirection.Counterclockwise, true, true);
            c.LineTo(new Point(r.Right, r.Top + e), true, true);
            c.QuadraticBezierTo(new Point(r.Right, r.Top), new Point(r.Right + e, r.Top), true, true);
        }
        dc.DrawGeometry(Solid(body), null, g);
        double ew = h * 0.2, eh = h * 0.42, cy = r.Top + h * 0.52, cx = r.Left + r.Width / 2, gap = h * 0.34;
        var lx = cx - gap / 2 - ew;
        var wink = new StreamGeometry();
        using (var c = wink.Open())
        {
            c.BeginFigure(new Point(lx, cy - eh / 2), false, false);
            c.LineTo(new Point(lx + ew * 1.05, cy), true, true);
            c.LineTo(new Point(lx, cy + eh / 2), true, true);
        }
        dc.DrawGeometry(null, new Pen(Solid(eye), ew * 0.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, wink);
        dc.DrawRoundedRectangle(Solid(eye), null, new Rect(cx + gap / 2, cy - eh / 2, ew, eh), ew / 2, ew / 2);
    }

    static void Text(DrawingContext dc, string s, string family, double size, FontWeight weight, Color c, double opacity, double x, double y, bool centered = false)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily(family), FontStyles.Normal, weight, FontStretches.Normal), size, Solid(Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B)), 1.0);
        dc.DrawText(ft, new Point(centered ? x - ft.Width / 2 : x, y));
    }

    static SolidColorBrush Solid(Color c) => new(c);
    static Color Rgb(int v) => Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
}

/// The finished strip slides out from under the notch like a photo booth print. Copy it, open it,
/// or drag it straight into a chat.
class BoothPrint : Window
{
    static BoothPrint? _open;
    static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    readonly TranslateTransform _feed = new();
    readonly RotateTransform _swing = new();
    readonly Border _bar;
    readonly double _h;
    Point _press;

    public static void Show(BitmapSource strip, string path)
    {
        _open?.Close();
        _open = new BoothPrint(strip, path);
        _open.Show();
    }

    BoothPrint(BitmapSource strip, string path)
    {
        Title = "Kilo Booth";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = (FontFamily)Application.Current.Resources["Ui"];
        SetResourceReference(ForegroundProperty, "Fg");
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);

        var wa = SystemParameters.WorkArea;
        const double below = 196; // just under the open notch
        _h = Math.Min(620, wa.Height - below - 90);
        var w = _h * strip.PixelWidth / strip.PixelHeight;
        Width = Math.Max(w, 300) + 60;
        Height = _h + 90;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + below;

        var img = new Image { Source = strip, Width = w, Height = _h, Cursor = Cursors.Hand, Clip = new RectangleGeometry(new Rect(0, 0, w, _h), 6, 6) };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var paper = new Grid
        {
            Width = w,
            Height = _h,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransformOrigin = new Point(0.5, 0),
            RenderTransform = new TransformGroup { Children = { _swing, _feed } },
            Children =
            {
                new Border { CornerRadius = new CornerRadius(6), Background = Brushes.White, Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 6, Direction = 270, Opacity = 0.45, RenderingBias = RenderingBias.Performance } },
                img,
            },
        };
        img.MouseLeftButtonDown += (_, e) => _press = e.GetPosition(this);
        img.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || (e.GetPosition(this) - _press).Length < 6) return;
            DragDrop.DoDragDrop(img, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy);
        };
        img.MouseLeftButtonUp += (_, e) => { if ((e.GetPosition(this) - _press).Length < 6) Open(path); };

        var copy = Pill("", "Copy");
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetImage(strip); ((TextBlock)((StackPanel)copy.Content).Children[1]).Text = "Copied"; }
            catch (Exception) { } // clipboard busy in another app
        };
        var show = Pill("", "Show");
        show.Click += (_, _) => Process.Start("explorer.exe", $"/select,\"{path}\"");
        var close = new Button { Content = "", Style = (Style)Application.Current.Resources["IconBtn"], FontSize = 11, Margin = new Thickness(4, 0, 0, 0) };
        close.Click += (_, _) => Dismiss();
        _bar = new Border
        {
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 12),
            Opacity = 0,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = 0.4, RenderingBias = RenderingBias.Performance },
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { copy, show, close } },
        };
        _bar.SetResourceReference(Border.BackgroundProperty, "NotchBg");
        _bar.SetResourceReference(Border.BorderBrushProperty, "SurfaceHi");

        // the strip feeds out of a slot at the window's top edge
        Content = new Grid
        {
            Children =
            {
                new Border { ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top, Height = _h + 30, Child = paper },
                _bar,
            },
        };
        Loaded += (_, _) => Print();
        Closed += (_, _) => { if (_open == this) _open = null; };
    }

    // Out it comes in two pulls, like a real printer, sways on its top edge, then the buttons appear.
    void Print()
    {
        var feed = new DoubleAnimationUsingKeyFrames();
        feed.KeyFrames.Add(new DiscreteDoubleKeyFrame(-_h - 30, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        feed.KeyFrames.Add(new EasingDoubleKeyFrame(-_h * 0.55, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(650)), EaseOut));
        feed.KeyFrames.Add(new LinearDoubleKeyFrame(-_h * 0.52, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(850))));
        feed.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1500)), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }));
        _feed.BeginAnimation(TranslateTransform.YProperty, feed);
        var swing = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(1350) };
        foreach (var (deg, ms) in new[] { (2.2, 220), (-1.4, 520), (0.6, 800), (0.0, 1050) })
            swing.KeyFrames.Add(new EasingDoubleKeyFrame(deg, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new SineEase()));
        _swing.BeginAnimation(RotateTransform.AngleProperty, swing);
        _bar.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(1500) });
    }

    void Dismiss()
    {
        var up = new DoubleAnimation(-_h - 30, TimeSpan.FromMilliseconds(380)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        up.Completed += (_, _) => Close();
        _feed.BeginAnimation(TranslateTransform.YProperty, up);
        _bar.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
    }

    static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception) { } // no photo viewer associated
    }

    static Button Pill(string glyph, string text) => new()
    {
        Style = (Style)Application.Current.Resources["PillBtn"],
        Margin = new Thickness(0, 0, 6, 0),
        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock { Text = glyph, Style = (Style)Application.Current.Resources["Ico"] },
                new TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0) },
            },
        },
    };
}
