# YT Music Terminal User Manual

## 1. What the program does

YT Music Terminal searches YouTube, resolves an audio stream with yt-dlp, and plays it through mpv without opening Chrome or a video window.

Three processes are involved:

- `ytmusic.exe` displays the terminal interface and manages the player.
- `yt-dlp.exe` runs temporarily during searches and track loading, then exits.
- `mpv.exe` remains open while the application is running and handles audio playback.

The program streams audio directly. It does not download songs for offline use. It selects the best available audio format by default.

## 2. Requirements

- Windows 10 or later
- Windows Terminal, PowerShell, or another ANSI-capable terminal
- yt-dlp
- mpv
- Deno, used by yt-dlp for current YouTube JavaScript challenges

The standalone `ytmusic.exe` does not require an installed .NET runtime.

## 3. Install the playback tools

Open PowerShell in the project directory and run:

```powershell
.\scripts\bootstrap-tools.ps1
```

This downloads verified x64 Windows versions of yt-dlp, Deno, and mpv into the local `tools` directory.

To replace existing copies with current releases:

```powershell
.\scripts\bootstrap-tools.ps1 -Force
```

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

### Run without installing

From the project directory, run the published executable:

```powershell
.\artifacts\win-x64\ytmusic.exe --yt-dlp .\tools\yt-dlp.exe --mpv .\tools\mpv.exe
```

If the project was built after running `scripts\bootstrap-tools.ps1`, the playback tools are copied next to the published application. You can then double-click `artifacts\win-x64\ytmusic.exe` or run it without path options:

```powershell
.\artifacts\win-x64\ytmusic.exe
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

While the search field is focused, normal characters—including `q`, `a`, and `h`—are added to the query. Press `Escape` or `Tab` to leave the search field.

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

```powershell
.\artifacts\win-x64\ytmusic.exe --yt-dlp .\tools\yt-dlp.exe --mpv .\tools\mpv.exe --diagnose
```

Run a muted end-to-end test that searches YouTube, resolves a track, starts playback, and reports memory usage:

```powershell
.\artifacts\win-x64\ytmusic.exe --yt-dlp .\tools\yt-dlp.exe --mpv .\tools\mpv.exe --smoke-test
```

Display all command-line options:

```powershell
.\artifacts\win-x64\ytmusic.exe --help
```

## 12. Settings and data

Application data is stored under:

```text
%LOCALAPPDATA%\YtMusicTerminal
```

Files in this directory include:

- `settings.json`: saved volume and configured tool paths.
- `history.json`: the last 100 played tracks.
- `library.json`: queue, favorites, shuffle/repeat state, and resume position.
- `mpv.log`: detailed playback-engine diagnostics.

The initial version does not read browser profiles, browser cookies, passwords, or YouTube account credentials.

## 13. Troubleshooting

### A required tool is missing

Run:

```powershell
.\scripts\bootstrap-tools.ps1
```

Then use `--diagnose` to confirm the detected paths and versions.

### Search or playback suddenly stops working

YouTube changes frequently. Update the external tools:

```powershell
.\scripts\bootstrap-tools.ps1 -Force
```

Retry the same search after the update.

If the mpv connection closes while a track is loading, the player automatically restarts mpv and retries once. If playback still fails, inspect `%LOCALAPPDATA%\YtMusicTerminal\mpv.log`; the displayed error also includes this path.

### A particular track cannot be played

The video may be private, age-restricted, geographically restricted, unavailable, or protected by a YouTube playback challenge. Try another upload of the track first.

Some YouTube environments require cookies or a PO-token provider. Credential and browser-cookie integration is intentionally not included in the initial version.

### The interface is corrupted or does not fit

- Use Windows Terminal rather than the legacy Windows Console Host.
- Resize the terminal to at least 70x18.
- Avoid resizing repeatedly while entering a search.
- If the program is interrupted without restoring the terminal, close and reopen that terminal tab.

### There is no audio

- Confirm Windows has an active output device.
- Increase volume with `+`.
- Check the Windows volume mixer for `mpv.exe`.
- Run the `--smoke-test` command and inspect its result.

### The program cannot save settings or history

Check that your Windows account can write to `%LOCALAPPDATA%\YtMusicTerminal`. A malformed JSON file will be reported rather than silently overwritten.

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

## 15. Resource usage

The measured Windows x64 playback footprint during development was approximately:

- `ytmusic.exe`: 29 MiB working set
- `mpv.exe`: 84 MiB working set
- Combined steady playback: 113 MiB

yt-dlp runs only while searching or resolving a stream and exits before steady playback. Actual measurements vary by Windows version, mpv build, audio driver, terminal, and track format.

## 16. Legal and third-party software

YT Music Terminal is an independent application and is not affiliated with YouTube or Google. You are responsible for complying with YouTube's terms, copyright rules, and the licenses of installed tools.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for external tool information.
