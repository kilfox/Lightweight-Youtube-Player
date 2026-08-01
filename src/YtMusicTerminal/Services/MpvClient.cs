using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using YtMusicTerminal.Models;

namespace YtMusicTerminal.Services;

public sealed class MpvClient : IAsyncDisposable
{
    private readonly string _executable;
    private readonly string? _logFile;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _restartLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _requests = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _snapshotLock = new();

    private Process? _process;
    private WindowsProcessJob? _processJob;
    private MpvIpcEndpoint? _ipcEndpoint;
    private Stream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Task? _readerTask;
    private PlaybackSnapshot _snapshot;
    private long _requestId;
    private bool _disposed;

    public MpvClient(string executable, int initialVolume, string? logFile = null)
    {
        _executable = executable;
        _logFile = logFile;
        _snapshot = PlaybackSnapshot.Initial(Math.Clamp(initialVolume, 0, 100));
    }

    public event Action? PlaybackEnded;

    public event Action<string>? PlaybackFailed;

    public event Action<PlaybackSnapshot>? SnapshotChanged;

    public PlaybackSnapshot Snapshot
    {
        get
        {
            lock (_snapshotLock)
            {
                return _snapshot;
            }
        }
    }

    public long WorkingSetBytes
    {
        get
        {
            var process = _process;
            if (process is null || process.HasExited)
            {
                return 0;
            }

            process.Refresh();
            return process.WorkingSet64;
        }
    }

    public long PrivateMemoryBytes
    {
        get
        {
            var process = _process;
            if (process is null || process.HasExited)
            {
                return 0;
            }

            process.Refresh();
            return process.PrivateMemorySize64;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process is not null)
        {
            return;
        }

        _ipcEndpoint = MpvIpcEndpoint.Create();

        string[] arguments =
        [
            "--no-config",
            "--idle=yes",
            "--video=no",
            "--vo=null",
            "--force-window=no",
            "--audio-display=no",
            "--terminal=no",
            "--input-default-bindings=no",
            "--load-scripts=no",
            "--osc=no",
            "--osd-level=0",
            "--autoload-files=no",
            $"--input-ipc-server={_ipcEndpoint.Argument}",
            $"--volume={_snapshot.Volume.ToString(CultureInfo.InvariantCulture)}",
            "--cache=yes",
            "--cache-secs=3",
            "--demuxer-max-bytes=4MiB",
            "--demuxer-max-back-bytes=512KiB",
            "--audio-buffer=0.2"
        ];
        if (!string.IsNullOrWhiteSpace(_logFile))
        {
            var logDirectory = Path.GetDirectoryName(_logFile);
            if (!string.IsNullOrWhiteSpace(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            arguments = [.. arguments, $"--log-file={_logFile}", "--msg-level=all=warn"];
        }

        var startInfo = CreateProcessStartInfo(arguments);

        var toolDirectory = Path.GetDirectoryName(_executable);
        if (!string.IsNullOrWhiteSpace(toolDirectory))
        {
            startInfo.Environment["PATH"] = string.Join(
                Path.PathSeparator,
                new[] { toolDirectory, Environment.GetEnvironmentVariable("PATH") }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        _processJob = OperatingSystem.IsWindows()
            ? WindowsProcessJob.CreateKillOnClose()
            : null;
        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var processStarted = false;
        try
        {
            if (!_process.Start())
            {
                throw new InvalidOperationException("Could not start mpv.");
            }

            processStarted = true;
            _processJob?.AddProcess(_process);
        }
        catch (System.ComponentModel.Win32Exception exception) when (!processStarted)
        {
            TryKillProcess();
            _process.Dispose();
            _process = null;
            _processJob?.Dispose();
            _processJob = null;
            _ipcEndpoint.Dispose();
            _ipcEndpoint = null;
            throw new FileNotFoundException("Could not start mpv.", _executable, exception);
        }
        catch
        {
            TryKillProcess();
            _process.Dispose();
            _process = null;
            _processJob?.Dispose();
            _processJob = null;
            _ipcEndpoint.Dispose();
            _ipcEndpoint = null;
            throw;
        }

        try
        {
            _pipe = await _ipcEndpoint.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            TryKillProcess();
            _processJob?.Dispose();
            _processJob = null;
            _ipcEndpoint.Dispose();
            _ipcEndpoint = null;
            throw;
        }

        _reader = new StreamReader(_pipe, new UTF8Encoding(false), leaveOpen: true);
        _writer = new StreamWriter(_pipe, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };
        _readerTask = ReadMessagesAsync(_shutdown.Token);

        await ObserveAsync(1, "time-pos", cancellationToken).ConfigureAwait(false);
        await ObserveAsync(2, "duration", cancellationToken).ConfigureAwait(false);
        await ObserveAsync(3, "pause", cancellationToken).ConfigureAwait(false);
        await ObserveAsync(4, "volume", cancellationToken).ConfigureAwait(false);
        await ObserveAsync(5, "idle-active", cancellationToken).ConfigureAwait(false);
    }

    public async Task LoadAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("The playback URL must be HTTP or HTTPS.", nameof(url));
        }

        UpdateSnapshot(snapshot => snapshot with
        {
            State = PlaybackState.Loading,
            Position = TimeSpan.Zero,
            Duration = TimeSpan.Zero,
            Error = null
        });
        try
        {
            await SendCommandAsync(["loadfile", url, "replace"], cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            await RestartAsync(cancellationToken).ConfigureAwait(false);
            await SendCommandAsync(["loadfile", url, "replace"], cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task TogglePauseAsync(CancellationToken cancellationToken)
    {
        await SendCommandAsync(["cycle", "pause"], cancellationToken).ConfigureAwait(false);
    }

    public async Task SeekAsync(int seconds, CancellationToken cancellationToken)
    {
        await SendCommandAsync(
            ["seek", seconds, "relative+exact"],
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SeekToAsync(double seconds, CancellationToken cancellationToken)
    {
        var safeSeconds = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
        await SendCommandAsync(
            ["seek", safeSeconds, "absolute+exact"],
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SetVolumeAsync(int volume, CancellationToken cancellationToken)
    {
        var safeVolume = Math.Clamp(volume, 0, 100);
        await SendCommandAsync(
            ["set_property", "volume", safeVolume],
            cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await SendCommandAsync(["stop"], cancellationToken).ConfigureAwait(false);
        UpdateSnapshot(snapshot => snapshot with
        {
            State = PlaybackState.Idle,
            Position = TimeSpan.Zero,
            Duration = TimeSpan.Zero
        });
    }

    private Task ObserveAsync(int observerId, string property, CancellationToken cancellationToken) =>
        SendCommandAsync(["observe_property", observerId, property], cancellationToken);

    private async Task RestartAsync(CancellationToken cancellationToken)
    {
        await _restartLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TryKillProcess();
            _processJob?.Dispose();
            _processJob = null;
            if (_readerTask is not null)
            {
                try
                {
                    await _readerTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                }
            }

            TryDispose(_writer);
            TryDispose(_reader);
            TryDispose(_pipe);
            _ipcEndpoint?.Dispose();
            _writer = null;
            _reader = null;
            _pipe = null;
            _ipcEndpoint = null;
            _readerTask = null;
            _process?.Dispose();
            _process = null;

            await StartAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _restartLock.Release();
        }
    }

    private async Task SendCommandAsync(IReadOnlyList<object?> command, CancellationToken cancellationToken)
    {
        var writer = _writer ?? throw new InvalidOperationException("mpv is not running.");
        var requestId = Interlocked.Increment(ref _requestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_requests.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException("Could not register an mpv request.");
        }

        var message = BuildCommand(command, requestId);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await writer.WriteLineAsync(message.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            _requests.TryRemove(requestId, out _);
            throw CreateConnectionException(exception);
        }
        catch
        {
            _requests.TryRemove(requestId, out _);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }

        JsonElement response;
        try
        {
            response = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _requests.TryRemove(requestId, out _);
        }

        if (response.TryGetProperty("error", out var errorElement)
            && errorElement.ValueKind == JsonValueKind.String
            && errorElement.GetString() is { } error
            && !string.Equals(error, "success", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"mpv command failed: {error}");
        }
    }

    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        var reader = _reader;
        if (reader is null)
        {
            return;
        }

        Exception? connectionFailure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                using var document = JsonDocument.Parse(line);
                var message = document.RootElement;
                if (message.TryGetProperty("request_id", out var requestIdElement)
                    && requestIdElement.TryGetInt64(out var requestId)
                    && _requests.TryGetValue(requestId, out var completion))
                {
                    completion.TrySetResult(message.Clone());
                    continue;
                }

                HandleEvent(message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException) when (_disposed)
        {
        }
        catch (Exception exception)
        {
            connectionFailure = CreateConnectionException(exception);
            FailPendingRequests(connectionFailure);
            if (!_disposed)
            {
                PlaybackFailed?.Invoke(connectionFailure.Message);
            }
        }
        finally
        {
            FailPendingRequests(connectionFailure ?? CreateConnectionException());
        }
    }

    private void HandleEvent(JsonElement message)
    {
        if (!message.TryGetProperty("event", out var eventElement)
            || eventElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        switch (eventElement.GetString())
        {
            case "property-change":
                HandlePropertyChange(message);
                break;
            case "file-loaded":
                UpdateSnapshot(snapshot => snapshot with { State = PlaybackState.Playing, Error = null });
                break;
            case "end-file":
                HandleEndFile(message);
                break;
        }
    }

    private void HandlePropertyChange(JsonElement message)
    {
        if (!message.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String
            || !message.TryGetProperty("data", out var data))
        {
            return;
        }

        switch (nameElement.GetString())
        {
            case "time-pos" when data.TryGetDouble(out var seconds):
                UpdateSnapshot(snapshot => snapshot with { Position = SafeTimeSpan(seconds) });
                break;
            case "duration" when data.TryGetDouble(out var seconds):
                UpdateSnapshot(snapshot => snapshot with { Duration = SafeTimeSpan(seconds) });
                break;
            case "pause" when data.ValueKind is JsonValueKind.True or JsonValueKind.False:
                var paused = data.GetBoolean();
                UpdateSnapshot(snapshot => snapshot with
                {
                    State = paused ? PlaybackState.Paused : PlaybackState.Playing
                });
                break;
            case "volume" when data.TryGetDouble(out var volume):
                UpdateSnapshot(snapshot => snapshot with
                {
                    Volume = Math.Clamp((int)Math.Round(volume), 0, 100)
                });
                break;
            case "idle-active" when data.ValueKind == JsonValueKind.True:
                UpdateSnapshot(snapshot => snapshot with
                {
                    State = PlaybackState.Idle,
                    Position = TimeSpan.Zero,
                    Duration = TimeSpan.Zero
                });
                break;
        }
    }

    private void HandleEndFile(JsonElement message)
    {
        var reason = message.TryGetProperty("reason", out var reasonElement)
            ? reasonElement.GetString()
            : null;
        if (string.Equals(reason, "error", StringComparison.Ordinal))
        {
            var error = message.TryGetProperty("file_error", out var fileError)
                ? fileError.GetString()
                : "unknown playback error";
            UpdateSnapshot(snapshot => snapshot with
            {
                State = PlaybackState.Error,
                Error = error
            });
            PlaybackFailed?.Invoke($"Playback failed: {error}");
            return;
        }

        UpdateSnapshot(snapshot => snapshot with
        {
            State = PlaybackState.Idle,
            Position = TimeSpan.Zero,
            Duration = TimeSpan.Zero
        });
        if (string.Equals(reason, "eof", StringComparison.Ordinal))
        {
            PlaybackEnded?.Invoke();
        }
    }

    private static string BuildCommand(IReadOnlyList<object?> command, long requestId)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("command");
            writer.WriteStartArray();
            foreach (var value in command)
            {
                WriteValue(writer, value);
            }

            writer.WriteEndArray();
            writer.WriteNumber("request_id", requestId);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case int number:
                writer.WriteNumberValue(number);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case double number:
                writer.WriteNumberValue(number);
                break;
            case bool boolean:
                writer.WriteBooleanValue(boolean);
                break;
            default:
                throw new ArgumentException($"Unsupported mpv argument type: {value.GetType().Name}");
        }
    }

    private static TimeSpan SafeTimeSpan(double seconds) =>
        double.IsFinite(seconds) && seconds >= 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;

    private void UpdateSnapshot(Func<PlaybackSnapshot, PlaybackSnapshot> update)
    {
        PlaybackSnapshot previous;
        PlaybackSnapshot current;
        lock (_snapshotLock)
        {
            previous = _snapshot;
            current = update(previous);
            _snapshot = current;
        }

        if (previous.State != current.State
            || previous.Volume != current.Volume
            || previous.Duration != current.Duration
            || previous.Error != current.Error)
        {
            SnapshotChanged?.Invoke(current);
        }
    }

    private void FailPendingRequests(Exception exception)
    {
        foreach (var request in _requests)
        {
            request.Value.TrySetException(exception);
        }
    }

    private IOException CreateConnectionException(Exception? innerException = null)
    {
        var message = "The mpv connection closed unexpectedly";
        try
        {
            if (_process is { HasExited: true } process)
            {
                message += $" (exit code {process.ExitCode})";
            }
        }
        catch (InvalidOperationException)
        {
        }

        if (!string.IsNullOrWhiteSpace(_logFile))
        {
            message += $". See {_logFile}";
        }
        else
        {
            message += ".";
        }

        return new IOException(message, innerException);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_writer is not null)
        {
            using var quitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try
            {
                await SendCommandAsync(["quit"], quitTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or InvalidOperationException)
            {
            }
        }

        _shutdown.Cancel();
        if (_readerTask is not null)
        {
            try
            {
                await _readerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        TryDispose(_writer);
        TryDispose(_reader);
        TryDispose(_pipe);
        _ipcEndpoint?.Dispose();
        _ipcEndpoint = null;

        if (_process is not null)
        {
            using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await _process.WaitForExitAsync(exitTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKillProcess();
            }

            _process.Dispose();
        }

        _processJob?.Dispose();
        _processJob = null;

        _writeLock.Dispose();
        _restartLock.Dispose();
        _shutdown.Dispose();
    }

    private void TryKillProcess()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private ProcessStartInfo CreateProcessStartInfo(IReadOnlyList<string> arguments)
    {
        var workingDirectory = Path.GetDirectoryName(_executable) ?? AppContext.BaseDirectory;
        var startInfo = new ProcessStartInfo
        {
            FileName = _executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = workingDirectory
        };

        if (OperatingSystem.IsWindows())
        {
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return startInfo;
        }

        startInfo.FileName = "/bin/sh";
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(
            "parent_pid=$1; shift; \"$@\" & child_pid=$!; "
            + "cleanup() { trap - EXIT HUP INT TERM; kill \"$child_pid\" 2>/dev/null || true; "
            + "wait \"$child_pid\" 2>/dev/null || true; }; "
            + "trap cleanup EXIT HUP INT TERM; "
            + "while kill -0 \"$parent_pid\" 2>/dev/null && kill -0 \"$child_pid\" 2>/dev/null; do sleep 1; done");
        startInfo.ArgumentList.Add("lightytp-mpv-supervisor");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(_executable);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void TryDispose(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }
}
