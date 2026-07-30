using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;

namespace YtMusicTerminal.Services;

internal sealed class MpvIpcEndpoint : IDisposable
{
    private readonly string? _pipeName;
    private readonly string? _socketPath;

    private MpvIpcEndpoint(string argument, string? pipeName, string? socketPath)
    {
        Argument = argument;
        _pipeName = pipeName;
        _socketPath = socketPath;
    }

    public string Argument { get; }

    public static MpvIpcEndpoint Create()
    {
        var name = $"lightytp-{Environment.ProcessId}-{Guid.NewGuid():N}";
        if (OperatingSystem.IsWindows())
        {
            return new MpvIpcEndpoint($"\\\\.\\pipe\\{name}", name, null);
        }

        var socketPath = Path.Combine(Path.GetTempPath(), $"{name}.sock");
        if (Encoding.UTF8.GetByteCount(socketPath) >= 100 && Directory.Exists("/tmp"))
        {
            socketPath = Path.Combine("/tmp", $"{name}.sock");
        }

        return new MpvIpcEndpoint(socketPath, null, socketPath);
    }

    public async Task<Stream> ConnectAsync(CancellationToken cancellationToken)
    {
        if (_pipeName is not null)
        {
            var pipe = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(5_000, cancellationToken).ConfigureAwait(false);
                return pipe;
            }
            catch
            {
                pipe.Dispose();
                throw;
            }
        }

        return await ConnectUnixSocketAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Stream> ConnectUnixSocketAsync(CancellationToken cancellationToken)
    {
        var socketPath = _socketPath
            ?? throw new InvalidOperationException("The mpv IPC endpoint is unavailable.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        while (true)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(
                    new UnixDomainSocketEndPoint(socketPath),
                    timeout.Token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException) when (!timeout.IsCancellationRequested)
            {
                socket.Dispose();
                await Task.Delay(50, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                socket.Dispose();
                throw new TimeoutException("Timed out while connecting to mpv.");
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }

    public void Dispose()
    {
        if (_socketPath is null)
        {
            return;
        }

        try
        {
            File.Delete(_socketPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
