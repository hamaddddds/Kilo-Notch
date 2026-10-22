using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Notch;

/// Kilo's own window chrome and file browser. No stock Windows dialogs.
public sealed class KiloDialog : Window
{
    readonly DockPanel _body = new() { Margin = new Thickness(24) };
    internal DockPanel Body => _body;
    public KiloDialog(string title, double width = 560, double height = 280)
    {
        Title = title; Width = width; Height = height; WindowStyle = WindowStyle.None;
        AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false; Topmost = true;
        FontFamily = (FontFamily)Application.Current.Resources["Ui"];
        SetResourceReference(ForegroundProperty, "Fg");
        Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), Application.Current.Resources["ThinBar"]);
        var shell = new Border { CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1), Child = _body };
        shell.SetResourceReference(Border.BackgroundProperty, "NotchBg"); shell.SetResourceReference(Border.BorderBrushProperty, "SurfaceHi");
        Content = shell;
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 20), Background = Brushes.Transparent };
        var close = Button("×", () => Close()); close.Width = 32; close.Padding = new Thickness(0);
        DockPanel.SetDock(close, Dock.Right); top.Children.Add(close);
        top.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        top.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is TextBlock) DragMove(); };
        DockPanel.SetDock(top, Dock.Top); _body.Children.Add(top);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }
    static Button Button(string text, Action action, bool accent = false)
    {
        var b = new Button { Content = text, Style = (Style)Application.Current.Resources[accent ? "AccentBtn" : "PillBtn"], Focusable = true };
        b.Click += (_, _) => action(); return b;
    }
    public static void Notice(string title, string message)
    {
        var dialog = new KiloDialog(title);
        var done = Button("Got it", () => dialog.Close(), true); done.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(done, Dock.Bottom); dialog._body.Children.Add(done);
        dialog._body.Children.Add(new TextBlock { Text = message, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 20) });
        dialog.ShowDialog();
    }
    public static string[] PickFiles()
    {
        var dialog = new KiloDialog("A little space for your files", 720, 520);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = Array.Empty<string>();
        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var count = new TextBlock { Text = "Choose files to keep on your shelf", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        count.SetResourceReference(TextBlock.ForegroundProperty, "FgDim");
        var add = Button("Add to shelf", () => { result = selected.Where(File.Exists).ToArray(); dialog.Close(); }, true);
        DockPanel.SetDock(add, Dock.Right); footer.Children.Add(add); footer.Children.Add(count);
        DockPanel.SetDock(footer, Dock.Bottom); dialog._body.Children.Add(footer);
        var nav = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var path = new TextBox { Style = (Style)Application.Current.Resources["Field"], Padding = new Thickness(10, 8, 10, 8), ToolTip = "Folder path · press Enter to open" };
        path.SetResourceReference(BackgroundProperty, "Surface");
        var list = new StackPanel();
        var current = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var generation = 0;
        async void Browse(string directory)
        {
            int request = ++generation;
            count.Text = "Opening folder…";
            try
            {
                var entries = await Task.Run(() => new DirectoryInfo(Path.GetFullPath(directory)).EnumerateFileSystemInfos()
                    .Where(f => (f.Attributes & FileAttributes.Hidden) == 0).OrderBy(f => f is not DirectoryInfo).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToArray());
                if (request != generation || !dialog.IsVisible) return;
                current = Path.GetFullPath(directory); path.Text = current; list.Children.Clear();
                foreach (var entry in entries)
                {
                    if (entry is DirectoryInfo)
                    {
                        var folder = Button("↗  " + entry.Name, () => Browse(entry.FullName));
                        folder.HorizontalContentAlignment = HorizontalAlignment.Left; folder.Margin = new Thickness(0, 0, 0, 5); list.Children.Add(folder);
                    }
                    else
                    {
                        var item = new CheckBox { Content = entry.Name, ToolTip = entry.FullName, IsChecked = selected.Contains(entry.FullName), Margin = new Thickness(0, 0, 0, 5), Style = (Style)Application.Current.Resources["ReactionToggle"] };
                        item.Checked += (_, _) => { selected.Add(entry.FullName); count.Text = $"{selected.Count} selected"; };
                        item.Unchecked += (_, _) => { selected.Remove(entry.FullName); count.Text = $"{selected.Count} selected"; };
                        list.Children.Add(item);
                    }
                }
                count.Text = entries.Length == 0 ? "This folder is empty" : $"{selected.Count} selected · {entries.Length} items";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { if (request == generation) count.Text = "Can't open that folder. Check the path or choose another."; }
        }
        var up = Button("↑", () => Browse(Directory.GetParent(current)?.FullName ?? current));
        up.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(up, Dock.Left); nav.Children.Add(up);
        var go = Button("Open", () => Browse(path.Text)); go.Margin = new Thickness(8, 0, 0, 0); DockPanel.SetDock(go, Dock.Right); nav.Children.Add(go); nav.Children.Add(path);
        path.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Browse(path.Text); e.Handled = true; } };
        DockPanel.SetDock(nav, Dock.Top); dialog._body.Children.Add(nav);
        var places = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (label, location) in new[] { ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)), ("Documents", current), ("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)), ("Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")) })
        { var b = Button(label, () => Browse(location)); b.Margin = new Thickness(0, 0, 7, 0); places.Children.Add(b); }
        DockPanel.SetDock(places, Dock.Top); dialog._body.Children.Add(places);
        dialog._body.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        dialog.Loaded += (_, _) => Browse(current);
        dialog.Closed += (_, _) => ++generation;
        dialog.ShowDialog(); return result;
    }
}
