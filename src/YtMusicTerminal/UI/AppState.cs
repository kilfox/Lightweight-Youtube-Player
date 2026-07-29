using YtMusicTerminal.Models;

namespace YtMusicTerminal.UI;

public enum FocusPane
{
    Search,
    Results,
    Queue,
    History,
    Favorites
}

public sealed class AppState
{
    public string SearchText { get; set; } = string.Empty;

    public IReadOnlyList<Track> SearchResults { get; set; } = [];

    public List<Track> Queue { get; } = [];

    public IReadOnlyList<HistoryEntry> History { get; set; } = [];

    public List<Track> Favorites { get; } = [];

    public int SelectedResult { get; set; }

    public int SelectedQueueItem { get; set; }

    public int SelectedHistoryItem { get; set; }

    public int SelectedFavorite { get; set; }

    public int CurrentQueueItem { get; set; } = -1;

    public FocusPane Focus { get; set; } = FocusPane.Search;

    public Track? NowPlaying { get; set; }

    public PlaybackSnapshot Playback { get; set; } = PlaybackSnapshot.Initial(70);

    public string StatusMessage { get; set; } = "Type a search and press Enter.";

    public bool IsSearching { get; set; }

    public bool IsResolving { get; set; }

    public bool ShowHelp { get; set; }

    public bool ShowFavorites { get; set; }

    public bool Shuffle { get; set; }

    public RepeatMode Repeat { get; set; }

    public Track? SelectedTrack => Focus switch
    {
        FocusPane.Results => ItemAt(SearchResults, SelectedResult),
        FocusPane.Queue => ItemAt(Queue, SelectedQueueItem),
        FocusPane.History => ItemAt(History, SelectedHistoryItem)?.Track,
        FocusPane.Favorites => ItemAt(Favorites, SelectedFavorite),
        _ => null
    };

    public void MoveSelection(int delta)
    {
        switch (Focus)
        {
            case FocusPane.Results:
                SelectedResult = Move(SelectedResult, delta, SearchResults.Count);
                break;
            case FocusPane.Queue:
                SelectedQueueItem = Move(SelectedQueueItem, delta, Queue.Count);
                break;
            case FocusPane.History:
                SelectedHistoryItem = Move(SelectedHistoryItem, delta, History.Count);
                break;
            case FocusPane.Favorites:
                SelectedFavorite = Move(SelectedFavorite, delta, Favorites.Count);
                break;
        }
    }

    public void ClampSelections()
    {
        SelectedResult = Move(SelectedResult, 0, SearchResults.Count);
        SelectedQueueItem = Move(SelectedQueueItem, 0, Queue.Count);
        SelectedHistoryItem = Move(SelectedHistoryItem, 0, History.Count);
        SelectedFavorite = Move(SelectedFavorite, 0, Favorites.Count);
    }

    private static int Move(int current, int delta, int count) =>
        count == 0 ? 0 : Math.Clamp(current + delta, 0, count - 1);

    private static T? ItemAt<T>(IReadOnlyList<T> items, int index) where T : class =>
        index >= 0 && index < items.Count ? items[index] : null;
}
