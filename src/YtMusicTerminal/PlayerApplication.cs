using System.Threading.Channels;
using YtMusicTerminal.Configuration;
using YtMusicTerminal.Models;
using YtMusicTerminal.Services;
using YtMusicTerminal.UI;

namespace YtMusicTerminal;

public sealed class PlayerApplication : IAsyncDisposable
{
    private const int SearchBatchSize = 10;

    private readonly AppSettings _initialSettings;
    private readonly SettingsStore _settingsStore;
    private readonly HistoryStore _historyStore;
    private readonly YtDlpClient _youtube;
    private readonly MpvClient _mpv;
    private readonly AppState _state;
    private readonly TerminalFrameRenderer _renderer = new();
    private readonly Channel<AppEvent> _events = Channel.CreateUnbounded<AppEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _shutdown = new();

    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _resolveCancellation;
    private int _searchOperation;
    private int _searchResultLimit;
    private int _resolveOperation;
    private int _lastWidth;
    private int _lastHeight;
    private bool _exitRequested;
    private bool _disposed;

    public PlayerApplication(
        AppSettings settings,
        SettingsStore settingsStore,
        HistoryStore historyStore,
        YtDlpClient youtube,
        MpvClient mpv)
    {
        _initialSettings = settings;
        _settingsStore = settingsStore;
        _historyStore = historyStore;
        _youtube = youtube;
        _mpv = mpv;
        _state = new AppState { Playback = PlaybackSnapshot.Initial(settings.Volume) };

        _mpv.PlaybackEnded += OnPlaybackEnded;
        _mpv.PlaybackFailed += OnPlaybackFailed;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);
        var token = linkedCancellation.Token;

        try
        {
            try
            {
                _state.History = await _historyStore.LoadAsync(token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                _state.StatusMessage = $"History unavailable: {exception.Message}";
            }

            await _mpv.StartAsync(token).ConfigureAwait(false);
            _state.Playback = _mpv.Snapshot;

            using var terminal = new TerminalSession();
            terminal.Enter();
            StartInputLoop(token);
            StartTickLoop(token);

            var dirty = true;
            while (!_exitRequested && !token.IsCancellationRequested)
            {
                if (dirty)
                {
                    var (width, height) = GetTerminalSize();
                    _lastWidth = width;
                    _lastHeight = height;
                    terminal.Write(_renderer.Render(_state, width, height));
                    dirty = false;
                }

                AppEvent appEvent;
                try
                {
                    appEvent = await _events.Reader.ReadAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }

                dirty = await HandleEventAsync(appEvent, token).ConfigureAwait(false);
            }
        }
        finally
        {
            var finalSettings = _initialSettings with { Volume = _mpv.Snapshot.Volume };
            try
            {
                await _settingsStore.SaveAsync(finalSettings, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Could not save settings: {exception.Message}");
            }
        }
    }

    private async Task<bool> HandleEventAsync(AppEvent appEvent, CancellationToken cancellationToken)
    {
        switch (appEvent)
        {
            case KeyPressed keyPressed:
                return await HandleKeyAsync(keyPressed.Key, cancellationToken).ConfigureAwait(false);
            case Tick:
                var snapshot = _mpv.Snapshot;
                var resized = GetTerminalSize() != (_lastWidth, _lastHeight);
                if (snapshot != _state.Playback)
                {
                    _state.Playback = snapshot;
                    return true;
                }

                return resized;
            case SearchCompleted search when search.Operation == _searchOperation:
                _state.IsSearching = false;
                if (search.Error is not null)
                {
                    _state.StatusMessage = search.Error;
                    if (!search.IsLoadMore)
                    {
                        _state.SearchResults = [];
                    }
                }
                else
                {
                    _state.SearchResults = search.Tracks ?? [];
                    _searchResultLimit = search.RequestedLimit;
                    if (!search.IsLoadMore)
                    {
                        _state.SelectedResult = 0;
                    }

                    _state.ClampSelections();
                    _state.Focus = FocusPane.Results;
                    _state.StatusMessage = $"{_state.SearchResults.Count} result(s). Enter plays; m loads 10 more.";
                }

                return true;
            case StreamResolved stream when stream.Operation == _resolveOperation:
                _state.IsResolving = false;
                if (stream.Error is not null || stream.Url is null)
                {
                    _state.StatusMessage = stream.Error ?? "Could not resolve the track.";
                    _state.Playback = _state.Playback with
                    {
                        State = PlaybackState.Error,
                        Error = _state.StatusMessage
                    };
                    return true;
                }

                return await StartPlaybackAsync(stream.Track, stream.Url, cancellationToken).ConfigureAwait(false);
            case PlaybackEnded:
                return BeginNextTrack();
            case PlaybackFailed playbackFailed:
                _state.StatusMessage = playbackFailed.Message;
                _state.Playback = _mpv.Snapshot;
                return true;
            default:
                return false;
        }
    }

    private async Task<bool> HandleKeyAsync(ConsoleKeyInfo key, CancellationToken cancellationToken)
    {
        if (key.Key == ConsoleKey.Q && key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            _exitRequested = true;
            return false;
        }

        if (_state.ShowHelp)
        {
            _state.ShowHelp = false;
            return true;
        }

        if (_state.Focus == FocusPane.Search)
        {
            return HandleSearchKey(key);
        }

        switch (key.Key)
        {
            case ConsoleKey.Q:
                _exitRequested = true;
                return false;
            case ConsoleKey.Tab:
                CycleFocus(key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1);
                return true;
            case ConsoleKey.UpArrow:
                _state.MoveSelection(-1);
                return true;
            case ConsoleKey.DownArrow:
                _state.MoveSelection(1);
                return true;
            case ConsoleKey.Enter:
                BeginPlaySelected();
                return true;
            case ConsoleKey.Spacebar:
                await ExecutePlayerCommandAsync(
                    () => _mpv.TogglePauseAsync(cancellationToken),
                    "Toggled playback.").ConfigureAwait(false);
                return true;
            case ConsoleKey.LeftArrow:
                await ExecutePlayerCommandAsync(
                    () => _mpv.SeekAsync(-5, cancellationToken),
                    "Seeked back 5 seconds.").ConfigureAwait(false);
                return true;
            case ConsoleKey.RightArrow:
                await ExecutePlayerCommandAsync(
                    () => _mpv.SeekAsync(5, cancellationToken),
                    "Seeked forward 5 seconds.").ConfigureAwait(false);
                return true;
            case ConsoleKey.Delete:
                RemoveSelectedQueueItem();
                return true;
        }

        switch (key.KeyChar)
        {
            case '/':
                _state.Focus = FocusPane.Search;
                return true;
            case '?':
                _state.ShowHelp = true;
                return true;
            case 'a':
            case 'A':
                AddSelectedToQueue();
                return true;
            case 'h':
            case 'H':
                _state.Focus = FocusPane.History;
                return true;
            case 'm':
            case 'M':
                BeginSearch(loadMore: true);
                return true;
            case 'n':
            case 'N':
                return BeginNextTrack();
            case 'p':
            case 'P':
                return BeginPreviousTrack();
            case 's':
            case 'S':
                await ExecutePlayerCommandAsync(
                    () => _mpv.StopAsync(cancellationToken),
                    "Playback stopped.").ConfigureAwait(false);
                _state.NowPlaying = null;
                return true;
            case '+':
            case '=':
                await ChangeVolumeAsync(5, cancellationToken).ConfigureAwait(false);
                return true;
            case '-':
            case '_':
                await ChangeVolumeAsync(-5, cancellationToken).ConfigureAwait(false);
                return true;
            default:
                return false;
        }
    }

    private bool HandleSearchKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                _state.Focus = FocusPane.Results;
                return true;
            case ConsoleKey.Tab:
                _state.Focus = key.Modifiers.HasFlag(ConsoleModifiers.Shift)
                    ? FocusPane.History
                    : FocusPane.Results;
                return true;
            case ConsoleKey.Enter:
                BeginSearch();
                return true;
            case ConsoleKey.Backspace when _state.SearchText.Length > 0:
                _state.SearchText = _state.SearchText[..^1];
                return true;
            default:
                if (!char.IsControl(key.KeyChar))
                {
                    _state.SearchText += key.KeyChar;
                    return true;
                }

                return false;
        }
    }

    private void BeginSearch(bool loadMore = false)
    {
        var query = _state.SearchText.Trim();
        if (query.Length == 0)
        {
            _state.StatusMessage = "Enter a search query first.";
            return;
        }

        if (loadMore && _state.SearchResults.Count == 0)
        {
            _state.StatusMessage = "Search first, then press m to load more results.";
            return;
        }

        var requestedLimit = loadMore
            ? Math.Min(50, Math.Max(SearchBatchSize, _searchResultLimit) + SearchBatchSize)
            : SearchBatchSize;
        if (loadMore && requestedLimit == _searchResultLimit)
        {
            _state.StatusMessage = "The 50-result search limit has been reached.";
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var operation = ++_searchOperation;
        var token = _searchCancellation.Token;
        _state.IsSearching = true;
        _state.StatusMessage = loadMore
            ? $"Loading 10 more results for '{query}'..."
            : $"Searching for '{query}'...";

        _ = Task.Run(async () =>
        {
            try
            {
                var tracks = await _youtube.SearchAsync(
                    query,
                    requestedLimit,
                    token).ConfigureAwait(false);
                _events.Writer.TryWrite(new SearchCompleted(
                    operation,
                    tracks,
                    null,
                    loadMore,
                    requestedLimit));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _events.Writer.TryWrite(new SearchCompleted(
                    operation,
                    null,
                    exception.Message,
                    loadMore,
                    requestedLimit));
            }
        }, CancellationToken.None);
    }

    private void BeginPlaySelected()
    {
        var track = _state.SelectedTrack;
        if (track is null)
        {
            _state.StatusMessage = "Select a track first.";
            return;
        }

        if (_state.Focus == FocusPane.Queue)
        {
            _state.CurrentQueueItem = _state.SelectedQueueItem;
        }
        else
        {
            _state.CurrentQueueItem = -1;
        }

        BeginResolve(track);
    }

    private void BeginResolve(Track track)
    {
        _resolveCancellation?.Cancel();
        _resolveCancellation?.Dispose();
        _resolveCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var operation = ++_resolveOperation;
        var token = _resolveCancellation.Token;
        _state.IsResolving = true;
        _state.NowPlaying = track;
        _state.Playback = _state.Playback with
        {
            State = PlaybackState.Loading,
            Position = TimeSpan.Zero,
            Duration = track.Duration ?? TimeSpan.Zero,
            Error = null
        };
        _state.StatusMessage = $"Resolving {track.Title}...";

        _ = Task.Run(async () =>
        {
            try
            {
                var url = await _youtube.ResolveAudioUrlAsync(track, token).ConfigureAwait(false);
                _events.Writer.TryWrite(new StreamResolved(operation, track, url, null));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _events.Writer.TryWrite(new StreamResolved(operation, track, null, exception.Message));
            }
        }, CancellationToken.None);
    }

    private async Task<bool> StartPlaybackAsync(
        Track track,
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            await _mpv.LoadAsync(url, cancellationToken).ConfigureAwait(false);
            _state.NowPlaying = track;
            _state.Playback = _mpv.Snapshot;
            _state.StatusMessage = $"Playing {track.Title}.";
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            _state.StatusMessage = exception.Message;
            _state.Playback = _state.Playback with
            {
                State = PlaybackState.Error,
                Error = exception.Message
            };
            return true;
        }

        try
        {
            _state.History = await _historyStore.AddAsync(track, cancellationToken).ConfigureAwait(false);
            _state.SelectedHistoryItem = 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _state.StatusMessage = $"Playing {track.Title}; history unavailable: {exception.Message}";
        }

        return true;
    }

    private void AddSelectedToQueue()
    {
        var track = _state.SelectedTrack;
        if (track is null)
        {
            _state.StatusMessage = "Select a result or history item first.";
            return;
        }

        _state.Queue.Add(track);
        _state.SelectedQueueItem = _state.Queue.Count - 1;
        _state.StatusMessage = $"Queued {track.Title}.";
    }

    private void RemoveSelectedQueueItem()
    {
        if (_state.Focus != FocusPane.Queue || _state.Queue.Count == 0)
        {
            return;
        }

        var removedIndex = _state.SelectedQueueItem;
        var removed = _state.Queue[removedIndex];
        _state.Queue.RemoveAt(removedIndex);
        if (_state.CurrentQueueItem > removedIndex)
        {
            _state.CurrentQueueItem--;
        }
        else if (_state.CurrentQueueItem == removedIndex)
        {
            _state.CurrentQueueItem = -1;
        }

        _state.ClampSelections();
        _state.StatusMessage = $"Removed {removed.Title} from the queue.";
    }

    private bool BeginNextTrack()
    {
        if (_state.Queue.Count == 0)
        {
            _state.StatusMessage = "The queue is empty.";
            return true;
        }

        var next = _state.CurrentQueueItem < 0 ? 0 : _state.CurrentQueueItem + 1;
        if (next >= _state.Queue.Count)
        {
            _state.StatusMessage = "Reached the end of the queue.";
            return true;
        }

        _state.CurrentQueueItem = next;
        _state.SelectedQueueItem = next;
        BeginResolve(_state.Queue[next]);
        return true;
    }

    private bool BeginPreviousTrack()
    {
        if (_state.Queue.Count == 0 || _state.CurrentQueueItem <= 0)
        {
            _state.StatusMessage = "There is no previous queued track.";
            return true;
        }

        _state.CurrentQueueItem--;
        _state.SelectedQueueItem = _state.CurrentQueueItem;
        BeginResolve(_state.Queue[_state.CurrentQueueItem]);
        return true;
    }

    private async Task ChangeVolumeAsync(int delta, CancellationToken cancellationToken)
    {
        var volume = Math.Clamp(_mpv.Snapshot.Volume + delta, 0, 100);
        await ExecutePlayerCommandAsync(
            () => _mpv.SetVolumeAsync(volume, cancellationToken),
            $"Volume {volume}%.").ConfigureAwait(false);
    }

    private async Task ExecutePlayerCommandAsync(Func<Task> command, string successMessage)
    {
        try
        {
            await command().ConfigureAwait(false);
            _state.StatusMessage = successMessage;
            _state.Playback = _mpv.Snapshot;
        }
        catch (InvalidOperationException exception)
        {
            _state.StatusMessage = exception.Message;
        }
    }

    private void CycleFocus(int direction)
    {
        FocusPane[] panes = [FocusPane.Search, FocusPane.Results, FocusPane.Queue, FocusPane.History];
        var current = Array.IndexOf(panes, _state.Focus);
        _state.Focus = panes[(current + direction + panes.Length) % panes.Length];
    }

    private void StartInputLoop(CancellationToken cancellationToken)
    {
        _ = Task.Run(() =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var key = Console.ReadKey(intercept: true);
                    if (!_events.Writer.TryWrite(new KeyPressed(key)))
                    {
                        return;
                    }
                }
                catch (InvalidOperationException)
                {
                    _events.Writer.TryWrite(new PlaybackFailed("Interactive console input is unavailable."));
                    return;
                }
            }
        }, CancellationToken.None);
    }

    private void StartTickLoop(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!_events.Writer.TryWrite(new Tick()))
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }, CancellationToken.None);
    }

    private static (int Width, int Height) GetTerminalSize()
    {
        try
        {
            return (Math.Max(Console.WindowWidth, 1), Math.Max(Console.WindowHeight, 1));
        }
        catch (IOException)
        {
            return (80, 24);
        }
    }

    private void OnPlaybackEnded() => _events.Writer.TryWrite(new PlaybackEnded());

    private void OnPlaybackFailed(string message) =>
        _events.Writer.TryWrite(new PlaybackFailed(message));

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        _searchCancellation?.Cancel();
        _resolveCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _resolveCancellation?.Dispose();
        _mpv.PlaybackEnded -= OnPlaybackEnded;
        _mpv.PlaybackFailed -= OnPlaybackFailed;
        await _mpv.DisposeAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }

    private abstract record AppEvent;

    private sealed record KeyPressed(ConsoleKeyInfo Key) : AppEvent;

    private sealed record Tick : AppEvent;

    private sealed record SearchCompleted(
        int Operation,
        IReadOnlyList<Track>? Tracks,
        string? Error,
        bool IsLoadMore,
        int RequestedLimit) : AppEvent;

    private sealed record StreamResolved(
        int Operation,
        Track Track,
        string? Url,
        string? Error) : AppEvent;

    private sealed record PlaybackEnded : AppEvent;

    private sealed record PlaybackFailed(string Message) : AppEvent;
}
