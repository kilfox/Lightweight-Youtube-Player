# LightYTP Terminal User Manual

This manual covers the terminal edition. See [GUI_MANUAL.md](GUI_MANUAL.md) for the optional graphical edition.

## 1. What the program does

LightYTP searches YouTube, resolves an audio stream with yt-dlp, and plays it through mpv without opening Chrome or a video window.

Three processes are involved:

- `ytmusic` (`ytmusic.exe` on Windows) displays the terminal interface and manages the player.
- yt-dlp runs temporarily during searches and track loading, then exits.
- mpv remains open while the application is running and handles audio playback.

The program streams audio directly. It does not download songs for offline use. It selects the best available audio format by default, and YouTube video ads are not inserted into the resolved audio stream.

## 2. Download LightYTP

Open the [latest GitHub release](https://github.com/kilfox/Lightweight-Youtube-Player/releases/latest) and download the file matching your computer:

| Platform | Release file |
| --- | --- |
| Windows 64-bit | `LightYTP-win-x64.zip` |
| Linux Intel/AMD 64-bit | `LightYTP-linux-x64.tar.gz` |
| Linux ARM64 | `LightYTP-linux-arm64.tar.gz` |
| Intel Mac | `LightYTP-macos-x64.tar.gz` |
| Apple Silicon Mac | `LightYTP-macos-arm64.tar.gz` |

Do not download GitHub's automatic Source code ZIP or TAR.GZ files. Those contain source code, not the ready-to-run application. The release executable is self-contained and does not require the .NET SDK or runtime.

## 3. Install LightYTP

### Windows

1. Right-click `LightYTP-win-x64.zip` and choose **Extract All**.
2. Open the extracted folder.
3. Double-click `install.cmd`.
4. Open a new Windows Terminal or PowerShell window.
5. Run `lightytp`.

The Windows package includes yt-dlp, mpv, and Deno. Installation does not require administrator access. If Windows displays a security prompt, confirm that you downloaded the file from the official `kilfox/Lightweight-Youtube-Player` release page before continuing.

### macOS

1. Install [Homebrew](https://brew.sh/) if it is not already installed.
2. Install the playback tools:

   ```shell
   brew install yt-dlp mpv deno
   ```

3. Extract the downloaded Intel or Apple Silicon release archive.
4. Open Terminal in the extracted folder.
5. Run:

   ```shell
   sh ./install.sh
   ```

6. Open a new terminal and run `lightytp`.

If macOS blocks the unsigned open-source executable, open **System Settings → Privacy & Security**, allow LightYTP, and launch it again.

### Linux

1. Install yt-dlp, mpv, and Deno with your distribution's package manager.
2. Extract the downloaded x64 or ARM64 archive.
3. Open a terminal in the extracted folder.
4. Run:

   ```shell
   sh ./install.sh
   ```

5. Open a new terminal and run `lightytp`.

The installer copies LightYTP to `~/.local/bin`. If that directory is not on `PATH`, the installer prints the exact line to add to your shell profile.

## 4. Launch the program

Start normally, start with a search, or open a YouTube URL directly:

```shell
lightytp
lightytp "Daft Punk Get Lucky"
lightytp "https://www.youtube.com/watch?v=..."
```

To update playback tools when YouTube changes cause problems:

```shell
lightytp update
```

Windows updates its bundled tools. macOS and Linux display the appropriate package-manager guidance.

To uninstall the terminal edition, run:

```shell
lightytp uninstall
```

Confirm when prompted. For unattended use, run `lightytp uninstall --yes`. The command removes the installed player and its launcher or PATH entry, but keeps favorites, history, queue, and settings for future reinstalls. Portable copies that were not installed with the standard installer must be removed manually.

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

When a queued track finishes normally, the next entry starts automatically. LightYTP resolves only the next queued track after a short idle delay so normal queue transitions are faster without competing with searches. If that resolution is already running when you press `n`, LightYTP reuses it instead of starting another process. Search results and resolved stream URLs are kept in a small, time-limited memory cache that is cleared when LightYTP exits. The queue itself is saved locally and restored the next time LightYTP starts.

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

LightYTP does not read browser profiles, browser cookies, passwords, or YouTube account credentials.

## 13. Troubleshooting

### A required tool is missing

On Windows, download `LightYTP-win-x64.zip` again from the latest release, extract all files, and rerun `install.cmd`.

Then use `lightytp --diagnose` to confirm the detected paths and versions.

On macOS, run `brew install yt-dlp mpv deno`. On Linux, install the same tools with your distribution's package manager.

### Search or playback suddenly stops working

YouTube changes frequently. Update the playback tools with:

```shell
lightytp update
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

## 14. Resource usage

The measured Windows x64 playback footprint during development was approximately:

- `ytmusic.exe`: 29 MiB working set
- `mpv.exe`: 84 MiB working set
- Combined steady playback: 113 MiB

yt-dlp runs only while searching or resolving a stream and exits before steady playback. Actual measurements vary by operating system, mpv build, audio driver, terminal, and track format.

## 15. Legal and third-party software

LightYTP is an independent application and is not affiliated with YouTube or Google. You are responsible for complying with YouTube's terms, copyright rules, and the licenses of installed tools.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for external tool information.
