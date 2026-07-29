namespace YtMusicTerminal.Models;

public sealed record HistoryEntry(Track Track, DateTimeOffset PlayedAt);

