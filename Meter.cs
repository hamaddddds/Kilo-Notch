using System.Runtime.InteropServices;

namespace Notch;

/// Peak level of whatever the speakers are playing (Core Audio meter, no audio capture).
static class Meter
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        void GetPeakValue(out float peak);
    }

    static IAudioMeterInformation? _meter;

    public static void Reset() => _meter = null; // default output changed (headphones etc.)

    public static float Peak()
    {
        try
        {
            if (_meter == null)
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                en.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out var dev);
                var iid = typeof(IAudioMeterInformation).GUID;
                dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var o);
                _meter = (IAudioMeterInformation)o;
            }
            _meter.GetPeakValue(out var p);
            return p;
        }
        catch (COMException) { _meter = null; return 0; } // device unplugged; reacquire on the next call
    }
}
