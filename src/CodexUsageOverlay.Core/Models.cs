namespace CodexUsageOverlay.Core;

public sealed record ContextUsage(bool IsAvailable, double UsedPercent)
{
    public static ContextUsage Waiting { get; } = new(false, 0);
}

public sealed record RateLimitMetric(
    bool IsAvailable,
    double RemainingPercent,
    DateTimeOffset? ResetsAt)
{
    public static RateLimitMetric Unavailable { get; } = new(false, 0, null);
}

public sealed record RateLimitSnapshot(RateLimitMetric FiveHour, RateLimitMetric Weekly)
{
    public static RateLimitSnapshot Waiting { get; } = new(
        RateLimitMetric.Unavailable,
        RateLimitMetric.Unavailable);
}

public sealed record ResetCreditItem(
    string Status,
    string ResetType,
    DateTimeOffset GrantedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RedeemedAt)
{
    public bool IsAvailable =>
        string.Equals(Status, "available", StringComparison.OrdinalIgnoreCase) &&
        RedeemedAt is null;
}

public sealed record ResetCreditSnapshot(
    bool IsAvailable,
    int AvailableCount,
    List<ResetCreditItem> Credits,
    DateTimeOffset FetchedAt)
{
    public static ResetCreditSnapshot Unavailable { get; } = new(false, 0, [], DateTimeOffset.UtcNow);
}

public sealed record AccountUsageSnapshot(
    bool IsAvailable,
    string? DisplayName,
    string? Email,
    long LifetimeTokens)
{
    public static AccountUsageSnapshot Unavailable { get; } = new(false, null, null, 0);
}
