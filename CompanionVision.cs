using System.Windows.Threading;

namespace Notch;

public static class CompanionVision
{
    static readonly Camera Cam = new(640, 250);
    static readonly FaceVision Detector = new();
    static readonly SemaphoreSlim Gate = new(1);
    static readonly DispatcherTimer Tick = new() { Interval = TimeSpan.FromMilliseconds(600) };
    static bool _busy, _wanted;
    public static bool Enabled { get; private set; }
    public static event Action? Changed;
    static CompanionVision() { Tick.Tick += async (_, _) => await Observe(); }
    public static async Task SetEnabled(bool on)
    {
        _wanted = on;
        await Gate.WaitAsync();
        try
        {
            if (_wanted && !Enabled)
            {
                await Cam.Start();
                if (!_wanted) { await Cam.Stop(); return; }
                Enabled = true;
                Detector.Reset();
                Tick.Start();
                MainWindow.I.React(Mood.Happy, 2);
            }
            else if (!_wanted)
            {
                Tick.Stop();
                Enabled = false;
                await Cam.Stop();
                MainWindow.I.ClearVision();
            }
        }
        catch (Exception)
        {
            _wanted = Enabled = false;
            Tick.Stop();
            await Cam.Stop();
            KiloDialog.Notice("Camera reactions", "Kilo couldn't start the camera. Check Windows camera access or close another camera app.");
        }
        finally { Gate.Release(); Changed?.Invoke(); }
    }
    static async Task Observe()
    {
        if (_busy || !Enabled || Cam.Frame == null) return;
        _busy = true;
        try
        {
            var still = Cam.Snap(320);
            if (still == null) return;
            var result = await Detector.Observe(still);
            if (Enabled && result is VisionResult face) MainWindow.I.VisionReaction(face);
            else if (Enabled && result == null) await SetEnabled(false);
        }
        catch (Exception) { await SetEnabled(false); }
        finally { _busy = false; }
    }
}
