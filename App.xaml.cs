using System.Windows;
using System.Windows.Controls;

namespace Notch;

public partial class App : Application
{
    Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // hosted by another program (e.g. a test runner): lend resources, start nothing
        if (System.Reflection.Assembly.GetEntryAssembly() != typeof(App).Assembly) return;
        _single = new Mutex(true, "Notch.SingleInstance", out var first);
        if (!first) { Shutdown(); return; }

        Theme.Apply();
        Store.Prune();
        Tray.Show("Kilo Notch", SettingsWindow.Open, TrayMenu);
        new MainWindow().Show();
    }

    // Built fresh each time, so the check marks are always current.
    ContextMenu TrayMenu()
    {
        var personality = new MenuItem { Header = "Personality", IsCheckable = true, IsChecked = Store.D.Settings.Personality };
        personality.Click += (_, _) => { Store.D.Settings.Personality = personality.IsChecked; Store.Save(); Notch.MainWindow.I.Collapse(force: true); };
        var startup = new MenuItem { Header = "Start with Windows", IsCheckable = true, IsChecked = AutoStart.Enabled };
        startup.Click += (_, _) => AutoStart.Enabled = startup.IsChecked;
        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) => SettingsWindow.Open();
        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => Shutdown();
        return new ContextMenu { Items = { settings, personality, startup, new Separator(), exit } };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Tray.Hide();
        if (Notch.MainWindow.I != null) Store.Save();
        base.OnExit(e);
    }
}
