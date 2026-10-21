using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Notch;

public enum Mood { Normal, Happy, Sad, Drowsy, Asleep, Wink }

/// Kilo, the notch's face: two glowing eyes with a handful of moods. The owner moves Gaze (the eyes)
/// and Bob/Tilt (the whole face, for hops and dancing); Mood swaps the eye shapes with a quick squint.
public class Kilo : Grid
{
    static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    static readonly IEasingFunction Pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };

    public readonly TranslateTransform Gaze = new(), Bob = new();
    public readonly RotateTransform Tilt = new();
    readonly ScaleTransform _blink = new(), _swap = new();
    readonly Grid _eyes = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly Dictionary<Mood, FrameworkElement> _faces = new();
    readonly FrameworkElement _tear;
    readonly TextBlock _z = new() { Text = "z", FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, -16, 6), Opacity = 0, RenderTransform = new TranslateTransform() };
    Mood _mood = Mood.Normal;

    public Kilo()
    {
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = new TransformGroup { Children = { Tilt, Bob } };
        _eyes.RenderTransformOrigin = new Point(0.5, 0.5);
        _eyes.RenderTransform = new TransformGroup { Children = { _swap, Gaze } };
        _eyes.Effect = new DropShadowEffect { Color = Colors.White, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.8, RenderingBias = RenderingBias.Performance };

        var open = Pair(Pill(15), Pill(15), 14);
        open.RenderTransformOrigin = new Point(0.5, 0.5);
        open.RenderTransform = _blink;
        _faces[Mood.Normal] = open;
        _faces[Mood.Drowsy] = open;
        _faces[Mood.Happy] = Pair(Stroke("M0,8 Q5.5,-3 11,8"), Stroke("M0,8 Q5.5,-3 11,8"), 12);
        _faces[Mood.Asleep] = Pair(Stroke("M0,2 Q5.5,10 11,2"), Stroke("M0,2 Q5.5,10 11,2"), 12);
        _faces[Mood.Wink] = Pair(Stroke("M1,1.5 L9.5,7.5 L1,13.5"), Pill(15), 13); // one eye squeezed shut, aiming
        // sad: brows lifted toward the middle, smaller eyes, a tear that slides down
        _tear = new Path { Data = Geometry.Parse("M3,0 C3,0 6,4 6,6 A3,3 0 1 1 0,6 C0,4 3,0 3,0 Z"), Fill = new SolidColorBrush(Color.FromRgb(0x8F, 0xD3, 0xFF)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1, 0, 0, -8), Opacity = 0, RenderTransform = new TranslateTransform() };
        _faces[Mood.Sad] = new Grid
        {
            Children =
            {
                Pair(new StackPanel { Children = { Stroke("M0,4.5 L9,0.5", 2.2), Pill(11, 2) } },
                     new StackPanel { Children = { Stroke("M0,0.5 L9,4.5", 2.2), Pill(11, 2) } }, 14),
                _tear,
            },
        };
        foreach (var f in _faces.Values.Distinct())
        {
            f.Visibility = f == open ? Visibility.Visible : Visibility.Collapsed;
            _eyes.Children.Add(f);
        }
        Children.Add(_eyes);
        Children.Add(_z);
        _z.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
    }

    /// The eyes' box, for working out where the cursor is relative to them.
    public FrameworkElement Eyes => _eyes;

    public Mood Mood
    {
        get => _mood;
        set
        {
            if (value == _mood) return;
            var was = _faces[_mood];
            _mood = value;
            var now = _faces[value];
            if (now != was)
            {
                was.Visibility = Visibility.Collapsed;
                now.Visibility = Visibility.Visible;
                _swap.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = Pop });
            }
            _blink.BeginAnimation(ScaleTransform.ScaleYProperty, value == Mood.Drowsy ? new DoubleAnimation(0.3, TimeSpan.FromMilliseconds(700)) { EasingFunction = EaseOut } : null);
            Loop(_tear, value == Mood.Sad, 6, 12, 1.6);
            Loop(_z, value == Mood.Asleep, 4, -9, 2);
        }
    }

    public void Blink(bool twice = false)
    {
        if (_mood != Mood.Normal) return;
        var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)), EaseOut));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(190)), EaseOut));
        if (twice)
        {
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(270)), EaseOut));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380)), EaseOut));
        }
        _blink.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    // a drifting, fading element (tear, z) that repeats while the mood lasts, at a low frame rate
    static void Loop(FrameworkElement e, bool on, double from, double to, double seconds)
    {
        var move = (TranslateTransform)e.RenderTransform;
        if (!on)
        {
            move.BeginAnimation(TranslateTransform.YProperty, null);
            e.BeginAnimation(OpacityProperty, null);
            e.Opacity = 0;
            return;
        }
        var d = TimeSpan.FromSeconds(seconds);
        var y = new DoubleAnimation(from, to, d) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn } };
        var o = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = d };
        o.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.2)));
        o.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        Timeline.SetDesiredFrameRate(y, 24);
        Timeline.SetDesiredFrameRate(o, 24);
        move.BeginAnimation(TranslateTransform.YProperty, y);
        e.BeginAnimation(OpacityProperty, o);
    }

    static StackPanel Pair(FrameworkElement left, FrameworkElement right, double gap)
    {
        left.Margin = new Thickness(left.Margin.Left, left.Margin.Top, gap, left.Margin.Bottom);
        return new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { left, right } };
    }

    static Border Pill(double h, double top = 0)
    {
        var b = new Border { Width = 9, Height = h, CornerRadius = new CornerRadius(4.5), Margin = new Thickness(0, top, 0, 0) };
        b.SetResourceReference(Border.BackgroundProperty, "Fg");
        return b;
    }

    static Path Stroke(string data, double thickness = 3)
    {
        var p = new Path { Data = Geometry.Parse(data), StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, HorizontalAlignment = HorizontalAlignment.Center };
        p.SetResourceReference(Shape.StrokeProperty, "Fg");
        return p;
    }
}
