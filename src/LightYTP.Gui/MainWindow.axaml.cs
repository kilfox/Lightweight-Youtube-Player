using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using YtMusicTerminal.Configuration;
using YtMusicTerminal.Models;
using YtMusicTerminal.Services;
using Track = YtMusicTerminal.Models.Track;

namespace LightYTP.Gui;

public sealed partial class MainWindow : Window
{
    private const int SearchBatchSize = 10;
    private const int MaximumSearchResults = 50;

    private readonly ObservableCollection<Track> _searchResults = [];
    private readonly ObservableCollection<Track> _queue = [];
    private readonly ObservableCollection<Track> _history = [];
    private readonly ObservableCollection<Track> _favorites = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly DispatcherTimer _playbackTimer;
    private readonly string? _startupInput;

    private Task? _initializationTask;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _resolveCancellation;
    private CancellationTokenSource? _prefetchCancellation;
    private Task<string?>? _prefetchTask;
    private string? _prefetchTrackId;
    private bool _prefetchStarted;

    private AppSettings _settings = new();
    private SettingsStore? _settingsStore;
    private HistoryStore? _historyStore;
    private LibraryStore? _libraryStore;
    private YtDlpClient? _youtube;
    private MpvClient? _mpv;
    private Track? _currentTrack;
    private RepeatMode _repeat;
    private bool _shuffle;
    private int _queueIndex = -1;
    private int _searchLimit = SearchBatchSize;
    private bool _updatingPlaybackControls;
    private bool _isClosing;
    private bool _closeCompleted;

    public MainWindow()
        : this([])
    {
    }

    public MainWindow(IReadOnlyList<string> args)
    {
        InitializeComponent();

        _startupInput = args.Count == 0 ? null : string.Join(' ', args);
        SearchResultsList.ItemsSource = _searchResults;
        QueueList.ItemsSource = _queue;
        HistoryList.ItemsSource = _history;
        FavoritesList.ItemsSource = _favorites;

        _playbackTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, OnPlaybackTick);
        Opened += OnOpened;
        Closing += OnClosing;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        _initializationTask = RunActionAsync(async () =>
        {
            await InitializePlayerAsync(_lifetime.Token);
            _playbackTimer.Start();
            SearchBox.Focus();

            if (!string.IsNullOrWhiteSpace(_startupInput))
            {
                SearchBox.Text = _startupInput;
                await RunSearchAsync(resetLimit: true);
            }
        });
        await _initializationTask;
    }

    private async Task InitializePlayerAsync(CancellationToken cancellationToken)
    {
        SetStatus("Finding playback tools...");
        var paths = AppPaths.CreateDefault();
        _settingsStore = new SettingsStore(paths.SettingsFile);
        _historyStore = new HistoryStore(paths.HistoryFile);
        _libraryStore = new LibraryStore(paths.LibraryFile);

        try
        {
            _settings = await _settingsStore.LoadAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException($"Could not load settings: {exception.Message}", exception);
        }

        var ytDlpName = OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";
        var mpvName = OperatingSystem.IsWindows() ? "mpv.exe" : "mpv";
        var ytDlpPath = ToolLocator.Find(ytDlpName, _settings.YtDlpPath, "YTMUSIC_YTDLP");
        var mpvPath = ToolLocator.Find(mpvName, _settings.MpvPath, "YTMUSIC_MPV");
        if (ytDlpPath is null || mpvPath is null)
        {
            var missing = new List<string>();
            if (ytDlpPath is null)
            {
                missing.Add("yt-dlp");
            }

            if (mpvPath is null)
            {
                missing.Add("mpv");
            }

            throw new InvalidOperationException(
                $"Missing {string.Join(" and ", missing)}. Reinstall the Windows GUI package or install the tools with your system package manager.");
        }

        _youtube = new YtDlpClient(ytDlpPath, new ProcessRunner());
        _mpv = new MpvClient(mpvPath, _settings.Volume, Path.Combine(paths.DataDirectory, "mpv-gui.log"));
        _mpv.PlaybackEnded += OnPlaybackEnded;
        _mpv.PlaybackFailed += OnPlaybackFailed;
        _mpv.SnapshotChanged += OnSnapshotChanged;

        var historyTask = _historyStore.LoadAsync(cancellationToken);
        var libraryTask = _libraryStore.LoadAsync(cancellationToken);
        await _mpv.StartAsync(cancellationToken);
        var history = await historyTask;
        var library = await libraryTask;

        Replace(_history, history.Select(entry => entry.Track));
        Replace(_queue, library.Queue);
        Replace(_favorites, library.Favorites);
        _currentTrack = library.LastTrack;
        _repeat = library.Repeat;
        _shuffle = library.Shuffle;
        _queueIndex = _currentTrack is null ? -1 : IndexOf(_queue, _currentTrack.Id);

        _updatingPlaybackControls = true;
        VolumeSlider.Value = _settings.Volume;
        _updatingPlaybackControls = false;
        if (_currentTrack is not null)
        {
            NowPlayingText.Text = _currentTrack.Title;
            ArtistText.Text = $"{_currentTrack.Artist}  •  ready to resume";
        }

        SetStatus("Ready. Search for a song or paste a YouTube URL.");
    }

    private async Task SearchOrPlayAsync(bool resetLimit, CancellationToken cancellationToken)
    {
        var query = SearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            SetStatus("Enter a song, artist, or YouTube URL.");
            return;
        }

        var youtube = RequireYoutube();
        if (LooksLikeUrl(query))
        {
            SetStatus("Reading the YouTube link...");
            var track = await youtube.GetTrackAsync(query, cancellationToken);
            Replace(_searchResults, [track]);
            SearchResultsList.SelectedIndex = 0;
            await RunPlayTrackAsync(track);
            return;
        }

        if (resetLimit)
        {
            _searchLimit = SearchBatchSize;
        }

        SetStatus($"Searching for {query}...");
        var tracks = await youtube.SearchAsync(query, _searchLimit, cancellationToken);
        Replace(_searchResults, tracks);
        SearchResultsList.SelectedIndex = tracks.Count == 0 ? -1 : 0;
        SetStatus(tracks.Count == 0
            ? "No results found."
            : $"{tracks.Count} results. Double-click a track or press PLAY.");
        BeginPrefetchNextTrack();
    }

    private async Task PlayTrackAsync(
        Track track,
        Task<string?>? prefetchTask,
        CancellationToken cancellationToken)
    {
        SetStatus($"Resolving {track.Title}...");
        var url = prefetchTask is null
            ? await RequireYoutube().ResolveAudioUrlAsync(track, cancellationToken)
            : await prefetchTask.WaitAsync(cancellationToken)
                ?? await RequireYoutube().ResolveAudioUrlAsync(track, cancellationToken);
        await RequireMpv().LoadAsync(url, cancellationToken);

        _currentTrack = track;
        _queueIndex = IndexOf(_queue, track.Id);
        NowPlayingText.Text = track.Title;
        ArtistText.Text = track.Artist;

        var entries = await RequireHistoryStore().AddAsync(track, cancellationToken);
        Replace(_history, entries.Select(entry => entry.Track));
        await SaveStateAsync(cancellationToken);
        SetStatus($"Playing {track.Title}");
        BeginPrefetchNextTrack();
    }

    private async Task RunSearchAsync(bool resetLimit)
    {
        CancelPrefetch();
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _searchCancellation.Token;
        try
        {
            await SearchOrPlayAsync(resetLimit, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task RunPlayTrackAsync(Track track)
    {
        var prefetchTask = string.Equals(_prefetchTrackId, track.Id, StringComparison.Ordinal)
            && Volatile.Read(ref _prefetchStarted)
                ? _prefetchTask
                : null;
        if (prefetchTask is null)
        {
            CancelPrefetch();
        }

        _resolveCancellation?.Cancel();
        _resolveCancellation?.Dispose();
        _resolveCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _resolveCancellation.Token;
        try
        {
            await PlayTrackAsync(track, prefetchTask, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task TogglePauseAsync(CancellationToken cancellationToken)
    {
        var mpv = RequireMpv();
        if (mpv.Snapshot.State == PlaybackState.Idle)
        {
            if (_currentTrack is not null)
            {
                await RunPlayTrackAsync(_currentTrack);
            }
            else
            {
                SetStatus("Select a track first.");
            }

            return;
        }

        await mpv.TogglePauseAsync(cancellationToken);
    }

    private async Task MoveQueueAsync(int offset, bool wrap, CancellationToken cancellationToken)
    {
        if (_queue.Count == 0)
        {
            SetStatus("The queue is empty.");
            return;
        }

        var index = _queueIndex;
        if (index < 0 && _currentTrack is not null)
        {
            index = IndexOf(_queue, _currentTrack.Id);
        }

        if (index < 0)
        {
            index = offset > 0 ? -1 : 0;
        }

        index += offset;
        if (wrap)
        {
            index = (index % _queue.Count + _queue.Count) % _queue.Count;
        }
        else if (index < 0 || index >= _queue.Count)
        {
            SetStatus("End of queue.");
            return;
        }

        _queueIndex = index;
        QueueList.SelectedIndex = index;
        await RunPlayTrackAsync(_queue[index]);
    }

    private async Task AdvanceAfterPlaybackAsync()
    {
        if (_currentTrack is not null && _repeat == RepeatMode.Track)
        {
            await RunActionAsync(() => RunPlayTrackAsync(_currentTrack));
            return;
        }

        if (_queue.Count == 0)
        {
            SetStatus("Playback finished.");
            return;
        }

        if (_shuffle && _queue.Count > 1)
        {
            var next = _queueIndex < 0
                ? Random.Shared.Next(_queue.Count)
                : Random.Shared.Next(_queue.Count - 1);
            if (_queueIndex >= 0 && next >= _queueIndex)
            {
                next++;
            }

            _queueIndex = Math.Clamp(next, 0, _queue.Count - 1);
            await RunActionAsync(() => RunPlayTrackAsync(_queue[_queueIndex]));
            return;
        }

        var nextIndex = _queueIndex + 1;
        if (nextIndex >= _queue.Count)
        {
            if (_repeat != RepeatMode.Queue)
            {
                SetStatus("End of queue.");
                return;
            }

            nextIndex = 0;
        }

        _queueIndex = Math.Max(0, nextIndex);
        await RunActionAsync(() => RunPlayTrackAsync(_queue[_queueIndex]));
    }

    private async Task SaveStateAsync(CancellationToken cancellationToken)
    {
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            if (_libraryStore is not null)
            {
                var position = _mpv?.Snapshot.Position.TotalSeconds ?? 0;
                await _libraryStore.SaveAsync(
                    new LibraryState
                    {
                        Queue = [.. _queue],
                        Favorites = [.. _favorites],
                        LastTrack = _currentTrack,
                        LastPositionSeconds = Math.Max(0, position),
                        Shuffle = _shuffle,
                        Repeat = _repeat
                    },
                    cancellationToken);
            }

            if (_settingsStore is not null)
            {
                var volume = _mpv?.Snapshot.Volume ?? _settings.Volume;
                await _settingsStore.SaveAsync(_settings with { Volume = volume }, cancellationToken);
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void OnPlaybackTick(object? sender, EventArgs e)
    {
        if (_mpv is null)
        {
            return;
        }

        var snapshot = _mpv.Snapshot;
        UpdatePlaybackControls(snapshot);
        _updatingPlaybackControls = true;
        PositionSlider.Value = Math.Clamp(snapshot.Position.TotalSeconds, 0, PositionSlider.Maximum);
        _updatingPlaybackControls = false;
        PositionText.Text = FormatTime(snapshot.Position);
    }

    private void OnPlaybackEnded() => Dispatcher.UIThread.Post(() => _ = AdvanceAfterPlaybackAsync());

    private void OnPlaybackFailed(string message) => Dispatcher.UIThread.Post(() =>
    {
        if (_currentTrack is not null)
        {
            _youtube?.InvalidateAudioUrl(_currentTrack.Id);
        }

        SetStatus(message);
    });

    private void OnSnapshotChanged(PlaybackSnapshot snapshot) =>
        Dispatcher.UIThread.Post(() => UpdatePlaybackControls(snapshot));

    private void UpdatePlaybackControls(PlaybackSnapshot snapshot)
    {
        _updatingPlaybackControls = true;
        PositionSlider.Maximum = Math.Max(1, snapshot.Duration.TotalSeconds);
        VolumeSlider.Value = snapshot.Volume;
        _updatingPlaybackControls = false;
        DurationText.Text = FormatTime(snapshot.Duration);
        if (_currentTrack is not null)
        {
            ArtistText.Text = $"{_currentTrack.Artist}  •  {snapshot.State.ToString().ToUpperInvariant()}";
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeCompleted)
        {
            return;
        }

        e.Cancel = true;
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _playbackTimer.Stop();
        SetStatus("Closing player...");
        _lifetime.Cancel();
        _searchCancellation?.Cancel();
        _resolveCancellation?.Cancel();
        CancelPrefetch();

        if (_initializationTask is not null)
        {
            await _initializationTask;
        }

        try
        {
            await SaveStateAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Could not save local data: {exception.Message}");
        }

        if (_mpv is not null)
        {
            _mpv.PlaybackEnded -= OnPlaybackEnded;
            _mpv.PlaybackFailed -= OnPlaybackFailed;
            _mpv.SnapshotChanged -= OnSnapshotChanged;
            await _mpv.DisposeAsync();
        }

        _searchCancellation?.Dispose();
        _resolveCancellation?.Dispose();
        _lifetime.Dispose();
        _saveLock.Dispose();
        _closeCompleted = true;
        Close();
    }

    private async Task RunActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    private void AddToQueue(Track? track)
    {
        if (track is null)
        {
            SetStatus("Select a track first.");
            return;
        }

        _queue.Add(track);
        BeginPrefetchNextTrack();
        SetStatus($"Added {track.Title} to the queue.");
        _ = RunActionAsync(() => SaveStateAsync(_lifetime.Token));
    }

    private void BeginPrefetchNextTrack()
    {
        if (_youtube is null || _currentTrack is null || _shuffle || _queue.Count == 0)
        {
            CancelPrefetch();
            return;
        }

        var currentIndex = _queueIndex < 0 ? IndexOf(_queue, _currentTrack.Id) : _queueIndex;
        var nextIndex = currentIndex < 0 ? 0 : currentIndex + 1;
        if (nextIndex >= _queue.Count)
        {
            if (_repeat != RepeatMode.Queue)
            {
                CancelPrefetch();
                return;
            }

            nextIndex = 0;
        }

        var nextTrack = _queue[nextIndex];
        if (nextTrack.Id == _currentTrack.Id)
        {
            CancelPrefetch();
            return;
        }

        if (string.Equals(_prefetchTrackId, nextTrack.Id, StringComparison.Ordinal)
            && _prefetchTask is not null)
        {
            return;
        }

        CancelPrefetch();
        _prefetchCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _prefetchCancellation.Token;
        _prefetchTrackId = nextTrack.Id;
        _prefetchTask = PrefetchAsync(nextTrack, token);
    }

    private async Task<string?> PrefetchAsync(Track track, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            Volatile.Write(ref _prefetchStarted, true);
            return await RequireYoutube().ResolveAudioUrlAsync(track, cancellationToken);
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

    private void ToggleFavorite(Track? track)
    {
        if (track is null)
        {
            SetStatus("Play or select a track first.");
            return;
        }

        var index = IndexOf(_favorites, track.Id);
        if (index >= 0)
        {
            _favorites.RemoveAt(index);
            SetStatus($"Removed {track.Title} from favorites.");
        }
        else
        {
            _favorites.Insert(0, track);
            SetStatus($"Added {track.Title} to favorites.");
        }

        _ = RunActionAsync(() => SaveStateAsync(_lifetime.Token));
    }

    private async void OnSearchClick(object? sender, RoutedEventArgs e) =>
        await RunActionAsync(() => RunSearchAsync(resetLimit: true));

    private async void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RunActionAsync(() => RunSearchAsync(resetLimit: true));
        }
    }

    private async void OnLoadMoreClick(object? sender, RoutedEventArgs e)
    {
        _searchLimit = Math.Min(MaximumSearchResults, _searchLimit + SearchBatchSize);
        await RunActionAsync(() => RunSearchAsync(resetLimit: false));
    }

    private async void OnPlaySearchClick(object? sender, RoutedEventArgs e) =>
        await PlaySelectedAsync(SearchResultsList);

    private async void OnSearchResultDoubleTapped(object? sender, TappedEventArgs e) =>
        await PlaySelectedAsync(SearchResultsList);

    private void OnAddSearchToQueueClick(object? sender, RoutedEventArgs e) =>
        AddToQueue(SearchResultsList.SelectedItem as Track);

    private async void OnPlayQueueClick(object? sender, RoutedEventArgs e) =>
        await PlaySelectedAsync(QueueList);

    private async void OnQueueDoubleTapped(object? sender, TappedEventArgs e) =>
        await PlaySelectedAsync(QueueList);

    private void OnRemoveQueueClick(object? sender, RoutedEventArgs e)
    {
        var index = QueueList.SelectedIndex;
        if (index < 0)
        {
            SetStatus("Select a queued track first.");
            return;
        }

        _queue.RemoveAt(index);
        _queueIndex = _currentTrack is null ? -1 : IndexOf(_queue, _currentTrack.Id);
        BeginPrefetchNextTrack();

        _ = RunActionAsync(() => SaveStateAsync(_lifetime.Token));
    }

    private void OnClearQueueClick(object? sender, RoutedEventArgs e)
    {
        _queue.Clear();
        _queueIndex = -1;
        BeginPrefetchNextTrack();
        SetStatus("Queue cleared.");
        _ = RunActionAsync(() => SaveStateAsync(_lifetime.Token));
    }

    private async void OnPlayHistoryClick(object? sender, RoutedEventArgs e) =>
        await PlaySelectedAsync(HistoryList);

    private async void OnHistoryDoubleTapped(object? sender, TappedEventArgs e) =>
        await PlaySelectedAsync(HistoryList);

    private void OnAddHistoryToQueueClick(object? sender, RoutedEventArgs e) =>
        AddToQueue(HistoryList.SelectedItem as Track);

    private async void OnPlayFavoriteClick(object? sender, RoutedEventArgs e) =>
        await PlaySelectedAsync(FavoritesList);

    private async void OnFavoriteDoubleTapped(object? sender, TappedEventArgs e) =>
        await PlaySelectedAsync(FavoritesList);

    private void OnRemoveFavoriteClick(object? sender, RoutedEventArgs e) =>
        ToggleFavorite(FavoritesList.SelectedItem as Track);

    private async Task PlaySelectedAsync(ListBox list)
    {
        if (list.SelectedItem is not Track track)
        {
            SetStatus("Select a track first.");
            return;
        }

        await RunActionAsync(() => RunPlayTrackAsync(track));
    }

    private async void OnTogglePauseClick(object? sender, RoutedEventArgs e) =>
        await RunActionAsync(() => TogglePauseAsync(_lifetime.Token));

    private async void OnStopClick(object? sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            await RequireMpv().StopAsync(_lifetime.Token);
            SetStatus("Stopped.");
        });

    private async void OnPreviousClick(object? sender, RoutedEventArgs e) =>
        await RunActionAsync(() => MoveQueueAsync(-1, wrap: true, _lifetime.Token));

    private async void OnNextClick(object? sender, RoutedEventArgs e) =>
        await RunActionAsync(() => MoveQueueAsync(1, wrap: true, _lifetime.Token));

    private void OnToggleFavoriteClick(object? sender, RoutedEventArgs e) => ToggleFavorite(_currentTrack);

    private async void OnPositionReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_updatingPlaybackControls || _mpv is null)
        {
            return;
        }

        await RunActionAsync(() => _mpv.SeekToAsync(PositionSlider.Value, _lifetime.Token));
    }

    private async void OnVolumeChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingPlaybackControls || _mpv is null)
        {
            return;
        }

        await RunActionAsync(() => _mpv.SetVolumeAsync((int)Math.Round(e.NewValue), _lifetime.Token));
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Q)
        {
            e.Handled = true;
            Close();
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F)
        {
            e.Handled = true;
            LibraryTabs.SelectedIndex = 0;
            SearchBox.Focus();
            return;
        }

        if (e.Source is TextBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                e.Handled = true;
                await RunActionAsync(() => TogglePauseAsync(_lifetime.Token));
                break;
            case Key.Left when _mpv is not null:
                e.Handled = true;
                await RunActionAsync(() => _mpv.SeekAsync(-5, _lifetime.Token));
                break;
            case Key.Right when _mpv is not null:
                e.Handled = true;
                await RunActionAsync(() => _mpv.SeekAsync(5, _lifetime.Token));
                break;
            case Key.Up:
                e.Handled = true;
                VolumeSlider.Value = Math.Min(100, VolumeSlider.Value + 5);
                break;
            case Key.Down:
                e.Handled = true;
                VolumeSlider.Value = Math.Max(0, VolumeSlider.Value - 5);
                break;
            case Key.N:
                e.Handled = true;
                await RunActionAsync(() => MoveQueueAsync(1, wrap: true, _lifetime.Token));
                break;
            case Key.P:
                e.Handled = true;
                await RunActionAsync(() => MoveQueueAsync(-1, wrap: true, _lifetime.Token));
                break;
            case Key.S when _mpv is not null:
                e.Handled = true;
                await RunActionAsync(() => _mpv.StopAsync(_lifetime.Token));
                break;
        }
    }

    private void OnTitlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnUninstallClick(object? sender, RoutedEventArgs e)
    {
        if (_isClosing)
        {
            return;
        }

        if (!UninstallService.TryCreateDefaultPlan(LightYtpEdition.Gui, out var plan, out var error))
        {
            SetStatus(error);
            return;
        }

        var confirmed = await new UninstallConfirmationWindow().ShowDialog<bool>(this);
        if (!confirmed)
        {
            return;
        }

        try
        {
            UninstallService.Schedule(plan!);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not start the uninstaller: {exception.Message}");
            return;
        }

        SetStatus("Uninstalling after the player closes...");
        Close();
    }

    private void SetStatus(string message) => StatusText.Text = message;

    private YtDlpClient RequireYoutube() =>
        _youtube ?? throw new InvalidOperationException("The player is not ready yet.");

    private MpvClient RequireMpv() =>
        _mpv ?? throw new InvalidOperationException("The player is not ready yet.");

    private HistoryStore RequireHistoryStore() =>
        _historyStore ?? throw new InvalidOperationException("The player is not ready yet.");

    private static int IndexOf(IEnumerable<Track> tracks, string id)
    {
        var index = 0;
        foreach (var track in tracks)
        {
            if (string.Equals(track.Id, id, StringComparison.Ordinal))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    private static void Replace(ObservableCollection<Track> destination, IEnumerable<Track> source)
    {
        destination.Clear();
        foreach (var track in source)
        {
            destination.Add(track);
        }
    }

    private static bool LooksLikeUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string FormatTime(TimeSpan value)
    {
        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}";
        }

        return $"{value.Minutes:00}:{value.Seconds:00}";
    }
}
