# YT Music Terminal

A lightweight, audio-only YouTube player for Windows terminals. It keeps Chromium and WebView out of the playback path: the C# terminal process owns the interface, yt-dlp resolves searches and streams, and mpv plays audio without opening a video window.

For complete installation, navigation, playback, queue, diagnostics, and troubleshooting instructions, see the [User Manual](MANUAL.md). For a quick control reference, see [HOTKEYS.md](HOTKEYS.md).

## Status

The initial player supports:

- YouTube search
- Audio-only streaming
- Play, pause, stop, seek, and volume controls
- An in-session queue with next/previous navigation
- Automatic queue advancement
- Local playback history
- Persistent queue and local favorites
- Shuffle, track repeat, and queue repeat
- Resume the last track and playback position
- Direct startup from a search query or YouTube URL
- Manual playback-tool updates
- Event-driven terminal rendering
- Clean mpv and yt-dlp process shutdown
- Trimmed, self-contained single-file publishing for Windows x64

## Requirements

- Windows 10 or later and an ANSI-capable terminal such as Windows Terminal
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for development
- yt-dlp, mpv, and Deno for playback

Install the playback tools into the ignored local `tools` directory:

```powershell
.\scripts\bootstrap-tools.ps1
```

The script downloads current x64 Windows releases from the projects' GitHub repositories and verifies their published SHA-256 checksums.

## Run from source

```powershell
dotnet run --project .\src\YtMusicTerminal\YtMusicTerminal.csproj -c Release
```

If `dotnet` is not on `PATH`, set `YTMUSIC_DOTNET` to its full path and run:

```powershell
.\scripts\run.ps1
```

## Install the global command

After building, install the player for the current Windows user:

```powershell
.\scripts\install.ps1
```

Open a new terminal and launch it from any directory:

```powershell
lightytp
```

Start with a search or URL directly:

```powershell
lightytp "Daft Punk Get Lucky"
lightytp "https://www.youtube.com/watch?v=..."
```

Update yt-dlp, mpv, and Deno only when requested:

```powershell
lightytp update
```

This installs the self-contained player and playback tools under `%LOCALAPPDATA%\Programs\LightYTP` and adds that directory to your user `PATH`. Administrator access is not required.

## Build a standalone executable

```powershell
.\scripts\build.ps1
```

The self-contained Windows build is written to `artifacts\win-x64`. You can also place playback tools on `PATH` or pass explicit paths.

When `scripts\bootstrap-tools.ps1` has been run before building, the build script automatically copies yt-dlp, Deno, and mpv into `artifacts\win-x64\tools`. In that case, `ytmusic.exe` can be launched directly or by double-clicking it.

Native AOT is also supported when the Visual Studio Desktop Development for C++ workload is installed:

```powershell
.\scripts\build.ps1 -NativeAot
```

```powershell
.\ytmusic.exe --yt-dlp C:\Tools\yt-dlp.exe --mpv C:\Tools\mpv.exe
```

Run dependency diagnostics with:

```powershell
.\ytmusic.exe --diagnose
```

Run a muted end-to-end playback check and print the combined application/mpv working set with:

```powershell
.\ytmusic.exe --smoke-test
```

## Keyboard controls

| Key | Action |
| --- | --- |
| `/` | Focus search |
| `Tab` / `Shift+Tab` | Change pane |
| `Enter` | Search or play selected track |
| `m` | Load 10 more search results, up to 50 |
| `a` | Add selected result/history item to queue |
| `f` | Add or remove the selected track from favorites |
| `v` | Focus favorites |
| `Delete` | Remove selected queued track |
| `Space` | Play or pause |
| `Left` / `Right` | Seek backward/forward five seconds |
| `+` / `-` | Change volume |
| `n` / `p` | Next/previous queued track |
| `x` | Toggle queue shuffle |
| `r` | Cycle repeat off, track, and queue |
| `F5` | Resume the last track and position |
| `s` | Stop |
| `h` | Focus history |
| `?` | Show keyboard help |
| `q` / `Ctrl+Q` | Quit |

While the search field is focused, ordinary characters—including `q`—are entered into the query. Use `Escape` or `Tab` to leave it. `Ctrl+Q` always quits.

## Data and configuration

Settings and history are stored under:

```text
%LOCALAPPDATA%\YtMusicTerminal
```

Queue, favorites, playback modes, and resume state are stored in `library.json`. mpv diagnostic output is written to `mpv.log`. If mpv's IPC connection closes while a track is loading, the player restarts mpv once and retries automatically.

Tool paths can also be supplied through `YTMUSIC_YTDLP` and `YTMUSIC_MPV`.

## Lightweight design

- No browser engine, Electron, WebView, or graphical UI toolkit
- No video decoding or video window
- No audio downloads or transcoding
- Best available audio is selected by default
- Bounded 4 MiB mpv forward cache and 512 KiB back-cache
- yt-dlp exits immediately after each search or stream resolution
- The terminal redraws only for input, state changes, terminal resizing, and playback progress
- History is capped at 100 entries

The current Windows x64 measurement is approximately 113 MiB combined working set during playback: roughly 29 MiB for `ytmusic.exe` and 84 MiB for the selected static mpv build. The published application itself is approximately 12 MiB. yt-dlp creates a temporary spike only while resolving YouTube data and is not resident during playback.

## Development

```powershell
dotnet build .\YtMusicTerminal.slnx -c Release
dotnet run --project .\tests\YtMusicTerminal.Tests\YtMusicTerminal.Tests.csproj -c Release
```

The test project intentionally uses no test framework dependency. It is a deterministic executable that returns a nonzero exit code on failure, keeping restore and trimming analysis minimal.

## YouTube reliability

YouTube changes its playback requirements frequently. Keep yt-dlp and Deno current. Some networks, accounts, or videos may also require cookies or a yt-dlp PO-token provider; the initial version deliberately does not read browser profiles or credentials.

## Legal note

This is an independent client and is not affiliated with YouTube or Google. You are responsible for complying with YouTube's terms, copyright rules, and the licenses of any third-party tools you install or redistribute. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
