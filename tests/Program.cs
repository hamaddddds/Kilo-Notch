using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Notch;

static class Program
{
    static int checks;
    static readonly string Output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/qa"));
    [STAThread]
    static int Main()
    {
        try
        {
            var app = new App(); app.InitializeComponent();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            // Change only the in-memory fixture. Never call Store.Save or open a camera.
            Store.D.Settings.Light = false;
            Store.D.Settings.BoothFrame = 0;
            Store.D.Settings.BoothFilter = 0;
            Store.D.Todos.Clear(); Store.D.Shelf.Clear();
            Theme.Apply();
            Directory.CreateDirectory(Output);
            var now = new DateTime(2026, 10, 22, 23, 50, 0);
            var update = new UpdateManifest("1.4.0", "https://github.com/hamaddddds/Kilo-Notch/releases/download/v1.4.0/KiloSetup.exe", new string('a', 64), "New things");
            Check(UpdateCenter.Valid(update), "versioned GitHub update manifest accepted");
            Check(!UpdateCenter.Valid(update with { Url = "https://example.com/setup.exe" }), "foreign update origin refused");
            Check(!UpdateCenter.Valid(update with { Sha256 = "bad" }) && !UpdateCenter.Valid(update with { Version = "../bad" }) && !UpdateCenter.Valid(update with { Notes = null! }), "malformed update data refused");
            Check(TodoSchedule.AtClock(now, 0, 23, 55) == now.Date.AddHours(23).AddMinutes(55), "reminder today stays today");
            Check(TodoSchedule.AtClock(now, 0, 0, 5) == now.Date.AddDays(1).AddMinutes(5), "past clock rolls to tomorrow");
            Check(TodoSchedule.AtClock(now, 1, 9, 0) == now.Date.AddDays(1).AddHours(9), "tomorrow 9 AM is explicit");
            Check(TodoSchedule.AtClock(now, 0, 23, 50).Date == now.Date.AddDays(1), "equal clock is future reminder");
            Check(Power.BatteryBand(false, false, 0) == 0, "desktop has no false battery alarm");
            Check(Power.BatteryBand(true, true, 5) == 0, "charging clears low battery alarm");
            Check(Power.BatteryBand(true, false, 20) == 1 && Power.BatteryBand(true, false, 10) == 2 && Power.BatteryBand(true, false, 21) == 0, "battery thresholds 20 and 10 percent");
            var pixels = new byte[] { 30, 90, 190, 255, 180, 70, 20, 255 };
            var source = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 8); source.Freeze();
            Check(ReferenceEquals(source, PhotoFilters.Apply(source, 0)), "original retains raw frame");
            for (int f = 1; f < PhotoFilters.Names.Length; f++)
            {
                var output = PhotoFilters.Apply(source, f);
                Check(output.IsFrozen && output.PixelWidth == 2, $"filter {f} safe for worker threads");
                var data = new byte[8]; output.CopyPixels(data, 8, 0);
                Check(!pixels.SequenceEqual(data), $"filter {f} changes pixels");
                if (f == 2) Check(data[0] == data[1] && data[1] == data[2], "mono luminance");
            }
            var untouched = new byte[8]; source.CopyPixels(untouched, 8, 0);
            Check(pixels.SequenceEqual(untouched), "filters preserve originals");
            var photo = SamplePhoto();
            var sheets = new List<BitmapSource>();
            for (int frame = 0; frame < 3; frame++)
            {
                var strip = Booth.Compose(new[] { photo, PhotoFilters.Apply(photo, 1), PhotoFilters.Apply(photo, 2) }, frame, new DateTime(2026, 10, 22), "Preview");
                Check(strip.PixelWidth == 600 && strip.PixelHeight > 1200, $"theme {frame} full strip");
                Save(strip, $"strip-{Booth.Frames[frame]}.png"); sheets.Add(strip);
            }
            Check(sheets.Select(s => s.PixelHeight).Distinct().Count() > 1, "theme recomposes frame geometry");

            Store.D.Shelf.Add(Path.Combine(Output, "strip-Kilo.png"));
            Store.D.Shelf.Add(Path.Combine(Output, "strip-Paper.png"));
            Store.D.Shelf.Add(Output);
            Store.D.Todos.Add(new TodoItem { Text = "Polish Kilo's animations", Due = DateTime.Today.AddHours(14) });
            Store.D.Todos.Add(new TodoItem { Text = "A little water break", Done = true, DoneAt = DateTime.Now });
            var shelf = new ShelfPage(); Render(shelf, 400, 126, "shelf");
            var todo = new TodoPage(); Render(todo, 400, 126, "todo");
            var editor = Field<Border>(todo, "Editor"); editor.Visibility = Visibility.Visible;
            Field<TextBox>(todo, "TaskBox").Text = "Finish the next little thing";
            Render(todo, 400, 126, "todo-editor");
            var booth = new PhotoPage(); Render(booth, 460, 158, "booth");
            var music = new MusicPage(); Render(music, 400, 126, "music");
            music.SetLyricsMode(true); Pump(160); music.SetLyricsMode(false); Pump(90); music.SetLyricsMode(true); Pump(700);
            Check(Math.Abs(music.LyricsProgress - 1) < .01, "interrupted lyrics reopen settles");
            Field<TextBlock>(music, "TitleText").Text = "Paper Skies";
            Field<TextBlock>(music, "ArtistText").Text = "Kilo sessions";
            var lyrics = Field<LyricsView>(music, "Lyr");
            lyrics.SetLines(new Lyric { Synced = false, Source = "Preview", Lines = new List<Line> {
                new Line { Words = new List<Word> { new Word { Text = "A little light at the edge" } } },
                new Line { Words = new List<Word> { new Word { Text = "A little rhythm in the rain" } } },
                new Line { Words = new List<Word> { new Word { Text = "And every moment moves" } } } } });
            Render(music, 628, 158, "lyrics");
            music.SetLyricsMode(false); Pump(500);
            Check(music.LyricsProgress == 0 && Field<Border>(music, "LyricsPane").Visibility == Visibility.Collapsed, "lyrics close stops hidden view");
            var moods = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var mood in Enum.GetValues<Mood>()) moods.Children.Add(new StackPanel { Width = 90, Children = {
                new Kilo { Width = 80, Height = 50, Mood = mood }, new TextBlock { Text = mood.ToString(), Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center } } });
            Render(moods, 810, 84, "moods");
            Store.D.Settings.Light = true; Theme.Apply();
            Render(new PhotoPage(), 460, 158, "booth-light");
            Store.D.Settings.Light = false; Store.D.Settings.LastPage = 0; Theme.Apply();
            var main = new MainWindow();
            main.Collapse(force: true);
            var root = Field<Grid>(main, "Root");
            root.Measure(new Size(780, 250)); root.Arrange(new Rect(0, 0, 780, 250)); root.UpdateLayout();
            main.SetLyrics(true); Pump(750); root.UpdateLayout();
            Check(Math.Abs(Field<Grid>(main, "OpenView").Width - 680) < 1, "whole notch expands with lyrics");
            Check(Math.Abs(Field<ColumnDefinition>(main.Music, "MainCol").ActualWidth - 270) < 2, "player retains 270px while lyrics reveal on right");
            Check(Grid.GetColumn((Border)Field<Border>(main.Music, "LyricsPane").Parent) == 1, "lyrics are right of album and title");
            Check(Field<Border>(main.Music, "LyricsPane").HorizontalAlignment == HorizontalAlignment.Left, "right lyric panel reveals from the start of each line");
            var integrated = new RenderTargetBitmap(1170, 375, 144, 144, PixelFormats.Pbgra32); integrated.Render(root); Save(integrated, "notch-lyrics.png");
            main.SetLyrics(false); Pump(500);
            Check(Math.Abs(Field<Grid>(main, "OpenView").Width - 500) < 1 && Math.Abs(Field<ContentControl>(main, "PageHost").Margin.Top - 38) < .1, "whole notch restores normal layout");
            var detect = new FaceVision().Observe(BitmapSource.Create(320, 240, 96, 96, PixelFormats.Bgra32, null, new byte[320 * 240 * 4], 320 * 4));
            var limit = DateTime.UtcNow.AddSeconds(10);
            while (!detect.IsCompleted && DateTime.UtcNow < limit) Pump(100);
            Check(detect.IsCompleted, "local face detection completes");
            if (detect.GetAwaiter().GetResult() is VisionResult detected) Check(!detected.Present, "blank frame has no face");
            else Console.WriteLine("SKIP face detection: unavailable on this Windows installation");
            Console.WriteLine($"PASS: {checks} checks. Renders: {Output}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(o)!;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    static void Render(FrameworkElement view, double w, double h, string name)
    {
        var root = new Border { Width = w + 40, Height = h + 40, Padding = new Thickness(20), CornerRadius = new CornerRadius(24),
            Background = Store.D.Settings.Light ? Brushes.WhiteSmoke : new LinearGradientBrush(Color.FromRgb(9, 12, 20), Color.FromRgb(27, 40, 102), 75), Child = view };
        TextElementColor(view);
        root.Measure(new Size(w + 40, h + 40)); root.Arrange(new Rect(0, 0, w + 40, h + 40)); root.UpdateLayout(); Pump(100);
        var image = new RenderTargetBitmap((int)((w + 40) * 1.5), (int)((h + 40) * 1.5), 144, 144, PixelFormats.Pbgra32); image.Render(root);
        Save(image, name + ".png"); root.Child = null;
        Check(view.DesiredSize.Width <= w + 1 && view.DesiredSize.Height <= h + 1, name + " fits content area");
    }
    static void TextElementColor(FrameworkElement view) => view.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, Theme.Fg == Colors.White ? Brushes.White : new SolidColorBrush(Theme.Fg));
    static void Save(BitmapSource source, string name)
    {
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(source));
        using var file = File.Create(Path.Combine(Output, name)); png.Save(file);
    }
    static BitmapSource SamplePhoto()
    {
        var v = new DrawingVisual(); using (var dc = v.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Colors.Coral, Colors.CornflowerBlue, 45), null, new Rect(0, 0, 640, 480));
            dc.DrawEllipse(Brushes.Gold, null, new Point(440, 130), 70, 70);
            dc.DrawRoundedRectangle(Brushes.MidnightBlue, null, new Rect(100, 180, 440, 220), 80, 80);
            dc.DrawEllipse(Brushes.White, null, new Point(270, 275), 15, 32);
            dc.DrawEllipse(Brushes.White, null, new Point(350, 275), 15, 32);
        }
        var bmp = new RenderTargetBitmap(640, 480, 96, 96, PixelFormats.Pbgra32); bmp.Render(v); bmp.Freeze(); return bmp;
    }
}
