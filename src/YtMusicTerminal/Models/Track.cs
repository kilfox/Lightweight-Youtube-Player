namespace YtMusicTerminal.Models;

public sealed record Track(
    string Id,
    string Title,
    string Artist,
    TimeSpan? Duration,
    string SourceUrl,
    string? ThumbnailUrl = null);

