namespace YtMusicTerminal.Configuration;

public sealed record AppSettings
{
    public string? YtDlpPath { get; init; }

    public string? MpvPath { get; init; }

    public int Volume { get; init; } = 70;
}
