using System.Text.Json.Nodes;
using CodexUsageOverlay.Core;

var tests = new (string Name, Action Run)[]
{
    ("status text parser reads Codex UI context indicator", StatusTextParserReadsCodexUiIndicator),
    ("status text parser reads Chinese Codex UI context indicator", StatusTextParserReadsChineseCodexUiIndicator),
    ("status text parser reads French Codex UI context indicator", StatusTextParserReadsFrenchCodexUiIndicator),
    ("status text parser reads French Codex UI context indicator with narrow spaces", StatusTextParserReadsFrenchCodexUiIndicatorWithNarrowSpaces),
    ("status text parser reads context remaining indicator", StatusTextParserReadsContextRemainingIndicator),
    ("status text parser reads English context left indicator", StatusTextParserReadsEnglishContextLeftIndicator),
    ("status text parser reads Chinese context remaining indicator", StatusTextParserReadsChineseContextRemainingIndicator),
    ("status text parser ignores bare percentages", StatusTextParserIgnoresBarePercentages),
    ("rate limit parser classifies five hour and weekly windows", RateLimitParserClassifiesWindows),
    ("rate limit parser reads app-server primary secondary shape", RateLimitParserReadsPrimarySecondaryShape),
    ("rate limit parser reads reset timestamps", RateLimitParserReadsResetTimestamps),
    ("rate limit parser treats one as one percent", RateLimitParserTreatsOneAsOnePercent),
    ("rate limit parser reads remaining and limit shape", RateLimitParserReadsRemainingAndLimitShape),
    ("reset credit parser reads available count and expirations", ResetCreditParserReadsAvailableCountAndExpirations),
    ("reset credit parser falls back to available credits", ResetCreditParserFallsBackToAvailableCredits)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

if (failures > 0)
{
    Environment.ExitCode = 1;
}

static void StatusTextParserReadsCodexUiIndicator()
{
    var usage = UsageParsers.ParseStatusText("Context usage: 40%");
    AssertTrue(usage.IsAvailable, "usage should be available");
    AssertNear(40, usage.UsedPercent, 0.01, "used percent");
}

static void StatusTextParserReadsChineseCodexUiIndicator()
{
    var usage = UsageParsers.ParseStatusText("上下文用量：47%");
    AssertTrue(usage.IsAvailable, "usage should be available");
    AssertNear(47, usage.UsedPercent, 0.01, "used percent");
}

static void StatusTextParserReadsFrenchCodexUiIndicator()
{
    var usage = UsageParsers.ParseStatusText("Utilisation du contexte\u00a0: 76\u00a0%");
    AssertTrue(usage.IsAvailable, "usage should be available");
    AssertNear(76, usage.UsedPercent, 0.01, "used percent");
}

static void StatusTextParserReadsFrenchCodexUiIndicatorWithNarrowSpaces()
{
    var usage = UsageParsers.ParseStatusText("Utilisation du contexte\u202f: 76\u202f%");
    AssertTrue(usage.IsAvailable, "usage should be available");
    AssertNear(76, usage.UsedPercent, 0.01, "used percent");
}

static void StatusTextParserReadsContextRemainingIndicator()
{
    var usage = UsageParsers.ParseStatusText("Contexte restant : 58 %");
    AssertTrue(usage.IsAvailable, "usage should be available");
    AssertNear(42, usage.UsedPercent, 0.01, "used percent from remaining percent");
}

static void StatusTextParserReadsEnglishContextLeftIndicator()
{
    var remaining = UsageParsers.ParseStatusText("Context remaining: 58%");
    AssertTrue(remaining.IsAvailable, "remaining usage should be available");
    AssertNear(42, remaining.UsedPercent, 0.01, "used percent from remaining percent");

    var left = UsageParsers.ParseStatusText("Context left: 58%");
    AssertTrue(left.IsAvailable, "left usage should be available");
    AssertNear(42, left.UsedPercent, 0.01, "used percent from left percent");
}

static void StatusTextParserReadsChineseContextRemainingIndicator()
{
    var usage = UsageParsers.ParseStatusText("上下文剩余：58%");
    AssertTrue(usage.IsAvailable, "usage should be available");
    AssertNear(42, usage.UsedPercent, 0.01, "used percent from remaining percent");
}

static void StatusTextParserIgnoresBarePercentages()
{
    var bare = UsageParsers.ParseStatusText("58%");
    AssertTrue(!bare.IsAvailable, "bare percent should not be parsed as context usage");

    var random = UsageParsers.ParseStatusText("random 58% text");
    AssertTrue(!random.IsAvailable, "random percent text should not be parsed as context usage");
}

static void RateLimitParserClassifiesWindows()
{
    var json = JsonNode.Parse("""
    {
      "result": {
        "limits": [
          { "windowDurationMins": 300, "remainingPercent": 0.6 },
          { "windowDurationMins": 10080, "remainingPercent": 80 }
        ]
      }
    }
    """);

    var limits = UsageParsers.ParseRateLimits(json);
    AssertTrue(limits.FiveHour.IsAvailable, "five hour limit should be available");
    AssertTrue(limits.Weekly.IsAvailable, "weekly limit should be available");
    AssertNear(60, limits.FiveHour.RemainingPercent, 0.01, "five hour remaining");
    AssertNear(80, limits.Weekly.RemainingPercent, 0.01, "weekly remaining");
}

static void RateLimitParserReadsPrimarySecondaryShape()
{
    var json = JsonNode.Parse("""
    {
      "result": {
        "rateLimits": {
          "primary": {
            "usedPercent": 41,
            "windowDurationMins": 300
          },
          "secondary": {
            "usedPercent": 12,
            "windowDurationMins": 10080
          }
        }
      }
    }
    """);

    var limits = UsageParsers.ParseRateLimits(json);
    AssertTrue(limits.FiveHour.IsAvailable, "five hour limit should be available");
    AssertTrue(limits.Weekly.IsAvailable, "weekly limit should be available");
    AssertNear(59, limits.FiveHour.RemainingPercent, 0.01, "five hour remaining from used percent");
    AssertNear(88, limits.Weekly.RemainingPercent, 0.01, "weekly remaining from used percent");
}

static void RateLimitParserReadsResetTimestamps()
{
    var json = JsonNode.Parse("""
    {
      "result": {
        "rateLimits": {
          "primary": {
            "usedPercent": 2,
            "windowDurationMins": 300,
            "resetsAt": 1782032873
          },
          "secondary": {
            "usedPercent": 21,
            "windowDurationMins": 10080,
            "resetsAt": 1782344086
          }
        }
      }
    }
    """);

    var limits = UsageParsers.ParseRateLimits(json);
    AssertTrue(limits.FiveHour.ResetsAt == DateTimeOffset.FromUnixTimeSeconds(1782032873), "five hour reset timestamp");
    AssertTrue(limits.Weekly.ResetsAt == DateTimeOffset.FromUnixTimeSeconds(1782344086), "weekly reset timestamp");
}

static void RateLimitParserTreatsOneAsOnePercent()
{
    var json = JsonNode.Parse("""
    {
      "result": {
        "rateLimits": {
          "primary": {
            "usedPercent": 1,
            "windowDurationMins": 300
          },
          "secondary": {
            "usedPercent": 1,
            "windowDurationMins": 10080
          }
        }
      }
    }
    """);

    var limits = UsageParsers.ParseRateLimits(json);
    AssertNear(99, limits.FiveHour.RemainingPercent, 0.01, "five hour remaining from one percent used");
    AssertNear(99, limits.Weekly.RemainingPercent, 0.01, "weekly remaining from one percent used");
}

static void RateLimitParserReadsRemainingAndLimitShape()
{
    var json = JsonNode.Parse("""
    {
      "result": {
        "limits": [
          { "label": "5 hour", "remaining": 32, "limit": 50 },
          { "label": "weekly", "remaining": 89, "limit": 100 }
        ]
      }
    }
    """);

    var limits = UsageParsers.ParseRateLimits(json);
    AssertNear(64, limits.FiveHour.RemainingPercent, 0.01, "five hour remaining from remaining/limit");
    AssertNear(89, limits.Weekly.RemainingPercent, 0.01, "weekly remaining from remaining/limit");
}

static void ResetCreditParserReadsAvailableCountAndExpirations()
{
    var json = JsonNode.Parse("""
    {
      "available_count": 3,
      "credits": [
        {
          "id": "credit_1",
          "reset_type": "codex_rate_limits",
          "status": "available",
          "granted_at": "2026-06-12T08:24:04Z",
          "expires_at": "2026-07-12T08:24:04Z",
          "redeemed_at": null
        },
        {
          "id": "credit_2",
          "reset_type": "codex_rate_limits",
          "status": "available",
          "granted_at": "2026-06-18T06:40:45Z",
          "expires_at": "2026-07-18T06:40:45Z"
        }
      ]
    }
    """);

    var snapshot = UsageParsers.ParseResetCredits(json, DateTimeOffset.FromUnixTimeSeconds(1782032873));
    AssertTrue(snapshot.IsAvailable, "reset credits should be available");
    AssertTrue(snapshot.AvailableCount == 3, "available count should come from response");
    AssertTrue(snapshot.Credits.Count == 2, "credit detail rows");
    AssertTrue(snapshot.Credits[0].ExpiresAt == DateTimeOffset.Parse("2026-07-12T08:24:04Z"), "first expiry");
    AssertTrue(snapshot.Credits[0].IsAvailable, "first credit should be available");
}

static void ResetCreditParserFallsBackToAvailableCredits()
{
    var json = JsonNode.Parse("""
    {
      "credits": [
        {
          "id": "credit_1",
          "status": "available",
          "granted_at": "2026-06-12T08:24:04Z",
          "expires_at": "2026-07-12T08:24:04Z"
        },
        {
          "id": "credit_2",
          "status": "redeemed",
          "granted_at": "2026-06-18T06:40:45Z",
          "expires_at": "2026-07-18T06:40:45Z",
          "redeemed_at": "2026-06-19T06:40:45Z"
        }
      ]
    }
    """);

    var snapshot = UsageParsers.ParseResetCredits(json);
    AssertTrue(snapshot.IsAvailable, "reset credits should be available");
    AssertTrue(snapshot.AvailableCount == 1, "available count should fall back to available credit rows");
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertNear(double expected, double actual, double tolerance, string message)
{
    if (Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}
