using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace ChatPi.App.Services;

internal sealed class SingleInstanceHost : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listener;

    public SingleInstanceHost(string userId)
    {
        var scope = $"{userId}|{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)))[..24];
        _pipeName = $"ChatPi-{id}";
        _mutex = new Mutex(initiallyOwned: true, $"Local\\{_pipeName}", out var created);
        try { IsPrimary = created || _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
    }

    public bool IsPrimary { get; }

    public void StartListening(Func<string?, Task> received)
    {
        if (!IsPrimary) throw new InvalidOperationException("Only the primary instance may listen.");
        _listener = Task.Run(async () =>
        {
            while (!_shutdown.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_shutdown.Token);
                    // Length-prefixing avoids an unbounded ReadLine on a malformed local request.
                    var header = new byte[4];
                    using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                    requestTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                    await server.ReadExactlyAsync(header, requestTimeout.Token);
                    var length = BitConverter.ToInt32(header);
                    if (length is < 0 or > 131072) continue;
                    var bytes = new byte[length];
                    await server.ReadExactlyAsync(bytes, requestTimeout.Token);
                    var path = length == 0 ? null : Encoding.UTF8.GetString(bytes);
                    await received(path);
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { break; }
                catch (Exception exception) when (exception is IOException or OperationCanceledException or UnauthorizedAccessException)
                {
                    // A disconnected client must not terminate the next-file listener.
                }
            }
        });
    }

    public async Task SendAsync(string? path)
    {
        // Resolve a relative path in the launching process, before forwarding it.
        var bytes = path is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(Path.GetFullPath(path));
        if (bytes.Length > 131072) throw new PathTooLongException();
        await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(timeout.Token);
        await client.WriteAsync(BitConverter.GetBytes(bytes.Length), timeout.Token);
        await client.WriteAsync(bytes, timeout.Token);
        await client.FlushAsync(timeout.Token);
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        // The UI thread owns the mutex; do not wait here for a dispatcher callback.
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
        if (_listener is null || _listener.IsCompleted) _shutdown.Dispose();
        else _ = _listener.ContinueWith(_ => _shutdown.Dispose(), TaskScheduler.Default);
    }
}
