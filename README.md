# Text Expander

A minimal, portable, open-source text expander for Windows, built with WinUI 3.

Type a trigger (e.g. `;sig`) anywhere → it's replaced with your snippet.

![Text Expander in dark mode](docs/screenshot.png)

## Download
Grab `TextExpander-portable-x64.zip` (or `arm64`) from **Releases**, unzip anywhere, run `TextExpander.exe`.
No installer, no .NET or Windows App SDK install needed.

## Features
- Fluent design (Mica, custom title bar), light/dark theme
- Snippets written in **Markdown** (CommonMark + tables, task lists, strikethrough, emoji, autolinks…)
- Live preview and a formatting toolbar (Ctrl+B / Ctrl+I / Ctrl+K)
- Expansions paste as **rich text** in apps that support it (Word, Outlook, Gmail, Teams…) and as clean plain text everywhere else (Notepad, terminals)
- Your clipboard is restored after each expansion, and nothing lands in Win+V history
- Auto-saves as you type

## Use
- **New snippet** (Ctrl+N), set a trigger like `;sig`, write the body in Markdown.
- Type the trigger anywhere and it gets replaced.
- Toggle switch pauses expansion. Expansion is disabled while the app itself is focused.
- Snippets are saved to `snippets.json` next to the exe (portable). If that folder isn't writable, it falls back to `%LOCALAPPDATA%\TextExpander\`.
- Auto-start: put a shortcut to the exe in `shell:startup`.

## Build from source
Requires Windows 10 1809+ and the .NET 8 SDK.

```
# run
dotnet run -p:Platform=x64

# portable build (installs .NET 8 SDK locally if missing)
powershell -ExecutionPolicy Bypass -File .\build.ps1

# or manually
dotnet publish -c Release -r win-x64 -p:Platform=x64 -o publish\TextExpander
```

Or open in Visual Studio 2022 (with the "WinUI application development" workload) and press F5.

## Releases
GitHub Actions builds portable zips on every push. Push a tag like `v1.0.0` to publish a release with the zips attached.

## Notes
- Uses a global low-level keyboard hook + `SendInput`. Nothing is logged or sent anywhere.
- Won't expand inside apps running as admin unless this app is also run as admin.
- Prefix triggers with a symbol (`;`, `//`) so they don't fire inside normal words.

## License
MIT. See [LICENSE](LICENSE).
