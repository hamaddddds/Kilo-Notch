using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace Notch;

/// Files kept close at hand. Items are references to the originals, not copies.
public partial class ShelfPage : UserControl
{
    static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico" };
    static readonly IEasingFunction Pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
    static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    static List<string> Saved => Store.D.Shelf;
    readonly HashSet<string> _fresh = new(StringComparer.OrdinalIgnoreCase); // tiles that should pop in

    public ShelfPage()
    {
        InitializeComponent();
        Refresh();
        IsVisibleChanged += (_, _) => { if (IsVisible && Saved.Count == 0) BobIcon(); };
    }

    // ---------- shelf contents ----------

    public void Add(IEnumerable<string> paths)
    {
        foreach (var p in paths)
            if (!Saved.Contains(p, StringComparer.OrdinalIgnoreCase)) { Saved.Add(p); _fresh.Add(p); }
        Store.Save();
        Refresh();
    }

    void Remove(string path, FrameworkElement? tile = null)
    {
        if (tile == null) { Saved.Remove(path); Store.Save(); Refresh(); return; }
        // shrink away, then drop it from the list
        var s = (ScaleTransform)tile.RenderTransform;
        var shrink = new DoubleAnimation(0.4, TimeSpan.FromMilliseconds(180)) { EasingFunction = EaseOut };
        shrink.Completed += (_, _) => { Saved.Remove(path); Store.Save(); Refresh(); };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        tile.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)));
    }

    void Clear(object sender, RoutedEventArgs e) { Saved.Clear(); Store.Save(); Refresh(); }

    void Refresh()
    {
        Items.Children.Clear();
        var n = 0;
        foreach (var p in Saved) Items.Children.Add(Tile(p, _fresh.Contains(p) ? n++ : -1));
        _fresh.Clear();
        var any = Saved.Count > 0;
        Empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        Full.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        Count.Text = $"{Saved.Count} item{(Saved.Count == 1 ? "" : "s")} · drag out or click to open";
    }

    void BobIcon()
    {
        var bob = new DoubleAnimation(0, 4, TimeSpan.FromMilliseconds(500)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2), EasingFunction = new SineEase() };
        ((TranslateTransform)EmptyIcon.RenderTransform).BeginAnimation(TranslateTransform.YProperty, bob);
    }

    FrameworkElement Tile(string path, int popIndex)
    {
        var name = new TextBlock { Text = Path.GetFileName(path.TrimEnd('\\')), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
        var size = new TextBlock { Text = SizeText(path), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center };
        size.SetResourceReference(TextBlock.ForegroundProperty, "FgDim");
        var thumb = new Border { Width = 40, Height = 36, CornerRadius = new CornerRadius(6) };
        RenderOptions.SetBitmapScalingMode(thumb, BitmapScalingMode.HighQuality);
        LoadThumb(path, thumb);
        var remove = new Button
        {
            Content = "", Style = (Style)FindResource("CircleBtn"), Width = 18, Height = 18, FontSize = 8,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, -2, 0),
            Opacity = 0, IsHitTestVisible = false,
        };
        Ui.SetRadius(remove, new CornerRadius(6));
        var box = new Border
        {
            Width = 78, Padding = new Thickness(6, 8, 6, 6), Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(12),
            Cursor = Cursors.Hand, ToolTip = path, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(),
            Child = new Grid { Children = { new StackPanel { Children = { thumb, name, size } }, remove } },
        };
        remove.Click += (_, _) => Remove(path, box);
        box.SetResourceReference(Border.BackgroundProperty, "SurfaceHi");

        // hover: lift the tile and reveal the remove button
        box.MouseEnter += (_, _) => { Scale(box, 1.06, Pop, 260); remove.IsHitTestVisible = true; remove.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150))); };
        box.MouseLeave += (_, _) => { Scale(box, 1, EaseOut, 220); remove.IsHitTestVisible = false; remove.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150))); };

        // press + move = drag the file out; press + release = open it
        Point down = default;
        var pressed = false;
        box.PreviewMouseLeftButtonDown += (_, e) => { down = e.GetPosition(box); pressed = true; Scale(box, 0.96, EaseOut, 120); };
        box.MouseMove += (_, e) =>
        {
            if (!pressed || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(box);
            if (Math.Abs(p.X - down.X) + Math.Abs(p.Y - down.Y) < 6) return;
            pressed = false;
            DragOut(path, box);
        };
        box.MouseLeftButtonUp += (_, _) =>
        {
            Scale(box, 1.06, Pop, 260);
            if (pressed) { pressed = false; Open(path); }
        };

        if (popIndex >= 0) // new tile: pop in, staggered
        {
            var delay = TimeSpan.FromMilliseconds(60 * popIndex);
            var grow = new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(520)) { BeginTime = delay, EasingFunction = Pop };
            ((ScaleTransform)box.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            ((ScaleTransform)box.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            box.Opacity = 0;
            box.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { BeginTime = delay });
        }
        return box;
    }

    static void Scale(FrameworkElement e, double to, IEasingFunction ease, int ms)
    {
        var s = (ScaleTransform)e.RenderTransform;
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    // The tile stays behind as a ghost while a photo-card preview rides under the cursor.
    void DragOut(string path, Border tile)
    {
        tile.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, TimeSpan.FromMilliseconds(150)));
        Scale(tile, 0.92, EaseOut, 150);
        var preview = PreviewCard(path, out var grab);
        if (!DragShell.DragOut(MainWindow.I.Handle, new[] { path }, preview, grab))
            DragDrop.DoDragDrop(tile, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy | DragDropEffects.Link);
        tile.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        Scale(tile, 1, Pop, 420);
    }

    void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Win32Exception) { if (!File.Exists(path) && !Directory.Exists(path)) Remove(path); }
    }

    void ScrollSideways(object sender, MouseWheelEventArgs e)
    {
        Scroll.ScrollToHorizontalOffset(Scroll.HorizontalOffset - e.Delta / 2.0);
        e.Handled = true;
    }

    // ---------- files hovering over the notch ----------

    public void DragPreview(string[] files)
    {
        // the overlay replaces the shelf while files hover, so their texts never stack on top of each other
        foreach (var under in new UIElement[] { Empty, Full })
            under.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)));
        DropZone.Visibility = Visibility.Visible;
        DropZone.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        var zs = (ScaleTransform)DropZone.RenderTransform;
        var zoom = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = Pop };
        zs.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
        zs.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
        Ants.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(0, -18, TimeSpan.FromMilliseconds(600)) { RepeatBehavior = RepeatBehavior.Forever });
        ZoneFill.BeginAnimation(OpacityProperty, new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(800)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        DropInfo.Text = files.Length == 1 ? Path.GetFileName(files[0].TrimEnd('\\')) : $"{files.Length} files";

        // up to three cards, fanned out, popping in one after another
        Cards.Children.Clear();
        var shown = files.Take(3).ToArray();
        var mid = (shown.Length - 1) / 2.0;
        for (int i = 0; i < shown.Length; i++)
        {
            var card = new Border
            {
                Width = 78, Height = 78, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 4, Direction = 270, Opacity = 0.45, RenderingBias = RenderingBias.Performance },
                RenderTransformOrigin = new Point(0.5, 0.9),
            };
            card.SetResourceReference(Border.BackgroundProperty, "SurfaceHi");
            RenderOptions.SetBitmapScalingMode(card, BitmapScalingMode.HighQuality);
            LoadThumb(shown[i], card, 160);
            Canvas.SetLeft(card, 21 + (i - mid) * 12);
            Canvas.SetTop(card, 11);
            var scale = new ScaleTransform();
            var rot = new RotateTransform();
            card.RenderTransform = new TransformGroup { Children = { scale, rot } };
            var delay = TimeSpan.FromMilliseconds(70 * i);
            var grow = new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(520)) { BeginTime = delay, EasingFunction = Pop };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, (i - mid) * 10, TimeSpan.FromMilliseconds(600)) { BeginTime = delay, EasingFunction = Pop });
            card.Opacity = 0;
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { BeginTime = delay });
            Cards.Children.Add(card);
        }
    }

    // The card stack leans toward the cursor.
    public void DragMove(Point p)
    {
        if (DropZone.Visibility != Visibility.Visible) return;
        var dx = Math.Clamp(p.X - ActualWidth / 2, -200, 200);
        var dy = Math.Clamp(p.Y - ActualHeight / 2, -80, 80);
        var d = TimeSpan.FromMilliseconds(220);
        CardsMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx * 0.1, d) { EasingFunction = EaseOut });
        CardsMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy * 0.12, d) { EasingFunction = EaseOut });
        CardsTilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(dx * 0.03, d) { EasingFunction = EaseOut });
    }

    public void DragCancel() => HideZone(TimeSpan.Zero);

    // Cards dive into the shelf, then the new tiles pop in. (The light wave lives in the notch itself.)
    public void DropFiles(string[] files)
    {
        foreach (Border card in Cards.Children)
        {
            var group = (TransformGroup)card.RenderTransform;
            var shrink = new DoubleAnimation(0.25, TimeSpan.FromMilliseconds(300)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseIn, Amplitude = 0.4 } };
            ((ScaleTransform)group.Children[0]).BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            ((ScaleTransform)group.Children[0]).BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(80) });
        }
        CardsMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(40, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        HideZone(TimeSpan.FromMilliseconds(220));
        var later = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        later.Tick += (_, _) => { later.Stop(); Add(files); };
        later.Start();
    }

    void HideZone(TimeSpan delay)
    {
        // bring the shelf's own content back once the overlay is gone
        foreach (var under in new UIElement[] { Empty, Full })
            under.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)) { BeginTime = delay + TimeSpan.FromMilliseconds(120) });
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { BeginTime = delay };
        fade.Completed += (_, _) =>
        {
            DropZone.Visibility = Visibility.Collapsed;
            Ants.BeginAnimation(Shape.StrokeDashOffsetProperty, null); // stop the endless animations
            ZoneFill.BeginAnimation(OpacityProperty, null);
            CardsMove.BeginAnimation(TranslateTransform.XProperty, null);
            CardsMove.BeginAnimation(TranslateTransform.YProperty, null);
            CardsTilt.BeginAnimation(RotateTransform.AngleProperty, null);
        };
        DropZone.BeginAnimation(OpacityProperty, fade);
    }

    // ---------- thumbnails ----------

    static bool IsImage(string path) => ImageExt.Contains(Path.GetExtension(path).ToLowerInvariant());

    // Photos decode off the UI thread and fade in; everything else gets its shell icon.
    static async void LoadThumb(string path, Border target, int width = 80)
    {
        if (!IsImage(path))
        {
            var icon = Icon(path);
            if (icon != null) target.Child = new Image { Source = icon, Width = 32, Height = 32 };
            return;
        }
        var bmp = await Task.Run(() => Decode(path, width));
        if (bmp == null) return;
        target.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
        target.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(250)));
    }

    static BitmapSource? Decode(string path, int width)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.DecodePixelWidth = width;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception) { return null; } // unreadable or unsupported image
    }

    // Renders the card that follows the cursor when a file is dragged out: the photo itself, or icon + name.
    static BitmapSource PreviewCard(string path, out Point grab)
    {
        const double box = 168, card = 112;
        var face = new Border
        {
            Width = card, Height = card, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(3),
            BorderBrush = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22)),
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 6, Direction = 270, Opacity = 0.5 },
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-5),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.HighQuality);
        var photo = IsImage(path) ? Decode(path, 260) : null;
        if (photo != null) face.Background = new ImageBrush(photo) { Stretch = Stretch.UniformToFill };
        else
        {
            face.Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new Image { Source = Icon(path), Width = 40, Height = 40 },
                    new TextBlock { Text = Path.GetFileName(path.TrimEnd('\\')), Foreground = Brushes.White, FontSize = 11, Margin = new Thickness(8, 6, 8, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center },
                },
            };
        }
        var root = new Grid { Width = box, Height = box, Children = { face } };
        root.Measure(new Size(box, box));
        root.Arrange(new Rect(0, 0, box, box));
        root.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(MainWindow.I);
        var bmp = new RenderTargetBitmap((int)(box * dpi.DpiScaleX), (int)(box * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(root);
        bmp.Freeze();
        grab = new Point(bmp.PixelWidth / 2.0, bmp.PixelHeight * 0.42);
        return bmp;
    }

    static ImageSource? Icon(string path)
    {
        try
        {
            var info = new SHFILEINFO();
            if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100 /* SHGFI_ICON */) == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            DestroyIcon(info.hIcon);
            src.Freeze();
            return src;
        }
        catch (Exception) { return null; } // the tile just shows without an icon
    }

    static string SizeText(string path)
    {
        if (Directory.Exists(path)) return "Folder";
        try
        {
            double b = new FileInfo(path).Length;
            string[] units = { "B", "KB", "MB", "GB" };
            var i = 0;
            while (b >= 1024 && i < units.Length - 1) { b /= 1024; i++; }
            return $"{b:0.#} {units[i]}";
        }
        catch (IOException) { return ""; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
}
