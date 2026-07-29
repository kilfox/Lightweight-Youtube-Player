using YtMusicTerminal.Models;
using YtMusicTerminal.Services;
using YtMusicTerminal.UI;

namespace YtMusicTerminal.Tests;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var tests = new List<(string Name, Func<Task> Run)>
        {
            ("Parses yt-dlp search output", ParseSearchOutputAsync),
            ("Renders the terminal layout", RenderLayoutAsync),
            ("Formats playback duration", FormatDurationAsync),
            ("Deduplicates bounded history newest-first", HistoryStoreAsync),
            ("Persists queue, favorites, and resume state", LibraryStoreAsync)
        };
        if (args.Contains("--live", StringComparer.Ordinal))
        {
            tests.Add(("Searches, resolves, and starts live playback", LivePlaybackAsync));
        }

        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run().ConfigureAwait(false);
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static Task ParseSearchOutputAsync()
    {
        const string json =
            """
            {
              "entries": [
                {
                  "id": "abc123",
                  "title": "Test Song",
                  "uploader": "Test Artist",
                  "duration": 185,
                  "url": "abc123",
                  "thumbnails": [{ "url": "https://img.example/small" }, { "url": "https://img.example/large" }]
                },
                { "id": null, "title": "Ignored" }
              ]
            }
            """;

        var tracks = YtDlpClient.ParseSearchResponse(json);
        Equal(1, tracks.Count);
        Equal("abc123", tracks[0].Id);
        Equal("Test Song", tracks[0].Title);
        Equal("Test Artist", tracks[0].Artist);
        Equal(TimeSpan.FromSeconds(185), tracks[0].Duration);
        Equal("https://www.youtube.com/watch?v=abc123", tracks[0].SourceUrl);
        Equal("https://img.example/large", tracks[0].ThumbnailUrl);
        return Task.CompletedTask;
    }

    private static Task RenderLayoutAsync()
    {
        var state = new AppState
        {
            SearchText = "lofi beats",
            SearchResults =
            [
                new Track("id", "Test Song", "Test Artist", TimeSpan.FromMinutes(3), "https://youtube.com/watch?v=id")
            ],
            Focus = FocusPane.Results,
            NowPlaying = new Track("id", "Test Song", "Test Artist", TimeSpan.FromMinutes(3), "https://youtube.com/watch?v=id"),
            Playback = new PlaybackSnapshot(
                PlaybackState.Playing,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(3),
                70)
        };

        var frame = new TerminalFrameRenderer().Render(state, 100, 30);
        Contains("LIGHTWEIGHT YOUTUBE PLAYER", frame);
        Contains("by KH!", frame);
        Contains("Results  [m +10]", frame);
        Contains("lofi beats", frame);
        Contains("Test Song", frame);
        Contains("01:00 / 03:00", frame);
        Contains("Vol 70%", frame);
        Contains("Queue", frame);

        state.Focus = FocusPane.Player;
        frame = new TerminalFrameRenderer().Render(state, 100, 30);
        Contains("● Now playing", frame);
        return Task.CompletedTask;
    }

    private static Task FormatDurationAsync()
    {
        Equal("00:05", TerminalFrameRenderer.FormatTime(TimeSpan.FromSeconds(5)));
        Equal("03:07", TerminalFrameRenderer.FormatTime(TimeSpan.FromSeconds(187)));
        Equal("1:02:03", TerminalFrameRenderer.FormatTime(TimeSpan.FromSeconds(3723)));
        return Task.CompletedTask;
    }

    private static async Task HistoryStoreAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ytmusic-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new HistoryStore(Path.Combine(directory, "history.json"), capacity: 2);
            var first = new Track("1", "First", "Artist", null, "https://youtube.com/watch?v=1");
            var second = new Track("2", "Second", "Artist", null, "https://youtube.com/watch?v=2");
            var third = new Track("3", "Third", "Artist", null, "https://youtube.com/watch?v=3");

            await store.AddAsync(first, CancellationToken.None).ConfigureAwait(false);
            await store.AddAsync(second, CancellationToken.None).ConfigureAwait(false);
            await store.AddAsync(third, CancellationToken.None).ConfigureAwait(false);
            await store.AddAsync(second, CancellationToken.None).ConfigureAwait(false);
            await store.AddAsync(second, CancellationToken.None).ConfigureAwait(false);
            var history = await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);

            Equal(2, history.Count);
            Equal("Second", history[0].Track.Title);
            Equal("Third", history[1].Track.Title);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task LivePlaybackAsync()
    {
        var ytDlpName = OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";
        var mpvName = OperatingSystem.IsWindows() ? "mpv.exe" : "mpv";
        var ytDlp = ToolLocator.Find(ytDlpName, null, "YTMUSIC_YTDLP")
            ?? throw new InvalidOperationException("yt-dlp is not installed.");
        var mpvPath = ToolLocator.Find(mpvName, null, "YTMUSIC_MPV")
            ?? throw new InvalidOperationException("mpv is not installed.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var youtube = new YtDlpClient(ytDlp, new ProcessRunner());
        var tracks = await youtube.SearchAsync(
            "Daft Punk Get Lucky official audio",
            3,
            timeout.Token).ConfigureAwait(false);
        if (tracks.Count == 0)
        {
            throw new InvalidOperationException("The live search returned no tracks.");
        }

        var url = await youtube.ResolveAudioUrlAsync(tracks[0], timeout.Token).ConfigureAwait(false);
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("The live resolver returned an invalid URL.");
        }

        await using var mpv = new MpvClient(mpvPath, initialVolume: 0);
        await mpv.StartAsync(timeout.Token).ConfigureAwait(false);
        await mpv.LoadAsync(url, timeout.Token).ConfigureAwait(false);

        while (mpv.Snapshot.State is PlaybackState.Idle or PlaybackState.Loading)
        {
            await Task.Delay(200, timeout.Token).ConfigureAwait(false);
        }

        if (mpv.Snapshot.State != PlaybackState.Playing)
        {
            throw new InvalidOperationException($"mpv entered state {mpv.Snapshot.State}.");
        }

        await mpv.StopAsync(timeout.Token).ConfigureAwait(false);
    }

    private static async Task LibraryStoreAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ytmusic-library-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var track = new Track("id", "Song", "Artist", TimeSpan.FromMinutes(3), "https://youtube.com/watch?v=id");
            var store = new LibraryStore(Path.Combine(directory, "library.json"));
            await store.SaveAsync(
                new LibraryState
                {
                    Queue = [track],
                    Favorites = [track],
                    LastTrack = track,
                    LastPositionSeconds = 42,
                    Shuffle = true,
                    Repeat = RepeatMode.Queue
                },
                CancellationToken.None).ConfigureAwait(false);

            var restored = await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            Equal(1, restored.Queue.Count);
            Equal(1, restored.Favorites.Count);
            Equal("id", restored.LastTrack?.Id);
            Equal(42d, restored.LastPositionSeconds);
            Equal(true, restored.Shuffle);
            Equal(RepeatMode.Queue, restored.Repeat);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    private static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected output to contain '{expected}'.");
        }
    }
}
