using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Notch;

public partial class FocusPage : UserControl
{
    static readonly IEasingFunction Pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
    static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    readonly TextBlock _playGlyph = new() { RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform() };
    bool _wasRunning, _wasBreak;

    public FocusPage()
    {
        InitializeComponent();
        PlayBtn.Content = _playGlyph;
        Time.RenderTransformOrigin = new Point(0.5, 0.5);
        Time.RenderTransform = new ScaleTransform();
        Pomodoro.Changed += Refresh;
        IsVisibleChanged += (_, _) => Refresh();
        Refresh();
    }

    void Refresh()
    {
        Time.Text = MainWindow.Clock(Pomodoro.Left);
        ModeText.Text = Pomodoro.Break ? "Short break" : "Focus time";
        _playGlyph.Text = Pomodoro.Running ? "" : "";
        BreakBtn.Style = (Style)FindResource(Pomodoro.Break ? "SoftBtn" : "PillBtn");
        FocusBtn.Style = (Style)FindResource(Pomodoro.Break ? "PillBtn" : "SoftBtn");

        // the ring sweeps continuously between ticks while running (only while you can see it)
        var now = Pomodoro.Left / Pomodoro.Total;
        if (Pomodoro.Running && IsVisible)
            FocusRing.BeginAnimation(Ring.ValueProperty, new DoubleAnimation(now, Math.Max(0, (Pomodoro.Left - TimeSpan.FromSeconds(1)) / Pomodoro.Total), TimeSpan.FromSeconds(1)));
        else
            FocusRing.BeginAnimation(Ring.ValueProperty, new DoubleAnimation(now, TimeSpan.FromMilliseconds(500)) { EasingFunction = EaseOut });

        if (Pomodoro.Running != _wasRunning) { _wasRunning = Pomodoro.Running; Bounce(_playGlyph, 0.5); }
        if (Pomodoro.Break != _wasBreak) { _wasBreak = Pomodoro.Break; Bounce(Time, 0.85); }
    }

    static void Bounce(UIElement e, double from)
    {
        var s = (ScaleTransform)e.RenderTransform;
        var a = new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(460)) { EasingFunction = Pop };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    void Toggle(object sender, RoutedEventArgs e) => Pomodoro.Toggle();
    void Reset(object sender, RoutedEventArgs e) { Pomodoro.Reset(); Bounce(Time, 0.85); }
    void BreakMode(object sender, RoutedEventArgs e) => Pomodoro.SetMode(true);
    void FocusMode(object sender, RoutedEventArgs e) => Pomodoro.SetMode(false);
}
