using System.Runtime.InteropServices;
using System.Text;

namespace TextExpander;

internal static class Native
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
    
    public const int VK_BACK = 0x08, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11,
        VK_MENU = 0x12, VK_CAPITAL = 0x14, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, StringBuilder buf, int cch, uint flags, IntPtr hkl);
    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint n, INPUT[] inputs, int size);

    public static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public static bool IsModifier(uint vk) => vk is
        0x10 or 0x11 or 0x12 or 0x14 or 0x5B or 0x5C or   // shift ctrl alt caps win
        0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;      // L/R variants

    /// <summary>Tag on our own injected keys so the hook can ignore them (and only them).</summary>
    public static readonly IntPtr Marker = (IntPtr)0x54455850; // "TEXP"

    /// <summary>Releases Shift, sends N backspaces, then Ctrl+V.</summary>
    public static void SendEraseAndPaste(int erase)
    {
        var inputs = new List<INPUT>();
        foreach (int vk in new[] { 0xA0, 0xA1 }) // L/R Shift held from typing the trigger -> would make Ctrl+Shift+V
            if (Down(vk)) inputs.Add(Key((ushort)vk, 0, KEYEVENTF_KEYUP));
        for (int i = 0; i < erase; i++) AddVk(inputs, VK_BACK);
        inputs.Add(Key(VK_CONTROL, 0, 0));
        AddVk(inputs, 0x56); // V
        inputs.Add(Key(VK_CONTROL, 0, KEYEVENTF_KEYUP));
        var arr = inputs.ToArray();
        SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
    }

    static void AddVk(List<INPUT> list, int vk)
    {
        list.Add(Key(vk, 0, 0));
        list.Add(Key(vk, 0, KEYEVENTF_KEYUP));
    }

    static INPUT Key(int vk, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = scan, dwFlags = flags, dwExtraInfo = Marker } }
    };
}
