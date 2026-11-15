namespace WinExpander;

/// <summary>Wraps rendered Markdown in a Fluent-styled HTML page for the preview pane.</summary>
internal static class PreviewPage
{
    public static string Build(string body, bool dark)
    {
        string fg = dark ? "#ffffff" : "#1a1a1a";
        string muted = dark ? "rgba(255,255,255,.62)" : "rgba(0,0,0,.6)";
        string border = dark ? "rgba(255,255,255,.12)" : "rgba(0,0,0,.12)";
        string codeBg = dark ? "rgba(255,255,255,.07)" : "rgba(0,0,0,.05)";
        string link = dark ? "#99ebff" : "#005fb8";
        string bg = dark ? "#1e1e1e" : "#fbfbfb"; // explicit: WebView2's own default follows the OS theme

        return $$"""
        <!DOCTYPE html>
        <html><head><meta charset="utf-8">
        <style>
          :root { color-scheme: {{(dark ? "dark" : "light")}}; }
          html, body { background: {{bg}}; margin: 0; }
          body { padding: 12px 16px; color: {{fg}}; font: 14px/1.55 "Segoe UI Variable Text", "Segoe UI", sans-serif; word-wrap: break-word; }
          h1, h2, h3, h4 { font-family: "Segoe UI Variable Display", "Segoe UI", sans-serif; font-weight: 600; margin: 1em 0 .4em; line-height: 1.25; }
          h1 { font-size: 1.75em; } h2 { font-size: 1.4em; } h3 { font-size: 1.15em; }
          body > :first-child { margin-top: 0; }
          p, ul, ol, table, pre, blockquote { margin: 0 0 .8em; }
          a { color: {{link}}; text-decoration: none; } a:hover { text-decoration: underline; }
          code { font: 12.5px "Cascadia Mono", Consolas, monospace; background: {{codeBg}}; padding: .15em .35em; border-radius: 4px; }
          pre { background: {{codeBg}}; padding: 10px 12px; border-radius: 6px; overflow-x: auto; }
          pre code { background: none; padding: 0; }
          blockquote { border-left: 3px solid {{border}}; padding-left: 12px; color: {{muted}}; }
          table { border-collapse: collapse; }
          th, td { border: 1px solid {{border}}; padding: 5px 10px; }
          th { font-weight: 600; }
          hr { border: 0; border-top: 1px solid {{border}}; }
          li:has(> input[type=checkbox]) { list-style: none; margin-left: -1.3em; }
          li > input[type=checkbox] { margin: 0 .45em 0 0; vertical-align: middle; }
          img { max-width: 100%; }
          ::-webkit-scrollbar { width: 8px; } ::-webkit-scrollbar-thumb { background: {{border}}; border-radius: 4px; }
        </style></head>
        <body>{{body}}</body></html>
        """;
    }
}
