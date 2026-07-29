using System.Text.Json;
using System.Diagnostics;
using YtMusicTerminal.Configuration;
using YtMusicTerminal.Models;
using YtMusicTerminal.Services;

namespace YtMusicTerminal;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var exitCode = await RunAsync(args).ConfigureAwait(false);
        if (exitCode != 0
            && args.Length == 0
            && !Console.IsInputRedirected
            && !Console.IsOutputRedirected)
        {
            Console.Error.WriteLine();
            Console.Error.Write("Press any key to close...");
            Console.ReadKey(intercept: true);
        }

        return exitCode;
    }

    private static async Task<int> RunAsync(string[] args)
    {
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine("Run ytmusic --help for usage.");
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (options.ShowVersion)
        {
            Console.WriteLine($"ytmusic {Version}");
            return 0;
        }

        var appPaths = AppPaths.CreateDefault();
        var settingsStore = new SettingsStore(appPaths.SettingsFile);
        AppSettings settings;
        try
        {
            settings = await settingsStore.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Could not load settings: {exception.Message}");
            return 2;
        }

        settings = settings with
        {
            YtDlpPath = options.YtDlpPath ?? settings.YtDlpPath,
            MpvPath = options.MpvPath ?? settings.MpvPath
        };

        var ytDlpName = OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";
        var mpvName = OperatingSystem.IsWindows() ? "mpv.exe" : "mpv";
        var ytDlpPath = ToolLocator.Find(ytDlpName, settings.YtDlpPath, "YTMUSIC_YTDLP");
        var mpvPath = ToolLocator.Find(mpvName, settings.MpvPath, "YTMUSIC_MPV");

        if (options.Diagnose)
        {
            return await DiagnoseAsync(ytDlpPath, mpvPath).ConfigureAwait(false);
        }

        if (ytDlpPath is null || mpvPath is null)
        {
            PrintMissingTools(ytDlpPath, mpvPath);
            return 2;
        }

        if (options.SmokeTest)
        {
            return await RunSmokeTestAsync(ytDlpPath, mpvPath).ConfigureAwait(false);
        }

        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("ytmusic requires an interactive terminal.");
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        var processRunner = new ProcessRunner();
        var youtube = new YtDlpClient(ytDlpPath, processRunner);
        var history = new HistoryStore(appPaths.HistoryFile);
        var mpv = new MpvClient(
            mpvPath,
            settings.Volume,
            Path.Combine(appPaths.DataDirectory, "mpv.log"));

        try
        {
            await using var application = new PlayerApplication(
                settings,
                settingsStore,
                history,
                youtube,
                mpv);
            await application.RunAsync(cancellation.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ytmusic failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunSmokeTestAsync(string ytDlpPath, string mpvPath)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var youtube = new YtDlpClient(ytDlpPath, new ProcessRunner());
        Console.WriteLine("Searching YouTube...");
        var tracks = await youtube.SearchAsync(
            "Daft Punk Get Lucky official audio",
            3,
            timeout.Token).ConfigureAwait(false);
        var track = tracks.FirstOrDefault()
            ?? throw new InvalidOperationException("The smoke-test search returned no tracks.");

        Console.WriteLine($"Resolving: {track.Title} — {track.Artist}");
        var url = await youtube.ResolveAudioUrlAsync(track, timeout.Token).ConfigureAwait(false);
        var appPaths = AppPaths.CreateDefault();
        await using var mpv = new MpvClient(
            mpvPath,
            initialVolume: 0,
            Path.Combine(appPaths.DataDirectory, "mpv-smoke.log"));
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

        await Task.Delay(1_000, timeout.Token).ConfigureAwait(false);
        using var currentProcess = Process.GetCurrentProcess();
        currentProcess.Refresh();
        var appWorkingSet = currentProcess.WorkingSet64;
        var appPrivate = currentProcess.PrivateMemorySize64;
        var mpvWorkingSet = mpv.WorkingSetBytes;
        var mpvPrivate = mpv.PrivateMemoryBytes;
        Console.WriteLine("Playback started successfully (muted). ");
        Console.WriteLine($"ytmusic working set: {FormatMegabytes(appWorkingSet)} MiB (private {FormatMegabytes(appPrivate)} MiB)");
        Console.WriteLine($"mpv working set:     {FormatMegabytes(mpvWorkingSet)} MiB (private {FormatMegabytes(mpvPrivate)} MiB)");
        Console.WriteLine($"combined:            {FormatMegabytes(appWorkingSet + mpvWorkingSet)} MiB");
        await mpv.StopAsync(timeout.Token).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> DiagnoseAsync(string? ytDlpPath, string? mpvPath)
    {
        Console.WriteLine($"ytmusic {Version}");
        var runner = new ProcessRunner();
        var success = true;

        success &= await PrintToolVersionAsync("yt-dlp", ytDlpPath, ["--version"], runner).ConfigureAwait(false);
        success &= await PrintToolVersionAsync("mpv", mpvPath, ["--version"], runner).ConfigureAwait(false);
        Console.WriteLine($"Data: {AppPaths.CreateDefault().DataDirectory}");
        return success ? 0 : 2;
    }

    private static async Task<bool> PrintToolVersionAsync(
        string name,
        string? path,
        IReadOnlyList<string> arguments,
        IProcessRunner runner)
    {
        if (path is null)
        {
            Console.WriteLine($"{name}: NOT FOUND");
            return false;
        }

        try
        {
            var result = await runner.RunAsync(
                path,
                arguments,
                Path.GetDirectoryName(path),
                null,
                TimeSpan.FromSeconds(5),
                CancellationToken.None).ConfigureAwait(false);
            var version = result.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? $"exit {result.ExitCode}";
            Console.WriteLine($"{name}: {version}");
            Console.WriteLine($"  {path}");
            return result.ExitCode == 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"{name}: ERROR ({exception.Message})");
            Console.WriteLine($"  {path}");
            return false;
        }
    }

    private static void PrintMissingTools(string? ytDlpPath, string? mpvPath)
    {
        Console.Error.WriteLine("Required playback tools are missing:");
        if (ytDlpPath is null)
        {
            Console.Error.WriteLine("  - yt-dlp");
        }

        if (mpvPath is null)
        {
            Console.Error.WriteLine("  - mpv");
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine("Run scripts\\bootstrap-tools.ps1 or provide paths with:");
        Console.Error.WriteLine("  ytmusic --yt-dlp C:\\path\\yt-dlp.exe --mpv C:\\path\\mpv.exe");
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            ytmusic - lightweight terminal YouTube music player

            Usage:
              ytmusic [options]

            Options:
              --yt-dlp <path>  Path to yt-dlp executable
              --mpv <path>     Path to mpv executable
              --diagnose       Print dependency versions and paths
              --smoke-test     Search and start muted playback, then print memory use
              --version        Print application version
              -h, --help       Show this help

            Environment:
              YTMUSIC_YTDLP    Path to yt-dlp executable
              YTMUSIC_MPV      Path to mpv executable

            Keys:
              /                Focus search
              Tab / Shift+Tab  Change pane
              Enter            Search or play selected track
              m                Load 10 more search results
              a                Add selected track to queue
              Delete           Remove selected queued track
              Space            Play/pause
              Left / Right     Seek backward/forward 5 seconds
              + / -            Change volume
              n / p            Next/previous queued track
              s                Stop
              h                Focus history
              ?                Show keyboard help
              q / Ctrl+Q       Quit
            """);
    }

    private static string Version =>
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    private static string FormatMegabytes(long bytes) =>
        (bytes / 1024d / 1024d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    private sealed record CliOptions(
        bool ShowHelp,
        bool ShowVersion,
        bool Diagnose,
        bool SmokeTest,
        string? YtDlpPath,
        string? MpvPath)
    {
        public static CliOptions Parse(IReadOnlyList<string> args)
        {
            var help = false;
            var version = false;
            var diagnose = false;
            var smokeTest = false;
            string? ytDlp = null;
            string? mpv = null;

            for (var index = 0; index < args.Count; index++)
            {
                switch (args[index])
                {
                    case "-h":
                    case "--help":
                        help = true;
                        break;
                    case "--version":
                        version = true;
                        break;
                    case "--diagnose":
                        diagnose = true;
                        break;
                    case "--smoke-test":
                        smokeTest = true;
                        break;
                    case "--yt-dlp":
                        ytDlp = ReadValue(args, ref index, "--yt-dlp");
                        break;
                    case "--mpv":
                        mpv = ReadValue(args, ref index, "--mpv");
                        break;
                    default:
                        throw new ArgumentException($"Unknown option '{args[index]}'.");
                }
            }

            return new CliOptions(help, version, diagnose, smokeTest, ytDlp, mpv);
        }

        private static string ReadValue(IReadOnlyList<string> args, ref int index, string option)
        {
            index++;
            if (index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
            {
                throw new ArgumentException($"Option '{option}' requires a path.");
            }

            return args[index];
        }
    }
}
