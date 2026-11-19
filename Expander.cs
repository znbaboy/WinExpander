using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace WinExpander;

/// <summary>
/// Global low-level keyboard hook. Keeps a rolling buffer of typed characters and,
/// when it ends with a trigger, erases the trigger and pastes the expansion.
/// Runs on every key press system-wide, so the hot path allocates nothing.
/// Must be created on a thread with a message loop (the UI thread).
/// </summary>
internal sealed unsafe class Expander : IDisposable
{
    private const int MaxBuffer = 64;

    private readonly MainViewModel _vm;
    private readonly DispatcherQueue _dispatcher;
    private readonly IntPtr _ownWindow;
    private readonly Native.LowLevelKeyboardProc _proc; // keep alive (GC)
    private IntPtr _hook;
    private IntPtr _lastWindow;

    // Reused buffers
    private readonly char[] _buf = new char[MaxBuffer];
    private int _len;
    private readonly byte[] _keyState = new byte[256];
    private readonly char[] _chars = new char[8];

    // Trigger table, rebuilt only when snippets change
    private Snippet[] _byLength = Array.Empty<Snippet>();
    private HashSet<char> _lastChars = new();
    private int _version = -1;

    public bool Enabled { get; set; } = true;

    public Expander(MainViewModel vm, DispatcherQueue dispatcher, IntPtr ownWindow)
    {
        _vm = vm;
        _dispatcher = dispatcher;
        _ownWindow = ownWindow;
        _proc = HookCallback;
        _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Enabled && (wParam == Native.WM_KEYDOWN || wParam == Native.WM_SYSKEYDOWN))
        {
            var k = (Native.KBDLLHOOKSTRUCT*)lParam;
            // Ignore only keys we injected ourselves; other injected input
            // (on-screen keyboard, AutoHotkey, ...) can still trigger snippets.
            if (k->dwExtraInfo != Native.Marker)
                OnKey(k->vkCode, k->scanCode);
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void OnKey(uint vk, uint scanCode)
    {
        // Reset when the user switches windows
        var fg = Native.GetForegroundWindow();
        if (fg != _lastWindow) { _len = 0; _lastWindow = fg; }
        if (fg == _ownWindow) return; // don't expand while editing snippets in this app

        if (vk == Native.VK_BACK) { if (_len > 0) _len--; return; }
        if (Native.IsModifier(vk)) return;

        // Shortcuts (Ctrl/Alt/Win + key) break the sequence
        if (Native.Down(Native.VK_CONTROL) || Native.Down(Native.VK_MENU) ||
            Native.Down(Native.VK_LWIN) || Native.Down(Native.VK_RWIN))
        { _len = 0; return; }

        _keyState[Native.VK_SHIFT] = Native.Down(Native.VK_SHIFT) ? (byte)0x80 : (byte)0;
        _keyState[Native.VK_CAPITAL] = (Native.GetKeyState(Native.VK_CAPITAL) & 1) != 0 ? (byte)1 : (byte)0;

        var layout = Native.GetKeyboardLayout(Native.GetWindowThreadProcessId(fg, out _));
        // flag 0x4: don't alter kernel keyboard state (keeps dead keys working)
        int n = Native.ToUnicodeEx(vk, scanCode, _keyState, _chars, _chars.Length, 0x4, layout);

        if (n == -1) return;                          // dead key, wait for next
        if (n <= 0 || char.IsControl(_chars[0]))      // arrows, Enter, Tab, Esc, etc.
        { _len = 0; return; }

        Append(n);

        if (_version != _vm.SnippetsVersion) Rebuild();
        if (!_lastChars.Contains(_buf[_len - 1])) return; // fast reject: no trigger ends with this char

        foreach (var s in _byLength)
        {
            if (!EndsWith(s.Trigger)) continue;
            _len = 0;
            int erase = s.Trigger.Length;
            string text = s.Text;
            // Run after the hook returns so the final trigger key lands first
            _dispatcher.TryEnqueue(() => Paster.Expand(erase, text, vk));
            return;
        }
    }

    private void Append(int n)
    {
        int overflow = _len + n - MaxBuffer;
        if (overflow > 0)
        {
            Array.Copy(_buf, overflow, _buf, 0, _len - overflow);
            _len -= overflow;
        }
        Array.Copy(_chars, 0, _buf, _len, n);
        _len += n;
    }

    private bool EndsWith(string trigger)
    {
        int t = trigger.Length;
        if (t > _len) return false;
        return trigger.AsSpan().SequenceEqual(_buf.AsSpan(_len - t, t));
    }

    private void Rebuild()
    {
        _byLength = _vm.Snippets
            .Where(s => s.Trigger.Length > 0)
            .OrderByDescending(s => s.Trigger.Length)
            .ToArray();
        _lastChars = _byLength.Select(s => s.Trigger[^1]).ToHashSet();
        _version = _vm.SnippetsVersion;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }
}
