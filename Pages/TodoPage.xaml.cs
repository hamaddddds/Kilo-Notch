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
    DateTime? _due;
    TodoItem? _editing;

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
        if (t.Due is DateTime due && !_editMode)
        {
            var d = new TextBlock { Text = (due.Date > DateTime.Today ? "Tomorrow " : "") + due.ToString("HH:mm"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            d.SetResourceReference(TextBlock.ForegroundProperty, !t.Done && due < DateTime.Now ? "Bad" : "FgDim");
            DockPanel.SetDock(d, Dock.Right);
            dock.Children.Add(d);
        }
        dock.Children.Add(text);
        row.Child = dock;
        row.SetResourceReference(Border.BackgroundProperty, t.Done ? "GoodSoft" : "Surface");
        row.MouseLeftButtonUp += (_, _) =>
        {
            if (_editMode)
            {
                _editing = t;
                _due = t.Due;
                TaskBox.Text = t.Text;
                UpdateTimeLabel();
                ShowEditor();
                return;
            }
            t.Done = !t.Done;
            t.DoneAt = t.Done ? DateTime.Now : null;
            if (t.Done) MainWindow.I.React(Mood.Happy, 3);
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
        _editing = null;
        _due = null;
        TaskBox.Text = "";
        UpdateTimeLabel();
        ShowEditor();
    }

    void ShowEditor()
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
    }

    void EditorKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) CloseEditor();
        else if (e.Key == Key.Enter)
        {
            SaveTask(sender, e);
        }
        else return;
        e.Handled = true;
    }

    void CloseEditor()
    {
        Editor.Visibility = Visibility.Collapsed;
        TaskBox.Text = "";
        _due = null;
        _editing = null;
        UpdateTimeLabel();
        Refresh();
        MainWindow.I.EndTyping();
    }

    void SaveTask(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TaskBox.Text)) { TaskBox.Focus(); return; }
        var item = _editing ?? new TodoItem();
        item.Text = TaskBox.Text.Trim();
        item.Due = _due;
        item.Notified = false;
        if (_editing == null) { Todos.Add(item); _added = item; }
        Store.Save();
        CloseEditor();
    }

    void UpdateTimeLabel() => TimeButton.Content = _due is DateTime d
        ? (d.Date == DateTime.Today ? "Today " : d.ToString("ddd ")) + d.ToString("HH:mm") + " ▾" : "No reminder ▾";

    void PickTime(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = TimeButton };
        void Option(string label, Func<DateTime?> value)
        {
            var option = new MenuItem { Header = label };
            option.Click += (_, _) => { _due = value(); UpdateTimeLabel(); };
            menu.Items.Add(option);
        }
        Option("No reminder", () => null);
        foreach (var minutes in new[] { 15, 30, 60 })
            Option($"In {minutes} minutes", () => DateTime.Now.AddMinutes(minutes));
        Option("Tomorrow · 09:00", () => DateTime.Today.AddDays(1).AddHours(9));
        menu.Items.Add(new Separator());
        var clock = new StackPanel { Margin = new Thickness(8) };
        var day = new ComboBox { ItemsSource = new[] { "Today", "Tomorrow" }, SelectedIndex = _due?.Date > DateTime.Today ? 1 : 0, Margin = new Thickness(0, 0, 0, 8) };
        var time = _due ?? DateTime.Now.AddHours(1);
        var hours = new ComboBox { ItemsSource = Enumerable.Range(0, 24).Select(n => n.ToString("00")), SelectedIndex = time.Hour, Width = 62 };
        var mins = new ComboBox { ItemsSource = Enumerable.Range(0, 60).Select(n => n.ToString("00")), SelectedIndex = time.Minute, Width = 62 };
        clock.Children.Add(day);
        clock.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { hours, new TextBlock { Text = " : ", VerticalAlignment = VerticalAlignment.Center }, mins } });
        var set = new Button { Content = "Set time", Margin = new Thickness(0, 8, 0, 0) };
        set.Click += (_, _) =>
        {
            _due = TodoSchedule.AtClock(DateTime.Now, day.SelectedIndex, hours.SelectedIndex, mins.SelectedIndex);
            UpdateTimeLabel();
            menu.IsOpen = false;
        };
        clock.Children.Add(set);
        menu.Items.Add(new MenuItem { Header = clock, StaysOpenOnClick = true });
        menu.Opened += (_, _) => MainWindow.I.Hold(true);
        menu.Closed += (_, _) => { MainWindow.I.Hold(false); MainWindow.I.AllowTyping(); Dispatcher.InvokeAsync(() => Keyboard.Focus(TaskBox), DispatcherPriority.Input); };
        menu.IsOpen = true;
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
