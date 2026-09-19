using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace TheHub.Common.IPC;

public class NamedPipeServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly Func<IpcMessage, Task<IpcMessage?>> _messageHandler;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public NamedPipeServer(string pipeName, Func<IpcMessage, Task<IpcMessage?>> messageHandler)
    {
        _pipeName = pipeName;
        _messageHandler = messageHandler;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipeStream = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipeStream.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(pipeStream, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipeStream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync(ct);
                if (!string.IsNullOrEmpty(line))
                {
                    var msg = JsonSerializer.Deserialize<IpcMessage>(line);
                    if (msg != null)
                    {
                        var response = await _messageHandler(msg);
                        if (response != null)
                        {
                            var respJson = JsonSerializer.Serialize(response);
                            await writer.WriteLineAsync(respJson.AsMemory(), ct);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PipeServer {_pipeName}] Error: {ex.Message}");
                await Task.Delay(200, ct);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_listenTask != null)
        {
            try { await _listenTask; } catch { }
        }
        _cts?.Dispose();
    }
}

public static class NamedPipeClient
{
    public static async Task<IpcMessage?> SendAsync(string pipeName, IpcMessage message, int timeoutMs = 1000)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            using var clientStream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            await clientStream.ConnectAsync(cts.Token);

            using var writer = new StreamWriter(clientStream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(clientStream, Encoding.UTF8, leaveOpen: true);

            var json = JsonSerializer.Serialize(message);
            await writer.WriteLineAsync(json.AsMemory(), cts.Token);

            var responseLine = await reader.ReadLineAsync(cts.Token);
            if (!string.IsNullOrEmpty(responseLine))
            {
                return JsonSerializer.Deserialize<IpcMessage>(responseLine);
            }
        }
        catch (Exception)
        {
            // Timeout or server not ready
        }
        return null;
    }
}
