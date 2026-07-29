namespace YtMusicTerminal.Configuration;

public sealed record AppPaths(string DataDirectory, string SettingsFile, string HistoryFile)
{
    public static AppPaths CreateDefault()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = AppContext.BaseDirectory;
        }

        var directory = Path.Combine(localData, "YtMusicTerminal");
        return new AppPaths(
            directory,
            Path.Combine(directory, "settings.json"),
            Path.Combine(directory, "history.json"));
    }
}

