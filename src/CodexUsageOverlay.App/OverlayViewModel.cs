using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CodexUsageOverlay.Core;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace CodexUsageOverlay.App;

public sealed class OverlayViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly LocalizedStrings Text = LocalizedStrings.Current;
    private static readonly Brush GreenBrush = CreateBrush("#8DDB3A");
    private static readonly Brush YellowBrush = CreateBrush("#F2C94C");
    private static readonly Brush RedBrush = CreateBrush("#FF4D4D");
    private static readonly Brush UnavailableBrush = CreateBrush("#4A4A4A");
    private static readonly TimeSpan ContextStaleGrace = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RateLimitStaleGrace = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ResetCreditMidnightDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ResetCreditManualCooldown = TimeSpan.FromSeconds(60);
    private const int ContextFastPollSeconds = 5;
    private const int ContextMediumPollSeconds = 10;
    private const int ContextSlowPollSeconds = 15;
    private const int ContextHiddenPollSeconds = 30;

    private readonly OverlaySettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly Dispatcher _dispatcher;
    private readonly CodexAppServerBridge _bridge = new();
    private readonly CodexStatusReader _statusReader = new();
    private readonly ResetCreditClient _resetCreditClient = new();
    private readonly ResetCreditCacheStore _resetCreditCacheStore = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _contextPollingTask;
    private Task? _resetCreditPollingTask;
    private volatile bool _isCodexWindowAvailable = true;
    private int _contextReadInProgress;
    private int _contextPollSeconds = ContextFastPollSeconds;

    private double _contextRingValue;
    private double _fiveHourValue;
    private double _weeklyValue;
    private string _contextGaugeText = "--";
    private string _contextGaugeDetailText = Text.WaitingData;
    private string _fiveHourGaugeText = "--";
    private string _weeklyGaugeText = "--";
    private string _fiveHourResetText = string.Empty;
    private string _weeklyResetText = string.Empty;
    private string _resetCreditsSummaryText = string.Empty;
    private string _resetCreditsTrayText = string.Empty;
    private string? _resetCreditsTooltipText;
    private string? _miniResetTooltipText;
    private string _trayTooltipText = $"{Text.ContextShort} --   {Text.WeeklyShort} --";
    private double? _trayStatusPercent;
    private Visibility _fiveHourLimitVisibility = Visibility.Collapsed;
    private Visibility _fiveHourResetVisibility = Visibility.Collapsed;
    private Visibility _weeklyResetVisibility = Visibility.Collapsed;
    private Visibility _resetCreditsVisibility = Visibility.Collapsed;
    private Brush _contextRingBrush = UnavailableBrush;
    private Brush _fiveHourRingBrush = UnavailableBrush;
    private Brush _weeklyRingBrush = UnavailableBrush;
    private bool _isCollapsed;
    private bool _isContextAvailable;
    private string _contextMiniText = $"{Text.ContextShort} --";
    private string _fiveHourMiniText = $"{Text.FiveHourShort} --";
    private string _weeklyMiniText = $"{Text.WeeklyShort} --";
    private RateLimitSnapshot _lastRateLimits = RateLimitSnapshot.Waiting;
    private ContextUsage? _lastValidContextUsage;
    private DateTimeOffset _lastValidContextAt;
    private RateLimitMetric? _lastValidFiveHour;
    private DateTimeOffset _lastValidFiveHourAt;
    private RateLimitMetric? _lastValidWeekly;
    private DateTimeOffset _lastValidWeeklyAt;
    private DateOnly? _lastResetCreditAttemptDate;
    private bool _resetCreditsHaveAvailableData;
    private bool _isResetCreditsRefreshing;
    private DateTimeOffset? _lastManualResetCreditAttemptAt;
    private Task? _resetCreditCooldownTask;
    private double? _lastContextUsedPercentForInterval;
    private bool _lastContextReadWasAvailable;
    private int _stableContextReadCount;

    public OverlayViewModel(OverlaySettings settings, SettingsStore settingsStore, Dispatcher dispatcher)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _dispatcher = dispatcher;
        _isCollapsed = settings.IsCollapsed;

        _bridge.RateLimitsChanged += limits => Dispatch(() => ApplyRateLimits(limits));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ContextTitle => Text.ContextTitle;
    public string FiveHourTitle => Text.FiveHourTitle;
    public string WeeklyTitle => Text.WeeklyTitle;
    public string RemainingLabel => Text.Remaining;
    public string ShowButtonText => Text.Show;
    public string ExitButtonText => Text.Exit;
    public string HideToTrayText => Text.HideToTray;
    public string ExpandFullModeText => Text.ExpandFullMode;
    public string RefreshResetCreditsText => Text.RefreshResetCredits;

    public double ContextRingValue
    {
        get => _contextRingValue;
        set => SetField(ref _contextRingValue, value);
    }

    public double FiveHourValue
    {
        get => _fiveHourValue;
        set => SetField(ref _fiveHourValue, value);
    }

    public double WeeklyValue
    {
        get => _weeklyValue;
        set => SetField(ref _weeklyValue, value);
    }

    public string ContextGaugeText
    {
        get => _contextGaugeText;
        set => SetField(ref _contextGaugeText, value);
    }

    public string ContextGaugeDetailText
    {
        get => _contextGaugeDetailText;
        set => SetField(ref _contextGaugeDetailText, value);
    }

    public string FiveHourGaugeText
    {
        get => _fiveHourGaugeText;
        set => SetField(ref _fiveHourGaugeText, value);
    }

    public string WeeklyGaugeText
    {
        get => _weeklyGaugeText;
        set => SetField(ref _weeklyGaugeText, value);
    }

    public string FiveHourResetText
    {
        get => _fiveHourResetText;
        set => SetField(ref _fiveHourResetText, value);
    }

    public string WeeklyResetText
    {
        get => _weeklyResetText;
        set => SetField(ref _weeklyResetText, value);
    }

    public string ResetCreditsSummaryText
    {
        get => _resetCreditsSummaryText;
        set => SetField(ref _resetCreditsSummaryText, value);
    }

    public string? ResetCreditsTooltipText
    {
        get => _resetCreditsTooltipText;
        set => SetField(ref _resetCreditsTooltipText, value);
    }

    public string? MiniResetTooltipText
    {
        get => _miniResetTooltipText;
        set => SetField(ref _miniResetTooltipText, value);
    }

    public string TrayTooltipText
    {
        get => _trayTooltipText;
        set => SetField(ref _trayTooltipText, value);
    }

    public double? TrayStatusPercent
    {
        get => _trayStatusPercent;
        set => SetField(ref _trayStatusPercent, value);
    }

    public Visibility FiveHourLimitVisibility
    {
        get => _fiveHourLimitVisibility;
        set => SetField(ref _fiveHourLimitVisibility, value);
    }

    public Visibility FiveHourResetVisibility
    {
        get => _fiveHourResetVisibility;
        set => SetField(ref _fiveHourResetVisibility, value);
    }

    public Visibility WeeklyResetVisibility
    {
        get => _weeklyResetVisibility;
        set => SetField(ref _weeklyResetVisibility, value);
    }

    public Visibility ResetCreditsVisibility
    {
        get => _resetCreditsVisibility;
        set => SetField(ref _resetCreditsVisibility, value);
    }

    public bool CanRefreshResetCredits => !_isResetCreditsRefreshing && !IsManualResetCreditCooldownActive();

    public Brush ContextRingBrush
    {
        get => _contextRingBrush;
        set => SetField(ref _contextRingBrush, value);
    }

    public Brush FiveHourRingBrush
    {
        get => _fiveHourRingBrush;
        set => SetField(ref _fiveHourRingBrush, value);
    }

    public Brush WeeklyRingBrush
    {
        get => _weeklyRingBrush;
        set => SetField(ref _weeklyRingBrush, value);
    }

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            if (!SetField(ref _isCollapsed, value))
            {
                return;
            }

            _settings.IsCollapsed = value;
            _settingsStore.Save(_settings);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FullPanelVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MiniPanelVisibility)));
        }
    }

    public Visibility FullPanelVisibility => IsCollapsed ? Visibility.Collapsed : Visibility.Visible;
    public Visibility MiniPanelVisibility => IsCollapsed ? Visibility.Visible : Visibility.Collapsed;

    public string ContextMiniText
    {
        get => _contextMiniText;
        set => SetField(ref _contextMiniText, value);
    }

    public string FiveHourMiniText
    {
        get => _fiveHourMiniText;
        set => SetField(ref _fiveHourMiniText, value);
    }

    public string WeeklyMiniText
    {
        get => _weeklyMiniText;
        set => SetField(ref _weeklyMiniText, value);
    }

    public async Task InitializeAsync()
    {
        _contextPollingTask = Task.Run(() => PollContextUsageAsync(_cts.Token));
        _resetCreditPollingTask = Task.Run(() => PollResetCreditsAsync(_cts.Token));
        ApplyCachedResetCredits();
        await RefreshContextUsageAsync(_cts.Token);
        _ = RefreshResetCreditsIfDueAsync(_cts.Token);

        try
        {
            await _bridge.StartAsync(cancellationToken: _cts.Token);
        }
        catch
        {
            Dispatch(() => ApplyRateLimits(RateLimitSnapshot.Waiting));
        }
    }

    public void SaveWindowPosition(double left, double top)
    {
        if (double.IsNaN(left) || double.IsNaN(top))
        {
            return;
        }

        _settings.WindowLeft = left;
        _settings.WindowTop = top;
        _settingsStore.Save(_settings);
    }

    public async Task RefreshResetCreditsManuallyAsync()
    {
        if (_isResetCreditsRefreshing || IsManualResetCreditCooldownActive())
        {
            return;
        }

        _lastManualResetCreditAttemptAt = DateTimeOffset.UtcNow;
        SetResetCreditsRefreshing(true);
        ScheduleResetCreditCooldownRelease(_cts.Token);
        try
        {
            var snapshot = await _resetCreditClient.FetchAsync(_cts.Token);
            _resetCreditCacheStore.Save(snapshot);
            _lastResetCreditAttemptDate = DateOnly.FromDateTime(DateTime.Now);
            Dispatch(() => ApplyResetCredits(snapshot));
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Manual refresh is opportunistic. Keep the last cached UI instead of clearing it.
        }
        finally
        {
            Dispatch(() => SetResetCreditsRefreshing(false));
        }
    }

    private void ApplyContextUsage(ContextUsage usage)
    {
        UpdateContextPollingCadence(usage);

        if (!usage.IsAvailable)
        {
            ApplyContextUnavailableOrCached();
            return;
        }

        _lastValidContextUsage = usage;
        _lastValidContextAt = DateTimeOffset.UtcNow;
        ApplyAvailableContextUsage(usage);
    }

    private void ApplyAvailableContextUsage(ContextUsage usage)
    {
        _isContextAvailable = true;
        var remainingPercent = Math.Clamp(100 - usage.UsedPercent, 0, 100);
        ContextRingValue = remainingPercent;
        ContextRingBrush = BrushForRemaining(remainingPercent);
        ContextGaugeText = $"{remainingPercent:0}%";
        ContextGaugeDetailText = Text.Remaining;
        UpdateMiniSummary();
    }

    private void ApplyContextUnavailableOrCached()
    {
        if (_lastValidContextUsage is not null &&
            DateTimeOffset.UtcNow - _lastValidContextAt <= ContextStaleGrace)
        {
            ApplyAvailableContextUsage(_lastValidContextUsage);
            return;
        }

        _isContextAvailable = false;
        ContextRingValue = 0;
        ContextGaugeText = "--";
        ContextGaugeDetailText = Text.WaitingData;
        ContextRingBrush = UnavailableBrush;
        UpdateMiniSummary();
    }

    private void ApplyRateLimits(RateLimitSnapshot limits)
    {
        var hasAuthoritativeMetric = limits.FiveHour.IsAvailable || limits.Weekly.IsAvailable;
        var fiveHour = ResolveMetric(
            limits.FiveHour,
            ref _lastValidFiveHour,
            ref _lastValidFiveHourAt,
            allowStaleFallback: !hasAuthoritativeMetric);
        var weekly = ResolveMetric(
            limits.Weekly,
            ref _lastValidWeekly,
            ref _lastValidWeeklyAt,
            allowStaleFallback: !hasAuthoritativeMetric);

        _lastRateLimits = new RateLimitSnapshot(fiveHour, weekly);
        FiveHourLimitVisibility = fiveHour.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
        FiveHourValue = fiveHour.IsAvailable ? fiveHour.RemainingPercent : 0;
        WeeklyValue = weekly.IsAvailable ? weekly.RemainingPercent : 0;
        FiveHourGaugeText = FormatPercent(fiveHour);
        WeeklyGaugeText = FormatPercent(weekly);
        var fiveHourReset = FormatResetTime(fiveHour);
        var weeklyReset = FormatResetDate(weekly);
        FiveHourResetText = fiveHourReset;
        WeeklyResetText = weeklyReset;
        UpdateMiniResetTooltip();
        FiveHourResetVisibility = string.IsNullOrEmpty(fiveHourReset) ? Visibility.Collapsed : Visibility.Visible;
        WeeklyResetVisibility = string.IsNullOrEmpty(weeklyReset) ? Visibility.Collapsed : Visibility.Visible;
        FiveHourRingBrush = fiveHour.IsAvailable ? BrushForRemaining(fiveHour.RemainingPercent) : UnavailableBrush;
        WeeklyRingBrush = weekly.IsAvailable ? BrushForRemaining(weekly.RemainingPercent) : UnavailableBrush;
        UpdateMiniSummary();
    }

    private static RateLimitMetric ResolveMetric(
        RateLimitMetric incoming,
        ref RateLimitMetric? lastValid,
        ref DateTimeOffset lastValidAt,
        bool allowStaleFallback)
    {
        if (incoming.IsAvailable)
        {
            lastValid = incoming;
            lastValidAt = DateTimeOffset.UtcNow;
            return incoming;
        }

        if (!allowStaleFallback)
        {
            lastValid = null;
            lastValidAt = default;
            return incoming;
        }

        if (lastValid is not null && DateTimeOffset.UtcNow - lastValidAt <= RateLimitStaleGrace)
        {
            return lastValid;
        }

        return incoming;
    }

    public void SetCodexWindowAvailable(bool isAvailable)
    {
        var wasAvailable = _isCodexWindowAvailable;
        _isCodexWindowAvailable = isAvailable;
        if (isAvailable && !wasAvailable)
        {
            ResetContextPollingCadence();
            _ = RefreshContextUsageAsync(_cts.Token);
        }
    }

    private async Task PollContextUsageAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var delay = _isCodexWindowAvailable
                    ? TimeSpan.FromSeconds(System.Threading.Volatile.Read(ref _contextPollSeconds))
                    : TimeSpan.FromSeconds(ContextHiddenPollSeconds);
                await Task.Delay(delay, cancellationToken);
                if (_isCodexWindowAvailable)
                {
                    await RefreshContextUsageAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PollResetCreditsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(DelayUntilNextResetCreditRefresh(), cancellationToken);
                await RefreshResetCreditsIfDueAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RefreshContextUsageAsync(CancellationToken cancellationToken)
    {
        if (!_isCodexWindowAvailable)
        {
            return;
        }

        if (System.Threading.Interlocked.Exchange(ref _contextReadInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            var usage = await _statusReader.ReadCurrentContextAsync(cancellationToken);
            Dispatch(() => ApplyContextUsage(usage));
        }
        finally
        {
            System.Threading.Volatile.Write(ref _contextReadInProgress, 0);
        }
    }

    private void ApplyCachedResetCredits()
    {
        var cached = _resetCreditCacheStore.Load();
        if (cached is null)
        {
            return;
        }

        if (ResetCreditCacheStore.IsFreshForToday(cached, DateTimeOffset.Now))
        {
            _lastResetCreditAttemptDate = DateOnly.FromDateTime(DateTime.Now);
        }

        ApplyResetCredits(cached);
    }

    private async Task RefreshResetCreditsIfDueAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_lastResetCreditAttemptDate == today)
        {
            return;
        }

        _lastResetCreditAttemptDate = today;
        try
        {
            var snapshot = await _resetCreditClient.FetchAsync(cancellationToken);
            _resetCreditCacheStore.Save(snapshot);
            Dispatch(() => ApplyResetCredits(snapshot));
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Keep the last cached UI. The daily automatic attempt remains low-frequency,
            // while the manual refresh button can retry transient failures.
        }
    }

    private void ApplyResetCredits(ResetCreditSnapshot snapshot)
    {
        var now = DateTimeOffset.Now;
        var availableCredits = snapshot.Credits
            .Where(credit => credit.IsAvailable && credit.ExpiresAt > now)
            .OrderBy(credit => credit.ExpiresAt)
            .ToList();

        if (!snapshot.IsAvailable || snapshot.AvailableCount <= 0 || availableCredits.Count == 0)
        {
            _resetCreditsHaveAvailableData = false;
            _resetCreditsTrayText = string.Empty;
            ResetCreditsSummaryText = string.Empty;
            ResetCreditsTooltipText = null;
            ResetCreditsVisibility = Visibility.Collapsed;
            UpdateMiniResetTooltip();
            return;
        }

        _resetCreditsHaveAvailableData = true;
        var earliest = availableCredits[0].ExpiresAt;
        ResetCreditsSummaryText = string.Format(
            CultureInfo.CurrentCulture,
            Text.ResetCreditsSummaryFormat,
            snapshot.AvailableCount,
            FormatResetCreditSummaryDate(earliest));
        _resetCreditsTrayText = string.Format(
            CultureInfo.CurrentCulture,
            Text.ResetCreditsTraySummaryFormat,
            snapshot.AvailableCount,
            FormatResetCreditSummaryDate(earliest));
        ResetCreditsTooltipText = FormatResetCreditsTooltip(snapshot.AvailableCount, availableCredits);
        ResetCreditsVisibility = Visibility.Visible;
        UpdateMiniResetTooltip();
    }

    private void SetResetCreditsRefreshing(bool isRefreshing)
    {
        if (_isResetCreditsRefreshing == isRefreshing)
        {
            return;
        }

        _isResetCreditsRefreshing = isRefreshing;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRefreshResetCredits)));
    }

    private bool IsManualResetCreditCooldownActive()
    {
        return _lastManualResetCreditAttemptAt is not null &&
            DateTimeOffset.UtcNow - _lastManualResetCreditAttemptAt.Value < ResetCreditManualCooldown;
    }

    private void ScheduleResetCreditCooldownRelease(CancellationToken cancellationToken)
    {
        _resetCreditCooldownTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ResetCreditManualCooldown, cancellationToken);
                Dispatch(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRefreshResetCredits))));
            }
            catch (OperationCanceledException)
            {
            }
        }, cancellationToken);
    }

    private void UpdateContextPollingCadence(ContextUsage usage)
    {
        if (!usage.IsAvailable)
        {
            _lastContextReadWasAvailable = false;
            ResetContextPollingCadence();
            return;
        }

        if (!_lastContextReadWasAvailable ||
            _lastContextUsedPercentForInterval is null ||
            Math.Abs(_lastContextUsedPercentForInterval.Value - usage.UsedPercent) >= 0.5)
        {
            _lastContextReadWasAvailable = true;
            _lastContextUsedPercentForInterval = usage.UsedPercent;
            ResetContextPollingCadence();
            return;
        }

        _stableContextReadCount++;
        var nextSeconds = _stableContextReadCount >= 4
            ? ContextSlowPollSeconds
            : _stableContextReadCount >= 2
                ? ContextMediumPollSeconds
                : ContextFastPollSeconds;
        System.Threading.Volatile.Write(ref _contextPollSeconds, nextSeconds);
    }

    private void ResetContextPollingCadence()
    {
        _stableContextReadCount = 0;
        System.Threading.Volatile.Write(ref _contextPollSeconds, ContextFastPollSeconds);
    }

    private void Dispatch(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        // 非阻塞回投：后台轮询线程不再被 UI 线程阻塞。
        // BeginInvoke 下动作改在 UI 线程稍后执行，若动作抛异常会成为 Dispatcher 未处理异常（可能终止进程），
        // 而原阻塞 Invoke 是把异常抛回后台调用线程。这些 apply 动作本身不抛异常，这里再包一层 try/catch 兜底，
        // 确保非阻塞化不会引入新的崩溃面（失败仅丢一次刷新，下轮轮询自动恢复）。
        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                action();
            }
            catch
            {
            }
        }));
    }

    private static string FormatPercent(RateLimitMetric metric)
    {
        return metric.IsAvailable ? $"{metric.RemainingPercent:0}%" : "--";
    }

    private static string FormatResetTime(RateLimitMetric metric)
    {
        if (!metric.IsAvailable || metric.ResetsAt is null)
        {
            return string.Empty;
        }

        var text = metric.ResetsAt.Value.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
        return WrapResetText(text);
    }

    private static string FormatResetDate(RateLimitMetric metric)
    {
        if (!metric.IsAvailable || metric.ResetsAt is null)
        {
            return string.Empty;
        }

        var culture = CultureInfo.CurrentUICulture;
        var format = culture.TwoLetterISOLanguageName switch
        {
            "zh" => "M月d日",
            "fr" => "d MMM",
            _ => "MMM d"
        };
        var text = metric.ResetsAt.Value.ToLocalTime().ToString(format, culture);
        return WrapResetText(text);
    }

    private static string WrapResetText(string text)
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh"
            ? $"（{text}）"
            : $"({text})";
    }

    private void UpdateMiniResetTooltip()
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrEmpty(FiveHourResetText))
        {
            parts.Add($"{Text.FiveHourTitle}{FiveHourResetText}");
        }

        if (!string.IsNullOrEmpty(WeeklyResetText))
        {
            parts.Add($"{Text.WeeklyTitle}{WeeklyResetText}");
        }

        if (_resetCreditsHaveAvailableData && !string.IsNullOrWhiteSpace(ResetCreditsSummaryText))
        {
            parts.Add(ResetCreditsSummaryText);
        }

        MiniResetTooltipText = parts.Count switch
        {
            0 => null,
            <= 2 => string.Join("    ", parts),
            _ => string.Join("    ", parts.Take(2)) + Environment.NewLine + parts[2]
        };
        UpdateTraySummary();
    }

    private static string FormatResetCreditsTooltip(int availableCount, IReadOnlyList<ResetCreditItem> credits)
    {
        var lines = new List<string>(credits.Count + 1)
        {
            string.Format(CultureInfo.CurrentCulture, Text.ResetCreditsDetailHeaderFormat, availableCount)
        };

        for (var index = 0; index < credits.Count; index++)
        {
            lines.Add(string.Format(
                CultureInfo.CurrentCulture,
                Text.ResetCreditsDetailLineFormat,
                index + 1,
                FormatResetCreditDetailDate(credits[index].ExpiresAt)));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatResetCreditSummaryDate(DateTimeOffset expiresAt)
    {
        var culture = CultureInfo.CurrentUICulture;
        var format = culture.TwoLetterISOLanguageName switch
        {
            "zh" => "M月d日",
            "fr" => "d MMM",
            _ => "MMM d"
        };

        return expiresAt.ToLocalTime().ToString(format, culture);
    }

    private static string FormatResetCreditDetailDate(DateTimeOffset expiresAt)
    {
        var culture = CultureInfo.CurrentUICulture;
        var format = culture.TwoLetterISOLanguageName switch
        {
            "zh" => "M月d日 HH:mm",
            "fr" => "d MMM HH:mm",
            _ => "MMM d HH:mm"
        };

        return expiresAt.ToLocalTime().ToString(format, culture);
    }

    private static TimeSpan DelayUntilNextResetCreditRefresh()
    {
        var now = DateTimeOffset.Now;
        var nextLocalRefresh = new DateTimeOffset(now.Date.AddDays(1).Add(ResetCreditMidnightDelay), now.Offset);
        var delay = nextLocalRefresh - now;
        return delay > TimeSpan.FromMinutes(1) ? delay : TimeSpan.FromMinutes(1);
    }

    private static Brush BrushForRemaining(double remainingPercent)
    {
        if (remainingPercent < 20) return RedBrush;
        if (remainingPercent < 60) return YellowBrush;
        return GreenBrush;
    }

    private static Brush CreateBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private void UpdateMiniSummary()
    {
        var context = _isContextAvailable ? $"{ContextRingValue:0}%" : "--";
        ContextMiniText = $"{Text.ContextShort} {context}";
        FiveHourMiniText = _lastRateLimits.FiveHour.IsAvailable
            ? $"{Text.FiveHourShort} {FormatPercent(_lastRateLimits.FiveHour)}"
            : string.Empty;
        WeeklyMiniText = $"{Text.WeeklyShort} {FormatPercent(_lastRateLimits.Weekly)}";
        UpdateTraySummary();
    }

    private void UpdateTraySummary()
    {
        var summaryParts = new List<string>(3)
        {
            ContextMiniText
        };
        if (_lastRateLimits.FiveHour.IsAvailable)
        {
            summaryParts.Add(FiveHourMiniText);
        }

        summaryParts.Add(WeeklyMiniText);
        var summary = string.Join(" · ", summaryParts);

        TrayTooltipText = string.IsNullOrWhiteSpace(_resetCreditsTrayText)
            ? summary
            : summary + Environment.NewLine + _resetCreditsTrayText;
        TrayStatusPercent = _lastRateLimits.FiveHour.IsAvailable
            ? _lastRateLimits.FiveHour.RemainingPercent
            : _lastRateLimits.Weekly.IsAvailable
                ? _lastRateLimits.Weekly.RemainingPercent
                : null;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_contextPollingTask is not null)
        {
            try
            {
                await _contextPollingTask;
            }
            catch
            {
            }
        }

        if (_resetCreditPollingTask is not null)
        {
            try
            {
                await _resetCreditPollingTask;
            }
            catch
            {
            }
        }

        if (_resetCreditCooldownTask is not null)
        {
            try
            {
                await _resetCreditCooldownTask;
            }
            catch
            {
            }
        }

        await _bridge.DisposeAsync();
        _cts.Dispose();
    }
}
