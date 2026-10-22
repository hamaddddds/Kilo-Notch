using System.Runtime.InteropServices;

namespace Notch;

/// Observes only key-down timing; never reads keys, text, or the destination app.
public sealed class TypingActivity : IDisposable
{
    delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
    readonly Hook _callback;
    IntPtr _hook;
    long _last;
    public bool Active => Environment.TickCount64 - Interlocked.Read(ref _last) < 1400;
    public TypingActivity() { _last = Environment.TickCount64 - 2000; _callback = OnKey; }
    public void Enable(bool enabled)
    {
        if (enabled && _hook == IntPtr.Zero) _hook = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
        else if (!enabled) Dispose();
    }
    IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && (message == (IntPtr)0x100 || message == (IntPtr)0x104))
            Interlocked.Exchange(ref _last, Environment.TickCount64);
        return CallNextHookEx(_hook, code, message, data);
    }
    public void Dispose() { if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, Hook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
}
