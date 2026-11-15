using Markdig;

namespace WinExpander;

/// <summary>Markdown → HTML / plain text (CommonMark + GitHub-style extensions).</summary>
internal static class Markdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()               // tables, task lists, strikethrough, footnotes, autolinks, …
        .UseEmojiAndSmiley()
        .UseSoftlineBreakAsHardlineBreak()     // single newlines stay newlines (snippet-friendly)
        .Build();

    public static string ToHtml(string md) => Markdig.Markdown.ToHtml(md ?? "", Pipeline);

    public static string ToPlain(string md) =>
        Markdig.Markdown.ToPlainText(md ?? "", Pipeline).Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
}
