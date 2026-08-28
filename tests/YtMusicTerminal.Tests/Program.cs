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
            ("Uses YouTube Mix for filtered related tracks", RelatedTracksMixAsync),
            ("Falls back to related-track search", RelatedTracksFallbackAsync),
            ("Caches searches and resolved audio URLs", YtDlpCacheAsync),
            ("Renders the terminal layout", RenderLayoutAsync),
            ("Formats playback duration", FormatDurationAsync),
            ("Builds safe edition-specific uninstall plans", UninstallPlansAsync),
            ("Requires one playback-tool update per app version", ToolUpdateGateAsync),
            ("Selects app upgrades for all release platforms", AppUpgradeTests.AssetSelectionAsync),
            ("Downloads and verifies an app release", AppUpgradeTests.PrepareAsync),
            ("Checks startup upgrade availability without downloading", AppUpgradeTests.StartupNoticeAsync),
            ("Rejects unsafe or incomplete app releases", AppUpgradeTests.RejectBadReleasesAsync),
            ("Extracts Unix upgrades safely and replaces atomically on Unix", AppUpgradeTests.UnixArchiveAndInstallAsync),
            ("Installs after parent exit with the Windows upgrade helper", AppUpgradeTests.WindowsHelperAsync),
            ("Reports failed Windows upgrades and retains recovery files", AppUpgradeTests.WindowsHelperFailureAsync),
            ("Handles upgrade CLI options before playback checks", AppUpgradeTests.CliAsync),
            ("Deduplicates bounded history newest-first", HistoryStoreAsync),
            ("Persists queue, favorites, and resume state", LibraryStoreAsync)
        };
        if (args.Contains("--upgrade-live", StringComparer.Ordinal))
        {
            tests.Add(("Downloads and verifies a live release without installing", AppUpgradeTests.LiveDownloadAsync));
        }
        if (args.Contains("--live", StringComparer.Ordinal))
        {
            tests.Add(("Extracts a Mix recommendation and starts muted playback", LivePlaybackAsync));
        }
        if (args.Contains("--mpv", StringComparer.Ordinal))
        {
            tests.Add(("Starts and controls mpv IPC", MpvIpcAsync));
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
                70),
            Autoplay = true
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
        Contains("Autoplay on", frame);

        state.Focus = FocusPane.Player;
        frame = new TerminalFrameRenderer().Render(state, 100, 30);
        Contains("● Now playing", frame);
        state.UpgradeNotice = "App v0.6.0 available - quit, run: lightytp upgrade";
        state.StatusMessage = "Playing Test Song.";
        frame = new TerminalFrameRenderer().Render(state, 70, 18);
        Contains(state.UpgradeNotice, frame);
        state.ShowHelp = true;
        frame = new TerminalFrameRenderer().Render(state, 70, 18);
        Contains(state.UpgradeNotice, frame);
        return Task.CompletedTask;
    }

    private static async Task YtDlpCacheAsync()
    {
        var runner = new FakeProcessRunner();
        var youtube = new YtDlpClient("yt-dlp", runner);

        var firstSearch = await youtube.SearchAsync("  Test   Song ", 10, CancellationToken.None);
        var cachedSearch = await youtube.SearchAsync("test song", 10, CancellationToken.None);
        Equal(1, firstSearch.Count);
        Equal(1, cachedSearch.Count);
        Equal(1, runner.SearchCalls);

        await youtube.SearchAsync("test song", 20, CancellationToken.None);
        Equal(2, runner.SearchCalls);

        var track = firstSearch[0];
        var firstUrl = await youtube.ResolveAudioUrlAsync(track, CancellationToken.None);
        var cachedUrl = await youtube.ResolveAudioUrlAsync(track, CancellationToken.None);
        Equal(firstUrl, cachedUrl);
        Equal(1, runner.ResolveCalls);

        youtube.InvalidateAudioUrl(track.Id);
        await youtube.ResolveAudioUrlAsync(track, CancellationToken.None);
        Equal(2, runner.ResolveCalls);
    }

    private static async Task RelatedTracksMixAsync()
    {
        const string mixJson =
            """
            {
              "entries": [
                { "id": "seed", "title": "Seed", "uploader": "Artist", "url": "seed" },
                { "id": "recent", "title": "Recent", "uploader": "Artist", "url": "recent" },
                { "id": "session", "title": "Session", "uploader": "Artist", "url": "session" },
                { "id": "related", "title": "Related", "uploader": "Other", "url": "related" },
                { "id": "related", "title": "Duplicate", "uploader": "Other", "url": "related" }
              ]
            }
            """;
        var runner = new RelatedProcessRunner(mixJson, mixExitCode: 0, searchJson: "{\"entries\":[]}");
        var youtube = new YtDlpClient("yt-dlp", runner);
        var seed = new Track("seed", "Seed", "Artist", null, "https://youtube.com/watch?v=seed");

        var tracks = await youtube.GetRelatedTracksAsync(
            seed,
            new HashSet<string>(["recent", "session"], StringComparer.Ordinal),
            CancellationToken.None);

        Equal(1, tracks.Count);
        Equal("related", tracks[0].Id);
        Equal(1, runner.MixCalls);
        Equal(0, runner.SearchCalls);
    }

    private static async Task RelatedTracksFallbackAsync()
    {
        const string exhaustedMixJson =
            """
            {
              "entries": [
                { "id": "seed", "title": "Seed", "uploader": "Artist", "url": "seed" },
                { "id": "recent", "title": "Recent", "uploader": "Artist", "url": "recent" }
              ]
            }
            """;
        const string searchJson =
            """
            {
              "entries": [
                { "id": "seed", "title": "Seed", "uploader": "Artist", "url": "seed" },
                { "id": "recent", "title": "Recent", "uploader": "Artist", "url": "recent" },
                { "id": "fallback", "title": "Fallback", "uploader": "Other", "url": "fallback" }
              ]
            }
            """;
        var runner = new RelatedProcessRunner("", mixExitCode: 1, searchJson);
        var youtube = new YtDlpClient("yt-dlp", runner);
        var seed = new Track("seed", "Seed", "Artist", null, "https://youtube.com/watch?v=seed");

        var tracks = await youtube.GetRelatedTracksAsync(
            seed,
            new HashSet<string>(["recent"], StringComparer.Ordinal),
            CancellationToken.None);

        Equal(1, tracks.Count);
        Equal("fallback", tracks[0].Id);
        Equal(1, runner.MixCalls);
        Equal(1, runner.SearchCalls);

        var exhaustedRunner = new RelatedProcessRunner(exhaustedMixJson, mixExitCode: 0, searchJson);
        var exhaustedYoutube = new YtDlpClient("yt-dlp", exhaustedRunner);
        var exhaustedTracks = await exhaustedYoutube.GetRelatedTracksAsync(
            seed,
            new HashSet<string>(["recent"], StringComparer.Ordinal),
            CancellationToken.None);
        Equal("fallback", exhaustedTracks.Single().Id);
        Equal(1, exhaustedRunner.MixCalls);
        Equal(1, exhaustedRunner.SearchCalls);
    }

    private static Task FormatDurationAsync()
    {
        Equal("00:05", TerminalFrameRenderer.FormatTime(TimeSpan.FromSeconds(5)));
        Equal("03:07", TerminalFrameRenderer.FormatTime(TimeSpan.FromSeconds(187)));
        Equal("1:02:03", TerminalFrameRenderer.FormatTime(TimeSpan.FromSeconds(3723)));
        return Task.CompletedTask;
    }

    private static Task UninstallPlansAsync()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var platform = OperatingSystem.IsWindows()
            ? UninstallPlatform.Windows
            : OperatingSystem.IsMacOS()
                ? UninstallPlatform.MacOS
                : UninstallPlatform.Linux;

        var terminal = UninstallService.CreateExpectedPlan(
            LightYtpEdition.Terminal,
            platform,
            home,
            localAppData,
            roamingAppData);
        var gui = UninstallService.CreateExpectedPlan(
            LightYtpEdition.Gui,
            platform,
            home,
            localAppData,
            roamingAppData);

        Equal("LightYTP Terminal", terminal.DisplayName);
        Equal("LightYTP GUI", gui.DisplayName);
        Equal(false, string.Equals(terminal.TargetPath, gui.TargetPath, StringComparison.OrdinalIgnoreCase));

        if (OperatingSystem.IsWindows())
        {
            Equal(Path.Combine(localAppData, "Programs", "LightYTP"), terminal.TargetPath);
            Equal(Path.Combine(localAppData, "Programs", "LightYTP-GUI"), gui.TargetPath);
            Equal(Path.Combine(roamingAppData, "Microsoft", "Windows", "Start Menu", "Programs", "LightYTP GUI.lnk"), gui.ShortcutPath);
            Equal(true, terminal.TargetIsDirectory);
        }
        else
        {
            Equal(Path.Combine(home, ".local", "bin", "lightytp"), terminal.TargetPath);
            Equal(false, terminal.TargetIsDirectory);
            Equal(Path.Combine(home, ".local", "bin", "lightytp-gui"), gui.LauncherPath);
        }

        return Task.CompletedTask;
    }

    private static async Task ToolUpdateGateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ytmusic-tests-{Guid.NewGuid():N}");
        var markerFile = Path.Combine(directory, "tools-update-version.txt");
        try
        {
            Equal(true, ToolUpdateGate.IsRequired(markerFile, "0.4.0"));

            await ToolUpdateGate.MarkCompletedAsync(markerFile, "0.4.0", CancellationToken.None);

            Equal(false, ToolUpdateGate.IsRequired(markerFile, "0.4.0"));
            Equal(true, ToolUpdateGate.IsRequired(markerFile, "0.5.0"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
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

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var stage = "search";
        try
        {
            var youtube = new YtDlpClient(ytDlp, new ProcessRunner());
            var tracks = await youtube.SearchAsync(
                "Daft Punk Get Lucky official audio",
                3,
                timeout.Token).ConfigureAwait(false);
            if (tracks.Count == 0)
            {
                throw new InvalidOperationException("The live search returned no tracks.");
            }

            stage = "Mix extraction";
            var related = await youtube.GetRelatedTracksAsync(
                tracks[0],
                new HashSet<string>(StringComparer.Ordinal),
                timeout.Token).ConfigureAwait(false);
            if (related.Count == 0)
            {
                throw new InvalidOperationException("The live Mix returned no eligible recommendation.");
            }

            stage = "audio URL resolution";
            var url = await youtube.ResolveAudioUrlAsync(related[0], timeout.Token).ConfigureAwait(false);
            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException("The live resolver returned an invalid URL.");
            }

            stage = "mpv startup";
            await using var mpv = new MpvClient(mpvPath, initialVolume: 0);
            await mpv.StartAsync(timeout.Token).ConfigureAwait(false);
            stage = "muted playback startup";
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
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Live test timed out during {stage}.");
        }
    }

    private static async Task MpvIpcAsync()
    {
        var mpvName = OperatingSystem.IsWindows() ? "mpv.exe" : "mpv";
        var mpvPath = ToolLocator.Find(mpvName, null, "YTMUSIC_MPV")
            ?? throw new InvalidOperationException("mpv is not installed.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var mpv = new MpvClient(mpvPath, initialVolume: 20);
        await mpv.StartAsync(timeout.Token).ConfigureAwait(false);
        var snapshotChanges = 0;
        mpv.SnapshotChanged += _ => Interlocked.Increment(ref snapshotChanges);
        await mpv.SetVolumeAsync(35, timeout.Token).ConfigureAwait(false);

        while (mpv.Snapshot.Volume != 35)
        {
            await Task.Delay(50, timeout.Token).ConfigureAwait(false);
        }

        Equal(PlaybackState.Idle, mpv.Snapshot.State);
        Equal(35, mpv.Snapshot.Volume);
        if (Volatile.Read(ref snapshotChanges) == 0)
        {
            throw new InvalidOperationException("No discrete playback snapshot notification was raised.");
        }

        var keyResponse = await mpv.SendCommandAsync(["get_property", "input-key-list"], timeout.Token);
        var supportedKeys = keyResponse.GetProperty("data").EnumerateArray()
            .Select(key => key.GetString()).ToHashSet(StringComparer.Ordinal);
        foreach (var key in new[] { "PLAY", "PAUSE", "PLAYPAUSE", "PLAYONLY", "PAUSEONLY", "XF86_PAUSE" })
        {
            if (!supportedKeys.Contains(key))
            {
                continue;
            }

            var isToggle = key is "PLAY" or "PAUSE" or "PLAYPAUSE" or "XF86_PAUSE";
            var isPlayOnly = key == "PLAYONLY";
            var expectedPause = isToggle || isPlayOnly;
            await mpv.SendCommandAsync(["set_property", "pause", expectedPause], timeout.Token);
            for (var press = 0; press < 3; press++)
            {
                expectedPause = isToggle ? !expectedPause : !isPlayOnly;
                await mpv.SendCommandAsync(["keypress", key], timeout.Token);
                // A keypress command queues the binding; wait for it to run before checking state.
                using var keyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                while (true)
                {
                    var response = await mpv.SendCommandAsync(["get_property", "pause"], timeout.Token);
                    if (response.GetProperty("data").GetBoolean() == expectedPause)
                    {
                        break;
                    }
                    if (keyTimeout.IsCancellationRequested)
                    {
                        throw new InvalidOperationException($"Media key {key}, press {press + 1}: expected pause={expectedPause}.");
                    }
                    await Task.Delay(20, timeout.Token);
                }
            }
        }
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
                    Repeat = RepeatMode.Queue,
                    Autoplay = true
                },
                CancellationToken.None).ConfigureAwait(false);

            var restored = await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            Equal(1, restored.Queue.Count);
            Equal(1, restored.Favorites.Count);
            Equal("id", restored.LastTrack?.Id);
            Equal(42d, restored.LastPositionSeconds);
            Equal(true, restored.Shuffle);
            Equal(RepeatMode.Queue, restored.Repeat);
            Equal(true, restored.Autoplay);

            await File.WriteAllTextAsync(
                Path.Combine(directory, "old-library.json"),
                "{\"Queue\":[],\"Favorites\":[],\"Shuffle\":false,\"Repeat\":0}");
            var oldStore = new LibraryStore(Path.Combine(directory, "old-library.json"));
            var oldState = await oldStore.LoadAsync(CancellationToken.None);
            Equal(false, oldState.Autoplay);
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

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public int SearchCalls { get; private set; }

        public int ResolveCalls { get; private set; }

        public Task<ProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            IReadOnlyDictionary<string, string?>? environment,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (arguments.Contains("--get-url", StringComparer.Ordinal))
            {
                ResolveCalls++;
                return Task.FromResult(new ProcessResult(0, "https://audio.example/stream\n", string.Empty));
            }

            SearchCalls++;
            const string json =
                """
                {
                  "entries": [
                    {
                      "id": "cache-test",
                      "title": "Test Song",
                      "uploader": "Test Artist",
                      "duration": 180,
                      "url": "cache-test"
                    }
                  ]
                }
                """;
            return Task.FromResult(new ProcessResult(0, json, string.Empty));
        }
    }

    private sealed class RelatedProcessRunner(
        string mixJson,
        int mixExitCode,
        string searchJson) : IProcessRunner
    {
        public int MixCalls { get; private set; }

        public int SearchCalls { get; private set; }

        public Task<ProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            IReadOnlyDictionary<string, string?>? environment,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (arguments.Any(argument => argument.Contains("list=RD", StringComparison.Ordinal)))
            {
                MixCalls++;
                return Task.FromResult(new ProcessResult(mixExitCode, mixJson, "Mix unavailable"));
            }

            SearchCalls++;
            return Task.FromResult(new ProcessResult(0, searchJson, string.Empty));
        }
    }
}
