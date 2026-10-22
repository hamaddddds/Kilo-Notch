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
    class Row { public Line Line = null!; public Border Box = null!; public List<WordFx> Words = new(); public ScaleTransform Scale = new(.94, .94); public TranslateTransform Drift = new(); }

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
    bool _synced, _hooked, _retry;
    int _active = -2;

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

        IsVisibleChanged += (_, _) => { _active = -2; Hook(); };
        SizeChanged += (_, _) => { _stack.Width = ActualWidth; _active = -2; Update(); }; // a Canvas doesn't size its children
        Player.StateChanged += Hook;
        Theme.Changed += Recolor;
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
        foreach (var line in lyric.Lines)
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
        _rows.Clear();
        _stack.Children.Clear();
        _synced = false;
        _active = -2;
        _scroll.BeginAnimation(TranslateTransform.YProperty, null);
        _scroll.Y = 0;
    }

    // Per-frame updates only while visible and playing.
    void Hook()
    {
        var want = IsVisible && _synced && Player.Playing;
        if (want != _hooked)
        {
            _hooked = want;
            if (want) CompositionTarget.Rendering += OnFrame;
            else CompositionTarget.Rendering -= OnFrame;
        }
        Update();
    }

    void OnFrame(object? sender, EventArgs e) => Update();

    void Update()
    {
        if (!_synced || _rows.Count == 0) return;
        var t = Player.Position.TotalSeconds + 0.1; // a hair early reads as "on time"
        var idx = _rows.FindLastIndex(r => r.Line.Start <= t);
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
            if (i != _active) Light(r, i < _active ? 1 : 0);
            // past lines fade faster than upcoming ones, like a karaoke screen
            var opacity = dist == 0 ? 1 : i < _active ? (dist == 1 ? 0.35 : 0.12) : dist == 1 ? 0.55 : dist == 2 ? 0.32 : 0.16;
            var duration = dist <= 5 ? d : TimeSpan.Zero;
            r.Box.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, duration));
            var s = r.Scale;
            var scale = dist == 0 ? 1.0 : 0.94;
            s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = Spring });
            s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = Spring });
            r.Drift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(i > _active ? Math.Min(12, dist * 3) : 0, duration)
                { BeginTime = TimeSpan.FromMilliseconds(dist <= 5 ? dist * 24 : 0), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            r.Box.Effect = dist == 0 ? new DropShadowEffect { Color = Theme.A, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.65, RenderingBias = RenderingBias.Performance }
                : dist <= 3 ? new BlurEffect { Radius = dist * 1.2, RenderingBias = RenderingBias.Performance }
                : null;
        }
        // keep the sung line about a third of the way down, fully inside the view
        var lineHeight = _active >= 0 ? _rows[_active].Box.ActualHeight : 0;
        var y = Math.Min(ActualHeight * 0.3, ActualHeight - lineHeight - 8) - top;
        _scroll.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(620)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    void Recolor()
    {
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
