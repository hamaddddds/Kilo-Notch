using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Notch;

/// Tray icon straight through Shell_NotifyIcon, so the app doesn't load WinForms just for this.
/// Messages arrive on a hidden message-only window; right-click opens a WPF menu.
static class Tray
{
    const int WM_TRAY = 0x8001, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205;
    const int NIM_ADD = 0, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    static readonly int TaskbarCreated = RegisterWindowMessage("TaskbarCreated");
    static HwndSource? _src;
    static NOTIFYICONDATA _data;
    static Action _click = () => { };
    static Func<ContextMenu> _menu = () => new ContextMenu();

    public static void Show(string tip, Action click, Func<ContextMenu> menu)
    {
        _click = click;
        _menu = menu;
        _src = new HwndSource(new HwndSourceParameters("KiloNotchTray") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 }); // HWND_MESSAGE
        _src.AddHook(Hook);
        ExtractIconEx(Environment.ProcessPath ?? "", 0, out _, out var small, 1); // the exe's own icon
        _data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = _src.Handle, uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP, uCallbackMessage = WM_TRAY, hIcon = small, szTip = tip,
        };
        Shell_NotifyIcon(NIM_ADD, ref _data);
    }

    public static void Hide()
    {
        if (_src == null) return;
        Shell_NotifyIcon(NIM_DELETE, ref _data);
        _src.Dispose();
        _src = null;
    }

    static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == TaskbarCreated) Shell_NotifyIcon(NIM_ADD, ref _data); // Explorer restarted: put the icon back
        if (msg != WM_TRAY) return IntPtr.Zero;
        switch ((int)lParam & 0xFFFF)
        {
            case WM_LBUTTONUP: _click(); break;
            case WM_RBUTTONUP:
                var m = _menu();
                m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                // a tray menu only closes on an outside click if its window is in the foreground
                m.Opened += (_, _) => { if (PresentationSource.FromVisual(m) is HwndSource s) SetForegroundWindow(s.Handle); };
                m.IsOpen = true;
                break;
        }
        handled = true;
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
}
