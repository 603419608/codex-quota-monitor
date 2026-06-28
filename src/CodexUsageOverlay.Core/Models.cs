namespace CodexUsageOverlay.Core;

public sealed record ContextUsage(bool IsAvailable, double UsedPercent)
{
    public static ContextUsage Waiting { get; } = new(false, 0);
}

public sealed record RateLimitMetric(
    bool IsAvailable,
    string Label,
    double UsedPercent,
    double RemainingPercent,
    DateTimeOffset? ResetsAt)
{
    public static RateLimitMetric Unavailable(string label) => new(false, label, 0, 0, null);
}

public sealed record RateLimitSnapshot(RateLimitMetric FiveHour, RateLimitMetric Weekly)
{
    public static RateLimitSnapshot Waiting { get; } = new(
        RateLimitMetric.Unavailable("5小时额度"),
        RateLimitMetric.Unavailable("周额度"));
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
