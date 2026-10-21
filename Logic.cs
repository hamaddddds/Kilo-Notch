using System.IO;
using System.Media;

namespace Notch;

public static class Pomodoro
{
    static Settings S => Store.D.Settings;
    static DateTime _endAt;

    public static bool Break { get; private set; }
    public static bool Running { get; private set; }
    public static TimeSpan Left { get; private set; } = TimeSpan.FromMinutes(Store.D.Settings.FocusMin);
    public static TimeSpan Total => TimeSpan.FromMinutes(Break ? S.BreakMin : S.FocusMin);
    public static event Action? Changed;
    public static event Action<bool>? Finished; // arg: the session that just ended was a break

    public static void Toggle()
    {
        if (Running) Left = _endAt - DateTime.Now;
        else _endAt = DateTime.Now + Left;
        Running = !Running;
        Changed?.Invoke();
    }

    public static void SetMode(bool brk) { Break = brk; Reset(); }

    public static void Reset()
    {
        Running = false;
        Left = Total;
        Changed?.Invoke();
    }

    public static void Tick()
    {
        if (!Running) return;
        Left = _endAt - DateTime.Now;
        if (Left <= TimeSpan.Zero)
        {
            var wasBreak = Break;
            Break = !Break;
            Running = false;
            Left = Total;
            Finished?.Invoke(wasBreak);
        }
        Changed?.Invoke();
    }
}

public static class Water
{
    static Settings S => Store.D.Settings;
    static DateTime _lastNag = DateTime.Now;

    public static IEnumerable<Sip> Today => Store.D.Sips.Where(s => s.At.Date == DateTime.Today);
    public static int Total => Today.Sum(s => s.Ml);
    public static event Action? Changed;

    public static void Add() { Store.D.Sips.Add(new Sip { At = DateTime.Now, Ml = S.WaterCup }); Store.Save(); Changed?.Invoke(); }

    public static void Undo()
    {
        var last = Today.LastOrDefault();
        if (last == null) return;
        Store.D.Sips.Remove(last);
        Store.Save();
        Changed?.Invoke();
    }

    public static void Refresh() => Changed?.Invoke();

    /// Nudge during active hours when it's been a while since the last glass.
    public static void Check(DateTime now)
    {
        if (!S.WaterRemind || now.Hour < S.WaterFrom || now.Hour >= S.WaterTo || Total >= S.WaterGoal) return;
        var lastSip = Today.Select(s => s.At).DefaultIfEmpty(DateTime.MinValue).Max();
        if (now - (lastSip > _lastNag ? lastSip : _lastNag) < TimeSpan.FromMinutes(S.WaterEvery)) return;
        _lastNag = now;
        Sound.Chime();
        MainWindow.I.Peek("", "AccentA", (double)Total / S.WaterGoal, "Time for some water",
            $"{Total / 1000.0:0.##} / {S.WaterGoal / 1000.0:0.##} L today", badge: "");
    }
}

static class Sound
{
    public static void Chime()
    {
        var f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify Calendar.wav");
        if (File.Exists(f)) new SoundPlayer(f).Play();
        else SystemSounds.Asterisk.Play();
    }
}
