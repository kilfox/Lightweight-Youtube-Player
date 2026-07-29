using System.Globalization;
using System.Text.Json;
using YtMusicTerminal.Models;

namespace YtMusicTerminal.Services;

public sealed class YtDlpClient
{
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(30);

    private readonly string _executable;
    private readonly IProcessRunner _processRunner;
    private readonly IReadOnlyDictionary<string, string?> _environment;

    public YtDlpClient(string executable, IProcessRunner processRunner)
    {
        _executable = executable;
        _processRunner = processRunner;

        var toolDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory;
        _environment = new Dictionary<string, string?>
        {
            ["PATH"] = string.Join(
                Path.PathSeparator,
                new[] { toolDirectory, Environment.GetEnvironmentVariable("PATH") }
                    .Where(value => !string.IsNullOrWhiteSpace(value)))
        };
    }

    public async Task<IReadOnlyList<Track>> SearchAsync(
        string query,
        int resultLimit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var safeLimit = Math.Clamp(resultLimit, 1, 50);
        var arguments = new[]
        {
            "--ignore-config",
            "--no-warnings",
            "--dump-single-json",
            "--flat-playlist",
            "--playlist-end",
            safeLimit.ToString(CultureInfo.InvariantCulture),
            $"ytsearch{safeLimit}:{query.Trim()}"
        };

        var result = await _processRunner.RunAsync(
            _executable,
            arguments,
            Path.GetDirectoryName(_executable),
            _environment,
            SearchTimeout,
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(CleanError(result.StandardError, "YouTube search failed."));
        }

        return ParseSearchResponse(result.StandardOutput);
    }

    public async Task<string> ResolveAudioUrlAsync(Track track, CancellationToken cancellationToken)
    {
        var arguments = new[]
        {
            "--ignore-config",
            "--no-warnings",
            "--no-playlist",
            "--format",
            "bestaudio/best",
            "--get-url",
            track.SourceUrl
        };

        var result = await _processRunner.RunAsync(
            _executable,
            arguments,
            Path.GetDirectoryName(_executable),
            _environment,
            ResolveTimeout,
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(CleanError(result.StandardError, "Could not resolve the audio stream."));
        }

        var url = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => Uri.TryCreate(line, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp));

        return url ?? throw new InvalidOperationException("yt-dlp did not return a playable audio URL.");
    }

    public async Task<Track> GetTrackAsync(string url, CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            _executable,
            ["--ignore-config", "--no-warnings", "--dump-single-json", "--no-playlist", url],
            Path.GetDirectoryName(_executable),
            _environment,
            ResolveTimeout,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(CleanError(result.StandardError, "Could not read the YouTube URL."));
        }

        using var document = JsonDocument.Parse(result.StandardOutput);
        return ParseTrack(document.RootElement)
            ?? throw new InvalidOperationException("yt-dlp did not return track metadata.");
    }

    public static IReadOnlyList<Track> ParseSearchResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("entries", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var tracks = new List<Track>();
        foreach (var entry in entries.EnumerateArray())
        {
            var track = ParseTrack(entry);
            if (track is null)
            {
                continue;
            }

            tracks.Add(track);
        }

        return tracks;
    }

    private static Track? ParseTrack(JsonElement entry)
    {
        var id = GetString(entry, "id");
        var title = GetString(entry, "title");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var artist = FirstNonEmpty(
                GetString(entry, "artist"),
                GetString(entry, "uploader"),
                GetString(entry, "channel"),
                "Unknown artist");
        var sourceUrl = FirstNonEmpty(
                GetString(entry, "webpage_url"),
                GetString(entry, "url"),
                $"https://www.youtube.com/watch?v={id}");

        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out _))
        {
            sourceUrl = $"https://www.youtube.com/watch?v={id}";
        }

        TimeSpan? duration = null;
        if (entry.TryGetProperty("duration", out var durationElement)
            && durationElement.TryGetDouble(out var durationSeconds)
            && durationSeconds >= 0)
        {
            duration = TimeSpan.FromSeconds(durationSeconds);
        }

        return new Track(id, title, artist, duration, sourceUrl, GetThumbnail(entry));
    }

    private static string? GetThumbnail(JsonElement entry)
    {
        var direct = GetString(entry, "thumbnail");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        if (!entry.TryGetProperty("thumbnails", out var thumbnails)
            || thumbnails.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? lastUrl = null;
        foreach (var thumbnail in thumbnails.EnumerateArray())
        {
            lastUrl = GetString(thumbnail, "url") ?? lastUrl;
        }

        return lastUrl;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!;

    private static string CleanError(string error, string fallback)
    {
        var message = error
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        return string.IsNullOrWhiteSpace(message) ? fallback : message;
    }
}
