using System.Text.Json.Nodes;

namespace CodexUsageOverlay.Core;

public sealed class CodexAppServerBridge : IAsyncDisposable
{
    private static readonly TimeSpan RateLimitPollingInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AccountUsagePollingInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromMinutes(5);

    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly object _reconnectSync = new();
    private readonly object _snapshotSync = new();
    private readonly AppServerClient? _initialClient;
    private readonly OAuthProfileReader _profileReader = new();
    private Task? _rateLimitPollingTask;
    private Task? _accountUsagePollingTask;
    private Task? _reconnectTask;
    private AppServerClient? _client;
    private RateLimitSnapshot? _lastGoodSnapshot;
    private string? _baselineLimitId;
    private TimeSpan _nextReconnectDelay = InitialReconnectDelay;
    private bool _initialClientUsed;
    private volatile bool _isDisposed;

    public CodexAppServerBridge(AppServerClient? client = null)
    {
        _initialClient = client;
    }

    public event Action<RateLimitSnapshot>? RateLimitsChanged;
    public event Action<AccountUsageSnapshot>? AccountUsageChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        _rateLimitPollingTask = Task.Run(() => PollRateLimitsAsync(_cts.Token));
        _accountUsagePollingTask = Task.Run(() => PollAccountUsageAsync(_cts.Token));

        try
        {
            await ConnectAndReplaceClientAsync(linked.Token);
            await RefreshRateLimitsAsync(linked.Token);
            await RefreshAccountUsageAsync(linked.Token);
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
            var parsed = UsageParsers.ParseRateLimits(new JsonObject { ["result"] = result?.DeepClone() });
            var rateLimits = (result as JsonObject)?["rateLimits"] as JsonObject;
            var baselineLimitId = rateLimits is null
                ? null
                : JsonNodeHelpers.DirectString(rateLimits, "limitId", "limit_id");

            lock (_snapshotSync)
            {
                if (!string.IsNullOrWhiteSpace(baselineLimitId))
                {
                    _baselineLimitId = baselineLimitId;
                }

                if (parsed.FiveHour.IsAvailable || parsed.Weekly.IsAvailable)
                {
                    _lastGoodSnapshot = parsed;
                }
            }

            RateLimitsChanged?.Invoke(parsed);
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

    private async Task PollAccountUsageAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(AccountUsagePollingInterval, cancellationToken);
                await RefreshAccountUsageAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<bool> RefreshAccountUsageAsync(CancellationToken cancellationToken)
    {
        var client = _client;
        if (client is null)
        {
            return false;
        }

        try
        {
            var account = await client.SendRequestAsync(
                "account/read",
                new JsonObject { ["refreshToken"] = false },
                cancellationToken);
            var usage = await client.SendRequestAsync(
                "account/usage/read",
                new JsonObject(),
                cancellationToken);
            var snapshot = UsageParsers.ParseAccountUsage(account, usage);
            if (!snapshot.IsAvailable)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(snapshot.DisplayName))
            {
                var localDisplayName = await _profileReader.ReadDisplayNameAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(localDisplayName))
                {
                    snapshot = snapshot with { DisplayName = localDisplayName };
                }
            }

            AccountUsageChanged?.Invoke(snapshot);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Account usage is supplementary. Keep the last good UI and let the
            // low-frequency poll retry without reconnecting a healthy app-server.
            return false;
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
                    await RefreshAccountUsageAsync(cancellationToken);
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
                ApplyRateLimitNotification(message);
                break;
        }
    }

    private void ApplyRateLimitNotification(JsonObject message)
    {
        if (message["params"] is not JsonObject parameters ||
            parameters["rateLimits"] is not JsonObject rateLimits)
        {
            return;
        }

        var notificationLimitId = JsonNodeHelpers.DirectString(rateLimits, "limitId", "limit_id");
        var incoming = UsageParsers.ParseRateLimits(message);
        var hasFiveHour = incoming.FiveHour.IsAvailable;
        var hasWeekly = incoming.Weekly.IsAvailable;
        if (!hasFiveHour && !hasWeekly)
        {
            return;
        }

        RateLimitSnapshot merged;
        lock (_snapshotSync)
        {
            if (!RateLimitUpdatePolicy.ShouldAcceptRateLimitNotification(
                    _baselineLimitId,
                    notificationLimitId))
            {
                return;
            }

            merged = RateLimitUpdatePolicy.MergeSparse(
                _lastGoodSnapshot,
                incoming,
                hasFiveHour,
                hasWeekly);
            _lastGoodSnapshot = merged;
        }

        RateLimitsChanged?.Invoke(merged);
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

        if (_accountUsagePollingTask is not null)
        {
            try
            {
                await _accountUsagePollingTask;
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
