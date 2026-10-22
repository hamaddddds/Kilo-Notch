using System.Windows;
using System.Windows.Controls;

namespace Notch;

public partial class App : Application
{
    Mutex? _single;
    EventWaitHandle? _shutdownSignal;
    RegisteredWaitHandle? _shutdownWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // hosted by another program (e.g. a test runner): lend resources, start nothing
        if (System.Reflection.Assembly.GetEntryAssembly() != typeof(App).Assembly) return;
        _single = new Mutex(true, "Notch.SingleInstance", out var first);
        if (!first) { Shutdown(); return; }
        _shutdownSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\KiloNotch.Shutdown");
        _shutdownWait = ThreadPool.RegisterWaitForSingleObject(_shutdownSignal, (_, _) => Dispatcher.InvokeAsync(Shutdown), null, Timeout.Infinite, true);

        Theme.Apply();
        Store.Prune();
        Tray.Show("Kilo Notch", SettingsWindow.Open, TrayMenu);
        new MainWindow().Show();
        UpdateCenter.Start();
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
        var update = new MenuItem { Header = UpdateCenter.Available is { } latest ? $"Update to {latest.Version}" : "Check for updates" };
        update.Click += async (_, _) => { if (UpdateCenter.Available != null) UpdateCenter.Show(); else await UpdateCenter.Check(true); };
        var vision = new MenuItem { Header = CompanionVision.Enabled ? "Camera reactions: ON (turn off)" : "Camera reactions: OFF (turn on)", IsCheckable = true, IsChecked = CompanionVision.Enabled };
        vision.Click += async (_, _) => await CompanionVision.SetEnabled(vision.IsChecked);
        exit.Click += (_, _) => Shutdown();
        return new ContextMenu { Items = { settings, update, personality, vision, startup, new Separator(), exit } };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Tray.Hide();
        _shutdownWait?.Unregister(null);
        _shutdownSignal?.Dispose();
        if (Notch.MainWindow.I != null) Store.Save();
        base.OnExit(e);
    }
}
