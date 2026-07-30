# YT Music Terminal

A lightweight, audio-only YouTube player for Windows, macOS, and Linux terminals. It keeps Chromium and WebView out of the playback path: the C# terminal process owns the interface, yt-dlp resolves searches and streams, and mpv plays audio without opening a video window.

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
- Trimmed, self-contained single-file publishing for Windows, macOS, and Linux

## Requirements

- Windows 10+, macOS 12+, or a modern 64-bit Linux distribution
- An ANSI-capable interactive terminal
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for development
- yt-dlp, mpv, and Deno for playback

On Windows, install the playback tools into the ignored local `tools` directory:

```powershell
.\scripts\bootstrap-tools.ps1
```

The script downloads current x64 Windows releases from the projects' GitHub repositories and verifies their published SHA-256 checksums. On macOS, install dependencies with `brew install yt-dlp mpv deno`. On Linux, install yt-dlp, mpv, and preferably Deno with your distribution's package manager.

## Run from source

```shell
dotnet run --project src/YtMusicTerminal/YtMusicTerminal.csproj -c Release
```

If `dotnet` is not on `PATH`, set `YTMUSIC_DOTNET` to its full path and run:

```powershell
.\scripts\run.ps1
```

## Install the global command

### Windows

After building, double-click `install.cmd`. It runs the installer with a process-only PowerShell execution-policy bypass and does not change the computer's permanent policy.

Alternatively, install from PowerShell:

```powershell
.\scripts\install.ps1
```

If PowerShell reports that script execution is disabled, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\install.ps1
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

### macOS and Linux

Install yt-dlp, mpv, and Deno first. Extract the release archive, then run:

```shell
sh ./install.sh
```

The installer copies the self-contained executable to `~/.local/bin/lightytp` without requiring administrator access. If that directory is not on `PATH`, it prints the exact line to add to your shell profile.

GitHub's automatic **Source code ZIP** is for developers and does not include a published executable. End users should download the release asset matching their operating system and CPU architecture.

| Platform | Release asset |
| --- | --- |
| Windows x64 | `LightYTP-win-x64.zip` |
| Linux x64 | `LightYTP-linux-x64.tar.gz` |
| Linux ARM64 | `LightYTP-linux-arm64.tar.gz` |
| macOS Intel | `LightYTP-macos-x64.tar.gz` |
| macOS Apple Silicon | `LightYTP-macos-arm64.tar.gz` |

## Build a standalone executable

Windows:

```powershell
.\scripts\build.ps1
```

The self-contained Windows build is written to `artifacts\win-x64`. You can also place playback tools on `PATH` or pass explicit paths.

When `scripts\bootstrap-tools.ps1` has been run before building, the build script automatically copies yt-dlp, Deno, and mpv into `artifacts\win-x64\tools`. In that case, `ytmusic.exe` can be launched directly or by double-clicking it.

Native AOT is also supported when the Visual Studio Desktop Development for C++ workload is installed:

```powershell
.\scripts\build.ps1 -NativeAot
```

macOS or Linux:

```shell
sh ./scripts/build.sh
```

The script detects `linux-x64`, `linux-arm64`, `osx-x64`, or `osx-arm64`. You can pass one of those runtime identifiers explicitly. All published executables are self-contained and do not require a separately installed .NET runtime.

Explicit dependency paths can be passed with `--yt-dlp` and `--mpv` on every platform.

Run dependency diagnostics with:

```shell
lightytp --diagnose
```

Run a muted end-to-end playback check and print the combined application/mpv working set with:

```shell
lightytp --smoke-test
```

## Keyboard controls

| Key | Action |
| --- | --- |
| `/` | Focus search |
| `Tab` / `Shift+Tab` | Change pane |
| `Escape` | Focus the player controls |
| `Enter` | Search or play selected track |
| `m` | Load 10 more search results, up to 50 |
| `a` | Add selected result/history item to queue |
| `f` | Add or remove the selected track from favorites |
| `v` | Focus favorites |
| `Delete` | Remove selected queued track |
| `Space` | Play or pause |
| `Left` / `Right` | Seek backward/forward five seconds |
| `Up` / `Down` | Change volume when the player is focused |
| `+` / `-` | Change volume |
| `n` / `p` | Next/previous queued track |
| `x` | Toggle queue shuffle |
| `r` | Cycle repeat off, track, and queue |
| `F5` | Resume the last track and position |
| `s` | Stop |
| `h` | Focus history |
| `?` | Show keyboard help |
| `q` / `Ctrl+Q` | Quit |

While the search field is focused, ordinary characters—including `q`—are entered into the query. Use `Escape` to focus the player or `Tab` to move to another section. `Ctrl+Q` always quits.

## Data and configuration

Settings and history are stored under:

Data follows the operating system's local application-data convention: `%LOCALAPPDATA%\YtMusicTerminal` on Windows, `~/Library/Application Support/YtMusicTerminal` on macOS, and `~/.local/share/YtMusicTerminal` on most Linux systems.

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

The measured Windows x64 footprint is approximately 113 MiB combined during playback: roughly 29 MiB for LightYTP and 84 MiB for the selected static mpv build. Actual usage varies by operating system and mpv package. yt-dlp creates a temporary spike only while resolving YouTube data and is not resident during playback.

## Development

```shell
dotnet build YtMusicTerminal.slnx -c Release
dotnet run --project tests/YtMusicTerminal.Tests/YtMusicTerminal.Tests.csproj -c Release
```

The test project intentionally uses no test framework dependency. It is a deterministic executable that returns a nonzero exit code on failure, keeping restore and trimming analysis minimal.

## YouTube reliability

YouTube changes its playback requirements frequently. Keep yt-dlp and Deno current. Some networks, accounts, or videos may also require cookies or a yt-dlp PO-token provider; the initial version deliberately does not read browser profiles or credentials.

## Legal note

This is an independent client and is not affiliated with YouTube or Google. You are responsible for complying with YouTube's terms, copyright rules, and the licenses of any third-party tools you install or redistribute. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
