namespace YtMusicTerminal.Models;

public enum RepeatMode
{
    Off,
    Track,
    Queue
}

public sealed record LibraryState
{
    public List<Track> Queue { get; init; } = [];

    public List<Track> Favorites { get; init; } = [];

    public Track? LastTrack { get; init; }

    public double LastPositionSeconds { get; init; }

    public bool Shuffle { get; init; }

    public RepeatMode Repeat { get; init; }
}
