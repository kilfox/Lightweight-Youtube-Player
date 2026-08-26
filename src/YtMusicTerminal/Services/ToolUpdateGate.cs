namespace YtMusicTerminal.Services;

public static class ToolUpdateGate
{
    public static bool IsRequired(string markerFile, string appVersion)
    {
        if (!File.Exists(markerFile))
        {
            return true;
        }

        try
        {
            var updatedVersion = File.ReadAllText(markerFile).Trim();
            return !string.Equals(updatedVersion, appVersion, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    public static async Task MarkCompletedAsync(
        string markerFile,
        string appVersion,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(markerFile)
            ?? throw new InvalidOperationException($"'{markerFile}' has no parent directory.");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(markerFile, appVersion, cancellationToken).ConfigureAwait(false);
    }
}
