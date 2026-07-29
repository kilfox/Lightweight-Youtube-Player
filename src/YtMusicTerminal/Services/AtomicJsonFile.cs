using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace YtMusicTerminal.Services;

internal static class AtomicJsonFile
{
    public static async Task WriteAsync<T>(
        string filePath,
        T value,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException($"'{filePath}' has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryFile = Path.Combine(
            directory,
            $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(
                temporaryFile,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    output,
                    value,
                    typeInfo,
                    cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryFile, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                File.Delete(temporaryFile);
            }
        }
    }
}
