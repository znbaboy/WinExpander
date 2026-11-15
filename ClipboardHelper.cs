using System.Runtime.InteropServices;
using System.Text;

namespace WinExpander;

/// <summary>Win32 clipboard: put rich (HTML) + plain text, back up and restore the user's clipboard.</summary>
internal static class ClipboardHelper
{
    public static IntPtr Owner; // our window handle (required so SetClipboardData works after EmptyClipboard)

    private const uint CF_UNICODETEXT = 13;
    private static readonly uint CF_HTML = RegisterClipboardFormat("HTML Format");
    // Keeps our temporary content (and the restore) out of Win+V clipboard history
    private static readonly uint CF_EXCLUDE = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");

    // Formats whose data are GDI handles, not HGLOBAL memory - can't be copied byte-wise
    private static readonly HashSet<uint> HandleFormats = new() { 2, 3, 9, 14, 0x80, 0x82, 0x83, 0x8E };

    public static List<(uint Format, byte[] Data)>? Backup()
    {
        if (!Open()) return null;
        try
        {
            var items = new List<(uint, byte[])>();
            uint f = 0;
            while ((f = EnumClipboardFormats(f)) != 0)
            {
                if (HandleFormats.Contains(f) || (f >= 0x0200 && f <= 0x03FF)) continue; // private/GDI ranges
                var h = GetClipboardData(f);
                if (h == IntPtr.Zero) continue;
                var bytes = ReadGlobal(h);
                if (bytes != null) items.Add((f, bytes));
            }
            return items;
        }
        catch { return null; }
        finally { CloseClipboard(); }
    }

    public static void Restore(List<(uint Format, byte[] Data)> items)
    {
        if (!Open()) return;
        try
        {
            EmptyClipboard();
            foreach (var (f, data) in items) SetGlobal(f, data);
            SetGlobal(CF_EXCLUDE, new byte[] { 0 });
        }
        finally { CloseClipboard(); }
    }

    public static bool SetRich(string plain, string htmlFragment)
    {
        if (!Open()) return false;
        try
        {
            EmptyClipboard();
            SetGlobal(CF_UNICODETEXT, Encoding.Unicode.GetBytes(plain.Replace("\n", "\r\n") + "\0"));
            SetGlobal(CF_HTML, BuildCfHtml(htmlFragment));
            SetGlobal(CF_EXCLUDE, new byte[] { 0 });
            return true;
        }
        finally { CloseClipboard(); }
    }

    /// <summary>Builds the CF_HTML payload (header offsets are UTF-8 byte offsets).</summary>
    private static byte[] BuildCfHtml(string fragment)
    {
        const string header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string pre = "<html><head><meta charset=\"utf-8\"></head><body>\r\n<!--StartFragment-->";
        const string post = "<!--EndFragment-->\r\n</body></html>";

        var utf8 = Encoding.UTF8;
        int startHtml = utf8.GetByteCount(string.Format(header, 0, 0, 0, 0));
        int startFrag = startHtml + utf8.GetByteCount(pre);
        int endFrag = startFrag + utf8.GetByteCount(fragment);
        int endHtml = endFrag + utf8.GetByteCount(post);

        string all = string.Format(header, startHtml, endHtml, startFrag, endFrag) + pre + fragment + post;
        var bytes = utf8.GetBytes(all);
        Array.Resize(ref bytes, bytes.Length + 1); // null terminator
        return bytes;
    }

    private static bool Open()
    {
        for (int i = 0; i < 20; i++)
        {
            if (OpenClipboard(Owner)) return true;
            Thread.Sleep(15);
        }
        return false;
    }

    private static byte[]? ReadGlobal(IntPtr h)
    {
        var size = (long)GlobalSize(h);
        if (size <= 0 || size > 256L * 1024 * 1024) return null;
        var p = GlobalLock(h);
        if (p == IntPtr.Zero) return null;
        try
        {
            var b = new byte[size];
            Marshal.Copy(p, b, 0, b.Length);
            return b;
        }
        finally { GlobalUnlock(h); }
    }

    private static void SetGlobal(uint format, byte[] data)
    {
        var h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)Math.Max(data.Length, 1));
        if (h == IntPtr.Zero) return;
        var p = GlobalLock(h);
        Marshal.Copy(data, 0, p, data.Length);
        GlobalUnlock(h);
        if (SetClipboardData(format, h) == IntPtr.Zero) GlobalFree(h); // on success the system owns it
    }

    private const uint GMEM_MOVEABLE = 0x0002;
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr hMem);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr h);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr h);
}

/// <summary>Performs an expansion: erase the trigger, paste rich + plain text, restore clipboard.</summary>
internal static class Paster
{
    private static bool _busy;

    public static async void Expand(int erase, string markdown, uint triggerVk)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            // Wait for the trigger's last key to be released and the target app to
            // process it - otherwise browsers can insert that char after our paste.
            for (int i = 0; i < 50 && Native.Down((int)triggerVk); i++) await Task.Delay(10);
            await Task.Delay(40);

            var backup = ClipboardHelper.Backup();
            string plain = AppSettings.Current.PlainText == PlainTextMode.MarkdownSource
                ? markdown
                : Markdown.ToPlain(markdown);
            if (!ClipboardHelper.SetRich(plain, Markdown.ToHtml(markdown))) return;
            Native.SendEraseAndPaste(erase);
            await Task.Delay(800); // give the target app time to read the clipboard
            if (backup != null) ClipboardHelper.Restore(backup);
        }
        catch (Exception ex) { Program.Log(ex); }
        finally { _busy = false; }
    }
}
