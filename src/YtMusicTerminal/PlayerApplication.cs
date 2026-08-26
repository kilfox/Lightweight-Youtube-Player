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
    private readonly LibraryStore _libraryStore;
    private readonly YtDlpClient _youtube;
    private readonly MpvClient _mpv;
    private readonly string? _startupInput;
    private readonly AppState _state;
    private readonly TerminalFrameRenderer _renderer = new();
    private readonly Channel<AppEvent> _events = Channel.CreateUnbounded<AppEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _shutdown = new();

    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _resolveCancellation;
    private CancellationTokenSource? _prefetchCancellation;
    private Task<string?>? _prefetchTask;
    private string? _prefetchTrackId;
    private bool _prefetchStarted;
    private CancellationTokenSource? _autoplayCancellation;
    private Task<AutoplayCandidate?>? _autoplayTask;
    private string? _autoplaySeedId;
    private readonly List<Track> _radioTracks = [];
    private int _radioIndex = -1;
    private bool _currentTrackIsAutoplay;
    private bool _manualStopRequested;
    private int _searchOperation;
    private int _searchResultLimit;
    private int _resolveOperation;
    private int _directOperation;
    private int _lastWidth;
    private int _lastHeight;
    private double _pendingResumeSeconds;
    private double _resumePositionSeconds;
    private Track? _lastPlayedTrack;
    private bool _exitRequested;
    private bool _disposed;

    public PlayerApplication(
        AppSettings settings,
        SettingsStore settingsStore,
        HistoryStore historyStore,
        LibraryStore libraryStore,
        YtDlpClient youtube,
        MpvClient mpv,
        string? startupInput = null)
    {
        _initialSettings = settings;
        _settingsStore = settingsStore;
        _historyStore = historyStore;
        _libraryStore = libraryStore;
        _youtube = youtube;
        _mpv = mpv;
        _startupInput = startupInput;
        _state = new AppState { Playback = PlaybackSnapshot.Initial(settings.Volume) };

        _mpv.PlaybackEnded += OnPlaybackEnded;
        _mpv.PlaybackFailed += OnPlaybackFailed;
        _mpv.SnapshotChanged += OnSnapshotChanged;
        _mpv.MediaNextRequested += OnMediaNextRequested;
        _mpv.MediaPreviousRequested += OnMediaPreviousRequested;
        _mpv.MediaStopRequested += OnMediaStopRequested;
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
                var library = await _libraryStore.LoadAsync(token).ConfigureAwait(false);
                _state.Queue.AddRange(library.Queue);
                _state.Favorites.AddRange(library.Favorites);
                _state.Shuffle = library.Shuffle;
                _state.Repeat = library.Repeat;
                _state.Autoplay = library.Autoplay;
                _lastPlayedTrack = library.LastTrack;
                _resumePositionSeconds = Math.Max(0, library.LastPositionSeconds);
                if (_lastPlayedTrack is not null)
                {
                    _state.StatusMessage = $"Press F5 to resume {_lastPlayedTrack.Title}.";
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                _state.StatusMessage = $"Local data unavailable: {exception.Message}";
            }

            await _mpv.StartAsync(token).ConfigureAwait(false);
            _state.Playback = _mpv.Snapshot;

            using var terminal = new TerminalSession();
            terminal.Enter();
            StartInputLoop(token);
            StartTickLoop(token);
            HandleStartupInput();

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

            try
            {
                await _libraryStore.SaveAsync(CreateLibraryState(), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Could not save library: {exception.Message}");
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
                    if (_state.NowPlaying is not null && snapshot.Position > TimeSpan.Zero)
                    {
                        _resumePositionSeconds = snapshot.Position.TotalSeconds;
                    }
                    return true;
                }

                return resized;
            case PlaybackSnapshotChanged snapshotChanged:
                _state.Playback = snapshotChanged.Snapshot;
                return true;
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

                BeginPrefetchNextTrack();

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

                return await StartPlaybackAsync(
                    stream.Track,
                    stream.Url,
                    stream.IsAutoplay,
                    cancellationToken).ConfigureAwait(false);
            case DirectTrackLoaded direct when direct.Operation == _directOperation:
                if (direct.Track is null)
                {
                    _state.IsResolving = false;
                    _state.StatusMessage = direct.Error ?? "Could not read the YouTube URL.";
                    return true;
                }

                ResetRadioSession();
                BeginResolve(direct.Track);
                return true;
            case PlaybackEnded:
                return await HandlePlaybackEndedAsync(cancellationToken).ConfigureAwait(false);
            case PlaybackFailed playbackFailed:
                if (_state.NowPlaying is not null)
                {
                    _youtube.InvalidateAudioUrl(_state.NowPlaying.Id);
                }
                _state.StatusMessage = playbackFailed.Message;
                _state.Playback = _mpv.Snapshot;
                return true;
            case MediaNextRequested:
                return await BeginNextTrackAsync(cancellationToken).ConfigureAwait(false);
            case MediaPreviousRequested:
                return BeginPreviousTrack();
            case MediaStopRequested:
                _manualStopRequested = true;
                await ExecutePlayerCommandAsync(
                    () => _mpv.StopAsync(cancellationToken),
                    "Playback stopped.").ConfigureAwait(false);
                CancelAutoplayPrefetch();
                _state.NowPlaying = null;
                _currentTrackIsAutoplay = false;
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

        if (key.Key == ConsoleKey.F5)
        {
            return ResumeLastTrack();
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
            case ConsoleKey.Escape:
                FocusPlayer();
                return true;
            case ConsoleKey.UpArrow:
                if (_state.Focus == FocusPane.Player)
                {
                    await ChangeVolumeAsync(5, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _state.MoveSelection(-1);
                }
                return true;
            case ConsoleKey.DownArrow:
                if (_state.Focus == FocusPane.Player)
                {
                    await ChangeVolumeAsync(-5, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _state.MoveSelection(1);
                }
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
                _state.ShowFavorites = false;
                _state.Focus = FocusPane.History;
                return true;
            case 'v':
            case 'V':
                _state.ShowFavorites = true;
                _state.Focus = FocusPane.Favorites;
                return true;
            case 'f':
            case 'F':
                ToggleFavorite();
                return true;
            case 'm':
            case 'M':
                BeginSearch(loadMore: true);
                return true;
            case 'n':
            case 'N':
                return await BeginNextTrackAsync(cancellationToken).ConfigureAwait(false);
            case 'p':
            case 'P':
                return BeginPreviousTrack();
            case 'x':
            case 'X':
                _state.Shuffle = !_state.Shuffle;
                BeginPrefetchNextTrack();
                _state.StatusMessage = $"Shuffle {(_state.Shuffle ? "enabled" : "disabled")}.";
                return true;
            case 'r':
            case 'R':
                CycleRepeatMode();
                return true;
            case 'y':
            case 'Y':
                ToggleAutoplay();
                return true;
            case 'c':
            case 'C':
                return ResumeLastTrack();
            case 's':
            case 'S':
                _manualStopRequested = true;
                await ExecutePlayerCommandAsync(
                    () => _mpv.StopAsync(cancellationToken),
                    "Playback stopped.").ConfigureAwait(false);
                CancelAutoplayPrefetch();
                _state.NowPlaying = null;
                _currentTrackIsAutoplay = false;
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
                FocusPlayer();
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

        CancelPrefetch();
        CancelAutoplayPrefetch();
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

        ResetRadioSession();
        BeginResolve(track);
    }

    private void BeginResolve(
        Track track,
        double resumePositionSeconds = 0,
        string? preparedUrl = null,
        bool isAutoplay = false)
    {
        _manualStopRequested = false;
        var prefetchTask = preparedUrl is null
            && string.Equals(_prefetchTrackId, track.Id, StringComparison.Ordinal)
            && Volatile.Read(ref _prefetchStarted)
                ? _prefetchTask
                : null;
        if (prefetchTask is null)
        {
            CancelPrefetch();
        }

        _resolveCancellation?.Cancel();
        _resolveCancellation?.Dispose();
        _resolveCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var operation = ++_resolveOperation;
        var token = _resolveCancellation.Token;
        _state.IsResolving = true;
        _pendingResumeSeconds = Math.Max(0, resumePositionSeconds);
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
                var url = preparedUrl ?? (prefetchTask is null
                    ? await _youtube.ResolveAudioUrlAsync(track, token).ConfigureAwait(false)
                    : await prefetchTask.WaitAsync(token).ConfigureAwait(false)
                        ?? await _youtube.ResolveAudioUrlAsync(track, token).ConfigureAwait(false));
                _events.Writer.TryWrite(new StreamResolved(operation, track, url, isAutoplay, null));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _events.Writer.TryWrite(new StreamResolved(operation, track, null, isAutoplay, exception.Message));
            }
        }, CancellationToken.None);
    }

    private async Task<bool> StartPlaybackAsync(
        Track track,
        string url,
        bool isAutoplay,
        CancellationToken cancellationToken)
    {
        try
        {
            await _mpv.LoadAsync(url, cancellationToken).ConfigureAwait(false);
            var resumePosition = _pendingResumeSeconds;
            if (resumePosition > 0)
            {
                await WaitForPlaybackStartAsync(cancellationToken).ConfigureAwait(false);
                await _mpv.SeekToAsync(resumePosition, cancellationToken).ConfigureAwait(false);
            }
            _pendingResumeSeconds = 0;
            _resumePositionSeconds = resumePosition;
            _state.NowPlaying = track;
            _currentTrackIsAutoplay = isAutoplay;
            _lastPlayedTrack = track;
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

        BeginPrefetchNextTrack();

        return true;
    }

    private async Task WaitForPlaybackStartAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (_mpv.Snapshot.State is PlaybackState.Idle or PlaybackState.Loading)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new InvalidOperationException("Timed out while preparing the track for resume.");
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
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
        BeginPrefetchNextTrack();
        _state.StatusMessage = $"Queued {track.Title}.";
    }

    private void ToggleFavorite()
    {
        var track = _state.SelectedTrack ?? _state.NowPlaying;
        if (track is null)
        {
            _state.StatusMessage = "Select or play a track first.";
            return;
        }

        var existing = _state.Favorites.FindIndex(item => item.Id == track.Id);
        if (existing >= 0)
        {
            _state.Favorites.RemoveAt(existing);
            _state.ClampSelections();
            _state.StatusMessage = $"Removed {track.Title} from favorites.";
        }
        else
        {
            _state.Favorites.Insert(0, track);
            _state.SelectedFavorite = 0;
            _state.StatusMessage = $"Added {track.Title} to favorites.";
        }
    }

    private void CycleRepeatMode()
    {
        _state.Repeat = _state.Repeat switch
        {
            RepeatMode.Off => RepeatMode.Track,
            RepeatMode.Track => RepeatMode.Queue,
            _ => RepeatMode.Off
        };
        BeginPrefetchNextTrack();
        _state.StatusMessage = $"Repeat {_state.Repeat.ToString().ToLowerInvariant()}.";
    }

    private void ToggleAutoplay()
    {
        _state.Autoplay = !_state.Autoplay;
        if (_state.Autoplay)
        {
            BeginPrefetchNextTrack();
        }
        else
        {
            CancelAutoplayPrefetch();
        }

        _state.StatusMessage = $"Autoplay {(_state.Autoplay ? "enabled" : "disabled")}.";
    }

    private bool ResumeLastTrack()
    {
        if (_lastPlayedTrack is null)
        {
            _state.StatusMessage = "There is no saved track to resume.";
            return true;
        }

        ResetRadioSession();
        BeginResolve(_lastPlayedTrack, _resumePositionSeconds);
        return true;
    }

    private async Task<bool> HandlePlaybackEndedAsync(CancellationToken cancellationToken)
    {
        if (_manualStopRequested)
        {
            _manualStopRequested = false;
            return true;
        }

        _resumePositionSeconds = 0;
        if (_state.Repeat == RepeatMode.Track && _state.NowPlaying is not null)
        {
            BeginResolve(_state.NowPlaying, isAutoplay: _currentTrackIsAutoplay);
            return true;
        }

        return await BeginNextTrackAsync(cancellationToken, naturalCompletion: true).ConfigureAwait(false);
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
        BeginPrefetchNextTrack();
        _state.StatusMessage = $"Removed {removed.Title} from the queue.";
    }

    private void BeginPrefetchNextTrack()
    {
        if (_state.NowPlaying is null || _state.Repeat == RepeatMode.Track)
        {
            CancelPrefetch();
            CancelAutoplayPrefetch();
            return;
        }

        if (HasExplicitQueuedNextTrack())
        {
            CancelAutoplayPrefetch();
            if (_state.Shuffle)
            {
                CancelPrefetch();
                return;
            }

            var nextIndex = GetLinearNextQueueIndex();
            if (nextIndex < 0)
            {
                CancelPrefetch();
                return;
            }

            BeginQueuePrefetch(_state.Queue[nextIndex]);
            return;
        }

        CancelPrefetch();
        if (_state.Autoplay)
        {
            BeginAutoplayPrefetch(_state.NowPlaying);
            return;
        }

        CancelAutoplayPrefetch();
    }

    private void BeginQueuePrefetch(Track nextTrack)
    {
        if (string.Equals(_prefetchTrackId, nextTrack.Id, StringComparison.Ordinal)
            && _prefetchTask is not null)
        {
            return;
        }

        CancelPrefetch();
        _prefetchCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var token = _prefetchCancellation.Token;
        _prefetchTrackId = nextTrack.Id;
        _prefetchTask = PrefetchAsync(nextTrack, token);
    }

    private async Task<string?> PrefetchAsync(Track track, CancellationToken cancellationToken)
    {
        try
        {
            Volatile.Write(ref _prefetchStarted, true);
            return await _youtube.ResolveAudioUrlAsync(track, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException)
        {
            System.Diagnostics.Debug.WriteLine($"Queue prefetch failed: {exception.Message}");
            return null;
        }
    }

    private void CancelPrefetch()
    {
        _prefetchCancellation?.Cancel();
        _prefetchCancellation?.Dispose();
        _prefetchCancellation = null;
        _prefetchTask = null;
        _prefetchTrackId = null;
        Volatile.Write(ref _prefetchStarted, false);
    }

    private bool HasExplicitQueuedNextTrack()
    {
        if (_state.Queue.Count == 0)
        {
            return false;
        }

        if (_state.CurrentQueueItem < 0)
        {
            return true;
        }

        if (_state.Shuffle)
        {
            return _state.Queue.Count > 1 || _state.Repeat == RepeatMode.Queue;
        }

        return _state.CurrentQueueItem + 1 < _state.Queue.Count
            || _state.Repeat == RepeatMode.Queue;
    }

    private int GetLinearNextQueueIndex()
    {
        if (_state.Queue.Count == 0)
        {
            return -1;
        }

        var nextIndex = _state.CurrentQueueItem < 0 ? 0 : _state.CurrentQueueItem + 1;
        if (nextIndex < _state.Queue.Count)
        {
            return nextIndex;
        }

        return _state.Repeat == RepeatMode.Queue ? 0 : -1;
    }

    private void BeginAutoplayPrefetch(Track seed)
    {
        if (_currentTrackIsAutoplay && _radioIndex + 1 < _radioTracks.Count)
        {
            CancelAutoplayPrefetch();
            return;
        }

        if (string.Equals(_autoplaySeedId, seed.Id, StringComparison.Ordinal)
            && _autoplayTask is not null)
        {
            return;
        }

        CancelAutoplayPrefetch();
        _autoplayCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var token = _autoplayCancellation.Token;
        _autoplaySeedId = seed.Id;
        _autoplayTask = PrepareAutoplayTrackAsync(seed, token);
    }

    private async Task<AutoplayCandidate?> PrepareAutoplayTrackAsync(
        Track seed,
        CancellationToken cancellationToken)
    {
        try
        {
            var excluded = _state.History
                .Take(20)
                .Select(entry => entry.Track.Id)
                .Concat(_radioTracks.Select(track => track.Id))
                .ToHashSet(StringComparer.Ordinal);
            var related = await _youtube.GetRelatedTracksAsync(
                seed,
                excluded,
                cancellationToken).ConfigureAwait(false);
            var track = related.FirstOrDefault();
            if (track is null)
            {
                return null;
            }

            var url = await _youtube.ResolveAudioUrlAsync(track, cancellationToken).ConfigureAwait(false);
            return new AutoplayCandidate(track, url);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException
                                          or InvalidOperationException
                                          or System.Text.Json.JsonException
                                          or TimeoutException)
        {
            System.Diagnostics.Debug.WriteLine($"Autoplay prefetch failed: {exception.Message}");
            return null;
        }
    }

    private async Task<bool> BeginNextAutoplayTrackAsync(CancellationToken cancellationToken)
    {
        if (_currentTrackIsAutoplay && _radioIndex + 1 < _radioTracks.Count)
        {
            _radioIndex++;
            BeginResolve(_radioTracks[_radioIndex], isAutoplay: true);
            return true;
        }

        if (_state.NowPlaying is null)
        {
            return true;
        }

        BeginAutoplayPrefetch(_state.NowPlaying);
        var task = _autoplayTask;
        var candidate = task is null
            ? null
            : await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!ReferenceEquals(task, _autoplayTask))
        {
            return true;
        }

        _autoplayCancellation?.Dispose();
        _autoplayCancellation = null;
        _autoplayTask = null;
        _autoplaySeedId = null;
        if (candidate is null)
        {
            _state.NowPlaying = null;
            _currentTrackIsAutoplay = false;
            _state.StatusMessage = "Autoplay could not find a related track.";
            return true;
        }

        if (!_currentTrackIsAutoplay)
        {
            _radioTracks.Clear();
            _radioTracks.Add(_state.NowPlaying);
            _radioIndex = 0;
        }

        if (_radioIndex + 1 < _radioTracks.Count)
        {
            _radioTracks.RemoveRange(_radioIndex + 1, _radioTracks.Count - _radioIndex - 1);
        }

        _radioTracks.Add(candidate.Track);
        _radioIndex = _radioTracks.Count - 1;
        BeginResolve(candidate.Track, preparedUrl: candidate.Url, isAutoplay: true);
        return true;
    }

    private void CancelAutoplayPrefetch()
    {
        _autoplayCancellation?.Cancel();
        _autoplayCancellation?.Dispose();
        _autoplayCancellation = null;
        _autoplayTask = null;
        _autoplaySeedId = null;
    }

    private void ResetRadioSession()
    {
        CancelAutoplayPrefetch();
        _radioTracks.Clear();
        _radioIndex = -1;
        _currentTrackIsAutoplay = false;
    }

    private async Task<bool> BeginNextTrackAsync(
        CancellationToken cancellationToken,
        bool naturalCompletion = false)
    {
        if (TryBeginNextQueuedTrack())
        {
            return true;
        }

        if (_state.Autoplay
            && _state.NowPlaying is not null
            && (naturalCompletion || _currentTrackIsAutoplay))
        {
            return await BeginNextAutoplayTrackAsync(cancellationToken).ConfigureAwait(false);
        }

        _state.StatusMessage = _state.Queue.Count == 0
            ? "The queue is empty."
            : "Reached the end of the queue.";
        return true;
    }

    private bool TryBeginNextQueuedTrack()
    {
        if (!HasExplicitQueuedNextTrack())
        {
            return false;
        }

        var candidates = Enumerable.Range(0, _state.Queue.Count)
            .Where(index => index != _state.CurrentQueueItem)
            .ToArray();
        var next = _state.Shuffle && candidates.Length > 0
            ? candidates[Random.Shared.Next(candidates.Length)]
            : _state.CurrentQueueItem < 0 ? 0 : _state.CurrentQueueItem + 1;
        if (next >= _state.Queue.Count)
        {
            if (_state.Repeat == RepeatMode.Queue)
            {
                next = 0;
            }
            else
            {
                return false;
            }
        }

        _state.CurrentQueueItem = next;
        _state.SelectedQueueItem = next;
        ResetRadioSession();
        BeginResolve(_state.Queue[next]);
        return true;
    }

    private bool BeginPreviousTrack()
    {
        if (_state.Queue.Count > 0)
        {
            if (_state.CurrentQueueItem > 0)
            {
                _state.CurrentQueueItem--;
                _state.SelectedQueueItem = _state.CurrentQueueItem;
                ResetRadioSession();
                BeginResolve(_state.Queue[_state.CurrentQueueItem]);
            }
            else
            {
                _state.StatusMessage = "There is no previous queued track.";
            }

            return true;
        }

        if (_currentTrackIsAutoplay && _radioIndex > 0)
        {
            _radioIndex--;
            BeginResolve(_radioTracks[_radioIndex], isAutoplay: true);
            return true;
        }

        _state.StatusMessage = "There is no previous radio track.";
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
        FocusPane[] panes =
        [
            FocusPane.Search,
            FocusPane.Results,
            FocusPane.Queue,
            _state.ShowFavorites ? FocusPane.Favorites : FocusPane.History,
            FocusPane.Player
        ];
        var current = Array.IndexOf(panes, _state.Focus);
        _state.Focus = panes[(current + direction + panes.Length) % panes.Length];
    }

    private void FocusPlayer()
    {
        _state.Focus = FocusPane.Player;
        _state.StatusMessage = "Player focused. Up/Down changes volume.";
    }

    private void HandleStartupInput()
    {
        var input = _startupInput?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            BeginDirectUrl(input);
            return;
        }

        _state.SearchText = input;
        BeginSearch();
    }

    private void BeginDirectUrl(string url)
    {
        var operation = ++_directOperation;
        var token = _shutdown.Token;
        _state.IsResolving = true;
        _state.StatusMessage = "Reading YouTube track information...";
        _ = Task.Run(async () =>
        {
            try
            {
                var track = await _youtube.GetTrackAsync(url, token).ConfigureAwait(false);
                _events.Writer.TryWrite(new DirectTrackLoaded(operation, track, null));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _events.Writer.TryWrite(new DirectTrackLoaded(operation, null, exception.Message));
            }
        }, CancellationToken.None);
    }

    private LibraryState CreateLibraryState() => new()
    {
        Queue = [.. _state.Queue],
        Favorites = [.. _state.Favorites],
        LastTrack = _lastPlayedTrack,
        LastPositionSeconds = Math.Max(0, _resumePositionSeconds),
        Shuffle = _state.Shuffle,
        Repeat = _state.Repeat,
        Autoplay = _state.Autoplay
    };

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

    private void OnSnapshotChanged(PlaybackSnapshot snapshot) =>
        _events.Writer.TryWrite(new PlaybackSnapshotChanged(snapshot));

    private void OnMediaNextRequested() => _events.Writer.TryWrite(new MediaNextRequested());

    private void OnMediaPreviousRequested() => _events.Writer.TryWrite(new MediaPreviousRequested());

    private void OnMediaStopRequested() => _events.Writer.TryWrite(new MediaStopRequested());

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
        CancelPrefetch();
        CancelAutoplayPrefetch();
        _mpv.PlaybackEnded -= OnPlaybackEnded;
        _mpv.PlaybackFailed -= OnPlaybackFailed;
        _mpv.SnapshotChanged -= OnSnapshotChanged;
        _mpv.MediaNextRequested -= OnMediaNextRequested;
        _mpv.MediaPreviousRequested -= OnMediaPreviousRequested;
        _mpv.MediaStopRequested -= OnMediaStopRequested;
        await _mpv.DisposeAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }

    private abstract record AppEvent;

    private sealed record KeyPressed(ConsoleKeyInfo Key) : AppEvent;

    private sealed record Tick : AppEvent;

    private sealed record PlaybackSnapshotChanged(PlaybackSnapshot Snapshot) : AppEvent;

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
        bool IsAutoplay,
        string? Error) : AppEvent;

    private sealed record DirectTrackLoaded(
        int Operation,
        Track? Track,
        string? Error) : AppEvent;

    private sealed record PlaybackEnded : AppEvent;

    private sealed record PlaybackFailed(string Message) : AppEvent;

    private sealed record MediaNextRequested : AppEvent;

    private sealed record MediaPreviousRequested : AppEvent;

    private sealed record MediaStopRequested : AppEvent;

    private sealed record AutoplayCandidate(Track Track, string Url);
}
