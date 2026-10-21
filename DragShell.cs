using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace Notch;

/// Explorer-style drag visuals: the floating file preview keeps showing over the notch with an
/// "Add to Shelf" label, and files dragged out of the shelf carry a photo preview under the cursor.
static class DragShell
{
    // ---------- drop target side (files coming in) ----------

    static IDropTargetHelper? _target;

    public static void Enter(IntPtr hwnd, IDataObject data)
    {
        try
        {
            if (data is not ComTypes.IDataObject com) return;
            _target ??= (IDropTargetHelper)new DragDropHelper();
            Describe(com, DropImage.Copy, "Add to %1", "Shelf");
            var pt = Cursor();
            _target.DragEnter(hwnd, com, ref pt, (int)DragDropEffects.Copy);
        }
        catch (Exception) { _target = null; } // no shell helper: the notch's own preview still shows
    }

    public static void Over()
    {
        try { var pt = Cursor(); _target?.DragOver(ref pt, (int)DragDropEffects.Copy); }
        catch (Exception) { }
    }

    public static void Leave()
    {
        try { _target?.DragLeave(); }
        catch (Exception) { }
    }

    public static void Drop(IDataObject data)
    {
        try { if (data is ComTypes.IDataObject com) { var pt = Cursor(); _target?.Drop(com, ref pt, (int)DragDropEffects.Copy); } }
        catch (Exception) { }
    }

    // ---------- drag source side (files going out) ----------

    /// Drags files out with a rendered preview under the cursor. Copy/link only, so dropping into a
    /// folder never moves the original away. Returns false if the shell route isn't available.
    public static bool DragOut(IntPtr hwnd, string[] paths, BitmapSource preview, Point grab)
    {
        var pidls = new IntPtr[paths.Length];
        try
        {
            for (int i = 0; i < paths.Length; i++)
                if (SHParseDisplayName(paths[i], IntPtr.Zero, out pidls[i], 0, out _) != 0) return false;
            if (SHCreateShellItemArrayFromIDLists((uint)pidls.Length, pidls, out var items) != 0) return false;
            var bhid = new Guid("B8C0BD9F-ED24-455C-83E6-D5390C4FE8C4"); // BHID_DataObject
            var iid = typeof(ComTypes.IDataObject).GUID;
            items.BindToHandler(IntPtr.Zero, ref bhid, ref iid, out var obj);
            var data = (ComTypes.IDataObject)obj;

            var helper = (IDragSourceHelper2)new DragDropHelper();
            helper.SetFlags(1); // DSH_ALLOWDROPDESCRIPTIONTEXT: targets may label the drop ("Copy to Desktop")
            var image = new SHDRAGIMAGE
            {
                sizeDragImage = new SIZE { cx = preview.PixelWidth, cy = preview.PixelHeight },
                ptOffset = new POINT { X = (int)grab.X, Y = (int)grab.Y },
                hbmpDragImage = ToHBitmap(preview),
                crColorKey = unchecked((int)0xFFFFFFFF), // use the alpha channel
            };
            helper.InitializeFromBitmap(ref image, data); // the helper now owns the bitmap
            SHDoDragDrop(hwnd, data, IntPtr.Zero, (int)(DragDropEffects.Copy | DragDropEffects.Link), out _);
            return true;
        }
        catch (Exception) { return false; }
        finally
        {
            foreach (var p in pidls) if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p);
        }
    }

    // 32-bit premultiplied DIB, bottom-up like every classic DIB
    static IntPtr ToHBitmap(BitmapSource src)
    {
        int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        src.CopyPixels(px, stride, 0);
        var bmi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = h, biPlanes = 1, biBitCount = 32 };
        var hbmp = CreateDIBSection(IntPtr.Zero, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        for (int y = 0; y < h; y++) Marshal.Copy(px, y * stride, bits + (h - 1 - y) * stride, stride);
        return hbmp;
    }

    static void Describe(ComTypes.IDataObject data, DropImage type, string message, string insert)
    {
        var fmt = new ComTypes.FORMATETC
        {
            cfFormat = unchecked((short)RegisterClipboardFormat("DropDescription")),
            dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = ComTypes.TYMED.TYMED_HGLOBAL,
        };
        var desc = new DROPDESCRIPTION { type = (int)type, szMessage = message, szInsert = insert };
        var size = Marshal.SizeOf<DROPDESCRIPTION>();
        var h = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(desc, h, false);
        var medium = new ComTypes.STGMEDIUM { tymed = ComTypes.TYMED.TYMED_HGLOBAL, unionmember = h };
        try { data.SetData(ref fmt, ref medium, true); }
        catch (Exception) { Marshal.FreeHGlobal(h); } // source doesn't take descriptions; the image still shows
    }

    static POINT Cursor() { GetCursorPos(out var p); return p; }

    enum DropImage { Copy = 1, Label = 6 }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] struct SHDRAGIMAGE { public SIZE sizeDragImage; public POINT ptOffset; public IntPtr hbmpDragImage; public int crColorKey; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DROPDESCRIPTION
    {
        public int type;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szMessage;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szInsert;
    }

    [ComImport, Guid("4657278A-411B-11D2-839A-00C04FD918D0")] class DragDropHelper { }

    [ComImport, Guid("4657278B-411B-11D2-839A-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDropTargetHelper
    {
        void DragEnter(IntPtr hwndTarget, ComTypes.IDataObject dataObject, ref POINT pt, int effect);
        void DragLeave();
        void DragOver(ref POINT pt, int effect);
        void Drop(ComTypes.IDataObject dataObject, ref POINT pt, int effect);
        void Show(bool show);
    }

    [ComImport, Guid("83E07D0D-0C5F-4163-BF1A-60B274051E40"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDragSourceHelper2
    {
        void InitializeFromBitmap(ref SHDRAGIMAGE dragImage, ComTypes.IDataObject dataObject);
        void InitializeFromWindow(IntPtr hwnd, ref POINT pt, ComTypes.IDataObject dataObject);
        void SetFlags(int flags);
    }

    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemArray
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
    }

    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string format);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
    [DllImport("shell32.dll")] static extern int SHCreateShellItemArrayFromIDLists(uint count, IntPtr[] pidls, out IShellItemArray items);
    [DllImport("shell32.dll")] static extern int SHDoDragDrop(IntPtr hwnd, ComTypes.IDataObject data, IntPtr dropSource, int okEffects, out int effect);
}
