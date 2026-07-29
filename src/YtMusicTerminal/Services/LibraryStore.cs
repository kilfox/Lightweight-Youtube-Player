using System.Text.Json;
using YtMusicTerminal.Models;
using YtMusicTerminal.Serialization;

namespace YtMusicTerminal.Services;

public sealed class LibraryStore(string filePath)
{
    public async Task<LibraryState> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return new LibraryState();
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync(
            stream,
            AppJsonContext.Default.LibraryState,
            cancellationToken).ConfigureAwait(false) ?? new LibraryState();
    }

    public Task SaveAsync(LibraryState state, CancellationToken cancellationToken) =>
        AtomicJsonFile.WriteAsync(
            filePath,
            state,
            AppJsonContext.Default.LibraryState,
            cancellationToken);
}
