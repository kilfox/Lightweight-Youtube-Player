# LightYTP

A lightweight, audio-only YouTube player for Windows, macOS, and Linux. Choose the original terminal edition or the optional compact Winamp-style GUI. Both play music without keeping Chrome, Electron, or a video window open, and YouTube video ads are not inserted into the resolved audio stream.

**Download the app from the [latest GitHub release](https://github.com/kilfox/Lightweight-Youtube-Player/releases/latest). Do not download GitHub's automatic Source code archive.**

For the terminal edition, see the [Terminal Manual](MANUAL.md) and [Terminal Hotkeys](HOTKEYS.md). For the graphical edition, see the [GUI Manual](GUI_MANUAL.md) and [GUI Hotkeys](GUI_HOTKEYS.md).

## Status

Both editions support:

- YouTube search
- Audio-only streaming
- Play, pause, stop, seek, and volume controls
- An in-session queue with next/previous navigation
- Automatic queue advancement
- One-track queue prefetch for faster transitions
- Bounded in-session search and stream-resolution caches
- Local playback history
- Persistent queue and local favorites
- Shuffle, track repeat, and queue repeat
- Resume the last track and playback position
- Direct startup from a search query or YouTube URL
- Manual playback-tool updates
- Shared queue, history, favorites, volume, and resume data
- Clean mpv and yt-dlp process shutdown
- Trimmed, self-contained single-file publishing for Windows, macOS, and Linux

The terminal package remains the smallest option and has no GUI dependencies. The GUI is a separate download and does not install or require the terminal edition.

## Download the correct release

| Your computer | Terminal edition | GUI edition |
| --- | --- | --- |
| Windows 64-bit | `LightYTP-win-x64.zip` | `LightYTP-GUI-win-x64.zip` |
| Linux Intel/AMD 64-bit | `LightYTP-linux-x64.tar.gz` | `LightYTP-GUI-linux-x64.tar.gz` |
| Linux ARM64 | `LightYTP-linux-arm64.tar.gz` | `LightYTP-GUI-linux-arm64.tar.gz` |
| Intel Mac | `LightYTP-macos-x64.tar.gz` | `LightYTP-GUI-macos-x64.tar.gz` |
| Apple Silicon Mac (M1 or newer) | `LightYTP-macos-arm64.tar.gz` | `LightYTP-GUI-macos-arm64.tar.gz` |

The release contains a self-contained LightYTP executable. Users do not need the .NET SDK.

## Install and launch

### Windows

1. Download `LightYTP-win-x64.zip` for the terminal or `LightYTP-GUI-win-x64.zip` for the GUI from the [latest release](https://github.com/kilfox/Lightweight-Youtube-Player/releases/latest).
2. Right-click the ZIP and choose **Extract All**.
3. Open the extracted folder and double-click `install.cmd`.
4. Launch the terminal edition from a new terminal or open **LightYTP GUI** from the Start menu:

```powershell
lightytp
lightytp-gui
```

The Windows release already includes yt-dlp, mpv, and Deno. Administrator access and a separate .NET installation are not required.

### macOS

1. Install the playback tools with [Homebrew](https://brew.sh/):

   ```shell
   brew install yt-dlp mpv deno
   ```

2. Download the terminal or GUI archive for Intel or Apple Silicon from the [latest release](https://github.com/kilfox/Lightweight-Youtube-Player/releases/latest).
3. Extract the archive and open Terminal in the extracted folder.
4. Install the global command:

   ```shell
   sh ./install.sh
   ```

5. Run `lightytp` for the terminal edition. The GUI installer places **LightYTP GUI** in `~/Applications` and also adds `lightytp-gui`.

If macOS blocks the unsigned open-source executable, open **System Settings → Privacy & Security** and allow LightYTP, then launch it again.

### Linux

1. Install yt-dlp, mpv, and Deno with your distribution's package manager.
2. Download the terminal or GUI archive for x64 or ARM64 from the [latest release](https://github.com/kilfox/Lightweight-Youtube-Player/releases/latest).
3. Extract it and open a terminal in that folder.
4. Install the global command:

   ```shell
   sh ./install.sh
   ```

5. Run `lightytp` for the terminal edition. Open **LightYTP GUI** from the application menu or run `lightytp-gui`.

The installer uses `~/.local/bin` and prints the exact `PATH` line if your shell does not already include that directory.

## Start using LightYTP

Launch normally, with a search, or with a YouTube URL:

```shell
lightytp
lightytp "Daft Punk Get Lucky"
lightytp "https://www.youtube.com/watch?v=..."
```

Launch the GUI normally or with an initial search or YouTube URL:

```shell
lightytp-gui
lightytp-gui "Daft Punk Get Lucky"
lightytp-gui "https://www.youtube.com/watch?v=..."
```

Inside the player:

1. Type a song or artist and press `Enter`.
2. Select a result with `Up` or `Down`.
3. Press `Enter` to play it.
4. Press `Escape` to focus the player controls.
5. Use `Up` and `Down` for volume, `Space` to pause, and `Left` or `Right` to seek.

Update playback tools when YouTube changes cause search or playback problems:

```shell
lightytp update
```

Run dependency diagnostics with:

```shell
lightytp --diagnose
```

Run a muted end-to-end playback check and print the combined application/mpv working set with:

```shell
lightytp --smoke-test
```

## Uninstall

Remove the terminal edition from a terminal:

```shell
lightytp uninstall
```

To remove the GUI edition, open LightYTP GUI and click **UNINSTALL** at the bottom of the window. Both actions ask for confirmation, remove only the selected edition, and keep favorites, history, queue, and settings in case you reinstall later.

## Terminal keyboard controls

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

The GUI supports mouse controls plus `Ctrl+F`, `Space`, arrow keys, `N`, `P`, `S`, and `Ctrl+Q`. See [GUI_HOTKEYS.md](GUI_HOTKEYS.md).

## Data and configuration

Data follows the operating system's local application-data convention: `%LOCALAPPDATA%\YtMusicTerminal` on Windows, `~/Library/Application Support/YtMusicTerminal` on macOS, and `~/.local/share/YtMusicTerminal` on most Linux systems.

Queue, favorites, playback modes, and resume state are stored in `library.json`. mpv diagnostic output is written to `mpv.log`. If mpv's IPC connection closes while a track is loading, the player restarts mpv once and retries automatically.

Tool paths can also be supplied through `YTMUSIC_YTDLP` and `YTMUSIC_MPV`.

## Lightweight design

- No browser engine, Electron, WebView, or video renderer in either edition
- No graphical toolkit in the terminal package; Avalonia and Skia are isolated to the optional GUI package
- No video decoding or video window
- No audio downloads or transcoding
- Best available audio is selected by default
- Bounded 4 MiB mpv forward cache and 512 KiB back-cache
- yt-dlp exits immediately after each search or stream resolution
- Search results and resolved stream URLs are cached only for the current session with fixed size and expiry limits
- Only the next queued track is resolved after a short idle delay; searches take priority and an active resolution is reused
- The terminal redraws only for input, state changes, terminal resizing, and playback progress
- History is capped at 100 entries

The measured Windows x64 footprint is approximately 113 MiB combined during playback: roughly 29 MiB for LightYTP and 84 MiB for the selected static mpv build. Actual usage varies by operating system and mpv package. yt-dlp creates a temporary spike only while resolving YouTube data and is not resident during playback.

The optional Windows GUI executable is approximately 21 MiB. Its measured settled idle working set is about 146 MiB plus 25 MiB for idle mpv; playback usage varies with the stream and renderer. Choose the terminal edition when minimum memory use is the priority.

## YouTube reliability

YouTube changes its playback requirements frequently. Keep yt-dlp and Deno current. Some networks, accounts, or videos may also require cookies or a yt-dlp PO-token provider; the initial version deliberately does not read browser profiles or credentials.

## Legal note

This is an independent client and is not affiliated with YouTube or Google. You are responsible for complying with YouTube's terms, copyright rules, and the licenses of any third-party tools you install or redistribute. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
