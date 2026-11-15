using System.Runtime.InteropServices;

namespace TextExpander;

/// <summary>System tray (notification area) icon with an Open / Pause / Exit menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint WM_TRAY = 0x8001; // WM_APP + 1
    private static readonly uint WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");
    /// <summary>Broadcast by a second instance to bring this one to the front.</summary>
    public static readonly uint WM_SHOWAPP = RegisterWindowMessage("WinTextExpander.ShowWindow");

    private readonly IntPtr _hwnd;
    private readonly IntPtr _icon;
    private bool _added;

    public event Action? OpenRequested;
    public event Action? PauseToggled;
    public event Action? ExitRequested;
    public Func<bool> IsPaused { get; set; } = () => false;

    public TrayIcon(IntPtr hwnd, string icoPath)
    {
        _hwnd = hwnd;
        int size = GetSystemMetrics(49 /* SM_CXSMICON */);
        _icon = LoadImage(IntPtr.Zero, icoPath, 1 /* IMAGE_ICON */, size, size, 0x10 /* LR_LOADFROMFILE */);
        Add();
    }

    private NOTIFYICONDATA Data() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        uCallbackMessage = WM_TRAY,
        hIcon = _icon,
        szTip = "Text Expander",
        szInfo = "",
        szInfoTitle = "",
    };

    private void Add()
    {
        var d = Data();
        d.uFlags = 0x1 | 0x2 | 0x4; // NIF_MESSAGE | NIF_ICON | NIF_TIP
        _added = Shell_NotifyIcon(0 /* NIM_ADD */, ref d);
        d.uVersion = 4; // NOTIFYICON_VERSION_4
        Shell_NotifyIcon(4 /* NIM_SETVERSION */, ref d);
    }

    public void SetTooltip(string text)
    {
        var d = Data();
        d.uFlags = 0x4;
        d.szTip = text;
        Shell_NotifyIcon(1 /* NIM_MODIFY */, ref d);
    }

    /// <summary>Call from the window procedure. Returns true if handled.</summary>
    public bool HandleMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TASKBARCREATED) { Add(); return false; }   // Explorer restarted
        if (msg == WM_SHOWAPP) { OpenRequested?.Invoke(); return true; }
        if (msg != WM_TRAY) return false;

        switch ((uint)((long)lParam & 0xFFFF))
        {
            case 0x400: // NIN_SELECT (click)
            case 0x401: // NIN_KEYSELECT
                OpenRequested?.Invoke();
                break;
            case 0x7B:  // WM_CONTEXTMENU (right click)
                ShowMenu();
                break;
        }
        return true;
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, 0x0 /* MF_STRING */, 1, "Open Text Expander");
        AppendMenu(menu, IsPaused() ? 0x8u /* MF_CHECKED */ : 0x0u, 2, "Pause expansion");
        AppendMenu(menu, 0x800 /* MF_SEPARATOR */, 0, null);
        AppendMenu(menu, 0x0, 3, "Exit");
        SetMenuDefaultItem(menu, 1, 0);

        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd); // so the menu closes when clicking elsewhere
        int cmd = TrackPopupMenuEx(menu, 0x100 /* TPM_RETURNCMD */ | 0x2 /* TPM_RIGHTBUTTON */, pt.X, pt.Y, _hwnd, IntPtr.Zero);
        DestroyMenu(menu);

        switch (cmd)
        {
            case 1: OpenRequested?.Invoke(); break;
            case 2: PauseToggled?.Invoke(); break;
            case 3: ExitRequested?.Invoke(); break;
        }
    }

    /// <summary>Makes Win32 popup menus follow the app theme (uxtheme, undocumented but stable since 1903).</summary>
    public static void SetMenuTheme(AppTheme theme)
    {
        try
        {
            SetPreferredAppMode(theme switch { AppTheme.Dark => 2, AppTheme.Light => 3, _ => 1 });
            FlushMenuThemes();
        }
        catch { /* older Windows: menus stay light */ }
    }

    public void Dispose()
    {
        if (_added)
        {
            var d = Data();
            Shell_NotifyIcon(2 /* NIM_DELETE */, ref d);
            _added = false;
        }
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint load);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPos);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("uxtheme.dll", EntryPoint = "#135")] private static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] private static extern void FlushMenuThemes();
}
