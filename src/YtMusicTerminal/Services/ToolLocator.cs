namespace YtMusicTerminal.Services;

public static class ToolLocator
{
    public static string? Find(string executableName, string? configuredPath, string environmentVariable)
    {
        foreach (var candidate in Candidates(executableName, configuredPath, environmentVariable))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return FindOnPath(executableName);
    }

    private static IEnumerable<string> Candidates(
        string executableName,
        string? configuredPath,
        string environmentVariable)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return configuredPath;
        }

        var environmentPath = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            yield return environmentPath;
        }

        yield return Path.Combine(AppContext.BaseDirectory, "tools", executableName);
        yield return Path.Combine(AppContext.BaseDirectory, executableName);
        yield return Path.Combine(Environment.CurrentDirectory, "tools", executableName);
    }

    private static string? FindOnPath(string executableName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), executableName);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }
}

