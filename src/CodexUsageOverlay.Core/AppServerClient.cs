using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexUsageOverlay.Core;

public sealed class AppServerClient : IAsyncDisposable
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private long _nextId;

    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private StreamReader? _stderr;
    private ClientWebSocket? _webSocket;
    private Task? _readLoop;
    private Task? _stderrLoop;
    private WindowsJobObject? _job;

    public event Action<JsonObject>? NotificationReceived;
    public event Action<Exception>? Disconnected;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = Environment.GetEnvironmentVariable("CODEX_USAGE_OVERLAY_APP_SERVER_WS");
        if (!string.IsNullOrWhiteSpace(endpoint) && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            try
            {
                await StartWebSocketAsync(uri, cancellationToken);
                return;
            }
            catch
            {
                await DisposeWebSocketAsync();
            }
        }

        StartStdioProcess();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var initParams = new JsonObject
        {
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "codex_usage_overlay",
                ["title"] = "Codex Usage Overlay",
                ["version"] = "0.1.5"
            },
            ["capabilities"] = new JsonObject
            {
                ["experimentalApi"] = true
            }
        };

        await SendRequestAsync("initialize", initParams, cancellationToken);
        await SendNotificationAsync("initialized", null, cancellationToken);
    }

    public async Task<JsonNode?> SendRequestAsync(string method, JsonObject? parameters = null, CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var message = new JsonObject
        {
            ["method"] = method,
            ["id"] = id
        };

        if (parameters is not null)
        {
            message["params"] = parameters;
        }

        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        requestCts.CancelAfter(DefaultRequestTimeout);

        await using var registration = requestCts.Token.Register(() =>
        {
            if (_pending.TryRemove(id, out var pending))
            {
                pending.TrySetCanceled(requestCts.Token);
            }
        });

        try
        {
            await SendMessageAsync(message, requestCts.Token);

            var response = await tcs.Task;
            if (response["error"] is JsonNode error)
            {
                throw new AppServerException(error.ToJsonString(JsonOptions));
            }

            return response["result"];
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }
    }

    public Task SendNotificationAsync(string method, JsonObject? parameters = null, CancellationToken cancellationToken = default)
    {
        var message = new JsonObject
        {
            ["method"] = method
        };

        if (parameters is not null)
        {
            message["params"] = parameters;
        }

        return SendMessageAsync(message, cancellationToken);
    }

    private async Task StartWebSocketAsync(Uri uri, CancellationToken cancellationToken)
    {
        _webSocket = new ClientWebSocket();
        await _webSocket.ConnectAsync(uri, cancellationToken);
        _readLoop = Task.Run(() => ReadWebSocketLoopAsync(_disposeCts.Token));
    }

    private void StartStdioProcess()
    {
        var codexExecutable = CodexExecutableResolver.Resolve();
        var psi = new ProcessStartInfo
        {
            FileName = codexExecutable,
            Arguments = "app-server --listen stdio://",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom
        };
        if (Path.IsPathFullyQualified(codexExecutable))
        {
            psi.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        _process = Process.Start(psi) ?? throw new AppServerException("无法启动 codex app-server。");
        _job = WindowsJobObject.TryCreateKillOnClose();
        if (_job is not null && !_job.TryAssign(_process))
        {
            _job.Dispose();
            _job = null;
        }

        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;
        _stderr = _process.StandardError;
        _readLoop = Task.Run(() => ReadStdioLoopAsync(_disposeCts.Token));
        _stderrLoop = Task.Run(() => DiscardStderrLoopAsync(_disposeCts.Token));
    }

    private async Task SendMessageAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var text = message.ToJsonString(JsonOptions);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_webSocket is not null)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
                return;
            }

            if (_stdin is null)
            {
                throw new AppServerException("app-server 尚未连接。");
            }

            await _stdin.WriteLineAsync(text.AsMemory(), cancellationToken);
            await _stdin.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _disposeCts.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            CompletePendingWithException(ex);
            RaiseDisconnected(ex);
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReadStdioLoopAsync(CancellationToken cancellationToken)
    {
        if (_stdout is null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await _stdout.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                HandleIncomingMessage(line);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            CompletePendingWithException(ex);
            RaiseDisconnected(ex);
            return;
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            var exception = new AppServerException("app-server stdout closed.");
            CompletePendingWithException(exception);
            RaiseDisconnected(exception);
        }
    }

    private async Task DiscardStderrLoopAsync(CancellationToken cancellationToken)
    {
        if (_stderr is null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await _stderr.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
        }
    }

    private async Task ReadWebSocketLoopAsync(CancellationToken cancellationToken)
    {
        if (_webSocket is null)
        {
            return;
        }

        try
        {
            var buffer = new byte[64 * 1024];
            while (!cancellationToken.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _webSocket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        var exception = new AppServerException("app-server websocket closed.");
                        CompletePendingWithException(exception);
                        RaiseDisconnected(exception);
                        return;
                    }

                    stream.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                var text = Encoding.UTF8.GetString(stream.ToArray());
                HandleIncomingMessage(text);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            CompletePendingWithException(ex);
            RaiseDisconnected(ex);
            return;
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            var exception = new AppServerException("app-server websocket closed.");
            CompletePendingWithException(exception);
            RaiseDisconnected(exception);
        }
    }

    private void HandleIncomingMessage(string text)
    {
        JsonObject? message;
        try
        {
            message = JsonNode.Parse(text) as JsonObject;
        }
        catch
        {
            return;
        }

        if (message is null)
        {
            return;
        }

        var id = ReadId(message["id"]);
        if (id.HasValue && _pending.TryRemove(id.Value, out var pending))
        {
            pending.TrySetResult(message);
            return;
        }

        NotificationReceived?.Invoke(message);
    }

    private void CompletePendingWithException(Exception exception)
    {
        foreach (var item in _pending.ToArray())
        {
            if (_pending.TryRemove(item.Key, out var pending))
            {
                pending.TrySetException(exception);
            }
        }
    }

    private void RaiseDisconnected(Exception exception)
    {
        if (_disposeCts.IsCancellationRequested)
        {
            return;
        }

        Disconnected?.Invoke(exception);
    }

    private static long? ReadId(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        try
        {
            return node.GetValue<long>();
        }
        catch
        {
            return long.TryParse(node.ToString(), out var parsed) ? parsed : null;
        }
    }

    private async ValueTask DisposeWebSocketAsync()
    {
        if (_webSocket is null)
        {
            return;
        }

        try
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
        }
        catch
        {
        }

        _webSocket.Dispose();
        _webSocket = null;
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        await DisposeWebSocketAsync();

        try
        {
            _stdin?.Dispose();
            _stdout?.Dispose();
            _stderr?.Dispose();
        }
        catch
        {
        }

        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            _process.Dispose();
            _process = null;
        }

        _job?.Dispose();
        _job = null;

        await WaitForTaskAsync(_readLoop);
        await WaitForTaskAsync(_stderrLoop);

        _sendLock.Dispose();
        _disposeCts.Dispose();
    }

    private static async Task WaitForTaskAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
    }
}

public sealed class AppServerException(string message) : Exception(message);
