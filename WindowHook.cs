using System.Runtime.InteropServices;

namespace TextExpander;

/// <summary>
/// Hooks the main window's WndProc: enforces a minimum size (DPI-aware) and
/// lets other components (tray icon) handle window messages.
/// </summary>
internal static class WindowHook
{
    private const int GWLP_WNDPROC = -4;
    private const uint WM_GETMINMAXINFO = 0x0024;

    /// <summary>Return true if the message was handled.</summary>
    public static Func<uint, IntPtr, IntPtr, bool>? Handler;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private static WndProc? _proc;   // keep alive (GC)
    private static IntPtr _oldProc;
    private static int _minW, _minH;

    public static void Apply(IntPtr hwnd, int minWidth, int minHeight)
    {
        _minW = minWidth;
        _minH = minHeight;
        _proc = Hook;
        _oldProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_proc));
    }

    private static IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            uint dpi = GetDpiForWindow(hWnd);
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            mmi.ptMinTrackSize.X = (int)(_minW * dpi / 96);
            mmi.ptMinTrackSize.Y = (int)(_minH * dpi / 96);
            Marshal.StructureToPtr(mmi, lParam, false);
        }
        else
        {
            try { if (Handler?.Invoke(msg, wParam, lParam) == true) return IntPtr.Zero; }
            catch (Exception ex) { Program.Log(ex); }
        }
        return CallWindowProc(_oldProc, hWnd, msg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr prev, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
}
