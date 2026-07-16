namespace CodexUsageOverlay.Core;

public static class RateLimitUpdatePolicy
{
    public static bool ShouldAcceptRateLimitNotification(
        string? baselineLimitId,
        string? notificationLimitId)
    {
        if (string.IsNullOrWhiteSpace(baselineLimitId) ||
            string.IsNullOrWhiteSpace(notificationLimitId))
        {
            return false;
        }

        return string.Equals(
            baselineLimitId,
            notificationLimitId,
            StringComparison.OrdinalIgnoreCase);
    }

    public static RateLimitSnapshot MergeSparse(
        RateLimitSnapshot? previous,
        RateLimitSnapshot incoming,
        bool hasFiveHour,
        bool hasWeekly)
    {
        if (previous is null)
        {
            return incoming;
        }

        return new RateLimitSnapshot(
            hasFiveHour ? incoming.FiveHour : previous.FiveHour,
            hasWeekly ? incoming.Weekly : previous.Weekly);
    }
}
