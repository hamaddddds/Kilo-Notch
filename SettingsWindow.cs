using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Notch;

/// Theme, behavior, timer and water settings in a frameless rounded window. Every change applies live.
public class SettingsWindow : Window
{
    static SettingsWindow? _open;
    static Settings S => Store.D.Settings;
    static readonly IEasingFunction Pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
    static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    readonly Grid _root = new() { Margin = new Thickness(18), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform() };
    readonly StackPanel _body = new() { Margin = new Thickness(18, 4, 18, 22) };
    readonly List<(FrameworkElement Dot, (string, double Hue, double Spread, double Sat) Preset)> _swatches = new();
    Slider _hue = null!, _spread = null!, _sat = null!;

    public static void Open()
    {
        _open ??= new SettingsWindow();
        _open.Show();
        if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
        _open.Activate();
    }

    /// The window's content without the window, for offscreen previews.
    public static FrameworkElement Preview()
    {
        var w = new SettingsWindow();
        var c = (FrameworkElement)w.Content;
        w.Content = null;
        return c;
    }

    SettingsWindow()
    {
        Title = "Kilo Notch settings";
        Width = 470;
        Height = 780;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = (FontFamily)Application.Current.Resources["Ui"];
        SetResourceReference(ForegroundProperty, "Fg");
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        Resources.Add(typeof(ScrollBar), Application.Current.Resources["ThinBar"]);

        // shadow on its own layer, so dragging a slider never re-runs the blur
        var shadow = new Border { CornerRadius = new CornerRadius(22), Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Direction = 270, Opacity = 0.45, RenderingBias = RenderingBias.Performance } };
        shadow.SetResourceReference(Border.BackgroundProperty, "NotchBg");
        var card = new Border { CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "NotchBg");
        card.SetResourceReference(Border.BorderBrushProperty, "SurfaceHi");

        var dock = new DockPanel();
        var bar = TitleBar();
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        var hero = Hero();
        DockPanel.SetDock(hero, Dock.Top);
        dock.Children.Add(hero);
        dock.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        card.Child = dock;
        _root.Children.Add(shadow);
        _root.Children.Add(card);
        Content = _root;

        Build();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) FadeClose(); };
        Loaded += (_, _) =>
        {
            // open: fade in and settle from slightly smaller
            _root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            var s = (ScaleTransform)_root.RenderTransform;
            var grow = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = Pop };
            s.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        };
        Closed += (_, _) => { _open = null; Store.Save(); };
    }

    void FadeClose()
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => Close();
        _root.BeginAnimation(OpacityProperty, fade);
        var s = (ScaleTransform)_root.RenderTransform;
        var shrink = new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(160)) { EasingFunction = EaseOut };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
    }

    // ---------- chrome ----------

    FrameworkElement TitleBar()
    {
        var logo = MiniNotch(36, 16, 6);
        var title = new StackPanel
        {
            Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = "Kilo Notch", FontSize = 15, FontWeight = FontWeights.SemiBold }, Dim("Settings", 11) },
        };
        var min = ChromeButton("", false);
        min.Click += (_, _) => WindowState = WindowState.Minimized;
        var close = ChromeButton("", true);
        close.Click += (_, _) => FadeClose();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = { min, close } };
        var bar = new Grid
        {
            Height = 58, Margin = new Thickness(18, 4, 12, 0), Background = Brushes.Transparent,
            Children = { new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { logo, title } }, buttons },
        };
        bar.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 1) DragMove(); };
        return bar;
    }

    Button ChromeButton(string glyph, bool danger)
    {
        var b = new Button { Content = glyph, Style = Res<Style>("IconBtn"), FontSize = 11, Width = 34, Height = 30, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0) };
        if (danger)
        {
            b.MouseEnter += (_, _) => b.SetResourceReference(ForegroundProperty, "Bad");
            b.MouseLeave += (_, _) => b.SetResourceReference(ForegroundProperty, "Fg");
        }
        return b;
    }

    // A tiny live notch: theme background, glow and eyes, all following the current theme.
    static FrameworkElement MiniNotch(double w, double h, double r)
    {
        var glow = new Border { CornerRadius = new CornerRadius(0, 0, r, r), OpacityMask = new LinearGradientBrush(Colors.Transparent, Colors.Black, 90) };
        glow.SetResourceReference(Border.BackgroundProperty, "Accent");
        var body = new Border { CornerRadius = new CornerRadius(0, 0, r, r), BorderThickness = new Thickness(1) };
        body.SetResourceReference(Border.BackgroundProperty, "NotchBg");
        body.SetResourceReference(Border.BorderBrushProperty, "SurfaceHi");
        var eyes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (var gap in new[] { h * 0.35, 0 })
        {
            var eye = new Border { Width = h * 0.22, Height = h * 0.42, CornerRadius = new CornerRadius(h), Margin = new Thickness(0, 0, gap, 0) };
            eye.SetResourceReference(Border.BackgroundProperty, "Fg");
            eyes.Children.Add(eye);
        }
        return new Grid { Width = w, Height = h, VerticalAlignment = VerticalAlignment.Center, Children = { body, glow, eyes } };
    }

    FrameworkElement Hero()
    {
        var notch = MiniNotch(190, 46, 20);
        var blink = new ScaleTransform();
        ((FrameworkElement)((Grid)notch).Children[2]).RenderTransform = blink;
        ((FrameworkElement)((Grid)notch).Children[2]).RenderTransformOrigin = new Point(0.5, 0.5);
        var a = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(4) };
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.6))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.7))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.82))));
        Timeline.SetDesiredFrameRate(a, 30);
        blink.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        var card = new Border
        {
            Height = 110, Margin = new Thickness(18, 0, 18, 10), CornerRadius = new CornerRadius(18),
            Child = new Grid
            {
                Children =
                {
                    new Border { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Child = notch },
                    new TextBlock { Text = "Live preview", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 12), Opacity = 0.6 },
                },
            },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        return card;
    }

    // ---------- content ----------

    void Build()
    {
        var look = Section("", "Appearance");
        look.Children.Add(Segmented());
        var swatches = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        foreach (var p in Theme.Presets) swatches.Children.Add(Swatch(p));
        look.Children.Add(swatches);
        _hue = Slider(look, "Color", 0, 360, S.Hue, v => $"{v:0}°", v => { S.Hue = v; Theme.Apply(); }, Rainbow());
        _spread = Slider(look, "Gradient width", 0, 180, S.Spread, v => $"{v:0}°", v => { S.Spread = v; Theme.Apply(); });
        _sat = Slider(look, "Vibrance", 0, 100, S.Sat * 100, v => $"{v:0}%", v => { S.Sat = v / 100; Theme.Apply(); });
        Slider(look, "Glow", 0, 100, S.Glow * 100, v => $"{v:0}%", v => { S.Glow = v / 100; Theme.Apply(); });

        var notch = Section("", "Notch");
        Toggle(notch, "Personality", "Eyes come out when nothing is happening", S.Personality, v => { S.Personality = v; MainWindow.I.Collapse(force: true); });
        Toggle(notch, "Album color glow", "Tint the music view with the cover art", S.AlbumGlow, v => { S.AlbumGlow = v; Theme.Apply(); });
        Toggle(notch, "Show my city", "Focus page shows where you are, e.g. Bekasi", S.ShowCity, v => { S.ShowCity = v; Location.Refresh(); });
        Toggle(notch, "Start with Windows", null, AutoStart.Enabled, v => AutoStart.Enabled = v);

        var focus = Section("", "Focus timer");
        Slider(focus, "Focus length", 5, 90, S.FocusMin, v => $"{v:0} min", v => { S.FocusMin = (int)v; if (!Pomodoro.Running) Pomodoro.Reset(); }, step: 5);
        Slider(focus, "Break length", 1, 30, S.BreakMin, v => $"{v:0} min", v => { S.BreakMin = (int)v; if (!Pomodoro.Running) Pomodoro.Reset(); });

        var water = Section("", "Water");
        Slider(water, "Daily goal", 500, 4000, S.WaterGoal, v => $"{v / 1000:0.0#} L", v => { S.WaterGoal = (int)v; Water.Refresh(); }, step: 100);
        Slider(water, "Glass size", 100, 1000, S.WaterCup, v => $"{v:0} ml", v => S.WaterCup = (int)v, step: 50);
        Slider(water, "Remind every", 15, 180, S.WaterEvery, v => $"{v:0} min", v => S.WaterEvery = (int)v, step: 15);
        Slider(water, "Active from", 0, 23, S.WaterFrom, v => $"{v:00}:00", v => { S.WaterFrom = (int)v; Water.Refresh(); });
        Slider(water, "Active until", 1, 24, S.WaterTo, v => $"{v:00}:00", v => { S.WaterTo = (int)v; Water.Refresh(); });

        var demo = Section("", "Try it");
        var demos = new WrapPanel();
        foreach (var (label, key) in new[] { ("Charging", "charging"), ("Headphones", "headphones"), ("Water", "water"), ("To-do", "todo"), ("Focus done", "focus") })
        {
            var b = new Button { Content = label, Style = Res<Style>("PillBtn"), Margin = new Thickness(0, 0, 8, 8) };
            b.SetResourceReference(BackgroundProperty, "SurfaceHi"); // the card itself is Surface
            b.Click += (_, _) => MainWindow.I?.Demo(key);
            demos.Children.Add(b);
        }
        demo.Children.Add(demos);

        var note = Dim("Lyrics come from Better Lyrics, or LRCLIB when Better Lyrics doesn't have the song; only the title, artist and length are sent, and only while the lyrics view is open. "
            + "Your city comes from Windows location, rounded to about 1 km before OpenStreetMap names it. Everything else stays on this PC.", 11);
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(4, 6, 4, 0);
        _body.Children.Add(note);
    }

    // A rounded card with an icon header; returns the panel to fill.
    StackPanel Section(string glyph, string title)
    {
        var icon = new TextBlock { Text = glyph, FontFamily = Res<FontFamily>("Icons"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentA");
        var head = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12),
            Children = { icon, new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold } },
        };
        var panel = new StackPanel { Children = { head } };
        var card = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(16, 14, 16, 8), Margin = new Thickness(0, 0, 0, 12), Child = panel };
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        _body.Children.Add(card);
        return panel;
    }

    // Dark / Light with a highlight that slides between them.
    FrameworkElement Segmented()
    {
        var grid = new Grid { Height = 40, Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var track = new Border { CornerRadius = new CornerRadius(12) };
        track.SetResourceReference(Border.BackgroundProperty, "SurfaceHi");
        Grid.SetColumnSpan(track, 2);
        grid.Children.Add(track);
        var slide = new TranslateTransform();
        var pill = new Border { CornerRadius = new CornerRadius(9), Margin = new Thickness(4), RenderTransform = slide };
        pill.SetResourceReference(Border.BackgroundProperty, "Accent");
        grid.Children.Add(pill);

        var labels = new List<TextBlock>();
        foreach (var (i, glyph, text) in new[] { (0, "", "Dark"), (1, "", "Light") })
        {
            var label = new TextBlock { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            label.Inlines.Add(new System.Windows.Documents.Run(glyph + "  ") { FontFamily = Res<FontFamily>("Icons") });
            label.Inlines.Add(new System.Windows.Documents.Run(text));
            Grid.SetColumn(label, i);
            labels.Add(label);
            var hit = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            Grid.SetColumn(hit, i);
            hit.MouseLeftButtonUp += (_, _) => { S.Light = i == 1; Theme.Apply(); Place(true); };
            grid.Children.Add(hit);
        }
        foreach (var l in labels) grid.Children.Add(l);

        void Place(bool animate)
        {
            var x = S.Light ? grid.ActualWidth / 2 : 0;
            if (animate) slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(420)) { EasingFunction = Pop });
            else { slide.BeginAnimation(TranslateTransform.XProperty, null); slide.X = x; }
            labels[0].SetResourceReference(TextBlock.ForegroundProperty, S.Light ? "FgDim" : "OnAccent");
            labels[1].SetResourceReference(TextBlock.ForegroundProperty, S.Light ? "OnAccent" : "FgDim");
        }
        grid.SizeChanged += (_, _) => { pill.Width = grid.ActualWidth / 2 - 8; pill.HorizontalAlignment = HorizontalAlignment.Left; Place(false); };
        return grid;
    }

    FrameworkElement Swatch((string Name, double Hue, double Spread, double Sat) p)
    {
        var mono = p.Sat < 0.05;
        var a = Theme.Hsl(p.Hue, p.Sat, mono ? 0.9 : 0.64);
        var b = Theme.Hsl(p.Hue + p.Spread, p.Sat, mono ? 0.45 : 0.58);
        var dot = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(14), Background = new LinearGradientBrush(a, b, 45),
            HorizontalAlignment = HorizontalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(),
        };
        dot.SetResourceReference(Border.BorderBrushProperty, "Fg");
        var item = new StackPanel
        {
            Width = 64, Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand, Background = Brushes.Transparent,
            Children = { dot, new TextBlock { Text = p.Name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) } },
        };
        item.MouseEnter += (_, _) => Scale(dot, 1.12);
        item.MouseLeave += (_, _) => Scale(dot, 1);
        item.MouseLeftButtonUp += (_, _) =>
        {
            // the sliders glide to the preset, so the whole theme morphs into it
            Glide(_hue, p.Hue);
            Glide(_spread, p.Spread);
            Glide(_sat, p.Sat * 100);
            Bounce(dot);
            MarkSwatch(p);
        };
        _swatches.Add((dot, p));
        if (Picked(p)) dot.BorderThickness = new Thickness(3);
        return item;
    }

    static bool Picked((string, double Hue, double Spread, double Sat) p) => Math.Abs(S.Hue - p.Hue) < 0.5 && Math.Abs(S.Spread - p.Spread) < 0.5 && Math.Abs(S.Sat - p.Sat) < 0.01;

    void MarkSwatch((string, double Hue, double Spread, double Sat) chosen)
    {
        foreach (var (dot, p) in _swatches) ((Border)dot).BorderThickness = new Thickness(p == chosen ? 3 : 0);
    }

    Slider Slider(Panel parent, string label, double min, double max, double value, Func<double, string> fmt, Action<double> set, Brush? track = null, double step = 1)
    {
        var val = new TextBlock { Text = fmt(value), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        val.SetResourceReference(TextBlock.ForegroundProperty, "FgDim");
        var head = new Grid { Children = { new TextBlock { Text = label, FontSize = 13 }, val } };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = step, IsSnapToTickEnabled = true, Margin = new Thickness(0, 2, 0, 10) };
        if (track != null) { slider.Background = track; slider.Foreground = Brushes.Transparent; } // the rainbow is its own fill
        slider.ValueChanged += (_, e) => { val.Text = fmt(e.NewValue); set(e.NewValue); };
        // clicking the track glides the thumb there instead of jumping
        slider.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && FindParent<Thumb>(d) != null) return;
            if (slider.Template.FindName("PART_Track", slider) is not Track t) return;
            var v = Math.Round(t.ValueFromPoint(e.GetPosition(t)) / step) * step;
            Glide(slider, Math.Clamp(v, min, max));
            e.Handled = true;
        };
        parent.Children.Add(head);
        parent.Children.Add(slider);
        return slider;
    }

    static void Glide(Slider s, double to)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(380)) { EasingFunction = EaseOut };
        a.Completed += (_, _) => { s.Value = to; s.BeginAnimation(RangeBase.ValueProperty, null); };
        s.BeginAnimation(RangeBase.ValueProperty, a);
    }

    void Toggle(Panel parent, string label, string? sub, bool value, Action<bool> set)
    {
        var sw = new ToggleButton { IsChecked = value, Style = Res<Style>("Switch"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        sw.Click += (_, _) => { set(sw.IsChecked == true); Store.Save(); };
        var text = new StackPanel { Margin = new Thickness(0, 0, 56, 0), Children = { new TextBlock { Text = label, FontSize = 13 } } };
        if (sub != null)
        {
            var s = Dim(sub, 11);
            s.TextWrapping = TextWrapping.Wrap;
            text.Children.Add(s);
        }
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12), Background = Brushes.Transparent, Children = { text, sw } };
        row.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not DependencyObject d || FindParent<ToggleButton>(d) == null) { sw.IsChecked = !sw.IsChecked; set(sw.IsChecked == true); Store.Save(); } };
        parent.Children.Add(row);
    }

    static TextBlock Dim(string text, double size)
    {
        var t = new TextBlock { Text = text, FontSize = size };
        t.SetResourceReference(TextBlock.ForegroundProperty, "FgDim");
        return t;
    }

    static void Scale(FrameworkElement e, double to)
    {
        var s = (ScaleTransform)e.RenderTransform;
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(260)) { EasingFunction = Pop };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    static void Bounce(FrameworkElement e)
    {
        var s = (ScaleTransform)e.RenderTransform;
        var a = new DoubleAnimation(0.8, 1.12, TimeSpan.FromMilliseconds(420)) { EasingFunction = Pop };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null && d is not T) d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    static Brush Rainbow()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int i = 0; i <= 6; i++) g.GradientStops.Add(new GradientStop(Theme.Hsl(i * 60, 0.9, 0.6), i / 6.0));
        return g;
    }

    static T Res<T>(string key) => (T)Application.Current.Resources[key];
}
