using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Notch;

public partial class MusicPage : UserControl
{
    static readonly IEasingFunction Pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
    static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly TextBlock _playGlyph = new() { RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform() };
    bool _seeking, _lyricsOn, _wasPlaying;
    string _lyricsFor = "", _shownSong = "";
    public static readonly DependencyProperty LyricsProgressProperty = DependencyProperty.Register(
        nameof(LyricsProgress), typeof(double), typeof(MusicPage), new PropertyMetadata(0.0,
            (d, e) => ((MusicPage)d).LyricsCol.Width = new GridLength(Math.Max(0, (double)e.NewValue * 358))));
    public double LyricsProgress { get => (double)GetValue(LyricsProgressProperty); set => SetValue(LyricsProgressProperty, value); }

    public MusicPage()
    {
        InitializeComponent();
        PlayBtn.Content = _playGlyph;
        Player.PropsChanged += OnProps;
        Player.StateChanged += OnState;
        _tick.Tick += (_, _) => UpdateTime();
        IsVisibleChanged += (_, _) => OnState();
        BarTrack.SizeChanged += (_, _) => UpdateTime();
        OnProps();
        OnState();
    }

    public void ShowUnavailable() => ArtistText.Text = "Media controls aren't available on this PC";

    void OnProps()
    {
        TitleText.Text = Player.HasSession ? Player.Title : "Nothing playing";
        ArtistText.Text = Player.HasSession ? Player.Artist : "Play something in Spotify, YouTube…";
        Art.Background = Player.Art == null ? null : new ImageBrush(Player.Art) { Stretch = Stretch.UniformToFill };

        // new song: the cover pops and the title slides up
        var song = Player.Title + "|" + Player.Artist;
        if (song != _shownSong)
        {
            _shownSong = song;
            var s = (ScaleTransform)ArtBox.RenderTransform;
            var pop = new DoubleAnimation(0.78, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = Pop };
            s.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            Art.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
            ((TranslateTransform)TitleStack.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(60), EasingFunction = EaseOut });
            TitleStack.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(60) });
        }
        if (_lyricsOn) LoadLyrics();
    }

    void OnState()
    {
        _playGlyph.Text = Player.Playing ? "" : "";
        if (Player.Playing != _wasPlaying)
        {
            _wasPlaying = Player.Playing;
            var s = (ScaleTransform)_playGlyph.RenderTransform;
            var pop = new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = Pop };
            s.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
        _tick.IsEnabled = IsVisible && Player.Playing;
        UpdateTime();
    }

    // Texts update once a second; between ticks the bar glides to where the song will be.
    void UpdateTime()
    {
        if (_seeking) return;
        var pos = Player.Position;
        var end = Player.Duration;
        Elapsed.Text = end > TimeSpan.Zero ? MainWindow.Clock(pos) : "";
        Remaining.Text = end > TimeSpan.Zero ? "-" + MainWindow.Clock(end - pos) : "";
        var now = end > TimeSpan.Zero ? pos / end : 0;
        var gliding = IsVisible && Player.Playing && end > TimeSpan.Zero;
        var next = gliding ? Math.Min(1, (pos + TimeSpan.FromSeconds(1)) / end) : now;
        var d = TimeSpan.FromSeconds(gliding ? 1 : 0.3);
        var w = BarTrack.ActualWidth;
        BarFill.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(now, next, d));
        KnobMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(now * w, next * w, d));
    }

    void BarEnter(object sender, MouseEventArgs e) => Grow(true);
    void BarLeave(object sender, MouseEventArgs e) { if (!_seeking) Grow(false); }

    void Grow(bool on)
    {
        var d = TimeSpan.FromMilliseconds(260);
        BarThick.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(on ? 1.75 : 1, d) { EasingFunction = Pop });
        Knob.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(160)));
        KnobScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(on ? 1 : 0.4, d) { EasingFunction = Pop });
        KnobScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(on ? 1 : 0.4, d) { EasingFunction = Pop });
    }

    void BarDown(object sender, MouseButtonEventArgs e)
    {
        if (Player.Duration <= TimeSpan.Zero) return;
        _seeking = true;
        Bar.CaptureMouse();
        BarMove(sender, e);
    }

    void BarMove(object sender, MouseEventArgs e)
    {
        if (!_seeking) return;
        var r = Ratio(e);
        BarFill.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        KnobMove.BeginAnimation(TranslateTransform.XProperty, null);
        BarFill.ScaleX = r;
        KnobMove.X = r * BarTrack.ActualWidth;
        Elapsed.Text = MainWindow.Clock(Player.Duration * r);
    }

    void BarUp(object sender, MouseButtonEventArgs e)
    {
        if (!_seeking) return;
        _seeking = false;
        Bar.ReleaseMouseCapture();
        Player.Seek(Player.Duration * Ratio(e));
        if (!Bar.IsMouseOver) Grow(false);
    }

    double Ratio(MouseEventArgs e) => Math.Clamp(e.GetPosition(BarTrack).X / Math.Max(1, BarTrack.ActualWidth), 0, 1);

    void Prev(object sender, RoutedEventArgs e) => Player.Prev();
    void Play(object sender, RoutedEventArgs e) => Player.Toggle();
    void Next(object sender, RoutedEventArgs e) => Player.Next();
    void ToggleLyrics(object sender, RoutedEventArgs e) => MainWindow.I.SetLyrics(!_lyricsOn);

    public void SetLyricsMode(bool on)
    {
        _lyricsOn = on;
        // Keep the lyric measure stable while revealing it. Reverse from the current
        // presentation value on rapid clicks instead of restarting at an endpoint.
        var duration = TimeSpan.FromMilliseconds(on ? 620 : 440);
        BeginAnimation(LyricsProgressProperty, new DoubleAnimation(on ? 1 : 0, duration) { EasingFunction = EaseOut });
        LyricsPane.Visibility = Visibility.Visible;
        LyricsPane.IsHitTestVisible = on;
        LyricsBtn.SetResourceReference(ForegroundProperty, on ? "AccentA" : "Fg");
        LyricsBtn.ToolTip = on ? "Close lyrics" : "Show lyrics";
        ((TranslateTransform)LyricsPane.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(on ? 0 : 22, duration) { EasingFunction = EaseOut });
        var fade = new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 420 : 180)) { EasingFunction = EaseOut };
        fade.Completed += (_, _) => { if (!_lyricsOn) LyricsPane.Visibility = Visibility.Collapsed; };
        LyricsPane.BeginAnimation(OpacityProperty, fade);
        if (on) LoadLyrics();
    }

    // Lyrics are only fetched while the lyrics view is open.
    async void LoadLyrics()
    {
        var key = Player.Title + "|" + Player.Artist;
        if (key == _lyricsFor) return;
        _lyricsFor = key;
        if (!Player.HasSession) { Lyr.SetStatus("Nothing playing"); return; }
        Lyr.SetStatus("Finding lyrics…");
        var lyric = await Lyrics.Get(Player.Title, Player.Artist, Player.Duration.TotalSeconds);
        if (key != _lyricsFor) return; // the song changed while we were looking
        if (lyric == null)
        {
            Lyr.SetStatus("No lyrics for this song yet");
            _lyricsFor = ""; // allow a retry next time (misses are cached, network errors aren't)
        }
        else Lyr.SetLines(lyric);
    }
}
