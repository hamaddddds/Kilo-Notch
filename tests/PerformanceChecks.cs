using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Notch;

public static class PerformanceChecks
{
    public static void Run(Action<bool, string> check)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
            throw new InvalidOperationException("Desktop performance checks run only in GitHub Actions.");
        Pacing(check);
        RetainedWave(check);
        HiddenMascot(check);
        BoundedImages(check);
    }

    static void Pacing(Action<bool, string> check)
    {
        foreach (var refresh in new[] { 60, 120, 144, 240 })
        {
            var pacer = new FramePacer();
            int accepted = 0;
            double motion = 0;
            for (var frame = 0; frame <= refresh * 10; frame++)
            {
                var at = TimeSpan.FromTicks((long)Math.Round((double)frame * TimeSpan.TicksPerSecond / refresh));
                if (pacer.TryStep(at, 60, out var seconds)) { accepted++; motion += seconds; }
                if (pacer.TryStep(at, 60, out _)) throw new InvalidOperationException("Duplicate rendering timestamp was accepted.");
            }
            check(accepted == 601, $"60 Hz pacing retains 601 inclusive frames over 10 seconds at {refresh} Hz");
            check(Math.Abs(motion - (10 + 1.0 / 60)) < .00001, $"motion time stays independent of {refresh} Hz monitor");
        }

        var rate = new FramePacer();
        var saved = 0;
        for (var frame = 0; frame <= 1440; frame++)
            if (rate.TryStep(TimeSpan.FromTicks((long)Math.Round((double)frame * TimeSpan.TicksPerSecond / 144)), 30, out _)) saved++;
        check(saved == 301, "energy saver retains 30 Hz pacing at 144 Hz");

        rate.Reset();
        check(rate.TryStep(TimeSpan.Zero, 60, out var first) && Math.Abs(first - 1.0 / 60) < 1e-9,
            "pacer accepts initial zero timestamp with finite first step");
        check(!rate.TryStep(TimeSpan.Zero, 60, out var duplicate) && duplicate == 0, "pacer rejects duplicate timestamp");
        check(!rate.TryStep(TimeSpan.FromTicks(166667), 30, out _) && rate.TryStep(TimeSpan.FromTicks(333333), 30, out _),
            "switch to saver waits for the 30 Hz deadline");
        check(rate.TryStep(TimeSpan.FromMilliseconds(50), 60, out var normal) && Math.Abs(normal - 1.0 / 60) < .000001,
            "return to normal rate resumes the 60 Hz deadline");
        check(rate.TryStep(TimeSpan.FromMilliseconds(20), 60, out var backwards) && Math.Abs(backwards - 1.0 / 60) < 1e-9,
            "backwards render clock resets pacing safely");
        check(rate.TryStep(TimeSpan.FromSeconds(10), 60, out var stalled) && stalled == .05 &&
            !rate.TryStep(TimeSpan.FromSeconds(10.001), 60, out _), "long stall clamps motion and does not burst catch-up frames");
        rate.Reset();
        check(rate.TryStep(TimeSpan.FromSeconds(10.001), 60, out _), "explicit reset allows the current timestamp again");
        bool invalid = false;
        try { rate.TryStep(TimeSpan.Zero, 0, out _); } catch (ArgumentOutOfRangeException) { invalid = true; }
        check(invalid, "zero target rate is rejected");

        var allocationPacer = new FramePacer();
        for (var i = 0; i < 2048; i++) allocationPacer.TryStep(TimeSpan.FromTicks(i * 166667L), 60, out _);
        allocationPacer.Reset();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) allocationPacer.TryStep(TimeSpan.FromTicks(i * 166667L), 60, out _);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        check(allocated <= 256, $"10,000 warmed frame steps allocate at most 256 bytes (observed {allocated})");
    }

    static void RetainedWave(Action<bool, string> check)
    {
        var wave = new Wave { Width = 90, Height = 30, Brush = Brushes.White };
        wave.Measure(new Size(90, 30)); wave.Arrange(new Rect(0, 0, 90, 30)); wave.UpdateLayout();
        var geometry = Field<GeometryGroup>(wave, "_geometry");
        var bars = Field<RectangleGeometry[]>(wave, "_bars").ToArray();
        var image = new RenderTargetBitmap(90, 30, 96, 96, PixelFormats.Pbgra32);
        image.Render(wave);
        var initial = bars.Select(bar => bar.Rect).ToArray();
        for (var i = 0; i < 120; i++)
        {
            wave.Phase = (i + 1) * .08;
            wave.Level = .4 + i % 7 * .07;
            wave.InvalidateVisual(); wave.UpdateLayout();
            image.Clear(); image.Render(wave);
        }
        check(ReferenceEquals(geometry, Field<GeometryGroup>(wave, "_geometry")) && geometry.Children.Count == 5 &&
            bars.Select((bar, i) => ReferenceEquals(bar, geometry.Children[i]) && ReferenceEquals(bar, Field<RectangleGeometry[]>(wave, "_bars")[i])).All(same => same),
            "120 visualizer redraws retain the group and all five bar geometries");
        check(bars.Where((bar, i) => bar.Rect != initial[i]).Any() && bars.Select((bar, i) =>
        {
            var expected = Math.Clamp(30 * wave.Level * (.3 + .7 * Math.Abs(Math.Sin(wave.Phase * (.9 + i * .23) + i * 1.9))), 10, 30);
            return Math.Abs(bar.Rect.Height - expected) < .00001 && bar.Rect.Width == 10;
        }).All(matches => matches),
            "retained visualizer geometry still follows animation values");
    }

    static void HiddenMascot(Action<bool, string> check)
    {
        var app = Application.Current;
        var oldMode = app.ShutdownMode;
        var oldMain = app.MainWindow;
        var mascot = new Kilo { Width = 80, Height = 50, Mood = Mood.Asleep };
        var window = new Window { Width = 120, Height = 70, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, ShowActivated = false, Background = Brushes.Black, Content = mascot };
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            check(!Field<bool>(mascot, "_sleepLoop"), "detached sleeping mascot has no repeating clocks");
            window.Show(); Pump();
            var z = Field<FrameworkElement>(mascot, "_z");
            var tear = Field<FrameworkElement>(mascot, "_tear");
            check(ClockCount(z) == 2 && Field<bool>(mascot, "_sleepLoop"), "visible sleeping mascot starts its drift and fade clocks");
            mascot.Visibility = Visibility.Collapsed; Pump();
            check(ClockCount(z) == 0 && !Field<bool>(mascot, "_sleepLoop") && mascot.Mood == Mood.Asleep,
                "collapsed mascot stops both sleep clocks and preserves mood");
            mascot.Visibility = Visibility.Visible; Pump();
            check(ClockCount(z) == 2 && Field<bool>(mascot, "_sleepLoop"), "sleep clocks resume when mascot reappears");
            mascot.Mood = Mood.Sad; Pump();
            check(ClockCount(z) == 0 && ClockCount(tear) == 2, "switching mood replaces sleep clocks with tear clocks");
            window.Hide(); Pump();
            check(ClockCount(tear) == 0 && !Field<bool>(mascot, "_sadLoop"), "hidden parent window stops tear clocks");
            window.Show(); Pump();
            check(ClockCount(tear) == 2 && mascot.Mood == Mood.Sad, "parent window reappearance restores tear clocks");
            window.Content = null; Pump();
            check(ClockCount(tear) == 0, "unloading mascot releases repeating clocks");
        }
        finally
        {
            window.Close();
            app.MainWindow = oldMain;
            app.ShutdownMode = oldMode;
        }
    }

    static int ClockCount(FrameworkElement element) =>
        (((TranslateTransform)element.RenderTransform).HasAnimatedProperties ? 1 : 0) +
        (DependencyPropertyHelper.GetValueSource(element, UIElement.OpacityProperty).IsAnimated ? 1 : 0);

    static void BoundedImages(Action<bool, string> check)
    {
        var camera = typeof(Player).Assembly.GetType("Notch.Camera", throwOnError: true)!;
        var snapshot = camera.GetMethod("Snapshot", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var dimensions in new[] { (960, 540, 320, 180), (540, 960, 180, 320), (40, 24, 40, 24) })
        {
            var source = Solid(dimensions.Item1, dimensions.Item2);
            var result = (BitmapSource)snapshot.Invoke(null, new object[] { source, 320 })!;
            check(result.IsFrozen && result.PixelWidth == dimensions.Item3 && result.PixelHeight == dimensions.Item4,
                $"camera snapshot bounds {dimensions.Item1}x{dimensions.Item2} without upscaling");
            var before = Pixels(result);
            var black = new byte[source.PixelWidth * source.PixelHeight * 4];
            source.WritePixels(new Int32Rect(0, 0, source.PixelWidth, source.PixelHeight), black, source.PixelWidth * 4, 0);
            check(before.SequenceEqual(Pixels(result)) && before.Any(b => b != 0), "frozen camera snapshot owns pixels independently of live writable frame");
        }

        var decode = typeof(Player).GetMethod("DecodeArt", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var dimensions in new[] { (800, 400, 256, 128), (400, 800, 128, 256), (40, 24, 40, 24) })
        {
            BitmapSource result;
            using (var input = new MemoryStream())
            {
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(Solid(dimensions.Item1, dimensions.Item2)));
                png.Save(input); input.Position = 0;
                result = (BitmapSource)decode.Invoke(null, new object[] { input })!;
            }
            check(result.IsFrozen && result.PixelWidth == dimensions.Item3 && result.PixelHeight == dimensions.Item4,
                $"album decoder bounds {dimensions.Item1}x{dimensions.Item2} on the longest edge without upscaling");
            check(Pixels(result).Any(b => b != 0), "album pixels remain readable after thumbnail stream is disposed");
        }
    }

    static WriteableBitmap Solid(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 30; pixels[i + 1] = 90; pixels[i + 2] = 190; pixels[i + 3] = 255; }
        var result = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        result.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        return result;
    }

    static byte[] Pixels(BitmapSource source)
    {
        var stride = (source.PixelWidth * source.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
