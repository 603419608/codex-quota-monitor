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
    ("account usage parser reads email and lifetime tokens", AccountUsageParserReadsEmailAndLifetimeTokens),
    ("account usage parser prefers display name", AccountUsageParserPrefersDisplayName),
    ("account usage parser rejects missing lifetime tokens", AccountUsageParserRejectsMissingLifetimeTokens),
    ("OAuth profile reader reads name claim", OAuthProfileReaderReadsNameClaim),
    ("OAuth profile reader ignores missing name claim", OAuthProfileReaderIgnoresMissingNameClaim),
    ("OAuth profile reader ignores malformed token", OAuthProfileReaderIgnoresMalformedToken),
    ("rate limit parser classifies five hour and weekly windows", RateLimitParserClassifiesWindows),
    ("rate limit parser reads app-server primary secondary shape", RateLimitParserReadsPrimarySecondaryShape),
    ("rate limit parser treats a weekly primary as weekly only", RateLimitParserTreatsWeeklyPrimaryAsWeeklyOnly),
    ("rate limit parser restores five hour when its window returns", RateLimitParserRestoresFiveHourWhenWindowReturns),
    ("rate limit parser reads reset timestamps", RateLimitParserReadsResetTimestamps),
    ("rate limit parser treats one as one percent", RateLimitParserTreatsOneAsOnePercent),
    ("rate limit parser treats fractional used percent as percent", RateLimitParserTreatsFractionalUsedPercentAsPercent),
    ("rate limit parser preserves sub-one used percent near reset", RateLimitParserPreservesSubOneUsedPercentNearReset),
    ("rate limit parser reads remaining and limit shape", RateLimitParserReadsRemainingAndLimitShape),
    ("rate limit parser reads invariant numeric strings", RateLimitParserReadsInvariantNumericStrings),
    ("rate limit update rejects model bucket", RateLimitUpdateRejectsModelBucket),
    ("rate limit update accepts total bucket", RateLimitUpdateAcceptsTotalBucket),
    ("rate limit update preserves weekly on five-hour-only update", RateLimitUpdatePreservesWeeklyOnFiveHourOnlyUpdate),
    ("rate limit update preserves five hour on weekly-only update", RateLimitUpdatePreservesFiveHourOnWeeklyOnlyUpdate),
    ("rate limit update rejects notification without id", RateLimitUpdateRejectsNotificationWithoutId),
    ("rate limit update rejects identified bucket before baseline", RateLimitUpdateRejectsIdentifiedBucketBeforeBaseline),
    ("rate limit update rejects when both ids missing", RateLimitUpdateRejectsWhenBothIdsMissing),
    ("rate limit update rejects empty notification id", RateLimitUpdateRejectsEmptyNotificationId),
    ("rate limit update rejects whitespace notification id", RateLimitUpdateRejectsWhitespaceNotificationId),
    ("rate limit update merge sparse keeps missing side unavailable with no previous snapshot", RateLimitUpdateMergeSparseKeepsMissingSideUnavailableWithNoPrevious),
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

static void AccountUsageParserReadsEmailAndLifetimeTokens()
{
    var account = JsonNode.Parse("""
    {
      "account": {
        "type": "chatgpt",
        "email": "user@example.com",
        "planType": "pro"
      }
    }
    """);
    var usage = JsonNode.Parse("""
    {
      "summary": {
        "lifetimeTokens": 2340720277,
        "peakDailyTokens": 112621176
      },
      "dailyUsageBuckets": []
    }
    """);

    var snapshot = UsageParsers.ParseAccountUsage(account, usage);
    AssertTrue(snapshot.IsAvailable, "account usage should be available");
    AssertTrue(snapshot.DisplayName is null, "display name should remain optional");
    AssertTrue(snapshot.Email == "user@example.com", "email fallback");
    AssertTrue(snapshot.LifetimeTokens == 2_340_720_277, "lifetime token total");
}

static void AccountUsageParserPrefersDisplayName()
{
    var account = JsonNode.Parse("""
    {
      "account": {
        "displayName": "Deng GuoChao",
        "email": "user@example.com"
      }
    }
    """);
    var usage = JsonNode.Parse("""
    {
      "summary": { "lifetime_tokens": 42 }
    }
    """);

    var snapshot = UsageParsers.ParseAccountUsage(account, usage);
    AssertTrue(snapshot.IsAvailable, "account usage should be available");
    AssertTrue(snapshot.DisplayName == "Deng GuoChao", "display name");
    AssertTrue(snapshot.Email == "user@example.com", "email remains available as fallback");
    AssertTrue(snapshot.LifetimeTokens == 42, "snake-case lifetime token total");
}

static void AccountUsageParserRejectsMissingLifetimeTokens()
{
    var account = JsonNode.Parse("""{ "account": { "email": "user@example.com" } }""");
    var usage = JsonNode.Parse("""{ "summary": { "peakDailyTokens": 123 } }""");

    var snapshot = UsageParsers.ParseAccountUsage(account, usage);
    AssertTrue(!snapshot.IsAvailable, "missing lifetime total should not render as zero");
}

static void OAuthProfileReaderReadsNameClaim()
{
    var token = CreateUnsignedIdToken("""{ "name": "  Deng GuoChao  " }""");
    var displayName = OAuthProfileReader.ParseDisplayNameFromIdToken(token);

    AssertTrue(displayName == "Deng GuoChao", "name claim should be trimmed and returned");
}

static void OAuthProfileReaderIgnoresMissingNameClaim()
{
    var token = CreateUnsignedIdToken("""{ "email": "user@example.com" }""");
    var displayName = OAuthProfileReader.ParseDisplayNameFromIdToken(token);

    AssertTrue(displayName is null, "email must not be treated as a display name");
}

static void OAuthProfileReaderIgnoresMalformedToken()
{
    var displayName = OAuthProfileReader.ParseDisplayNameFromIdToken("not-a-jwt");

    AssertTrue(displayName is null, "malformed tokens should safely fall back to email");
}

static string CreateUnsignedIdToken(string payload)
{
    static string Encode(string value)
    {
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    return $"{Encode("""{ "alg": "none" }""")}.{Encode(payload)}.";
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

static void RateLimitParserTreatsWeeklyPrimaryAsWeeklyOnly()
{
    var json = JsonNode.Parse("""
    {
      "result": {
        "rateLimits": {
          "primary": {
            "usedPercent": 14,
            "windowDurationMins": 10080,
            "resetsAt": 1782344086
          },
          "secondary": null
        }
      }
    }
    """);

    var limits = UsageParsers.ParseRateLimits(json);
    AssertTrue(!limits.FiveHour.IsAvailable, "five hour limit should be unavailable when no five-hour window exists");
    AssertTrue(limits.Weekly.IsAvailable, "weekly primary should be classified as the weekly limit");
    AssertNear(86, limits.Weekly.RemainingPercent, 0.01, "weekly remaining from primary window");
    AssertTrue(limits.Weekly.ResetsAt == DateTimeOffset.FromUnixTimeSeconds(1782344086), "weekly primary reset timestamp");
}

static void RateLimitParserRestoresFiveHourWhenWindowReturns()
{
    var json = JsonNode.Parse("""
    {
      "result": {
        "rateLimits": {
          "primary": {
            "usedPercent": 14,
            "windowDurationMins": 300
          },
          "secondary": null
        }
      }
    }
    """);

    var limits = UsageParsers.ParseRateLimits(json);
    AssertTrue(limits.FiveHour.IsAvailable, "five hour limit should return when a five-hour window is present");
    AssertTrue(!limits.Weekly.IsAvailable, "weekly limit should remain unavailable when no weekly window exists");
    AssertNear(86, limits.FiveHour.RemainingPercent, 0.01, "restored five hour remaining");
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

static void RateLimitParserTreatsFractionalUsedPercentAsPercent()
{
    var json = JsonNode.Parse("""
    {
      "result": {
        "rateLimits": {
          "primary": { "usedPercent": 0.6, "windowDurationMins": 300 },
          "secondary": { "usedPercent": 0.6, "windowDurationMins": 10080 }
        }
      }
    }
    """);

    var limits = UsageParsers.ParseRateLimits(json);
    AssertNear(99.4, limits.FiveHour.RemainingPercent, 0.01, "fractional five hour used percent");
    AssertNear(99.4, limits.Weekly.RemainingPercent, 0.01, "fractional weekly used percent");
}

static void RateLimitParserPreservesSubOneUsedPercentNearReset()
{
    var json = JsonNode.Parse("""
    {
      "result": {
        "rateLimits": {
          "primary": { "used_percent": 0.4, "windowDurationMins": 300 },
          "secondary": { "percent_used": 0.4, "windowDurationMins": 10080 }
        }
      }
    }
    """);

    var limits = UsageParsers.ParseRateLimits(json);
    AssertNear(99.6, limits.FiveHour.RemainingPercent, 0.01, "sub-one five hour used percent");
    AssertNear(99.6, limits.Weekly.RemainingPercent, 0.01, "sub-one weekly used percent");
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

static void RateLimitParserReadsInvariantNumericStrings()
{
    var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
    try
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        var json = JsonNode.Parse("""
        {
          "result": {
            "limits": [
              { "windowDurationMins": 10080, "remainingPercent": "0.6" }
            ]
          }
        }
        """);

        var limits = UsageParsers.ParseRateLimits(json);
        AssertNear(60, limits.Weekly.RemainingPercent, 0.01, "invariant numeric string under French culture");
    }
    finally
    {
        System.Globalization.CultureInfo.CurrentCulture = previousCulture;
    }
}

static void RateLimitUpdateRejectsModelBucket()
{
    var accepted = RateLimitUpdatePolicy.ShouldAcceptRateLimitNotification(
        "codex",
        "codex_bengalfox");

    AssertTrue(!accepted, "model-specific bucket should not replace the total bucket");
}

static void RateLimitUpdateAcceptsTotalBucket()
{
    var accepted = RateLimitUpdatePolicy.ShouldAcceptRateLimitNotification("codex", "CODEX");
    AssertTrue(accepted, "matching total bucket should be accepted case-insensitively");

    var merged = RateLimitUpdatePolicy.MergeSparse(
        CreateRateLimitSnapshot(90, 80),
        CreateRateLimitSnapshot(75, 65),
        hasFiveHour: true,
        hasWeekly: true);
    AssertNear(75, merged.FiveHour.RemainingPercent, 0.01, "accepted total bucket five hour update");
    AssertNear(65, merged.Weekly.RemainingPercent, 0.01, "accepted total bucket weekly update");
}

static void RateLimitUpdatePreservesWeeklyOnFiveHourOnlyUpdate()
{
    var merged = RateLimitUpdatePolicy.MergeSparse(
        CreateRateLimitSnapshot(90, 80),
        CreateRateLimitSnapshot(70, 0),
        hasFiveHour: true,
        hasWeekly: false);

    AssertNear(70, merged.FiveHour.RemainingPercent, 0.01, "five-hour-only update");
    AssertNear(80, merged.Weekly.RemainingPercent, 0.01, "five-hour-only update should preserve weekly");
}

static void RateLimitUpdatePreservesFiveHourOnWeeklyOnlyUpdate()
{
    var merged = RateLimitUpdatePolicy.MergeSparse(
        CreateRateLimitSnapshot(90, 80),
        CreateRateLimitSnapshot(0, 60),
        hasFiveHour: false,
        hasWeekly: true);

    AssertNear(90, merged.FiveHour.RemainingPercent, 0.01, "weekly-only update should preserve five hour");
    AssertNear(60, merged.Weekly.RemainingPercent, 0.01, "weekly-only update");
}

static void RateLimitUpdateRejectsNotificationWithoutId()
{
    AssertTrue(
        !RateLimitUpdatePolicy.ShouldAcceptRateLimitNotification("codex", null),
        "notification without a limit id must not override the total bucket");
}

static void RateLimitUpdateRejectsIdentifiedBucketBeforeBaseline()
{
    AssertTrue(
        !RateLimitUpdatePolicy.ShouldAcceptRateLimitNotification(null, "codex"),
        "identified notification should wait until the baseline bucket is known");
}

static void RateLimitUpdateRejectsWhenBothIdsMissing()
{
    AssertTrue(
        !RateLimitUpdatePolicy.ShouldAcceptRateLimitNotification(null, null),
        "notification must not be accepted when neither baseline nor notification id is known");
}

static void RateLimitUpdateRejectsEmptyNotificationId()
{
    AssertTrue(
        !RateLimitUpdatePolicy.ShouldAcceptRateLimitNotification("codex", string.Empty),
        "empty notification limit id must not override the total bucket");
}

static void RateLimitUpdateRejectsWhitespaceNotificationId()
{
    AssertTrue(
        !RateLimitUpdatePolicy.ShouldAcceptRateLimitNotification("codex", "   "),
        "whitespace-only notification limit id must not override the total bucket");
}

static void RateLimitUpdateMergeSparseKeepsMissingSideUnavailableWithNoPrevious()
{
    var incoming = new RateLimitSnapshot(
        new RateLimitMetric(true, 90, null),
        RateLimitMetric.Unavailable);

    var merged = RateLimitUpdatePolicy.MergeSparse(
        previous: null,
        incoming,
        hasFiveHour: true,
        hasWeekly: false);

    AssertTrue(merged.FiveHour.IsAvailable, "five hour metric from the notification should be available");
    AssertTrue(!merged.Weekly.IsAvailable, "weekly metric missing from the notification stays unavailable when there is no previous snapshot");
}

static RateLimitSnapshot CreateRateLimitSnapshot(double fiveHourRemaining, double weeklyRemaining)
{
    return new RateLimitSnapshot(
        new RateLimitMetric(true, fiveHourRemaining, null),
        new RateLimitMetric(true, weeklyRemaining, null));
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
