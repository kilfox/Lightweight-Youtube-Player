# LightYTP GUI User Manual

LightYTP GUI is the optional compact graphical edition. It uses the same audio-only playback engine and local data as LightYTP Terminal, but it does not require a terminal window while running.

## Choose the correct download

Download the GUI archive for your computer from the [latest GitHub release](https://github.com/kilfox/Lightweight-Youtube-Player/releases/latest):

| Platform | GUI release file |
| --- | --- |
| Windows 64-bit | `LightYTP-GUI-win-x64.zip` |
| Linux Intel/AMD 64-bit | `LightYTP-GUI-linux-x64.tar.gz` |
| Linux ARM64 | `LightYTP-GUI-linux-arm64.tar.gz` |
| Intel Mac | `LightYTP-GUI-macos-x64.tar.gz` |
| Apple Silicon Mac | `LightYTP-GUI-macos-arm64.tar.gz` |

Do not download GitHub's automatic Source code archive. The release packages are self-contained and do not require a separate .NET installation.

## Install

### Windows

1. Extract `LightYTP-GUI-win-x64.zip` completely.
2. Double-click `install.cmd`.
3. Open **LightYTP GUI** from the Start menu.

The Windows GUI package includes yt-dlp, mpv, and Deno and does not require administrator access. You can also launch it from a new terminal with `lightytp-gui`.

### macOS

1. Install the playback tools with `brew install yt-dlp mpv deno`.
2. Extract the correct Intel or Apple Silicon GUI archive.
3. Open Terminal in the extracted folder and run `sh ./install.sh`.
4. Open **LightYTP GUI** from `~/Applications`.

The installer also creates the optional `lightytp-gui` terminal command. If macOS blocks the unsigned application, open **System Settings → Privacy & Security**, allow it, and launch it again.

### Linux

1. Install yt-dlp, mpv, and Deno with your distribution's package manager.
2. Extract the correct x64 or ARM64 GUI archive.
3. Open a terminal in the extracted folder and run `sh ./install.sh`.
4. Open **LightYTP GUI** from your application menu or launch it with `lightytp-gui`.

The installer uses `~/.local/share/lightytp-gui` for the application and `~/.local/bin` for its launcher.

## Search and play

1. Enter a song, artist, or YouTube URL in the search box.
2. Press `Enter` or click **SEARCH**.
3. Double-click a result or select it and click **PLAY**.
4. Click **LOAD 10 MORE** for another result batch, up to 50 results.

You can also launch the GUI with an initial search or URL:

```shell
lightytp-gui "Daft Punk Get Lucky"
lightytp-gui "https://www.youtube.com/watch?v=..."
```

## Player controls

- Use the transport buttons for previous, play/pause, stop, and next.
- Drag the timeline to seek.
- Use the volume slider to change volume.
- Click the heart button to add or remove the current track from favorites.
- Double-click list entries to play them.

See [GUI_HOTKEYS.md](GUI_HOTKEYS.md) for keyboard controls.

## Queue, history, and favorites

- **Queue**: add tracks from search or history, play them in order, remove entries, or clear the list.
- **History**: stores the latest 100 unique played tracks with the newest first.
- **Favorites**: stores selected tracks locally and shares them with the terminal edition.

The GUI also respects shuffle and repeat settings last selected in the terminal edition.
Only the next queued track is resolved after a short idle delay for faster transitions. Searches take priority, and an active resolution is reused if you request that track. Search results and resolved stream URLs use a small, time-limited memory cache that is cleared when the GUI exits.

## Shared local data

Both editions use the same settings, history, queue, favorites, and resume data:

- Windows: `%LOCALAPPDATA%\YtMusicTerminal`
- macOS: `~/Library/Application Support/YtMusicTerminal`
- Linux: `~/.local/share/YtMusicTerminal`

Avoid running both editions simultaneously because the most recently closed edition writes the final queue and settings state.

## Resource usage

The GUI has no browser engine, WebView, Electron runtime, video decoder, or video window. The measured Windows x64 GUI executable is approximately 21 MiB. Its settled idle working set is about 146 MiB plus 25 MiB for idle mpv; playback usage varies. Use the terminal edition when the smallest possible memory footprint is more important than mouse controls.

## Troubleshooting

- If Windows reports missing tools, download the GUI ZIP again, extract every file, and rerun `install.cmd`.
- On macOS, update dependencies with `brew upgrade yt-dlp mpv deno`.
- On Linux, update yt-dlp, mpv, and Deno through your package manager.
- Playback details are written to `mpv-gui.log` in the shared data directory.
- Closing the GUI shuts down its background mpv process. If the program is forcibly terminated, the platform process supervisor also stops mpv.

LightYTP GUI does not download tracks for offline use and does not read browser profiles, cookies, passwords, or account credentials.
