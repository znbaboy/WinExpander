using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinExpander;

public partial class Snippet : ObservableObject
{
    private string _trigger = "";
    private string _text = "";

    public string Trigger
    {
        get => _trigger;
        set { if (SetProperty(ref _trigger, value ?? "")) OnPropertyChanged(nameof(DisplayTrigger)); }
    }

    /// <summary>Snippet body, written in Markdown (line endings normalized to \n).</summary>
    public string Text
    {
        get => _text;
        set
        {
            var v = (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n'); // TextBox uses \r internally
            if (SetProperty(ref _text, v)) OnPropertyChanged(nameof(Summary));
        }
    }

    [JsonIgnore]
    public string DisplayTrigger => string.IsNullOrWhiteSpace(Trigger) ? "(no trigger)" : Trigger;

    [JsonIgnore]
    public string Summary
    {
        get
        {
            var line = Markdown.ToPlain(Text)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            return string.IsNullOrEmpty(line) ? "Empty snippet" : line;
        }
    }
}

internal static class SnippetStore
{
    // Portable: keep snippets.json next to the exe. Falls back to %LOCALAPPDATA%
    // if the app folder isn't writable (e.g. installed under Program Files).
    private static readonly string Dir = PickDir();
    public static string Folder => Dir;
    private static readonly string FilePath = Path.Combine(Dir, "snippets.json");

    private static string PickDir()
    {
        string appDir = AppContext.BaseDirectory;
        try
        {
            string probe = Path.Combine(appDir, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return appDir;
        }
        catch
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(local, "WinExpander");
            string legacy = Path.Combine(local, "TextExpander"); // previous app name
            if (!Directory.Exists(dir) && Directory.Exists(legacy))
            {
                try { Directory.Move(legacy, dir); } catch { return legacy; }
            }
            return dir;
        }
    }

    public static ObservableCollection<Snippet> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<Snippet>>(File.ReadAllText(FilePath));
                if (list != null) return new ObservableCollection<Snippet>(list);
            }
        }
        catch { /* fall through to defaults */ }

        return new ObservableCollection<Snippet>
        {
            new() { Trigger = ";sig", Text = "Best regards,\n**Your Name**" },
            new() { Trigger = ";em",  Text = "me@example.com" },
            new()
            {
                Trigger = ";md",
                Text = "## Markdown works\n\n- **Bold**, *italic*, ~~strike~~, `code`\n- [Links](https://example.com)\n- [x] Task lists\n\n| Tables | too |\n|---|---|\n| a | b |"
            },
        };
    }

    public static void Save(IEnumerable<Snippet> snippets)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(snippets.ToList(), new JsonSerializerOptions { WriteIndented = true }));
    }
}

public enum PlainTextMode { MarkdownSource, StrippedText }

public enum AppTheme { System, Light, Dark }

/// <summary>App settings (settings.json next to snippets.json).</summary>
public class AppSettings
{
    /// <summary>What apps that only accept plain text receive.</summary>
    public PlainTextMode PlainText { get; set; } = PlainTextMode.MarkdownSource;

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Closing the window hides it to the tray instead of exiting.</summary>
    public bool CloseToTray { get; set; } = true;

    public static AppSettings Current { get; private set; } = Load();

    private static string FilePath => Path.Combine(SnippetStore.Folder, "settings.json");

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SnippetStore.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Program.Log(ex); }
    }
}
