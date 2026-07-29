using System.Text.Json;
using YtMusicTerminal.Configuration;
using YtMusicTerminal.Serialization;

namespace YtMusicTerminal.Services;

public sealed class SettingsStore(string filePath)
{
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync(
            stream,
            AppJsonContext.Default.AppSettings,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Settings file '{filePath}' is empty.");
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) =>
        AtomicJsonFile.WriteAsync(
            filePath,
            settings,
            AppJsonContext.Default.AppSettings,
            cancellationToken);
}

