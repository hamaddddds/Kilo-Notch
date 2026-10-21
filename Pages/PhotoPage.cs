using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Notch;

/// Kilo's photo booth: a live mirror, a 3-2-1 with Kilo winking like he's aiming the camera, three shots,
/// and a strip in the chosen frame that prints out under the notch. The camera runs only after you
/// press Start, and stops as soon as the page is out of sight.
public class PhotoPage : UserControl
{
    static readonly IEasingFunction Pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
    static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    const int Shots = 3;

    readonly Camera _cam = new();
    readonly Image _live = new() { Stretch = Stretch.UniformToFill, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(-1, 1) };
    readonly TextBlock _count = new() { FontSize = 52, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(), Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0.6, RenderingBias = RenderingBias.Performance } };
    readonly Border _flash = new() { Background = Brushes.White, Opacity = 0, IsHitTestVisible = false };
    readonly StackPanel _hint = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8), Opacity = 0 };
    readonly Kilo _kilo = new();
    readonly TextBlock _aim = new() { Text = "", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 2, -2), Opacity = 0 };
    readonly TextBlock _status = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _sub = new() { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
    readonly TextBlock _goIcon = new(), _goText = new() { Margin = new Thickness(6, 0, 0, 0) };
    readonly Button _go, _again;
    readonly StackPanel _frames = new() { Orientation = Orientation.Horizontal };
    bool _busy, _starting, _denied;
    BitmapSource? _last;
    string? _lastPath;

    public PhotoPage()
    {
        _aim.Style = (Style)Application.Current.Resources["Ico"];
        _aim.SetResourceReference(TextBlock.ForegroundProperty, "AccentA");
        _sub.Style = (Style)Application.Current.Resources["Dim"];
        _goIcon.Style = (Style)Application.Current.Resources["Ico"];

        // left: the mirror
        var camIcon = new TextBlock { Text = "", FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center, Style = (Style)Application.Current.Resources["Ico"] };
        camIcon.SetResourceReference(TextBlock.ForegroundProperty, "FgDim");
        _hint.Children.Add(camIcon);
        _hint.Children.Add(new TextBlock { Text = "Camera's off", Style = (Style)Application.Current.Resources["Dim"], FontSize = 11, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        for (int i = 0; i < Shots; i++)
            _dots.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Brushes.White, Opacity = 0.35, Margin = new Thickness(2.5, 0, 2.5, 0) });
        RenderOptions.SetBitmapScalingMode(_live, BitmapScalingMode.LowQuality); // it's a live preview: speed over polish
        var mirror = new Border { Width = 170, Height = 124, CornerRadius = new CornerRadius(14), Child = new Grid { Children = { _hint, _live, _dots, _count, _flash } } };
        mirror.SetResourceReference(Border.BackgroundProperty, "Surface");
        mirror.Clip = new RectangleGeometry(new Rect(0, 0, 170, 124), 14, 14);

        // right: Kilo and what's happening, the frame, the buttons
        var who = new DockPanel { LastChildFill = true };
        var face = new Grid { Width = 46, Margin = new Thickness(0, 0, 10, 0), Children = { _kilo, _aim } };
        DockPanel.SetDock(face, Dock.Left);
        who.Children.Add(face);
        who.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _status, _sub } });

        for (int i = 0; i < Booth.Frames.Length; i++)
        {
            var k = i;
            var b = new Button { Content = Booth.Frames[i], FontSize = 12, Padding = new Thickness(11, 5, 11, 5), Margin = new Thickness(0, 0, 6, 0) };
            b.Click += (_, _) => { Store.D.Settings.BoothFrame = k; MarkFrame(); };
            _frames.Children.Add(b);
        }
        MarkFrame();

        _go = new Button { Style = (Style)Application.Current.Resources["AccentBtn"], Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _goIcon, _goText } } };
        _go.Click += (_, _) => Go();
        _again = new Button { Style = (Style)Application.Current.Resources["CircleBtn"], Content = "", Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed, ToolTip = "Last strip" };
        _again.Click += (_, _) => { if (_last != null && _lastPath != null) BoothPrint.Show(_last, _lastPath); };

        var right = new Grid { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), Children = { _go, _again } };
        _frames.Margin = new Thickness(0, 10, 0, 0);
        Grid.SetRow(_frames, 1);
        Grid.SetRow(buttons, 2);
        right.Children.Add(who);
        right.Children.Add(_frames);
        right.Children.Add(buttons);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(right, 1);
        root.Children.Add(mirror);
        root.Children.Add(right);
        Content = root;

        _cam.NewFrame += () =>
        {
            _live.Source = _cam.Frame;
            _hint.Visibility = Visibility.Collapsed;
            _live.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) Off(); };
        Idle();
    }

    void Idle()
    {
        Say(_cam.On ? "Ready when you are" : "Kilo's photo booth", _cam.On ? "Pick a frame, then snap" : $"{Shots} shots, printed as a strip");
        Label(_cam.On ? "" : "", _cam.On ? "Snap" : "Start camera");
        _kilo.Mood = Mood.Normal;
    }

    void Say(string status, string sub) { _status.Text = status; _sub.Text = sub; }
    void Label(string glyph, string text) { _goIcon.Text = glyph; _goText.Text = text; }

    void MarkFrame()
    {
        for (int i = 0; i < _frames.Children.Count; i++)
            ((Button)_frames.Children[i]).Style = (Style)Application.Current.Resources[i == Store.D.Settings.BoothFrame ? "SoftBtn" : "PillBtn"];
    }

    async void Go()
    {
        if (_busy || _starting) return;
        if (_denied) { Process.Start(new ProcessStartInfo("ms-settings:privacy-webcam") { UseShellExecute = true }); return; }
        if (_cam.On) { await Shoot(); return; }

        _starting = true;
        Say("Waking the camera…", "One sec");
        try
        {
            await _cam.Start();
            if (!IsVisible) { Off(); return; } // you scrolled away while it started
            _kilo.Mood = Mood.Happy;
            await Task.Delay(900);
            Idle();
        }
        catch (UnauthorizedAccessException)
        {
            _denied = true;
            Say("Camera is off for desktop apps", "Windows Settings › Privacy › Camera");
            Label("", "Open settings");
            _kilo.Mood = Mood.Sad;
        }
        catch (Exception)
        {
            Say("No camera found", "Or another app is using it");
            _kilo.Mood = Mood.Sad;
        }
        finally { _starting = false; }
    }

    async Task Shoot()
    {
        _busy = true;
        _go.IsEnabled = false;
        MainWindow.I.Hold(true);
        var shots = new List<BitmapSource>();
        foreach (Ellipse d in _dots.Children) d.Opacity = 0.35;
        _dots.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        try
        {
            for (int i = 0; i < Shots && _cam.On; i++)
            {
                Say($"Shot {i + 1} of {Shots}", i == 0 ? "Look at the camera!" : "Strike another pose");
                _kilo.Mood = Mood.Wink; // aiming
                _aim.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
                for (int n = 3; n >= 1 && _cam.On; n--)
                {
                    Count(n);
                    await Task.Delay(700);
                }
                if (_cam.Snap() is not BitmapSource shot) break;
                shots.Add(shot);
                Sound.Shutter();
                _flash.BeginAnimation(OpacityProperty, Blink());
                MainWindow.I.Flash();
                _dots.Children[i].BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
                _aim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
                _kilo.Mood = Mood.Happy;
                await Task.Delay(800);
            }
            if (shots.Count == Shots)
            {
                Say("Printing…", "Kilo's developing your strip");
                await Task.Delay(50); // let the text show before the render
                var strip = Booth.Compose(shots, Store.D.Settings.BoothFrame, DateTime.Now, Store.D.Settings.ShowCity ? Location.City : null);
                _last = strip;
                _lastPath = Booth.Save(strip);
                BoothPrint.Show(strip, _lastPath);
                _again.Visibility = Visibility.Visible;
                Say("Nice one!", "Saved to Pictures › Kilo Booth");
                Label("", "Again");
                _kilo.Mood = Mood.Happy;
            }
            else Idle();
        }
        catch (Exception) { Say("Couldn't save the strip", "Is the Pictures folder writable?"); _kilo.Mood = Mood.Sad; }
        finally
        {
            _busy = false;
            _go.IsEnabled = true;
            _dots.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromSeconds(1) });
            MainWindow.I.Hold(false);
        }
    }

    void Count(int n)
    {
        _count.Text = n.ToString();
        var s = (ScaleTransform)_count.RenderTransform;
        var pop = new DoubleAnimation(1.6, 1, TimeSpan.FromMilliseconds(450)) { EasingFunction = Pop };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(680)), EaseOut));
        _count.BeginAnimation(OpacityProperty, fade);
    }

    static DoubleAnimationUsingKeyFrames Blink()
    {
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(30))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(450)), EaseOut));
        return a;
    }

    // Out of sight: the camera (and its light) goes off.
    async void Off()
    {
        if (!_cam.On) return;
        await _cam.Stop();
        _live.Source = null;
        _hint.Visibility = Visibility.Visible;
        if (!_busy) Idle();
    }
}
