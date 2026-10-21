using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Notch;

public partial class WaterPage : UserControl
{
    static Settings S => Store.D.Settings;
    static readonly IEasingFunction Pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
    static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    // animated stand-in for the liters number, so it counts up instead of jumping
    static readonly DependencyProperty ShownProperty = DependencyProperty.Register("Shown", typeof(double), typeof(WaterPage),
        new PropertyMetadata(0.0, (d, e) => ((WaterPage)d).Amount.Text = ((double)e.NewValue / 1000).ToString("0.##")));

    int[] _bars = Array.Empty<int>();

    public WaterPage()
    {
        InitializeComponent();
        Amount.Text = "0";
        Water.Changed += Refresh;
        IsVisibleChanged += (_, _) => { if (IsVisible) { _bars = Array.Empty<int>(); Refresh(); } }; // regrow the chart on show
        Refresh();
    }

    void Refresh()
    {
        var total = Water.Total;
        var goal = Math.Max(1, S.WaterGoal);
        var pct = Math.Min(100, total * 100 / goal);
        BeginAnimation(ShownProperty, new DoubleAnimation(total, TimeSpan.FromMilliseconds(700)) { EasingFunction = EaseOut });
        Goal.Text = $" / {goal / 1000.0:0.##} L";
        Pct.Text = $"{pct}%";
        Note.Text = pct >= 100 ? " · Goal reached" : $" · {goal - total} ml to go";
        FillScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(pct / 100.0, TimeSpan.FromMilliseconds(700)) { EasingFunction = Pop });
        Remind.IsChecked = S.WaterRemind;
        DrawChart();
    }

    // One bar per active hour, scaled to the busiest hour (at least 400 ml). Changed bars grow in.
    void DrawChart()
    {
        Chart.Children.Clear();
        int from = S.WaterFrom, to = Math.Max(S.WaterTo, S.WaterFrom + 1);
        var ml = Enumerable.Range(from, to - from).Select(h => Water.Today.Where(s => s.At.Hour == h).Sum(s => s.Ml)).ToArray();
        var fresh = _bars.Length != ml.Length;
        var max = Math.Max(400, ml.Max());
        double w = 112, h = 80, slot = w / ml.Length, bar = Math.Min(9, slot * 0.62);
        for (int i = 0; i < ml.Length; i++)
        {
            var x = i * slot + (slot - bar) / 2;
            Add(Pill(bar, h, "Surface"), x, 0);
            if (ml[i] > 0)
            {
                var bh = Math.Max(bar, h * ml[i] / max);
                var b = Pill(bar, bh, "AccentV");
                if (fresh || ml[i] != _bars[i])
                {
                    var grow = new ScaleTransform(1, 0);
                    b.RenderTransformOrigin = new Point(0.5, 1);
                    b.RenderTransform = grow;
                    grow.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600)) { BeginTime = TimeSpan.FromMilliseconds(fresh ? 30 * i : 0), EasingFunction = Pop });
                }
                Add(b, x, h - bh);
            }
            if ((from + i) % 3 == 0) Add(Label($"{from + i:00}"), x - 3, h + 3);
        }
        Add(Label($"{max}"), w + 6, -7);
        Add(Label($"{max / 2}"), w + 6, h / 2 - 7);
        Add(Label("0 ml"), w + 6, h - 7);
        _bars = ml;
    }

    void Add(UIElement e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); Chart.Children.Add(e); }

    static Border Pill(double w, double h, string brush)
    {
        var b = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(w / 2) };
        b.SetResourceReference(Border.BackgroundProperty, brush);
        return b;
    }

    static TextBlock Label(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 10 };
        t.SetResourceReference(TextBlock.ForegroundProperty, "FgDim");
        return t;
    }

    void DrinkClick(object sender, RoutedEventArgs e)
    {
        // the drop hops and splashes
        var hop = new DoubleAnimationUsingKeyFrames();
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(-7, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)), EaseOut));
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), new BounceEase { Bounces = 2, Bounciness = 3 }));
        DropMove.BeginAnimation(TranslateTransform.YProperty, hop);
        var splash = new DoubleAnimation(1.35, 1, TimeSpan.FromMilliseconds(500)) { BeginTime = TimeSpan.FromMilliseconds(380), EasingFunction = Pop };
        DropScale.BeginAnimation(ScaleTransform.ScaleXProperty, splash);
        DropScale.BeginAnimation(ScaleTransform.ScaleYProperty, splash);
        Water.Add();
    }

    void UndoClick(object sender, MouseButtonEventArgs e) { Water.Undo(); e.Handled = true; }
    void RemindClick(object sender, RoutedEventArgs e) { S.WaterRemind = Remind.IsChecked == true; Store.Save(); }
    void OpenSettings(object sender, RoutedEventArgs e) => SettingsWindow.Open();
}
