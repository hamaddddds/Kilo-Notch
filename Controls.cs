using System.Windows;
using System.Windows.Media;

namespace Notch;

/// Damped spring stepped once per rendered frame. Retargeting keeps the current velocity,
/// so an interrupted open/close bends smoothly instead of restarting.
public class Spring
{
    public double Value, Velocity, Target;
    public double Stiffness = 400, Damping = 30;
    public double Epsilon = 0.05;

    public Spring(double value = 0) { Value = Target = value; }

    public bool Settled => Math.Abs(Value - Target) < Epsilon && Math.Abs(Velocity) < Epsilon;

    public void Step(double dt)
    {
        // small fixed substeps keep stiff springs stable at any frame rate
        while (dt > 0)
        {
            var h = Math.Min(dt, 1 / 240.0);
            Velocity += (Stiffness * (Target - Value) - Damping * Velocity) * h;
            Value += Velocity * h;
            dt -= h;
        }
        if (Settled) { Value = Target; Velocity = 0; }
    }

    public void Tune(double stiffness, double damping) { Stiffness = stiffness; Damping = damping; }
}

/// Corner radius for the shared button template.
public static class Ui
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached("Radius", typeof(CornerRadius), typeof(Ui),
        new FrameworkPropertyMetadata(new CornerRadius(10)));
    public static CornerRadius GetRadius(DependencyObject d) => (CornerRadius)d.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject d, CornerRadius v) => d.SetValue(RadiusProperty, v);
}

/// Light inside the notch while files hover over it: an ambient wash plus a pool of light rising from the
/// bottom edge under the cursor. On drop the pool swells and a soft wave rolls out along the bottom.
/// Everything fades toward the top so the text above stays readable. The owner sets the fields per frame.
public class DropGlow : FrameworkElement
{
    public Rect Body;            // the notch body, in this element's coordinates
    public double Hover, Rise, Wave = -1, X; // Wave: 0..1 progress of the drop wave, -1 = none
    public Color A, B;

    public DropGlow() => IsHitTestVisible = false;

    protected override void OnRender(DrawingContext dc)
    {
        double ambient = 0.26 * Hover + 0.34 * Rise, pool = 0.42 * Hover + 0.3 * Rise;
        if ((ambient < 0.004 && pool < 0.004 && Wave < 0) || Body.Width <= 0) return;
        var b = Body;
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };
        fade.GradientStops.Add(new GradientStop(Colors.Black, 0));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(140, 0, 0, 0), 0.42));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(20, 0, 0, 0), 0.78));
        fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        dc.PushOpacityMask(fade);
        dc.DrawRectangle(Brushes.Transparent, null, b); // pins the mask to the body's box

        if (ambient > 0.004)
            dc.DrawRectangle(new LinearGradientBrush(Alpha(A, ambient), Alpha(B, ambient), 0), null, b);

        var t = Math.Clamp((X - b.X) / b.Width, 0, 1);
        var c = Mix(A, B, t);
        if (pool > 0.004)
        {
            var r = b.Width * (0.4 + 0.15 * Rise);
            var g = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = new Point(X, b.Bottom + 18), GradientOrigin = new Point(X, b.Bottom + 18), RadiusX = r, RadiusY = r };
            g.GradientStops.Add(new GradientStop(Alpha(Mix(c, Colors.White, 0.2), pool), 0));
            g.GradientStops.Add(new GradientStop(Alpha(c, pool * 0.45), 0.55));
            g.GradientStops.Add(new GradientStop(Alpha(c, 0), 1));
            dc.DrawRectangle(g, null, b);
        }

        if (Wave >= 0 && Wave < 1)
        {
            var q = 1 - Math.Pow(1 - Wave, 3); // races out, then eases
            double radius = 24 + b.Width * q, band = 30, a = 0.24 * (1 - q), outer = radius + band;
            var g = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = new Point(X, b.Bottom - 14), GradientOrigin = new Point(X, b.Bottom - 14), RadiusX = outer, RadiusY = outer };
            var light = Mix(c, Colors.White, 0.7);
            g.GradientStops.Add(new GradientStop(Alpha(light, 0), Math.Max(0, radius - band) / outer));
            g.GradientStops.Add(new GradientStop(Alpha(light, a), radius / outer));
            g.GradientStops.Add(new GradientStop(Alpha(light, 0), 1));
            dc.DrawRectangle(g, null, b);
        }
        dc.Pop();
    }

    static Color Alpha(Color c, double a) => Color.FromArgb((byte)(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);
    static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}

/// Progress ring: a faint full circle with the value drawn on top, clockwise from 12 o'clock.
public class Ring : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(Ring),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(nameof(Brush), typeof(Brush), typeof(Ring),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush Brush { get => (Brush)GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
    public double Thickness { get; set; } = 3;

    protected override void OnRender(DrawingContext dc)
    {
        var r = Math.Min(ActualWidth, ActualHeight) / 2 - Thickness / 2;
        if (r <= 0) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var pen = new Pen(Brush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.PushOpacity(0.25);
        dc.DrawEllipse(null, pen, c, r, r);
        dc.Pop();

        var v = Math.Clamp(Value, 0, 1);
        if (v <= 0.001) return;
        if (v >= 0.999) { dc.DrawEllipse(null, pen, c, r, r); return; }
        var a = v * 2 * Math.PI;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X, c.Y - r), false, false);
            ctx.ArcTo(new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a)), new Size(r, r), 0, v > 0.5, SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, pen, g);
    }
}

/// Little equalizer bars. Static as an icon; the owner bumps Phase/Level to make it dance.
public class Wave : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(nameof(Level), typeof(double), typeof(Wave),
        new FrameworkPropertyMetadata(0.7, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(nameof(Brush), typeof(Brush), typeof(Wave),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public Brush Brush { get => (Brush)GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
    public double Phase { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        const int bars = 5;
        var w = ActualWidth / (bars * 2 - 1);
        // one geometry for all bars, so a gradient brush runs across the whole wave
        var g = new GeometryGroup();
        for (int i = 0; i < bars; i++)
        {
            var k = 0.3 + 0.7 * Math.Abs(Math.Sin(Phase * (0.9 + i * 0.23) + i * 1.9));
            var h = Math.Clamp(ActualHeight * Level * k, w, ActualHeight);
            g.Children.Add(new RectangleGeometry(new Rect(i * 2 * w, (ActualHeight - h) / 2, w, h), w / 2, w / 2));
        }
        dc.DrawGeometry(Brush, null, g);
    }
}
