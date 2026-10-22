using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Notch;

public partial class MainWindow : Window
{
    public static MainWindow I { get; private set; } = null!;

    enum View { Resting, Closed, Peek, Open }
    static readonly Size RestingSize = new(150, 38), ClosedSize = new(236, 38), PeekSize = new(330, 58),
        OpenSize = new(500, 188), LyricsSize = new(680, 188), PhotoSize = new(560, 220);
    // One glow brush for the notch's whole life; its colors animate instead of being swapped.
    readonly LinearGradientBrush _glow = new() { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), GradientStops = { new(Colors.Black, 0), new(Colors.Black, 1) } };
    // the song's colors (seek bar, visualizers, dance notes), gliding the same way
    readonly LinearGradientBrush _vibe = new() { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), GradientStops = { new(Colors.White, 0), new(Colors.White, 1) } };
    const double Ear = 10; // width of the flared shoulders
    const int ShelfIndex = 3;
    static readonly (string, string)[] Titles = { ("", ""), ("Pomodoro", ""), ("Water ", "Reminder"), ("Shelf", ""), ("Todo", "List"), ("Photo ", "Booth") };
    static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    static readonly IEasingFunction Pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 };

    public MusicPage Music { get; } = new();
    readonly ShelfPage _shelf = new();
    readonly TodoPage _todo = new();
    readonly FrameworkElement[] _pages;

    // notch body size, driven by springs every rendered frame
    readonly Spring _w = new(RestingSize.Width), _h = new(RestingSize.Height);
    readonly Spring _gx = new() { Epsilon = 0.01 }, _gy = new() { Epsilon = 0.01 };
    // files hovering over the notch: how lit it is, the cursor x, and the drop wave's clock (-1 = none)
    readonly Spring _hov = new() { Epsilon = 0.002, Stiffness = 50, Damping = 14 };
    double _cursorX, _dropX, _dropT = -1;
    // dancing: a hop on each beat, a sway that flips side every beat
    readonly Spring _bob = new() { Stiffness = 320, Damping = 13 }, _tilt = new() { Stiffness = 120, Damping = 13 };
    double _loud, _sinceBeat, _sway = 1;
    int _beats;
    Size _base = RestingSize;
    bool _looping;
    TimeSpan _lastFrame;

    View _view = View.Resting;
    FrameworkElement? _shown;
    bool _lyrics, _typing, _dragInside, _dancing, _held, _wasPlaying;
    int _page, _sleep, _water; // _sleep: 0 awake, 1 drowsy, 2 asleep
    Mood _react;
    IntPtr _hwnd;
    DateTime _today = DateTime.Today, _lastWheel, _reactUntil, _lastOpen = DateTime.Now, _nextFidget = DateTime.Now.AddSeconds(40);
    double _phase, _level;
    readonly Random _rnd = new();
    readonly TypingActivity _keys = new();
    VisionResult? _visionFace;
    int _batteryNotice;
    bool _saving, _wasTyping;
    bool LowEnergy => Store.D.Settings.EnergySaver && (Power.Saving || Power.HasBattery && !Power.Plugged && Power.Percent <= 20);
    readonly DispatcherTimer _beat = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _gazePoll = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly DispatcherTimer _enter = new() { Interval = TimeSpan.FromMilliseconds(60) };  // hover intent: a cursor passing by doesn't open it
    readonly DispatcherTimer _leave = new() { Interval = TimeSpan.FromMilliseconds(300) };
    readonly DispatcherTimer _dragLeave = new() { Interval = TimeSpan.FromMilliseconds(80) };
    readonly DispatcherTimer _peekEnd = new() { Interval = TimeSpan.FromSeconds(4) };
    readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly DispatcherTimer _moodEnd = new();
    readonly DispatcherTimer _trim = new() { Interval = TimeSpan.FromSeconds(45) }; // while collapsed, give memory back this often

    public MainWindow()
    {
        I = this;
        InitializeComponent();
        Closed += async (_, _) => { _keys.Dispose(); await CompanionVision.SetEnabled(false); };
        Face.ToolTip = "Kilo · double-click for a little hello";
        CompanionVision.Changed += () => { VisionLight.Visibility = CompanionVision.Enabled ? Visibility.Visible : Visibility.Collapsed; if (_view is View.Resting or View.Closed) Collapse(); };
        Face.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { React(Mood.Wink, 2); e.Handled = true; } };
        _pages = new FrameworkElement[] { Music, new FocusPage(), new WaterPage(), _shelf, _todo, new PhotoPage() };
        Music.Fill.Background = _vibe;
        Music.Viz.Brush = _vibe;
        ClosedWave.Brush = _vibe;
        NoteL.Foreground = NoteR.Foreground = _vibe;
        for (int k = 0; k < _pages.Length; k++) Dots.Children.Add(Dot(k));
        Place();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.InvokeAsync(Place);
        Root.SizeChanged += (_, _) => Reshape();
        Deactivated += (_, _) => EndTyping();

        _beat.Tick += (_, _) => Beat();
        _gazePoll.Tick += (_, _) => PollGaze();
        _enter.Tick += (_, _) => { _enter.Stop(); if (CursorInNotch()) Open(); };
        _leave.Tick += (_, _) => { _leave.Stop(); if (!CursorInNotch()) Collapse(); };
        GlowShape.Fill = _glow;
        Location.Changed += () => { if (_page == 1) SetTitle(); };
        _dragLeave.Tick += (_, _) => { _dragLeave.Stop(); DragGone(); };
        _peekEnd.Tick += (_, _) => { _peekEnd.Stop(); if (!CursorInNotch()) Collapse(); };
        _blink.Tick += (_, _) => Blink();
        _moodEnd.Tick += (_, _) => { _moodEnd.Stop(); UpdateFace(); };
        _trim.Tick += (_, _) => { if (_view != View.Open) Trim(heapToo: !_looping); };

        Theme.Changed += ApplyGlow;
        Player.StateChanged += () =>
        {
            Meter.Reset();
            if (_wasPlaying && !Player.Playing) React(Mood.Sad, 3); // aww, the music stopped
            _wasPlaying = Player.Playing;
            Refresh();
        };
        _water = Water.Total;
        Water.Changed += () => { if (Water.Total > _water) React(Mood.Happy, 4); _water = Water.Total; };
        Player.PropsChanged += () =>
        {
            ClosedArt.Background = Player.Art == null ? null : new ImageBrush(Player.Art) { Stretch = Stretch.UniformToFill };
            ApplyGlow();
        };
        Pomodoro.Changed += Refresh;
        Pomodoro.Finished += wasBreak =>
        {
            Sound.Chime();
            React(Mood.Happy, 4);
            Peek(wasBreak ? "" : "", "Accent", 1, wasBreak ? "Break's over" : "Focus session done",
                wasBreak ? "Ready for the next round?" : $"Take a {Store.D.Settings.BreakMin} min break");
        };
        Power.Init(plugged =>
        {
            if (plugged) React(Mood.Happy, 3);
            Peek(plugged ? "" : "", plugged ? "Good" : "FgDim", Power.Percent / 100.0,
            plugged ? "Charging" : "On battery", plugged ? "Charger connected" : "Charger disconnected", $"{Power.Percent}%");
        });
        Headphones.Init((name, pct) =>
        {
            Meter.Reset();
            Peek("", "AccentA", (pct ?? 100) / 100.0, name, pct is int p ? $"Connected · {p}%" : "Connected", badge: "");
        });

        SetPage(Store.D.Settings.LastPage);
        Loaded += async (_, _) =>
        {
            Collapse(force: true);
            Beat();
            _beat.Start();
            Location.Start();
            try { await Player.Init(); }
            catch (Exception) { Music.ShowUnavailable(); } // no media session service (very old Windows)
        };
    }

    void Place()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top; // hang below a top taskbar instead of covering it
    }


    // ---------- frame loop: runs only while something is moving ----------

    void StartLoop()
    {
        if (_looping) return;
        _looping = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += Frame;
    }

    void Frame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_saving && _lastFrame != TimeSpan.Zero && (now - _lastFrame).TotalMilliseconds < 32) return;
        if (now == _lastFrame) return; // Rendering can fire more than once per frame
        var dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _lastFrame).TotalSeconds, 1 / 20.0);
        _lastFrame = now;
        var busy = false;

        if (_dragInside || !_hov.Settled || _dropT >= 0)
        {
            _hov.Target = _dragInside ? 1 : 0;
            _hov.Step(dt);
            if (_dropT >= 0 && (_dropT += dt * 0.7) >= 2) _dropT = -1;
            // the notch breathes in a little while something hovers over it
            _w.Target = _base.Width + 9 * _hov.Value;
            _h.Target = _base.Height + 6 * _hov.Value;
            Light();
            busy = true;
        }
        if (!_w.Settled || !_h.Settled)
        {
            _w.Step(dt);
            _h.Step(dt);
            Reshape();
            busy = true;
        }
        if (!_gx.Settled || !_gy.Settled)
        {
            _gx.Step(dt);
            _gy.Step(dt);
            Face.Gaze.X = _gx.Value;
            Face.Gaze.Y = _gy.Value;
            busy = true;
        }
        if (DanceLive)
        {
            Dance(dt);
            busy = true;
        }
        if (WaveLive)
        {
            _level = Math.Max(Meter.Peak(), _level * Math.Pow(0.86, dt * 30)); // fast attack, slow decay
            _phase += dt * 6.5;
            var level = Math.Min(1, 0.3 + _level * 1.5);
            foreach (var wave in new[] { ClosedWave, HeadWave, Music.Viz })
            {
                if (!wave.IsVisible) continue;
                wave.Phase = _phase;
                wave.Level = level;
                wave.InvalidateVisual();
            }
            busy = true;
        }
        if (!busy)
        {
            CompositionTarget.Rendering -= Frame;
            _looping = false;
        }
    }

    void Light()
    {
        static double Smooth(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
        var d = _dropT;
        var rise = d >= 0 && d < 1.1 ? Math.Sin(Smooth(d / 1.1) * Math.PI) : 0;
        var fade = d >= 0 ? (d < 0.9 ? 1 : 1 - Smooth((d - 0.9) / 1.1)) : 1;
        DropLight.Hover = Math.Clamp(_hov.Value, 0, 1.04) * fade;
        DropLight.Rise = rise * fade;
        DropLight.Wave = d >= 0 && d < 1.4 ? d / 1.4 : -1;
        DropLight.X = d >= 0 ? _dropX : _cursorX;
        DropLight.Body = new Rect((Root.ActualWidth - _w.Value) / 2, 0, _w.Value, _h.Value);
        DropLight.A = Theme.A;
        DropLight.B = Theme.B;
        DropLight.InvalidateVisual();
    }

    // the visualizers dance whenever music plays and one of them is on screen
    bool WaveLive => Player.Playing && Visibility == Visibility.Visible && (_view == View.Closed || _view == View.Open);
    bool DanceLive => _dancing && _view == View.Resting && Visibility == Visibility.Visible;

    // Kilo bops along: a hop on each beat (harder on loud ones), swaying to the other side every beat,
    // a note floating up every other beat. Quiet stretches still get a slow sway so he never freezes.
    void Dance(double dt)
    {
        var peak = Meter.Peak();
        _loud += (peak - _loud) * Math.Min(1, dt * 3); // the song's running loudness
        _sinceBeat += dt;
        if ((peak > 0.05 && peak > _loud * 1.25 && _sinceBeat > 0.3) || _sinceBeat > 0.9)
        {
            _sinceBeat = 0;
            _bob.Velocity = -55 - 60 * Math.Min(1, peak);
            _sway = -_sway;
            _tilt.Target = 9 * _sway;
            if (++_beats % 2 == 0) Note(_sway > 0 ? NoteR : NoteL);
        }
        _bob.Step(dt);
        _tilt.Step(dt);
        Face.Bob.Y = _bob.Value;
        Face.Bob.X = _tilt.Value * 0.35;
        Face.Tilt.Angle = _tilt.Value;
        Face.Gaze.X = _tilt.Value * 0.3; // looking toward the side he leans to
    }

    static void Note(TextBlock n)
    {
        var d = TimeSpan.FromMilliseconds(950);
        ((TranslateTransform)n.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, -13, d) { EasingFunction = EaseOut });
        var fade = new DoubleAnimationUsingKeyFrames { Duration = d };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.2)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        n.BeginAnimation(OpacityProperty, fade);
    }

    void Reshape()
    {
        double w = Math.Max(2, _w.Value), h = Math.Max(2, _h.Value), e = Ear, x = (Root.ActualWidth - w) / 2;
        if (Root.ActualWidth <= 0) return;
        var r = Math.Min(Math.Clamp(h * 0.42, 10, 30), w / 2);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x - e, 0), true, true);
            c.QuadraticBezierTo(new Point(x, 0), new Point(x, e), true, true);
            c.LineTo(new Point(x, h - r), true, true);
            c.ArcTo(new Point(x + r, h), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
            c.LineTo(new Point(x + w - r, h), true, true);
            c.ArcTo(new Point(x + w, h - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
            c.LineTo(new Point(x + w, e), true, true);
            c.QuadraticBezierTo(new Point(x + w, 0), new Point(x + w + e, 0), true, true);
        }
        g.Freeze();
        ShadowShape.Data = NotchShape.Data = GlowShape.Data = g;
        var clip = new RectangleGeometry(new Rect(x, -r, w, h + r), r, r); // square top, round bottom
        clip.Freeze();
        Body.Clip = clip;
    }

    // ---------- views ----------

    void Morph(Size body, View v)
    {
        var grow = v is View.Open or View.Peek;
        var wasOpen = _view == View.Open;
        _view = v;
        _base = body;
        _w.Target = body.Width;
        _h.Target = body.Height;
        if (grow) { _w.Tune(430, 27); _h.Tune(360, 24); } // a little overshoot, like it's breathing in
        else { _w.Tune(520, 45); _h.Tune(560, 47); }       // settle without bounce
        StartLoop();

        var show = v switch { View.Resting => RestingView, View.Closed => ClosedView, View.Peek => PeekView, _ => OpenView };
        ShowView(show, grow, wasOpen && v == View.Open);
        UpdateFace();
        ApplyGlow();
        UpdateTimers();
    }

    // Crossfade: the old view fades out fast while the new one scales and fades in. Every animation
    // starts from wherever the previous one left off, so a quick in-out-in never blinks to empty.
    void ShowView(FrameworkElement v, bool grow, bool same)
    {
        if (_shown == v && same) return;
        var old = _shown;
        _shown = v;
        if (old != null && old != v)
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
            fade.Completed += (_, _) => { if (_shown != old) old.Visibility = Visibility.Collapsed; };
            old.BeginAnimation(OpacityProperty, fade);
        }
        var wasHidden = v.Visibility != Visibility.Visible || v.Opacity < 0.05;
        v.Visibility = Visibility.Visible;
        if (wasHidden) v.Opacity = 0;
        var delay = TimeSpan.FromMilliseconds(wasHidden ? (grow ? 70 : 40) : 0);
        v.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { BeginTime = delay, EasingFunction = EaseOut });
        if (!wasHidden) return; // it was mid fade-out: just bring it back, no re-pop
        var s = (ScaleTransform)v.RenderTransform;
        var scale = new DoubleAnimation(grow ? 0.88 : 1.06, 1, TimeSpan.FromMilliseconds(460)) { BeginTime = delay, EasingFunction = Pop };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
    }

    public void Collapse(bool force = false)
    {
        if (_typing || _dragInside || _held) return;
        _peekEnd.Stop();
        var rest = Store.D.Settings.Personality && (_dancing || Store.D.Settings.TypingPeek && _keys.Active || CompanionVision.Enabled || (!Player.Playing && !Pomodoro.Running));
        if (!_dancing)
        {
            _bob.Value = _bob.Velocity = _tilt.Value = _tilt.Velocity = _tilt.Target = 0;
            Face.Bob.X = Face.Bob.Y = Face.Tilt.Angle = 0;
            Face.Gaze.X = _gx.Value;
        }
        var v = rest ? View.Resting : View.Closed;
        if (v != _view || force) { Morph(rest ? RestingSize : ClosedSize, v); _trim.Stop(); _trim.Start(); }
        RefreshClosed();
    }

    // Lightweight mode: while collapsed, hand idle pages back to Windows. Measured: ~119 MB → ~18 MB in
    // Task Manager, settling around 35 MB even with the visualizer running; pages fault back in on use.
    // The heap is compacted only when nothing animates, so a blocking GC never costs a frame.
    static void Trim(bool heapToo)
    {
        if (heapToo)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
        }
        SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
    }

    void Open()
    {
        _leave.Stop();
        _peekEnd.Stop();
        _trim.Stop();
        if (DateTime.Now - _lastOpen > TimeSpan.FromMinutes(20)) React(Mood.Happy, 3); // missed you
        _lastOpen = DateTime.Now;
        var size = _lyrics ? LyricsSize : _page == 5 ? PhotoSize : OpenSize;
        if (_view == View.Open && _base == size) return;
        OpenView.BeginAnimation(WidthProperty, new DoubleAnimation(size.Width, TimeSpan.FromMilliseconds(_lyrics ? 620 : 440)) { EasingFunction = EaseOut });
        OpenView.BeginAnimation(HeightProperty, new DoubleAnimation(size.Height, TimeSpan.FromMilliseconds(440)) { EasingFunction = EaseOut });
        Morph(size, View.Open);
        RefreshClosed();
    }

    public void Peek(string glyph, string ringBrush, double value, string title, string sub, string? right = null, string? badge = null)
    {
        if (_view == View.Open) return; // they're already looking at the notch
        PeekGlyph.Text = glyph;
        PeekRing.SetResourceReference(Ring.BrushProperty, ringBrush);
        PeekRing.BeginAnimation(Ring.ValueProperty, new DoubleAnimation(0, value, TimeSpan.FromMilliseconds(900)) { BeginTime = TimeSpan.FromMilliseconds(120), EasingFunction = EaseOut });
        PeekTitle.Text = title;
        PeekSub.Text = sub;
        PeekRight.Text = right ?? "";
        PeekRight.Visibility = right == null ? Visibility.Collapsed : Visibility.Visible;
        PeekBadge.Visibility = badge == null ? Visibility.Collapsed : Visibility.Visible;
        PeekBadgeGlyph.Text = badge ?? "";
        ((TranslateTransform)PeekText.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(80), EasingFunction = EaseOut });
        if (badge != null)
        {
            var pulse = new DoubleAnimation(1, 1.14, TimeSpan.FromMilliseconds(320)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2), BeginTime = TimeSpan.FromMilliseconds(300), EasingFunction = EaseOut };
            var bs = (ScaleTransform)PeekBadge.RenderTransform;
            bs.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            bs.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }
        Morph(PeekSize, View.Peek);
        _peekEnd.Stop();
        _peekEnd.Start();
    }

    public void Demo(string what)
    {
        var pct = Power.HasBattery ? Power.Percent : 70;
        switch (what)
        {
            case "lowbattery": React(Mood.LowPower, 8); Peek("", "Bad", .1, "Kilo needs a little power", "Plug in when you can · taking it easy", "10%"); break;
            case "curious": React(Mood.Curious, 6); break;
            case "surprised": React(Mood.Surprised, 3); break;
            case "charging": Peek("", "Good", pct / 100.0, "Charging", "Charger connected", $"{pct}%"); break;
            case "headphones": Peek("", "AccentA", 0.7, "Demo Buds", "Connected · 70%", badge: ""); break;
            case "water": Peek("", "AccentA", 0.5, "Time for some water", "0.75 / 1.5 L today", badge: ""); break;
            case "todo": Peek("", "Good", 1, "Reminder", "Reply to the group chat", badge: ""); break;
            case "focus": Peek("", "Accent", 1, "Focus session done", $"Take a {Store.D.Settings.BreakMin} min break"); break;
        }
    }

    // Colors glide instead of jumping, and the music view keeps one glow whether or not lyrics are open.
    // The seek bar, visualizers and the music view's glow all wear the song's colors.
    void ApplyGlow()
    {
        var s = Store.D.Settings;
        var (va, vb) = Player.Vibe;
        var (a, b) = _view == View.Open && _page == 0 ? (va, vb) : (Theme.A, Theme.B);
        var d = TimeSpan.FromMilliseconds(500);
        _glow.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(a, d));
        _glow.GradientStops[1].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(b, d));
        _vibe.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(va, TimeSpan.FromMilliseconds(700)));
        _vibe.GradientStops[1].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(vb, TimeSpan.FromMilliseconds(700)));
        var f = _view switch { View.Resting => 1.0, View.Peek => 0.55, View.Open => 0.6, _ => 0.25 };
        // a very bright glow (white covers, Mono theme) would wash out the white text above it
        var bright = (Theme.Luma(a) + Theme.Luma(b)) / 2;
        if (bright > 0.65) f *= 0.55;
        GlowShape.BeginAnimation(OpacityProperty, new DoubleAnimation(s.Glow * f * (LowEnergy ? .55 : 1), d) { EasingFunction = EaseOut });
    }

    // ---------- live bits ----------

    void Refresh()
    {
        if (_view is View.Resting or View.Closed) Collapse();
        RefreshClosed();
        UpdateTimers();
    }

    void RefreshClosed()
    {
        var now = DateTime.Now;
        ClosedTime.Text = Pomodoro.Running ? Clock(Pomodoro.Left) : now.ToString("HH:mm");
        ClosedArt.Visibility = Visibility.Collapsed;
        ClosedRing.Visibility = Visibility.Visible;
        if (Pomodoro.Running)
        {
            ClosedRing.Value = Pomodoro.Left / Pomodoro.Total;
            ClosedRing.SetResourceReference(Ring.BrushProperty, "Accent");
        }
        else if (Power.HasBattery) BatteryRing(ClosedRing);
        else if (Player.Art != null) { ClosedRing.Visibility = Visibility.Collapsed; ClosedArt.Visibility = Visibility.Visible; }
        else ClosedRing.Visibility = Visibility.Hidden;
        ClosedWave.Visibility = Player.Playing ? Visibility.Visible : Visibility.Hidden;

        HeadDate.Text = now.ToString("ddd d MMM yyyy");
        HeadTime.Text = now.ToString("HH:mm");
        HeadRing.Visibility = Power.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        if (Power.HasBattery) BatteryRing(HeadRing);
    }

    static void BatteryRing(Ring r)
    {
        r.Value = Power.Percent / 100.0;
        r.SetResourceReference(Ring.BrushProperty, Power.Plugged || Power.Percent > 20 ? "Good" : "Bad");
    }

    public static string Clock(TimeSpan t)
    {
        var s = (int)Math.Ceiling(Math.Max(0, t.TotalSeconds));
        return $"{s / 60:00}:{s % 60:00}";
    }

    void Beat()
    {
        Pomodoro.Tick();
        RefreshClosed();
        var now = DateTime.Now;
        _keys.Enable(Store.D.Settings.Personality && Store.D.Settings.TypingPeek);
        var typingNow = Store.D.Settings.Personality && Store.D.Settings.TypingPeek && _keys.Active;
        if (typingNow != _wasTyping)
        {
            _wasTyping = typingNow;
            if (_view is View.Resting or View.Closed) Collapse();
            if (typingNow) Face.Bob.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(7, 0, TimeSpan.FromMilliseconds(480)) { EasingFunction = EaseOut });
        }
        var saving = LowEnergy;
        if (_saving != saving) { _saving = saving; ApplyGlow(); UpdateTimers(); }
        int batteryBand = Power.BatteryBand(Power.HasBattery, Power.Plugged, Power.Percent);
        if (batteryBand > _batteryNotice)
        {
            React(Mood.LowPower, 5);
            Peek("", "Bad", Power.Percent / 100.0, batteryBand == 2 ? "Kilo needs a little power" : "Let's save some energy",
                "Plug in when you can · taking it easy", $"{Power.Percent}%");
        }
        _batteryNotice = batteryBand;
        UpdateFace();
        Water.Check(now);
        TodoPage.CheckDue(now);
        if (now.Date != _today)
        {
            _today = now.Date;
            Store.Prune();
            Water.Refresh();
            _todo.Refresh();
        }

        // step aside for full-screen apps, games and presentations
        if (SHQueryUserNotificationState(out var q) == 0)
        {
            var want = q is 2 or 3 or 4 ? Visibility.Hidden : Visibility.Visible;
            if (Visibility != want) { Visibility = want; UpdateTimers(); }
        }

        // While you're away: with music on Kilo starts dancing, otherwise he gets drowsy and falls asleep.
        // Any input wakes him up with a happy hello.
        var idle = IdleTime();
        var personality = Store.D.Settings.Personality && Visibility == Visibility.Visible;
        var dance = personality && !LowEnergy && Player.Playing && !Pomodoro.Running && idle > TimeSpan.FromSeconds(20);
        if (dance != _dancing)
        {
            _dancing = dance;
            if (_view is View.Resting or View.Closed) Collapse();
        }
        var sleep = Player.Playing || _visionFace is { Present: true } ? 0 : idle > TimeSpan.FromMinutes(5) ? 2 : idle > TimeSpan.FromSeconds(90) ? 1 : 0;
        if (sleep != _sleep)
        {
            var woke = sleep < _sleep;
            _sleep = sleep;
            UpdateFace();
            UpdateTimers();
            if (woke) React(Mood.Happy, 1.6);
        }
        // now and then, a little mood of his own: a happy hum, or a sulk when you haven't come by in a while
        if (now >= _nextFidget)
        {
            _nextFidget = now.AddSeconds(30 + _rnd.Next(40));
            if (_sleep == 0 && !_dancing)
                React(now - _lastOpen > TimeSpan.FromMinutes(20) && _rnd.Next(2) == 0 ? Mood.Sad : Mood.Happy, 1.8);
        }
    }

    void UpdateTimers()
    {
        var watching = _view == View.Resting && Visibility == Visibility.Visible && _sleep == 0 && !_dancing;
        _gazePoll.IsEnabled = watching;
        _gazePoll.Interval = TimeSpan.FromMilliseconds(LowEnergy ? 100 : 40);
        _blink.IsEnabled = watching;
        if (WaveLive || DanceLive) StartLoop();
    }

    // What Kilo feels right now: sleep beats everything, then a passing reaction, then dancing,
    // then worries that last (battery nearly empty, a glass of water overdue).
    void UpdateFace()
    {
        var worried = (Power.HasBattery && !Power.Plugged && Power.Percent <= 20) || Water.Thirsty;
        Face.Mood = _sleep == 2 ? Mood.Asleep : _sleep == 1 ? Mood.Drowsy
            : DateTime.Now < _reactUntil ? _react
            : LowEnergy ? Mood.LowPower
            : Store.D.Settings.TypingPeek && _keys.Active ? Mood.Curious
            : _visionFace is { Present: true } ? Mood.Curious
            : _dancing ? Mood.Happy
            : worried ? Mood.Sad : Mood.Normal;
    }

    /// A short-lived mood: shown now if Kilo is on screen, or when the notch next settles back to him.
    public void React(Mood m, double seconds)
    {
        _react = m;
        _reactUntil = DateTime.Now.AddSeconds(seconds);
        _moodEnd.Stop();
        _moodEnd.Interval = TimeSpan.FromSeconds(seconds);
        _moodEnd.Start();
        UpdateFace();
        if (m == Mood.Happy && _view == View.Resting && _sleep == 0)
        {
            _gy.Velocity = -60; // a little hop
            StartLoop();
        }
    }

    /// Keeps the notch open (photo booth countdown) even if the cursor wanders off.
    public void Hold(bool on)
    {
        _held = on;
        if (!on && !CursorInNotch()) _leave.Start();
    }

    public void Flash()
    {
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(40))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(480)), EaseOut));
        FlashLayer.BeginAnimation(OpacityProperty, a);
    }

    // Cheap cursor poll; the spring only runs (and only redraws) while the eyes are actually moving.
    void PollGaze()
    {
        var eyes = Face.Eyes;
        if (!eyes.IsVisible) return;
        GetCursorPos(out var p);
        var c = eyes.PointToScreen(new Point(eyes.ActualWidth / 2, eyes.ActualHeight / 2));
        var tx = Math.Clamp((p.X - c.X) / 70, -5, 5);
        var ty = Math.Clamp((p.Y - c.Y) / 150, -1.5, 2.5);
        if (Store.D.Settings.TypingPeek && _keys.Active) { tx = Math.Sin(Environment.TickCount64 / 240.0) * 2; ty = 3; }
        else if (_visionFace is { Present: true } face) { tx = (0.5 - face.X) * 10; ty = (face.Y - 0.5) * 5; }
        if (Math.Abs(tx - _gx.Target) + Math.Abs(ty - _gy.Target) < 0.05) return;
        _gx.Target = tx;
        _gy.Target = ty;
        _gx.Tune(170, 18);
        _gy.Tune(170, 18);
        StartLoop();
    }

    public void ClearVision() { _visionFace = null; Face.ToolTip = "Kilo · double-click for a little hello"; UpdateFace(); }
    public void VisionReaction(VisionResult face)
    {
        if (face.Present && _visionFace is not { Present: true }) React(Mood.Happy, 2);
        else if (!face.Present && _visionFace is { Present: true }) React(Mood.Curious, 2);
        _visionFace = face;
        Face.ToolTip = "Camera reactions ON · turn off in tray or Settings";
    }

    void Blink()
    {
        _blink.Interval = TimeSpan.FromSeconds(2 + _rnd.NextDouble() * 4);
        Face.Blink(twice: _rnd.Next(5) == 0);
    }

    // ---------- pages ----------

    Border Dot(int k)
    {
        var dot = new Border { Height = 5, Width = 5, CornerRadius = new CornerRadius(2.5), Margin = new Thickness(2.5, 0, 2.5, 0), Cursor = Cursors.Hand };
        dot.SetResourceReference(Border.BackgroundProperty, "SurfaceHi");
        dot.MouseLeftButtonUp += (_, _) => SetPage(k, Math.Sign(k - _page));
        return dot;
    }

    void SetPage(int i, int dir = 0)
    {
        _page = Math.Clamp(i, 0, _pages.Length - 1);
        Store.D.Settings.LastPage = _page;
        var p = _pages[_page];
        PageHost.Content = p;
        if (dir != 0)
        {
            var slide = new TranslateTransform();
            p.RenderTransform = slide;
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40 * dir, 0, TimeSpan.FromMilliseconds(480)) { EasingFunction = Pop });
            p.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = EaseOut });
            ((TranslateTransform)PageTitle.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(12 * dir, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = EaseOut });
            PageTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        }

        SetTitle();

        for (int k = 0; k < Dots.Children.Count; k++)
        {
            var dot = (Border)Dots.Children[k];
            dot.BeginAnimation(WidthProperty, new DoubleAnimation(k == _page ? 16 : 5, TimeSpan.FromMilliseconds(380)) { EasingFunction = Pop });
            dot.SetResourceReference(Border.BackgroundProperty, k == _page ? "Fg" : "SurfaceHi");
        }
        UpdateArrows();
        ApplyGlow();
        if (_view == View.Open) Open();
    }

    // Page title; the focus page shows where you are (e.g. "Bekasi") once the location is known.
    void SetTitle()
    {
        var (first, second) = Titles[_page];
        PageTitle.Inlines.Clear();
        if (_page == 1 && Location.City is string city)
        {
            var pin = new Run(" ") { FontFamily = (FontFamily)FindResource("Icons"), FontSize = 13 };
            pin.SetResourceReference(TextElement.ForegroundProperty, "AccentA");
            PageTitle.Inlines.Add(pin);
            PageTitle.Inlines.Add(new Run(city));
            return;
        }
        PageTitle.Inlines.Add(new Run(first));
        var accent = new Run(second);
        accent.SetResourceReference(TextElement.ForegroundProperty, "AccentA");
        PageTitle.Inlines.Add(accent);
    }

    void UpdateArrows()
    {
        PrevPage.Visibility = !_lyrics && _page > 0 ? Visibility.Visible : Visibility.Hidden;
        NextPage.Visibility = !_lyrics && _page < _pages.Length - 1 ? Visibility.Visible : Visibility.Hidden;
    }

    public void SetLyrics(bool on)
    {
        _lyrics = on;
        foreach (var chrome in new FrameworkElement[] { Header, Dots })
        {
            chrome.IsHitTestVisible = !on;
            chrome.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 0 : 1, TimeSpan.FromMilliseconds(on ? 140 : 320)));
        }
        PageHost.BeginAnimation(MarginProperty, new ThicknessAnimation(on ? new Thickness(26, 16, 26, 14) : new Thickness(50, 38, 50, 24),
            TimeSpan.FromMilliseconds(on ? 620 : 440)) { EasingFunction = EaseOut });
        UpdateArrows();
        Music.SetLyricsMode(on);
        Open();
    }

    void PagePrev(object sender, RoutedEventArgs e) => SetPage(_page - 1, -1);
    void PageNext(object sender, RoutedEventArgs e) => SetPage(_page + 1, 1);

    // ---------- input ----------

    void NotchEnter(object sender, MouseEventArgs e)
    {
        _leave.Stop();
        if (_view != View.Open) _enter.Start();
    }

    void NotchLeave(object sender, MouseEventArgs e)
    {
        _enter.Stop();
        if (!_typing) _leave.Start();
    }

    void NotchWheel(object sender, MouseWheelEventArgs e)
    {
        if (_view != View.Open || _lyrics || (DateTime.Now - _lastWheel).TotalMilliseconds < 350) return;
        var dir = e.Delta < 0 ? 1 : -1;
        if (_page + dir < 0 || _page + dir >= _pages.Length) return;
        _lastWheel = DateTime.Now;
        SetPage(_page + dir, dir);
        e.Handled = true;
    }

    // WPF raises DragLeave/DragEnter while moving between child elements, so a real "left the
    // notch" is a DragLeave that no DragEnter/DragOver follows within a moment.
    void NotchDragEnter(object sender, DragEventArgs e)
    {
        _dragLeave.Stop();
        _leave.Stop();
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        e.Effects = files != null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        _cursorX = e.GetPosition(Root).X;
        if (_dragInside) { DragShell.Over(); return; }
        _dragInside = true;
        StartLoop();
        DragShell.Enter(_hwnd, e.Data);
        if (files == null) return;
        if (_lyrics) SetLyrics(false);
        if (_page != ShelfIndex) SetPage(ShelfIndex, 1);
        Open();
        _shelf.DragPreview(files);
    }

    void NotchDragOver(object sender, DragEventArgs e)
    {
        _dragLeave.Stop();
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        _cursorX = e.GetPosition(Root).X;
        DragShell.Over();
        _shelf.DragMove(e.GetPosition(_shelf));
    }

    void NotchDragLeave(object sender, DragEventArgs e) => _dragLeave.Start();

    void DragGone()
    {
        if (!_dragInside) return;
        _dragInside = false;
        DragShell.Leave();
        _shelf.DragCancel();
        _leave.Start();
    }

    void NotchDrop(object sender, DragEventArgs e)
    {
        _dragLeave.Stop();
        _dragInside = false;
        DragShell.Drop(e.Data);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            _shelf.DropFiles(files);
            React(Mood.Happy, 3); // yum
            // the light swells, a wave rolls out along the bottom, and the notch gulps: wider, then springs back
            _dropX = e.GetPosition(Root).X;
            _dropT = 0;
            _w.Tune(380, 14);
            _h.Tune(380, 14);
            _w.Velocity += 330;
            _h.Velocity -= 215;
            StartLoop();
        }
        else _shelf.DragCancel();
        e.Handled = true;
        _leave.Start();
    }

    bool CursorInNotch()
    {
        GetCursorPos(out var p);
        var local = Root.PointFromScreen(new Point(p.X, p.Y));
        var x = (Root.ActualWidth - _w.Value) / 2;
        return local.X >= x - Ear && local.X <= x + _w.Value + Ear && local.Y >= -4 && local.Y <= _h.Value + 4;
    }

    // The notch never steals focus, except while you type a task.
    public void AllowTyping()
    {
        _typing = true;
        _leave.Stop();
        SetNoActivate(false);
        Activate();
    }

    public void EndTyping()
    {
        if (!_typing) return;
        _typing = false;
        Keyboard.ClearFocus();
        SetNoActivate(true);
        if (!CursorInNotch()) Collapse();
    }

    public IntPtr Handle => _hwnd;

    void OpenSettings(object sender, RoutedEventArgs e) => SettingsWindow.Open();
    void Exit(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    // ---------- Win32 ----------

    const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        SetNoActivate(true);
    }

    void SetNoActivate(bool on)
    {
        var s = GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW; // tool window: stays out of Alt+Tab
        SetWindowLong(_hwnd, GWL_EXSTYLE, on ? s | WS_EX_NOACTIVATE : s & ~WS_EX_NOACTIVATE);
    }

    static TimeSpan IdleTime()
    {
        var i = new LASTINPUTINFO { cbSize = 8 };
        GetLastInputInfo(ref i);
        return TimeSpan.FromMilliseconds((uint)Environment.TickCount - i.dwTime);
    }

    struct POINT { public int X, Y; }
    struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);
}
