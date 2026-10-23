using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Notch;

static class LyricMotionChecks
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(Action<bool, string> check)
    {
        var view = new LyricsView { Width = 319, Height = 156 };
        var app = Application.Current;
        var oldMode = app.ShutdownMode;
        var oldMain = app.MainWindow;
        var position = typeof(Player).GetField("_pos", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousPosition = position.GetValue(null);
        var update = (Action)typeof(LyricsView).GetMethod("Update", Private)!.CreateDelegate(typeof(Action), view);
        var host = new Window { Width = 359, Height = 196, WindowStyle = WindowStyle.None, ShowActivated = false,
            ShowInTaskbar = false, Background = Brushes.Black, Content = view };
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            host.Show(); Pump(40);
            view.SetLines(new Lyric { Synced = true, Source = "Synthetic timing fixture", Lines = Enumerable.Range(0, 1000)
                .Select(i => new Line { Start = i * 2, Words = new() { new Word { Start = i * 2, End = i * 2 + 1, Text = "A short generated timing line " + i } } }).ToList() });
            var rows = (IList)typeof(LyricsView).GetField("_rows", Private)!.GetValue(view)!;
            foreach (var index in new[] { 0, 1, 500, 999, 37, 0 })
            {
                position.SetValue(null, TimeSpan.FromSeconds(index * 2)); update(); host.UpdateLayout();
                check((int)typeof(LyricsView).GetField("_active", Private)!.GetValue(view)! == index, $"lyrics seek and rewind selects line {index}");
                int animated = 0;
                foreach (var row in rows)
                {
                    var box = (Border)row!.GetType().GetField("Box")!.GetValue(row)!;
                    var transform = (TransformGroup)box.RenderTransform;
                    if (transform.HasAnimatedProperties || transform.Children.Any(t => t.HasAnimatedProperties) || DependencyPropertyHelper.GetValueSource(box, UIElement.OpacityProperty).IsAnimated) animated++;
                }
                check(animated <= 14, $"1000-line song animates at most two seven-line neighbourhoods ({animated})");
                Pump(740);
                check(rows.Cast<object>().All(row => !(bool)row.GetType().GetField("Animating")!.GetValue(row)!), "settled lyric transitions release their clocks");
                var active = rows[index]!;
                var activeBox = (Border)active.GetType().GetField("Box")!.GetValue(active)!;
                var point = activeBox.TranslatePoint(new Point(0, 0), view);
                check(point.Y >= -1 && point.Y < view.ActualHeight && activeBox.Opacity == 1, $"settled lyric {index} remains readable in viewport");
            }
            view.Visibility = Visibility.Collapsed; Pump(40);
            check(!(bool)typeof(LyricsView).GetField("_hooked", Private)!.GetValue(view)!, "hidden lyric view has no frame callback");
            view.Visibility = Visibility.Visible; Pump(740);
            check((int)typeof(LyricsView).GetField("_active", Private)!.GetValue(view)! == 0, "lyrics reopen at the current line");
            view.SetStatus("Fixture cleared");
            check(rows.Count == 0, "changing songs releases lyric rows and effects");
        }
        finally
        {
            position.SetValue(null, previousPosition);
            host.Close(); app.MainWindow = oldMain; app.ShutdownMode = oldMode;
        }
    }
    static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
