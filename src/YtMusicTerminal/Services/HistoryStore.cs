using System.Text.Json;
using YtMusicTerminal.Models;
using YtMusicTerminal.Serialization;

namespace YtMusicTerminal.Services;

public sealed class HistoryStore(string filePath, int capacity = 100)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<IReadOnlyList<HistoryEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(filePath))
            {
                return [];
            }

            await using var stream = File.OpenRead(filePath);
            var entries = await JsonSerializer.DeserializeAsync(
                stream,
                AppJsonContext.Default.ListHistoryEntry,
                cancellationToken).ConfigureAwait(false);
            return (entries ?? [])
                .DistinctBy(entry => entry.Track.Id, StringComparer.Ordinal)
                .Take(capacity)
                .ToArray();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<HistoryEntry>> AddAsync(
        Track track,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<HistoryEntry> entries;
            if (File.Exists(filePath))
            {
                await using var input = File.OpenRead(filePath);
                entries = await JsonSerializer.DeserializeAsync(
                    input,
                    AppJsonContext.Default.ListHistoryEntry,
                    cancellationToken).ConfigureAwait(false) ?? [];
            }
            else
            {
                entries = [];
            }

            entries = entries
                .DistinctBy(entry => entry.Track.Id, StringComparer.Ordinal)
                .ToList();
            entries.RemoveAll(entry => string.Equals(
                entry.Track.Id,
                track.Id,
                StringComparison.Ordinal));
            entries.Insert(0, new HistoryEntry(track, DateTimeOffset.UtcNow));
            if (entries.Count > capacity)
            {
                entries.RemoveRange(capacity, entries.Count - capacity);
            }

            await AtomicJsonFile.WriteAsync(
                filePath,
                entries,
                AppJsonContext.Default.ListHistoryEntry,
                cancellationToken).ConfigureAwait(false);
            return entries;
        }
        finally
        {
            _lock.Release();
        }
    }
}
