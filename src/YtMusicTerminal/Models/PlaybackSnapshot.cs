namespace YtMusicTerminal.Models;

public enum PlaybackState
{
    Idle,
    Loading,
    Playing,
    Paused,
    Error
}

public sealed record PlaybackSnapshot(
    PlaybackState State,
    TimeSpan Position,
    TimeSpan Duration,
    int Volume,
    string? Error = null)
{
    public static PlaybackSnapshot Initial(int volume) =>
        new(PlaybackState.Idle, TimeSpan.Zero, TimeSpan.Zero, volume);
}

