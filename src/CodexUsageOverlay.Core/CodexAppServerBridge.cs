using System.Text.Json.Nodes;

namespace CodexUsageOverlay.Core;

public sealed class CodexAppServerBridge : IAsyncDisposable
{
    private static readonly TimeSpan RateLimitPollingInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromMinutes(5);

    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly object _reconnectSync = new();
    private readonly AppServerClient? _initialClient;
    private Task? _rateLimitPollingTask;
    private Task? _reconnectTask;
    private AppServerClient? _client;
    private TimeSpan _nextReconnectDelay = InitialReconnectDelay;
    private bool _initialClientUsed;
    private volatile bool _isDisposed;

    public CodexAppServerBridge(AppServerClient? client = null)
    {
        _initialClient = client;
    }

    public event Action<RateLimitSnapshot>? RateLimitsChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        _rateLimitPollingTask = Task.Run(() => PollRateLimitsAsync(_cts.Token));

        try
        {
            await ConnectAndReplaceClientAsync(linked.Token);
            await RefreshRateLimitsAsync(linked.Token);
        }
        catch (OperationCanceledException) when (linked.Token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            RateLimitsChanged?.Invoke(RateLimitSnapshot.Waiting);
            ScheduleReconnect();
        }
    }

    private async Task<bool> RefreshRateLimitsAsync(
        CancellationToken cancellationToken,
        bool scheduleReconnectOnFailure = true)
    {
        var client = _client;
        if (client is null)
        {
            RateLimitsChanged?.Invoke(RateLimitSnapshot.Waiting);
            if (scheduleReconnectOnFailure)
            {
                ScheduleReconnect();
            }

            return false;
        }

        try
        {
            var result = await client.SendRequestAsync("account/rateLimits/read", new JsonObject(), cancellationToken);
            RateLimitsChanged?.Invoke(UsageParsers.ParseRateLimits(new JsonObject { ["result"] = result?.DeepClone() }));
            _nextReconnectDelay = InitialReconnectDelay;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            RateLimitsChanged?.Invoke(RateLimitSnapshot.Waiting);
            if (scheduleReconnectOnFailure)
            {
                ScheduleReconnect();
            }

            return false;
        }
    }

    private async Task PollRateLimitsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RateLimitPollingInterval, cancellationToken);
                await RefreshRateLimitsAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ConnectAndReplaceClientAsync(CancellationToken cancellationToken)
    {
        await _connectionGate.WaitAsync(cancellationToken);
        AppServerClient? newClient = null;
        try
        {
            if (_isDisposed)
            {
                return;
            }

            await DisposeCurrentClientCoreAsync();
            newClient = CreateClient();
            AttachClient(newClient);
            await newClient.StartAsync(cancellationToken);
            await newClient.InitializeAsync(cancellationToken);
            _client = newClient;
            newClient = null;
        }
        finally
        {
            if (newClient is not null)
            {
                DetachClient(newClient);
                await newClient.DisposeAsync();
            }

            _connectionGate.Release();
        }
    }

    private AppServerClient CreateClient()
    {
        if (_initialClient is not null && !_initialClientUsed)
        {
            _initialClientUsed = true;
            return _initialClient;
        }

        return new AppServerClient();
    }

    private void AttachClient(AppServerClient client)
    {
        client.NotificationReceived += OnNotification;
        client.Disconnected += OnDisconnected;
    }

    private void DetachClient(AppServerClient client)
    {
        client.NotificationReceived -= OnNotification;
        client.Disconnected -= OnDisconnected;
    }

    private void OnDisconnected(Exception _)
    {
        if (_isDisposed)
        {
            return;
        }

        RateLimitsChanged?.Invoke(RateLimitSnapshot.Waiting);
        ScheduleReconnect();
    }

    private void ScheduleReconnect()
    {
        if (_isDisposed || _cts.IsCancellationRequested)
        {
            return;
        }

        lock (_reconnectSync)
        {
            if (_reconnectTask is { IsCompleted: false })
            {
                return;
            }

            _reconnectTask = Task.Run(() => ReconnectLoopAsync(_cts.Token));
        }
    }

    private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_nextReconnectDelay, cancellationToken);
                await ConnectAndReplaceClientAsync(cancellationToken);
                if (await RefreshRateLimitsAsync(cancellationToken, scheduleReconnectOnFailure: false))
                {
                    return;
                }

                await DisposeCurrentClientAsync();
                throw new AppServerException("app-server reconnected but rate limit refresh failed.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                RateLimitsChanged?.Invoke(RateLimitSnapshot.Waiting);
                _nextReconnectDelay = NextReconnectDelay(_nextReconnectDelay);
            }
        }
    }

    private static TimeSpan NextReconnectDelay(TimeSpan current)
    {
        var nextTicks = Math.Min(current.Ticks * 2, MaxReconnectDelay.Ticks);
        return TimeSpan.FromTicks(nextTicks);
    }

    private void OnNotification(JsonObject message)
    {
        var method = JsonNodeHelpers.DirectString(message, "method") ?? string.Empty;
        switch (method)
        {
            case "account/rateLimits/updated":
                RateLimitsChanged?.Invoke(UsageParsers.ParseRateLimits(message));
                break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _isDisposed = true;
        _cts.Cancel();
        if (_rateLimitPollingTask is not null)
        {
            try
            {
                await _rateLimitPollingTask;
            }
            catch
            {
            }
        }

        if (_reconnectTask is not null)
        {
            try
            {
                await _reconnectTask;
            }
            catch
            {
            }
        }

        await DisposeCurrentClientAsync();
        _connectionGate.Dispose();
        _cts.Dispose();
    }

    private async ValueTask DisposeCurrentClientAsync()
    {
        await _connectionGate.WaitAsync();
        try
        {
            await DisposeCurrentClientCoreAsync();
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private async ValueTask DisposeCurrentClientCoreAsync()
    {
        var client = _client;
        _client = null;
        if (client is null)
        {
            return;
        }

        DetachClient(client);
        await client.DisposeAsync();
    }
}
