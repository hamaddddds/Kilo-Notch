using System.Windows;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Enumeration.Pnp;
using Windows.System.Power;

namespace Notch;

public static class Power
{
    public static bool HasBattery => PowerManager.BatteryStatus != BatteryStatus.NotPresent;
    public static int Percent => PowerManager.RemainingChargePercent;
    public static bool Plugged => PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent;

    public static void Init(Action<bool> pluggedChanged)
    {
        var was = Plugged;
        PowerManager.PowerSupplyStatusChanged += (_, _) => Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var now = Plugged;
            if (now == was || !HasBattery) return;
            was = now;
            pluggedChanged(now);
        });
    }
}

/// Watches paired Bluetooth devices and reports audio ones as they connect, with battery if the headset shares it.
public static class Headphones
{
    const string ConnectedKey = "System.Devices.Aep.IsConnected";
    const string ContainerKey = "System.Devices.Aep.ContainerId";
    const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2"; // DEVPKEY_Bluetooth_Battery
    static readonly Dictionary<string, bool> State = new();
    static DeviceWatcher? _watcher;
    static bool _ready;

    public static void Init(Action<string, int?> connected)
    {
        _watcher = DeviceInformation.CreateWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(true),
            new[] { ConnectedKey, ContainerKey }, DeviceInformationKind.AssociationEndpoint);
        _watcher.Added += (_, d) => Ui(() => State[d.Id] = d.Properties.TryGetValue(ConnectedKey, out var c) && c is true);
        _watcher.Removed += (_, u) => Ui(() => State.Remove(u.Id));
        _watcher.EnumerationCompleted += (_, _) => Ui(() => _ready = true);
        _watcher.Updated += (_, u) => Ui(() =>
        {
            if (!u.Properties.TryGetValue(ConnectedKey, out var v) || v is not bool now) return;
            var was = State.TryGetValue(u.Id, out var w) && w;
            State[u.Id] = now;
            if (now && !was && _ready) OnConnected(u.Id, connected);
        });
        _watcher.Start();
    }

    static void Ui(Action a) => Application.Current.Dispatcher.InvokeAsync(a);

    static async void OnConnected(string id, Action<string, int?> connected)
    {
        try
        {
            using var dev = await BluetoothDevice.FromIdAsync(id);
            if (dev == null || dev.ClassOfDevice.MajorClass != BluetoothMajorClass.AudioVideo) return;
            var info = await DeviceInformation.CreateFromIdAsync(id, new[] { ContainerKey }, DeviceInformationKind.AssociationEndpoint);
            int? pct = null;
            if (info.Properties.TryGetValue(ContainerKey, out var c) && c is Guid container)
            {
                await Task.Delay(1500); // headsets report battery a moment after connecting
                pct = await Battery(container);
            }
            connected(dev.Name, pct);
        }
        catch (Exception) { } // device dropped mid-query; nothing to show
    }

    static async Task<int?> Battery(Guid container)
    {
        try
        {
            var nodes = await PnpObject.FindAllAsync(PnpObjectType.Device, new[] { BatteryKey }, $"System.Devices.ContainerId:=\"{{{container}}}\"");
            foreach (var n in nodes)
                if (n.Properties.TryGetValue(BatteryKey, out var b) && b is byte pct) return pct;
        }
        catch (Exception) { } // some stacks don't expose battery; show "Connected" without it
        return null;
    }
}
