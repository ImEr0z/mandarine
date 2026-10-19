using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace Mandarine.App.Services;

/// <summary>
/// Keeps a single Mandarine per user session. Later launches (context menu, "Send to", double-click)
/// forward their command line to the running instance through a named pipe, then exit.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const int MaxPayload = 4 * 1024 * 1024;

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private CancellationTokenSource? _listening;

    public SingleInstance(string id)
    {
        var user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{id}-{user}", out var createdNew);
        IsFirstInstance = createdNew;
        _pipeName = $"{id}-{user}";
    }

    public bool IsFirstInstance { get; }

    /// <summary>Raised on a background thread with the arguments of a later launch.</summary>
    public event Action<string[]>? ArgumentsReceived;

    public void Listen()
    {
        _listening = new CancellationTokenSource();
        var token = _listening.Token;
        _ = Task.Run(() => ListenAsync(token), token);
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                // Explorer forwards at most a few thousand paths: anything bigger is not a real launch.
                var buffer = new byte[MaxPayload + 1];
                var length = 0;
                int read;
                while ((read = await server.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    length += read;
                    if (length > MaxPayload)
                    {
                        throw new InvalidDataException("Message trop long.");
                    }
                }

                ArgumentsReceived?.Invoke(JsonSerializer.Deserialize<string[]>(buffer.AsSpan(0, length)) ?? []);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // A client that disconnects early or sends garbage must never stop the listener:
                // without it, every later right-click would be lost.
                ErrorLog.Write(ex);
                await Task.Delay(500, cancellationToken).ConfigureAwait(false); // never spin on a persistent failure
            }
        }
    }

    public bool TrySendToFirstInstance(string[] arguments)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(TimeSpan.FromSeconds(5));
            client.Write(JsonSerializer.SerializeToUtf8Bytes(arguments));
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _listening?.Cancel();
        if (IsFirstInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
