# YT Music Terminal User Manual

## 1. What the program does

YT Music Terminal searches YouTube, resolves an audio stream with yt-dlp, and plays it through mpv without opening Chrome or a video window.

Three processes are involved:

- `ytmusic` (`ytmusic.exe` on Windows) displays the terminal interface and manages the player.
- yt-dlp runs temporarily during searches and track loading, then exits.
- mpv remains open while the application is running and handles audio playback.

The program streams audio directly. It does not download songs for offline use. It selects the best available audio format by default.

## 2. Requirements

- Windows 10+, macOS 12+, or a modern 64-bit Linux distribution
- An ANSI-capable interactive terminal
- yt-dlp
- mpv
- Deno, used by yt-dlp for current YouTube JavaScript challenges

The published standalone executable does not require an installed .NET runtime.

## 3. Install the playback tools

### Windows

Open PowerShell in the project directory and run:

```powershell
.\scripts\bootstrap-tools.ps1
```

This downloads verified x64 Windows versions of yt-dlp, Deno, and mpv into the local `tools` directory.

To replace existing copies with current releases:

```powershell
.\scripts\bootstrap-tools.ps1 -Force
```

### macOS

With Homebrew installed:

```shell
brew install yt-dlp mpv deno
```

### Linux

Install yt-dlp, mpv, and preferably Deno with your distribution's package manager. Package names and versions differ by distribution; keep yt-dlp current because YouTube changes frequently.

## 4. Start the program

### Install the `lightytp` command

After downloading and extracting the Windows release, double-click `install.cmd`. This uses a process-only PowerShell execution-policy bypass without changing the permanent system policy.

You can alternatively install it for your Windows user from PowerShell:

```powershell
.\scripts\install.ps1
```

If script execution is disabled, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\install.ps1
```

Open a new terminal, then start the player from any directory:

```powershell
lightytp
```

The installer copies the player and its tools to `%LOCALAPPDATA%\Programs\LightYTP` and adds that directory to your user `PATH`. It does not require administrator access.

Do not use GitHub's automatic Source code ZIP unless you intend to build the project with the .NET SDK. End users need the `LightYTP-win-x64.zip` file from the Releases section.

On macOS or Linux, extract the release archive matching your CPU and run:

```shell
sh ./install.sh
```

This installs `lightytp` under `~/.local/bin`. The installer reports if that directory must be added to `PATH`. macOS users may need to approve the unsigned open-source executable in Privacy & Security after its first launch.

### Run without installing

From the project directory, run the published executable:

```powershell
.\artifacts\win-x64\ytmusic.exe --yt-dlp .\tools\yt-dlp.exe --mpv .\tools\mpv.exe
```

If the project was built after running `scripts\bootstrap-tools.ps1`, the playback tools are copied next to the published application. You can then double-click `artifacts\win-x64\ytmusic.exe` or run it without path options:

```powershell
.\artifacts\win-x64\ytmusic.exe
```

On macOS or Linux, run the matching published executable directly:

```shell
./artifacts/linux-x64/ytmusic
```

You can also run from source when the .NET 10 SDK is installed:

```powershell
.\scripts\run.ps1
```

Start with a search query or YouTube URL directly:

```powershell
lightytp "Daft Punk Get Lucky"
lightytp "https://www.youtube.com/watch?v=..."
```

Manually update the bundled playback tools:

```powershell
lightytp update
```

On macOS and Linux, `lightytp update` prints the appropriate package-manager guidance because those systems own their installed playback tools.

### Configure permanent tool paths

Instead of passing paths on every launch, set environment variables:

```powershell
$env:YTMUSIC_YTDLP = "C:\Tools\yt-dlp.exe"
$env:YTMUSIC_MPV = "C:\Tools\mpv.exe"
```

To persist them for your Windows user account:

```powershell
[Environment]::SetEnvironmentVariable("YTMUSIC_YTDLP", "C:\Tools\yt-dlp.exe", "User")
[Environment]::SetEnvironmentVariable("YTMUSIC_MPV", "C:\Tools\mpv.exe", "User")
```

Open a new terminal after setting persistent environment variables.

On macOS or Linux, use shell environment variables instead:

```shell
export YTMUSIC_YTDLP=/path/to/yt-dlp
export YTMUSIC_MPV=/path/to/mpv
```

## 5. Interface overview

The interface contains four selectable panes and a player area:

```text
┌ Search ────────────────────┐
│ Search text                │
├ Results ───────────┬ Queue ┤
│ Matching tracks    │ Tracks│
│                    ├───────┤
│                    │History│
├────────────────────┴───────┤
│ Now playing and progress   │
└────────────────────────────┘
```

The pane whose title begins with `●` currently has keyboard focus. The highlighted row is the selected track.

The minimum supported terminal size is 70 columns by 18 rows. Resize the terminal if a size warning appears.

## 6. Search and play music

1. Press `/` to focus the search field.
2. Type a song, artist, album, or other search phrase.
3. Press `Enter` to load the first 10 results.
4. Use `Up` and `Down` to select a result.
5. Press `Enter` to resolve and play it immediately.

Press `m` from outside the search field to load 10 more results. Additional batches are available up to 50 results.

Search results come from YouTube search. Adding terms such as `official audio`, an artist name, or an album name can improve the result order.

While the search field is focused, normal characters—including `q`, `a`, and `h`—are added to the query. Press `Escape` to focus the player or `Tab` to move to another section.

## 7. Use the queue

Select a search result or history entry and press `a` to append it to the queue.

Queue controls:

- `Enter`: play the selected queued track.
- `n`: play the next queued track.
- `p`: play the previous queued track.
- `Delete`: remove the selected queue entry.
- `Up` / `Down`: change the selected queue entry.

When a queued track finishes normally, the next entry starts automatically. The queue is saved locally and restored the next time LightYTP starts.

Press `x` to toggle shuffle. Press `r` to cycle between repeat off, repeat track, and repeat queue.

## 8. Use playback history

Every successfully started track is added to history. Press `h` to focus the History pane.

From history you can:

- Press `Enter` to play the selected track again.
- Press `a` to add it to the queue.
- Use `Up` and `Down` to navigate.

The most recent entry appears first. History is limited to 100 entries and persists between sessions.

Press `f` on a selected or currently playing track to add or remove it from local favorites. Press `v` to show favorites and `h` to return to history.

## 9. Playback controls

| Key | Action |
| --- | --- |
| `Space` | Pause or resume |
| `Up` / `Down` | Raise or lower volume by 5% when the player is focused |
| `Left` | Seek backward five seconds |
| `Right` | Seek forward five seconds |
| `+` or `=` | Increase volume by 5% |
| `-` or `_` | Decrease volume by 5% |
| `n` | Next queued track |
| `p` | Previous queued track |
| `s` | Stop playback |
| `x` | Toggle queue shuffle |
| `r` | Cycle repeat mode |
| `F5` | Resume the last track and saved position |

The volume is saved when the program exits normally.

## 10. Navigation and application controls

| Key | Action |
| --- | --- |
| `/` | Focus the search field |
| `Escape` | Focus the player controls from any section |
| `Tab` | Focus the next pane |
| `Shift+Tab` | Focus the previous pane |
| `Up` / `Down` | Move within the focused list |
| `Enter` | Search or play the selected track |
| `m` | Load 10 more search results, up to 50 |
| `a` | Add the selected track to the queue |
| `f` | Add or remove a favorite |
| `v` | Focus favorites |
| `Delete` | Remove the selected queue entry |
| `h` | Focus history |
| `?` | Show the keyboard help line |
| `q` | Quit when the search field is not focused |
| `Ctrl+Q` | Quit from anywhere |

## 11. Diagnostics

Verify that the application can find its dependencies:

```shell
lightytp --diagnose
```

Run a muted end-to-end test that searches YouTube, resolves a track, starts playback, and reports memory usage:

```shell
lightytp --smoke-test
```

Display all command-line options:

```shell
lightytp --help
```

## 12. Settings and data

Application data is stored under `%LOCALAPPDATA%\YtMusicTerminal` on Windows, `~/Library/Application Support/YtMusicTerminal` on macOS, and `~/.local/share/YtMusicTerminal` on most Linux systems.

Files in this directory include:

- `settings.json`: saved volume and configured tool paths.
- `history.json`: the last 100 played tracks.
- `library.json`: queue, favorites, shuffle/repeat state, and resume position.
- `mpv.log`: detailed playback-engine diagnostics.

The initial version does not read browser profiles, browser cookies, passwords, or YouTube account credentials.

## 13. Troubleshooting

### A required tool is missing

On Windows, run:

```powershell
.\scripts\bootstrap-tools.ps1
```

Then use `--diagnose` to confirm the detected paths and versions.

On macOS, run `brew install yt-dlp mpv deno`. On Linux, install the same tools with your distribution's package manager.

### Search or playback suddenly stops working

YouTube changes frequently. On Windows, update the external tools with:

```powershell
.\scripts\bootstrap-tools.ps1 -Force
```

Retry the same search after the update.

On macOS, use `brew upgrade yt-dlp mpv deno`. On Linux, update the packages with your distribution's package manager.

If the mpv connection closes while a track is loading, the player automatically restarts mpv and retries once. If playback still fails, inspect the displayed `mpv.log` path.

### A particular track cannot be played

The video may be private, age-restricted, geographically restricted, unavailable, or protected by a YouTube playback challenge. Try another upload of the track first.

Some YouTube environments require cookies or a PO-token provider. Credential and browser-cookie integration is intentionally not included in the initial version.

### The interface is corrupted or does not fit

- On Windows, use Windows Terminal rather than the legacy Windows Console Host.
- Resize the terminal to at least 70x18.
- Avoid resizing repeatedly while entering a search.
- If the program is interrupted without restoring the terminal, close and reopen that terminal tab.

### There is no audio

- Confirm the operating system has an active output device.
- Increase volume with `+`.
- Check the operating system volume mixer for mpv.
- Run the `--smoke-test` command and inspect its result.

### The program cannot save settings or history

Check that your account can write to the platform data directory described above. A malformed JSON file will be reported rather than silently overwritten.

## 14. Build the program

Install the .NET 10 SDK, then run:

```powershell
.\scripts\build.ps1
```

The trimmed, self-contained Windows executable is written to:

```text
artifacts\win-x64\ytmusic.exe
```

Native AOT publishing is optional and requires the Visual Studio Desktop Development for C++ workload:

```powershell
.\scripts\build.ps1 -NativeAot
```

On macOS or Linux:

```shell
sh ./scripts/build.sh
```

Pass `linux-x64`, `linux-arm64`, `osx-x64`, or `osx-arm64` to override automatic platform detection.

## 15. Resource usage

The measured Windows x64 playback footprint during development was approximately:

- `ytmusic.exe`: 29 MiB working set
- `mpv.exe`: 84 MiB working set
- Combined steady playback: 113 MiB

yt-dlp runs only while searching or resolving a stream and exits before steady playback. Actual measurements vary by operating system, mpv build, audio driver, terminal, and track format.

## 16. Legal and third-party software

YT Music Terminal is an independent application and is not affiliated with YouTube or Google. You are responsible for complying with YouTube's terms, copyright rules, and the licenses of installed tools.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for external tool information.
