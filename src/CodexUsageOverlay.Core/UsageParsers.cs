using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CodexUsageOverlay.Core;

public static class UsageParsers
{
    private static readonly Regex ContextUsedRegex = new(
        @"(?:\bContext\s+usage\s*:|上下文用量\s*[：:]|Utilisation\s+du\s+contexte\s*[：:])\s*(?<percent>\d+(?:\.\d+)?)\s*%",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ContextRemainingRegex = new(
        @"(?:\bContext\s+(?:remaining|left)\s*:|上下文剩余\s*[：:]|Contexte\s+restant\s*[：:])\s*(?<percent>\d+(?:\.\d+)?)\s*%",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static RateLimitSnapshot ParseRateLimits(JsonNode? message)
    {
        var payload = JsonNodeHelpers.Payload(message);
        RateLimitMetric? fiveHour = null;
        RateLimitMetric? weekly = null;

        if (payload is JsonObject payloadObject)
        {
            var rootLimits = payloadObject["rateLimits"] as JsonObject ?? payloadObject;
            AssignRateLimitWindow(rootLimits["primary"] as JsonObject, ref fiveHour, ref weekly);
            AssignRateLimitWindow(rootLimits["secondary"] as JsonObject, ref fiveHour, ref weekly);
        }

        foreach (var item in JsonNodeHelpers.FindObjectsInArrays(payload, "limits", "rateLimits", "rate_limits", "windows"))
        {
            AssignRateLimitWindow(item, ref fiveHour, ref weekly);
        }

        return new RateLimitSnapshot(
            fiveHour ?? RateLimitMetric.Unavailable("5小时额度"),
            weekly ?? RateLimitMetric.Unavailable("周额度"));
    }

    public static ContextUsage ParseStatusText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ContextUsage.Waiting;
        }

        if (TryMatchPercent(ContextUsedRegex, text, out var usedPercent))
        {
            return new ContextUsage(true, Clamp(usedPercent, 0, 100));
        }

        if (TryMatchPercent(ContextRemainingRegex, text, out var remainingPercent))
        {
            return new ContextUsage(true, Clamp(100 - remainingPercent, 0, 100));
        }

        return ContextUsage.Waiting;
    }

    public static ResetCreditSnapshot ParseResetCredits(JsonNode? message, DateTimeOffset? fetchedAt = null)
    {
        var payload = JsonNodeHelpers.Payload(message);
        if (payload is null)
        {
            return ResetCreditSnapshot.Unavailable;
        }

        ResetCreditResponseDto? response;
        try
        {
            response = payload.Deserialize<ResetCreditResponseDto>(JsonOptions);
        }
        catch
        {
            return ResetCreditSnapshot.Unavailable;
        }

        var credits = response?.Credits?
            .Select(ParseResetCreditItem)
            .OfType<ResetCreditItem>()
            .OrderBy(c => c.ExpiresAt)
            .ToList() ?? [];

        if (response?.AvailableCount is null && credits.Count == 0)
        {
            return ResetCreditSnapshot.Unavailable;
        }

        return new ResetCreditSnapshot(
            true,
            Math.Max(0, response?.AvailableCount ?? credits.Count(c => c.IsAvailable)),
            credits,
            fetchedAt ?? DateTimeOffset.UtcNow);
    }

    public static bool IsContextIndicator(string? text)
    {
        return !string.IsNullOrWhiteSpace(text) &&
            (ContextUsedRegex.IsMatch(text) || ContextRemainingRegex.IsMatch(text));
    }

    private static bool TryMatchPercent(Regex regex, string text, out double percent)
    {
        percent = 0;
        var match = regex.Match(text);
        return match.Success &&
            double.TryParse(match.Groups["percent"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out percent);
    }

    private static ResetCreditItem? ParseResetCreditItem(ResetCreditItemDto item)
    {
        var grantedAt = ParseDateTimeOffset(item.GrantedAt);
        var expiresAt = ParseDateTimeOffset(item.ExpiresAt);
        var redeemedAt = ParseDateTimeOffset(item.RedeemedAt);

        if (grantedAt is null || expiresAt is null)
        {
            return null;
        }

        return new ResetCreditItem(
            item.Status ?? "unknown",
            item.ResetType ?? "rate_limit_reset",
            grantedAt.Value,
            expiresAt.Value,
            redeemedAt);
    }

    private static void AssignRateLimitWindow(
        JsonObject? item,
        ref RateLimitMetric? fiveHour,
        ref RateLimitMetric? weekly)
    {
        if (item is null)
        {
            return;
        }

        var metric = ParseRateLimitMetric(item);
        if (!metric.IsAvailable)
        {
            return;
        }

        var duration = JsonNodeHelpers.DirectNumber(
            item,
            "windowDurationMins",
            "window_duration_mins",
            "durationMinutes",
            "duration_minutes");
        var label = JsonNodeHelpers.DirectString(item, "label", "name", "type", "window") ?? string.Empty;

        if (IsWeekly(duration, label))
        {
            weekly ??= metric with { Label = "周额度" };
        }
        else if (IsFiveHour(duration, label))
        {
            fiveHour ??= metric with { Label = "5小时额度" };
        }
    }

    private static RateLimitMetric ParseRateLimitMetric(JsonObject item)
    {
        var label = JsonNodeHelpers.DirectString(item, "label", "name", "type", "window") ?? "额度";
        var usedPercent = NormalizeUsedPercent(JsonNodeHelpers.DirectNumber(item, "usedPercent", "used_percent", "percentUsed", "percent_used"));
        var remainingPercent = NormalizeRemainingPercent(JsonNodeHelpers.DirectNumber(item, "remainingPercent", "remaining_percent", "percentRemaining", "percent_remaining"));
        var resetsAt = ParseResetsAt(item);

        var remaining = JsonNodeHelpers.DirectNumber(item, "remaining", "remainingTokens", "remaining_tokens", "remainingRequests", "remaining_requests");
        var limit = JsonNodeHelpers.DirectNumber(item, "limit", "max", "total", "totalTokens", "total_tokens", "totalRequests", "total_requests");

        if (!remainingPercent.HasValue && remaining.HasValue && limit is > 0)
        {
            remainingPercent = remaining.Value / limit.Value * 100;
        }

        if (!usedPercent.HasValue && remainingPercent.HasValue)
        {
            usedPercent = 100 - remainingPercent.Value;
        }

        if (!remainingPercent.HasValue && usedPercent.HasValue)
        {
            remainingPercent = 100 - usedPercent.Value;
        }

        if (!usedPercent.HasValue && !remainingPercent.HasValue)
        {
            return RateLimitMetric.Unavailable(label);
        }

        return new RateLimitMetric(
            true,
            label,
            Clamp(usedPercent ?? 0, 0, 100),
            Clamp(remainingPercent ?? 0, 0, 100),
            resetsAt);
    }

    private static DateTimeOffset? ParseResetsAt(JsonObject item)
    {
        var unixSeconds = JsonNodeHelpers.DirectNumber(item, "resetsAt", "resetAt", "resets_at", "reset_at", "resetTime", "reset_time");
        if (unixSeconds.HasValue)
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(Math.Round(unixSeconds.Value)));
            }
            catch
            {
                return null;
            }
        }

        var text = JsonNodeHelpers.DirectString(item, "resetsAt", "resetAt", "resets_at", "reset_at", "resetTime", "reset_time");
        if (DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? text)
    {
        if (DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool IsFiveHour(double? durationMinutes, string label)
    {
        if (label.Contains("5h", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("5 hour", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("five", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return durationMinutes is >= 240 and <= 360;
    }

    private static bool IsWeekly(double? durationMinutes, string label)
    {
        if (label.Contains("week", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("weekly", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return durationMinutes is >= 7 * 24 * 60;
    }

    private static double? NormalizeUsedPercent(double? value)
    {
        return value.HasValue ? Clamp(value.Value, 0, 100) : null;
    }

    private static double? NormalizeRemainingPercent(double? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var percent = value.Value;
        if (percent is >= 0 and < 1)
        {
            percent *= 100;
        }

        return Clamp(percent, 0, 100);
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class ResetCreditResponseDto
    {
        [JsonPropertyName("available_count")]
        public int? AvailableCount { get; set; }

        [JsonPropertyName("credits")]
        public List<ResetCreditItemDto>? Credits { get; set; }
    }

    private sealed class ResetCreditItemDto
    {
        [JsonPropertyName("reset_type")]
        public string? ResetType { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("granted_at")]
        public string? GrantedAt { get; set; }

        [JsonPropertyName("expires_at")]
        public string? ExpiresAt { get; set; }

        [JsonPropertyName("redeemed_at")]
        public string? RedeemedAt { get; set; }
    }
}
