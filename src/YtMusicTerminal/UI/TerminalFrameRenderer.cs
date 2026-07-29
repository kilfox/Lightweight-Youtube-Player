using System.Text;
using YtMusicTerminal.Models;

namespace YtMusicTerminal.UI;

public sealed class TerminalFrameRenderer
{
    private const string Reverse = "\u001b[7m";
    private const string Dim = "\u001b[2m";
    private const string Brand = "\u001b[1;36m";
    private const string Reset = "\u001b[0m";

    public string Render(AppState state, int width, int height)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        var canvas = new Canvas(width, height);

        if (width < 70 || height < 18)
        {
            canvas.Write(0, 0, "Lightweight YouTube Player by KH!");
            canvas.Write(0, 2, $"Terminal is too small ({width}x{height}).");
            canvas.Write(0, 3, "Resize to at least 70x18. Ctrl+Q quits.");
            return canvas.Build();
        }

        var playerY = height - 6;
        const int searchY = 1;
        const int mainY = 4;
        var mainHeight = playerY - mainY;
        var leftWidth = Math.Max(40, width * 2 / 3);
        var rightWidth = width - leftWidth;
        var queueHeight = Math.Max(5, mainHeight / 2);
        var historyHeight = mainHeight - queueHeight;

        const string brandText = "◆ LIGHTWEIGHT YOUTUBE PLAYER ◆  by KH!";
        var brandX = Math.Max(0, (width - brandText.Length) / 2);
        canvas.Write(brandX, 0, brandText, width);
        canvas.Style(brandX, 0, Math.Min(brandText.Length, width - brandX), Brand);

        canvas.Box(0, searchY, width, 3, PaneTitle("Search  [/ to focus]", state.Focus == FocusPane.Search));
        var searchText = state.SearchText.Length == 0 ? "Search YouTube Music..." : state.SearchText;
        canvas.Write(2, searchY + 1, searchText, width - 4);
        if (state.Focus == FocusPane.Search)
        {
            canvas.Highlight(1, searchY + 1, width - 2);
        }

        canvas.Box(0, mainY, leftWidth, mainHeight, PaneTitle("Results  [m +10]", state.Focus == FocusPane.Results));
        DrawTracks(
            canvas,
            state.SearchResults,
            state.SelectedResult,
            state.Focus == FocusPane.Results,
            1,
            mainY + 1,
            leftWidth - 2,
            mainHeight - 2,
            state.NowPlaying);

        canvas.Box(leftWidth, mainY, rightWidth, queueHeight, PaneTitle("Queue  [a add, Del remove]", state.Focus == FocusPane.Queue));
        DrawTracks(
            canvas,
            state.Queue,
            state.SelectedQueueItem,
            state.Focus == FocusPane.Queue,
            leftWidth + 1,
            mainY + 1,
            rightWidth - 2,
            queueHeight - 2,
            state.NowPlaying);

        var libraryY = mainY + queueHeight;
        if (state.ShowFavorites)
        {
            canvas.Box(
                leftWidth,
                libraryY,
                rightWidth,
                historyHeight,
                PaneTitle("Favorites  [v]", state.Focus == FocusPane.Favorites));
            DrawTracks(
                canvas,
                state.Favorites,
                state.SelectedFavorite,
                state.Focus == FocusPane.Favorites,
                leftWidth + 1,
                libraryY + 1,
                rightWidth - 2,
                historyHeight - 2,
                state.NowPlaying);
        }
        else
        {
            canvas.Box(
                leftWidth,
                libraryY,
                rightWidth,
                historyHeight,
                PaneTitle("History  [h]", state.Focus == FocusPane.History));
            DrawHistory(
                canvas,
                state.History,
                state.SelectedHistoryItem,
                state.Focus == FocusPane.History,
                leftWidth + 1,
                libraryY + 1,
                rightWidth - 2,
                historyHeight - 2);
        }

        var modes = $"{(state.Shuffle ? "Shuffle on" : "Shuffle off")} | Repeat {state.Repeat.ToString().ToLowerInvariant()}";
        canvas.Box(
            0,
            playerY,
            width,
            5,
            PaneTitle($"Now playing  [{modes}]", state.Focus == FocusPane.Player));
        DrawPlayer(canvas, state, playerY, width);

        var busy = state.IsSearching ? "Searching...  " : state.IsResolving ? "Loading track...  " : string.Empty;
        var help = state.ShowHelp
            ? "Esc player | Up/Down volume | Tab panes | Enter play | m more | Space pause | Ctrl+Q quit"
            : "? help  |  Ctrl+Q quit  |  " + busy + state.StatusMessage;
        canvas.Write(0, height - 1, help, width);
        canvas.Style(0, height - 1, width, Dim);

        return canvas.Build();
    }

    private static void DrawTracks(
        Canvas canvas,
        IReadOnlyList<Track> tracks,
        int selected,
        bool focused,
        int x,
        int y,
        int width,
        int height,
        Track? nowPlaying)
    {
        if (tracks.Count == 0)
        {
            canvas.Write(x + 1, y, "(empty)", Math.Max(0, width - 2));
            return;
        }

        var start = ScrollStart(selected, tracks.Count, height);
        for (var row = 0; row < height && start + row < tracks.Count; row++)
        {
            var index = start + row;
            var track = tracks[index];
            var marker = nowPlaying?.Id == track.Id ? "▶" : " ";
            var duration = track.Duration is { } value ? FormatTime(value) : "--:--";
            var text = $"{marker} {track.Title} — {track.Artist}  {duration}";
            canvas.Write(x, y + row, text, width);
            if (focused && index == selected)
            {
                canvas.Highlight(x, y + row, width);
            }
        }
    }

    private static void DrawHistory(
        Canvas canvas,
        IReadOnlyList<HistoryEntry> entries,
        int selected,
        bool focused,
        int x,
        int y,
        int width,
        int height)
    {
        if (entries.Count == 0)
        {
            canvas.Write(x + 1, y, "(empty)", Math.Max(0, width - 2));
            return;
        }

        var start = ScrollStart(selected, entries.Count, height);
        for (var row = 0; row < height && start + row < entries.Count; row++)
        {
            var index = start + row;
            var entry = entries[index];
            canvas.Write(x, y + row, $"{entry.Track.Title} — {entry.Track.Artist}", width);
            if (focused && index == selected)
            {
                canvas.Highlight(x, y + row, width);
            }
        }
    }

    private static void DrawPlayer(Canvas canvas, AppState state, int playerY, int width)
    {
        if (state.NowPlaying is null)
        {
            canvas.Write(2, playerY + 1, "Nothing is playing.", width - 4);
            canvas.Write(2, playerY + 2, $"Volume {state.Playback.Volume}%", width - 4);
            return;
        }

        var stateMarker = state.Playback.State switch
        {
            PlaybackState.Playing => "▶",
            PlaybackState.Paused => "Ⅱ",
            PlaybackState.Loading => "…",
            PlaybackState.Error => "!",
            _ => "■"
        };
        canvas.Write(
            2,
            playerY + 1,
            $"{stateMarker} {state.NowPlaying.Title} — {state.NowPlaying.Artist}",
            width - 4);

        var position = state.Playback.Position;
        var duration = state.Playback.Duration > TimeSpan.Zero
            ? state.Playback.Duration
            : state.NowPlaying.Duration ?? TimeSpan.Zero;
        var timeText = $"{FormatTime(position)} / {FormatTime(duration)}";
        var volumeText = $"Vol {state.Playback.Volume}%";
        var barWidth = Math.Max(10, width - timeText.Length - volumeText.Length - 10);
        var progress = duration.TotalSeconds > 0
            ? Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1)
            : 0;
        var filled = (int)Math.Round(progress * barWidth);
        var bar = new string('━', filled) + new string('─', barWidth - filled);
        canvas.Write(2, playerY + 2, $"{timeText}  {bar}  {volumeText}", width - 4);

        if (state.Playback.Error is { Length: > 0 } error)
        {
            canvas.Write(2, playerY + 3, error, width - 4);
        }
    }

    private static string PaneTitle(string title, bool focused) => focused ? $"● {title}" : title;

    private static int ScrollStart(int selected, int count, int height)
    {
        if (height <= 0 || count <= height)
        {
            return 0;
        }

        return Math.Clamp(selected - height / 2, 0, count - height);
    }

    public static string FormatTime(TimeSpan value)
    {
        value = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";
    }

    private sealed class Canvas
    {
        private readonly char[][] _rows;
        private readonly List<StyleSpan>[] _styles;

        public Canvas(int width, int height)
        {
            Width = width;
            Height = height;
            _rows = new char[height][];
            _styles = new List<StyleSpan>[height];
            for (var row = 0; row < height; row++)
            {
                _rows[row] = Enumerable.Repeat(' ', width).ToArray();
                _styles[row] = [];
            }
        }

        public int Width { get; }

        public int Height { get; }

        public void Box(int x, int y, int width, int height, string title)
        {
            if (width < 2 || height < 2)
            {
                return;
            }

            Write(x, y, "┌" + new string('─', width - 2) + "┐", width);
            Write(x, y + height - 1, "└" + new string('─', width - 2) + "┘", width);
            for (var row = y + 1; row < y + height - 1; row++)
            {
                Write(x, row, "│");
                Write(x + width - 1, row, "│");
            }

            if (title.Length > 0 && width > 4)
            {
                Write(x + 2, y, $" {title} ", width - 4);
            }
        }

        public void Write(int x, int y, string text, int? maxWidth = null)
        {
            if (y < 0 || y >= Height || x >= Width || string.IsNullOrEmpty(text))
            {
                return;
            }

            var sourceIndex = x < 0 ? -x : 0;
            var targetX = Math.Max(0, x);
            var available = Math.Min(maxWidth ?? text.Length, Width - targetX);
            for (var index = sourceIndex; index < text.Length && index - sourceIndex < available; index++)
            {
                _rows[y][targetX + index - sourceIndex] = char.IsControl(text[index]) ? ' ' : text[index];
            }
        }

        public void Highlight(int x, int y, int width) => Style(x, y, width, Reverse);

        public void Style(int x, int y, int width, string sequence)
        {
            if (y < 0 || y >= Height || width <= 0)
            {
                return;
            }

            var start = Math.Clamp(x, 0, Width);
            var length = Math.Clamp(width, 0, Width - start);
            if (length > 0)
            {
                _styles[y].Add(new StyleSpan(start, length, sequence));
            }
        }

        public string Build()
        {
            var output = new StringBuilder(Width * Height + Height * 4);
            output.Append("\u001b[H");
            for (var row = 0; row < Height; row++)
            {
                var styles = _styles[row].OrderBy(style => style.Start).ToArray();
                var column = 0;
                foreach (var style in styles)
                {
                    if (style.Start > column)
                    {
                        output.Append(_rows[row], column, style.Start - column);
                    }

                    output.Append(style.Sequence);
                    output.Append(_rows[row], style.Start, style.Length);
                    output.Append(Reset);
                    column = style.Start + style.Length;
                }

                if (column < Width)
                {
                    output.Append(_rows[row], column, Width - column);
                }

                if (row < Height - 1)
                {
                    output.Append("\r\n");
                }
            }

            return output.ToString();
        }

        private sealed record StyleSpan(int Start, int Length, string Sequence);
    }
}
