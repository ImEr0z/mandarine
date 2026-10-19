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
                using var reader = new StreamReader(server);
                var payload = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                ArgumentsReceived?.Invoke(JsonSerializer.Deserialize<string[]>(payload) ?? []);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // A client that disconnects early or sends garbage must not stop the listener.
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
