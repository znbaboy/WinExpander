using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;

namespace TextExpander;

/// <summary>
/// Global low-level keyboard hook. Keeps a rolling buffer of typed characters and,
/// when it ends with a trigger, erases the trigger and types the expansion.
/// Must be created on a thread with a message loop (the UI thread).
/// </summary>
internal sealed class Expander : IDisposable
{
    private const int MaxBuffer = 64;

    private readonly Func<IEnumerable<Snippet>> _getSnippets;
    private readonly DispatcherQueue _dispatcher;
    private readonly StringBuilder _buffer = new();
    private readonly Native.LowLevelKeyboardProc _proc; // keep alive (GC)
    private IntPtr _hook;
    private IntPtr _lastWindow;

    public bool Enabled { get; set; } = true;

    private readonly IntPtr _ownWindow;

    public Expander(Func<IEnumerable<Snippet>> getSnippets, DispatcherQueue dispatcher, IntPtr ownWindow)
    {
        _ownWindow = ownWindow;
        _getSnippets = getSnippets;
        _dispatcher = dispatcher;
        _proc = HookCallback;
        _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Enabled && (wParam == Native.WM_KEYDOWN || wParam == Native.WM_SYSKEYDOWN))
        {
            var k = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            // Ignore only keys we injected ourselves; other injected input
            // (on-screen keyboard, AutoHotkey, ...) can still trigger snippets.
            if (k.dwExtraInfo != Native.Marker)
                OnKey(k);
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void OnKey(Native.KBDLLHOOKSTRUCT k)
    {
        // Reset when the user switches windows
        var fg = Native.GetForegroundWindow();
        if (fg != _lastWindow) { _buffer.Clear(); _lastWindow = fg; }
        if (fg == _ownWindow) return; // don't expand while editing snippets in this app

        uint vk = k.vkCode;
        if (vk == Native.VK_BACK) { if (_buffer.Length > 0) _buffer.Length--; return; }
        if (Native.IsModifier(vk)) return;

        // Shortcuts (Ctrl/Alt/Win + key) break the sequence
        if (Native.Down(Native.VK_CONTROL) || Native.Down(Native.VK_MENU) ||
            Native.Down(Native.VK_LWIN) || Native.Down(Native.VK_RWIN))
        { _buffer.Clear(); return; }

        var state = new byte[256];
        if (Native.Down(Native.VK_SHIFT)) state[Native.VK_SHIFT] = 0x80;
        if ((Native.GetKeyState(Native.VK_CAPITAL) & 1) != 0) state[Native.VK_CAPITAL] = 0x01;

        uint thread = Native.GetWindowThreadProcessId(fg, out _);
        var layout = Native.GetKeyboardLayout(thread);
        var chars = new StringBuilder(8);
        // flag 0x4: don't alter kernel keyboard state (keeps dead keys working)
        int n = Native.ToUnicodeEx(vk, k.scanCode, state, chars, chars.Capacity, 0x4, layout);

        if (n == -1) return;                         // dead key, wait for next
        if (n <= 0 || char.IsControl(chars[0]))      // arrows, Enter, Tab, Esc, etc.
        { _buffer.Clear(); return; }

        _buffer.Append(chars.ToString(0, n));
        if (_buffer.Length > MaxBuffer) _buffer.Remove(0, _buffer.Length - MaxBuffer);

        var typed = _buffer.ToString();
        var match = _getSnippets()
            .Where(s => s.Trigger.Length > 0 && typed.EndsWith(s.Trigger, StringComparison.Ordinal))
            .OrderByDescending(s => s.Trigger.Length)
            .FirstOrDefault();

        if (match != null)
        {
            _buffer.Clear();
            int erase = match.Trigger.Length;
            string text = match.Text;
            // Run after the hook returns so the final trigger key lands first
            _dispatcher.TryEnqueue(() => Paster.Expand(erase, text, vk));
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }
}
