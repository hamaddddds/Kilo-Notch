using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Notch;

/// Better Lyrics-style view: the sung line grows and glows, syllables light up as they're sung,
/// lines further away blur out, and the list springs along with the song. Click a line to jump there.
public class LyricsView : Grid
{
    class WordFx { public Word W = null!; public LinearGradientBrush Brush = null!; }
    class Row { public Line Line = null!; public Border Box = null!; public List<WordFx> Words = new(); public ScaleTransform Scale = new(.94, .94); public TranslateTransform Drift = new(); public int Style = int.MinValue, MotionVersion; public bool Animating; }

    static readonly IEasingFunction Spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
    readonly StackPanel _stack = new() { VerticalAlignment = VerticalAlignment.Top };
    readonly TranslateTransform _scroll = new();
    readonly TextBlock _status = new()
    {
        FontSize = 14, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };
    readonly TextBlock _source = new() { FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0.7 };
    readonly List<Row> _rows = new();
    readonly FramePacer _frames = new();
    readonly BlurEffect[] _blur = Enumerable.Range(1, 3).Select(i =>
    {
        var effect = new BlurEffect { Radius = i * 1.2, RenderingBias = RenderingBias.Performance };
        effect.Freeze(); return effect;
    }).ToArray();
    DropShadowEffect _highlight = null!;
    bool _synced, _hooked, _retry;
    int _active = -2, _scrollVersion;

    public LyricsView()
    {
        // No opacity mask: it would re-render the whole list every frame. Lines fade by distance instead.
        // The list sits on a Canvas: inside a Grid it would be layout-clipped to the view's height
        // *before* the scroll transform, so scrolling down slid the only drawn part out of sight.
        ClipToBounds = true;
        Background = Brushes.Transparent;
        _stack.RenderTransform = _scroll;
        Children.Add(new Canvas { Children = { _stack } });
        _status.SetResourceReference(TextBlock.ForegroundProperty, "FgDim");
        _source.SetResourceReference(TextBlock.ForegroundProperty, "FgDim");
        Children.Add(_status);
        Children.Add(_source);

        IsVisibleChanged += (_, _) =>
        {
            _active = -2;
            foreach (var row in _rows) { StopMotion(row); row.Style = int.MinValue; }
            _scrollVersion++;
            _scroll.BeginAnimation(TranslateTransform.YProperty, null);
            Hook();
        };
        SizeChanged += (_, _) => { _stack.Width = ActualWidth; _active = -2; Update(); }; // a Canvas doesn't size its children
        Loaded += (_, _) => { Player.StateChanged += Hook; Theme.Changed += Recolor; Recolor(); Hook(); };
        Unloaded += (_, _) =>
        {
            Player.StateChanged -= Hook; Theme.Changed -= Recolor; Unhook();
            foreach (var row in _rows) StopMotion(row);
            _scrollVersion++;
            _scroll.BeginAnimation(TranslateTransform.YProperty, null);
        };
        Recolor();
        // plain (unsynced) lyrics scroll by hand
        MouseWheel += (_, e) =>
        {
            if (_synced) return;
            var target = Math.Clamp(_scroll.Y + e.Delta / 3.0, Math.Min(12, ActualHeight - _stack.ActualHeight - 20), 12);
            _scroll.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            e.Handled = true;
        };
    }

    public void SetStatus(string text)
    {
        Clear();
        _status.Text = text;
        _status.Visibility = Visibility.Visible;
        _source.Text = "";
        Hook();
    }

    public void SetLines(Lyric lyric)
    {
        Clear();
        _status.Visibility = Visibility.Collapsed;
        _synced = lyric.Synced;
        _source.Text = lyric.Source + (lyric.Synced ? "" : " · not synced");
        foreach (var line in lyric.Synced ? lyric.Lines.OrderBy(l => l.Start) : lyric.Lines.AsEnumerable())
        {
            var row = new Row { Line = line };
            var wrap = new WrapPanel();
            foreach (var w in line.Words)
            {
                var fx = new WordFx
                {
                    W = w,
                    Brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), GradientStops = { new(), new(), new(), new() } },
                };
                // background vocals sit smaller, like Better Lyrics shows them
                wrap.Children.Add(new TextBlock { Text = w.Space ? w.Text + " " : w.Text, FontSize = w.Bg ? 12 : 16, FontWeight = w.Bg ? FontWeights.SemiBold : FontWeights.Bold, Foreground = fx.Brush, VerticalAlignment = VerticalAlignment.Bottom });
                row.Words.Add(fx);
            }
            row.Box = new Border
            {
                Child = wrap, Padding = new Thickness(0, 6, 8, 6), Background = Brushes.Transparent,
                RenderTransformOrigin = new Point(0, 0.5), RenderTransform = new TransformGroup { Children = { row.Scale, row.Drift } },
                Opacity = _synced ? 0.3 : 0.85,
            };
            if (_synced)
            {
                var at = line.Start;
                row.Box.Cursor = Cursors.Hand;
                row.Box.MouseLeftButtonUp += (_, _) => Player.Seek(TimeSpan.FromSeconds(at));
            }
            _stack.Children.Add(row.Box);
            _rows.Add(row);
        }
        Recolor();
        foreach (var r in _rows) Light(r, _synced ? 0 : 1);
        if (!_synced) _scroll.Y = 12;
        Hook();
    }

    void Clear()
    {
        Unhook();
        foreach (var row in _rows) StopMotion(row);
        _rows.Clear();
        _stack.Children.Clear();
        _synced = false;
        _active = -2;
        _scrollVersion++;
        _scroll.BeginAnimation(TranslateTransform.YProperty, null);
        _scroll.Y = 0;
    }

    // Per-frame updates only while visible and playing.
    void Hook()
    {
        var want = IsLoaded && IsVisible && _synced && Player.Playing;
        if (want != _hooked)
        {
            _hooked = want;
            _frames.Reset();
            if (want) CompositionTarget.Rendering += OnFrame;
            else CompositionTarget.Rendering -= OnFrame;
        }
        Update();
    }

    void Unhook()
    {
        if (_hooked) CompositionTarget.Rendering -= OnFrame;
        _hooked = false;
        _frames.Reset();
    }

    void OnFrame(object? sender, EventArgs e)
    {
        if (_frames.TryStep(((RenderingEventArgs)e).RenderingTime, 60, out _)) Update();
    }

    void Update()
    {
        if (!_synced || _rows.Count == 0) return;
        var t = Player.Position.TotalSeconds + 0.1; // a hair early reads as "on time"
        // Binary search does no per-frame closure allocation, including when seeking backwards.
        int lo = 0, hi = _rows.Count - 1, idx = -1;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (_rows[mid].Line.Start <= t) { idx = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        if (idx != _active) { _active = idx; Restyle(); }
        if (idx < 0) return;
        foreach (var w in _rows[idx].Words)
            Sweep(w, w.W.End > w.W.Start ? Math.Clamp((t - w.W.Start) / (w.W.End - w.W.Start), 0, 1) : 1);
    }

    void Restyle()
    {
        if (!IsVisible) return;
        UpdateLayout();
        // Not measured yet (just became visible inside a hidden parent): try again once layout has run,
        // instead of scrolling to a position computed from zero heights.
        if (ActualHeight <= 0 || (_active >= 0 && _rows[_active].Box.ActualHeight <= 0))
        {
            if (!_retry) { _retry = true; Dispatcher.InvokeAsync(() => { _retry = false; _active = -2; Update(); }, DispatcherPriority.Loaded); }
            return;
        }

        var d = TimeSpan.FromMilliseconds(420);
        double top = 0;
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            if (i < _active) top += r.Box.ActualHeight; // layout heights, not transformed positions
            var dist = _active < 0 ? i + 1 : Math.Abs(i - _active);
            var style = i < _active ? -Math.Min(4, dist) : Math.Min(4, dist);
            if (r.Style == style)
            {
                if (dist > 3 && r.Animating) StopMotion(r);
                continue;
            }
            var wasNearby = Math.Abs((long)r.Style) <= 3;
            r.Style = style;
            if (i != _active) Light(r, i < _active ? 1 : 0);
            // past lines fade faster than upcoming ones, like a karaoke screen
            var opacity = dist == 0 ? 1 : i < _active ? (dist == 1 ? 0.35 : 0.12) : dist == 1 ? 0.55 : dist == 2 ? 0.32 : 0.16;
            // Only the visible neighbourhood gets clocks. Distant rows settle directly.
            var animate = dist <= 3 || wasNearby;
            var oldOpacity = r.Box.Opacity;
            var motion = ++r.MotionVersion;
            r.Box.BeginAnimation(OpacityProperty, null);
            r.Box.Opacity = opacity;
            r.Animating = animate;
            if (animate)
            {
                // Hold the settled opacity until the longest stagger ends, then detach
                // all four clocks. A superseded transition cannot clear a newer one.
                var fade = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(oldOpacity, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(opacity, KeyTime.FromTimeSpan(d)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(opacity, KeyTime.FromTimeSpan(d + TimeSpan.FromMilliseconds(72))));
                fade.Completed += (_, _) => { if (r.MotionVersion == motion) StopMotion(r); };
                r.Box.BeginAnimation(OpacityProperty, fade);
            }
            var s = r.Scale;
            var scale = dist == 0 ? 1.0 : 0.94;
            Move(s, ScaleTransform.ScaleXProperty, scale, animate, d, Spring);
            Move(s, ScaleTransform.ScaleYProperty, scale, animate, d, Spring);
            Move(r.Drift, TranslateTransform.YProperty, i > _active ? Math.Min(12, dist * 3) : 0, animate, d,
                new CubicEase { EasingMode = EasingMode.EaseOut }, Math.Min(3, dist) * 24);
            r.Box.Effect = dist == 0 ? _highlight
                : dist <= 3 ? _blur[dist - 1]
                : null;
        }
        // keep the sung line about a third of the way down, fully inside the view
        var lineHeight = _active >= 0 ? _rows[_active].Box.ActualHeight : 0;
        var y = Math.Min(ActualHeight * 0.3, ActualHeight - lineHeight - 8) - top;
        var scrollVersion = ++_scrollVersion;
        Move(_scroll, TranslateTransform.YProperty, y, true, TimeSpan.FromMilliseconds(620), new CubicEase { EasingMode = EasingMode.EaseOut },
            completed: () => { if (_scrollVersion == scrollVersion) _scroll.BeginAnimation(TranslateTransform.YProperty, null); });
    }

    static void Move(Animatable target, DependencyProperty property, double value, bool animate, TimeSpan duration, IEasingFunction ease, int delay = 0, Action? completed = null)
    {
        var from = (double)target.GetValue(property);
        target.BeginAnimation(property, null);
        target.SetValue(property, value);
        if (!animate || Math.Abs(from - value) <= .001) return;
        AnimationTimeline animation;
        if (delay == 0)
            animation = new DoubleAnimation(from, value, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        else
        {
            var stagger = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            stagger.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            stagger.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay))));
            stagger.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(duration + TimeSpan.FromMilliseconds(delay)), ease));
            animation = stagger;
        }
        if (completed != null) animation.Completed += (_, _) => completed();
        target.BeginAnimation(property, animation);
    }

    static void StopMotion(Row row)
    {
        row.MotionVersion++;
        row.Animating = false;
        row.Box.BeginAnimation(OpacityProperty, null);
        row.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        row.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        row.Drift.BeginAnimation(TranslateTransform.YProperty, null);
    }

    void Recolor()
    {
        _highlight = new DropShadowEffect { Color = Theme.A, BlurRadius = 18, ShadowDepth = 0, Opacity = .65, RenderingBias = RenderingBias.Performance };
        _highlight.Freeze();
        var on = Theme.Fg;
        var off = Color.FromArgb(155, on.R, on.G, on.B);
        foreach (var r in _rows)
            foreach (var w in r.Words)
            {
                var g = w.Brush.GradientStops;
                g[0].Color = g[1].Color = on;
                g[2].Color = g[3].Color = off;
                g[3].Offset = 1;
            }
        if (_active >= 0 && _active < _rows.Count) _rows[_active].Box.Effect = _highlight;
    }

    static void Light(Row r, double p) { foreach (var w in r.Words) Sweep(w, p); }

    // p: 0 = not sung yet, 1 = fully sung. A soft edge sweeps left to right.
    static void Sweep(WordFx w, double p)
    {
        var o = -0.25 + 1.25 * p;
        var g = w.Brush.GradientStops;
        if (g[1].Offset == o) return;
        g[1].Offset = o;
        g[2].Offset = o + 0.25;
    }
}
