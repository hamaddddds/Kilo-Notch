using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Notch;

public partial class TodoPage : UserControl
{
    static List<TodoItem> Todos => Store.D.Todos;
    static readonly IEasingFunction Pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 };
    static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    bool _editMode;
    TodoItem? _added, _toggled; // rows that should animate on the next refresh
    string _score = "";

    public TodoPage()
    {
        InitializeComponent();
        Score.RenderTransformOrigin = new Point(0, 0.5);
        Score.RenderTransform = new ScaleTransform();
        Editor.RenderTransform = new TranslateTransform();
        Refresh();
    }

    public void Refresh()
    {
        var score = $"{Todos.Count(t => t.Done)}/{Todos.Count}";
        if (score != _score && _score != "") Bounce(Score, 0.8);
        Score.Text = _score = score;
        Tasks.Children.Clear();
        var i = 0;
        foreach (var t in Todos.OrderBy(t => t.Done).ThenBy(t => t.Due ?? DateTime.MaxValue)) Tasks.Children.Add(Row(t, i++));
        EmptyHint.Visibility = Todos.Count == 0 && Editor.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        EditBtn.Style = (Style)FindResource(_editMode ? "SoftBtn" : "PillBtn");
        _added = _toggled = null;
    }

    FrameworkElement Row(TodoItem t, int index)
    {
        var check = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(t.Done ? 0 : 1.6),
            VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(),
            Child = new TextBlock
            {
                Text = t.Done ? "" : "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 11, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        if (t.Done) check.SetResourceReference(Border.BackgroundProperty, "Good");
        else check.SetResourceReference(Border.BorderBrushProperty, "FgDim");

        var text = new TextBlock
        {
            Text = t.Text, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 6, 0), TextDecorations = t.Done ? TextDecorations.Strikethrough : null,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, t.Done ? "FgDim" : "Fg");

        var row = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 5), Cursor = Cursors.Hand,
            RenderTransform = new TranslateTransform(),
        };
        var dock = new DockPanel();
        DockPanel.SetDock(check, Dock.Left);
        dock.Children.Add(check);
        if (_editMode)
        {
            var del = new Button { Content = "", Style = (Style)FindResource("IconBtn"), FontSize = 12, Padding = new Thickness(6, 2, 6, 2) };
            del.Click += (_, _) => Delete(t, row);
            DockPanel.SetDock(del, Dock.Right);
            dock.Children.Add(del);
        }
        else if (t.Due is DateTime due)
        {
            var d = new TextBlock { Text = due.ToString("HH:mm"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            d.SetResourceReference(TextBlock.ForegroundProperty, !t.Done && due < DateTime.Now ? "Bad" : "FgDim");
            DockPanel.SetDock(d, Dock.Right);
            dock.Children.Add(d);
        }
        dock.Children.Add(text);
        row.Child = dock;
        row.SetResourceReference(Border.BackgroundProperty, t.Done ? "GoodSoft" : "Surface");
        row.MouseLeftButtonUp += (_, _) =>
        {
            if (_editMode) return;
            t.Done = !t.Done;
            t.DoneAt = t.Done ? DateTime.Now : null;
            _toggled = t;
            Store.Save();
            Refresh();
        };

        if (t == _added) // new task slides in
        {
            ((TranslateTransform)row.RenderTransform).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(480)) { EasingFunction = Pop });
            row.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        }
        if (t == _toggled) Bounce(check, 0.4); // the check pops
        if (_editMode) // rows wiggle into edit mode, one after another
            ((TranslateTransform)row.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(40 * index), EasingFunction = Pop });
        return row;
    }

    void Delete(TodoItem t, Border row)
    {
        var slide = new DoubleAnimation(-60, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        slide.Completed += (_, _) => { Todos.Remove(t); Store.Save(); Refresh(); };
        ((TranslateTransform)row.RenderTransform).BeginAnimation(TranslateTransform.XProperty, slide);
        row.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
    }

    static void Bounce(UIElement e, double from)
    {
        var s = (ScaleTransform)e.RenderTransform;
        var a = new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(460)) { EasingFunction = Pop };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    void AddTask(object sender, RoutedEventArgs e)
    {
        Editor.Visibility = Visibility.Visible;
        EmptyHint.Visibility = Visibility.Collapsed;
        ((TranslateTransform)Editor.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = Pop });
        Editor.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        MainWindow.I.AllowTyping();
        Dispatcher.InvokeAsync(() => Keyboard.Focus(TaskBox), DispatcherPriority.Input);
    }

    void EditTasks(object sender, RoutedEventArgs e) { _editMode = !_editMode; Refresh(); }

    void EditorChanged(object sender, TextChangedEventArgs e)
    {
        TaskHint.Visibility = TaskBox.Text == "" ? Visibility.Visible : Visibility.Collapsed;
        DueHint.Visibility = DueBox.Text == "" ? Visibility.Visible : Visibility.Collapsed;
    }

    void EditorKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) CloseEditor();
        else if (e.Key == Key.Enter)
        {
            var text = TaskBox.Text.Trim();
            if (text != "")
            {
                _added = new TodoItem { Text = text, Due = ParseDue(DueBox.Text) };
                Todos.Add(_added);
                Store.Save();
            }
            CloseEditor();
        }
        else return;
        e.Handled = true;
    }

    void CloseEditor()
    {
        Editor.Visibility = Visibility.Collapsed;
        TaskBox.Text = DueBox.Text = "";
        Refresh();
        MainWindow.I.EndTyping();
    }

    // "14:30", "9.15" or just "9" → the next time that clock time comes around
    static DateTime? ParseDue(string s)
    {
        var m = Regex.Match(s.Trim(), @"^(\d{1,2})(?:[:.](\d{2}))?$");
        if (!m.Success) return null;
        int h = int.Parse(m.Groups[1].Value), min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        if (h > 23 || min > 59) return null;
        var due = DateTime.Today.AddHours(h).AddMinutes(min);
        return due < DateTime.Now ? due.AddDays(1) : due;
    }

    public static void CheckDue(DateTime now)
    {
        foreach (var t in Todos.Where(t => !t.Done && !t.Notified && t.Due <= now).ToList())
        {
            t.Notified = true;
            Store.Save();
            Sound.Chime();
            MainWindow.I.Peek("", "Good", 1, "Reminder", t.Text, badge: "");
        }
    }
}
