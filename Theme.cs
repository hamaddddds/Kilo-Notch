using System.Windows;
using System.Windows.Media;

namespace Notch;

/// Swaps the app-wide brushes; everything else reads them through DynamicResource.
public static class Theme
{
    public static Color A { get; private set; }
    public static Color B { get; private set; }
    public static Color Fg { get; private set; }
    public static event Action? Changed;

    public static readonly (string Name, double Hue, double Spread, double Sat)[] Presets =
    {
        ("Purple", 265, 50, 0.9), ("Ocean", 195, 45, 0.9), ("Sunset", 335, 55, 0.95),
        ("Mint", 150, 40, 0.75), ("Aurora", 140, 150, 0.85), ("Mono", 0, 0, 0),
    };

    public static void Apply()
    {
        var s = Store.D.Settings;
        var mono = s.Sat < 0.05;
        A = Hsl(s.Hue, s.Sat, mono ? (s.Light ? 0.2 : 0.92) : s.Light ? 0.52 : 0.64);
        B = Hsl(s.Hue + s.Spread, s.Sat, mono ? 0.5 : s.Light ? 0.48 : 0.58);
        Fg = s.Light ? Rgb(0x111114) : Colors.White;

        var r = Application.Current.Resources;
        r["NotchBg"] = Solid(s.Light ? Rgb(0xF4F4F7) : Colors.Black);
        r["Fg"] = Solid(Fg);
        r["FgDim"] = Solid(s.Light ? Rgb(0x6E6E76) : Rgb(0x8E8E96));
        r["Surface"] = Solid(s.Light ? Rgb(0xE4E4EA) : Rgb(0x1E1E22));
        r["SurfaceHi"] = Solid(s.Light ? Rgb(0xD2D2DA) : Rgb(0x2E2E35));
        r["Hover"] = Solid(s.Light ? Color.FromArgb(0x14, 0, 0, 0) : Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
        r["AccentA"] = Solid(A);
        r["AccentB"] = Solid(B);
        r["AccentSoft"] = Solid(Color.FromArgb(0x48, A.R, A.G, A.B));
        r["Accent"] = Frozen(new LinearGradientBrush(A, B, 0));
        r["AccentV"] = Frozen(new LinearGradientBrush(A, B, 90));

        // Text on colored backgrounds picks black or white by how bright the background really is,
        // so a white accent (Mono, pale hues) never ends up with white text on it.
        Color dark = Rgb(0x111114), bg = s.Light ? Rgb(0xF4F4F7) : Colors.Black;
        r["OnAccent"] = Solid((Luma(A) + Luma(B)) / 2 > 0.6 ? dark : Colors.White);
        r["OnAccentSoft"] = Solid(Luma(Mix(bg, A, 0x48 / 255.0)) > 0.55 ? dark : Fg);
        Changed?.Invoke();
    }

    /// Perceived brightness 0..1.
    public static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
    static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    static Brush Solid(Color c) => Frozen(new SolidColorBrush(c));
    static Brush Frozen(Brush b) { b.Freeze(); return b; }
    static Color Rgb(int v) => Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);

    public static Color Hsl(double h, double s, double l)
    {
        h = (h % 360 + 360) % 360 / 360;
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        byte C(double t)
        {
            t = t < 0 ? t + 1 : t > 1 ? t - 1 : t;
            var v = t < 1 / 6.0 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6 : p;
            return (byte)Math.Round(v * 255);
        }
        return Color.FromRgb(C(h + 1 / 3.0), C(h), C(h - 1 / 3.0));
    }
}
