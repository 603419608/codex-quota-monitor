using System.Globalization;

namespace CodexUsageOverlay.App;

public sealed class LocalizedStrings
{
    public static LocalizedStrings Current => ForCulture(CultureInfo.CurrentUICulture);

    public required string ContextTitle { get; init; }
    public required string FiveHourTitle { get; init; }
    public required string WeeklyTitle { get; init; }
    public required string Remaining { get; init; }
    public required string WaitingData { get; init; }
    public required string Show { get; init; }
    public required string Exit { get; init; }
    public required string HideToTray { get; init; }
    public required string ExpandFullMode { get; init; }
    public required string ContextShort { get; init; }
    public required string FiveHourShort { get; init; }
    public required string WeeklyShort { get; init; }
    public required string ResetCreditsUnavailable { get; init; }
    public required string ResetCreditsSummaryFormat { get; init; }
    public required string ResetCreditsTraySummaryFormat { get; init; }
    public required string ResetCreditsDetailHeaderFormat { get; init; }
    public required string ResetCreditsDetailLineFormat { get; init; }

    private static LocalizedStrings ForCulture(CultureInfo culture)
    {
        return culture.TwoLetterISOLanguageName switch
        {
            "zh" => Chinese,
            "fr" => French,
            _ => English
        };
    }

    private static LocalizedStrings Chinese { get; } = new()
    {
        ContextTitle = "上下文容量",
        FiveHourTitle = "5小时额度",
        WeeklyTitle = "周额度",
        Remaining = "剩余",
        WaitingData = "等待数据",
        Show = "显示",
        Exit = "退出",
        HideToTray = "隐藏到托盘",
        ExpandFullMode = "展开完整模式",
        ContextShort = "上下文",
        FiveHourShort = "5小时",
        WeeklyShort = "周",
        ResetCreditsUnavailable = "重置机会不可用",
        ResetCreditsSummaryFormat = "重置机会 {0} 次 · 最早 {1} 过期",
        ResetCreditsTraySummaryFormat = "重置 {0}次 · {1}",
        ResetCreditsDetailHeaderFormat = "重置机会 {0} 次",
        ResetCreditsDetailLineFormat = "{0} · {1} 过期"
    };

    private static LocalizedStrings English { get; } = new()
    {
        ContextTitle = "Context",
        FiveHourTitle = "5-hour limit",
        WeeklyTitle = "Weekly limit",
        Remaining = "left",
        WaitingData = "Waiting",
        Show = "Show",
        Exit = "Exit",
        HideToTray = "Hide to tray",
        ExpandFullMode = "Expand full mode",
        ContextShort = "Context",
        FiveHourShort = "5h",
        WeeklyShort = "Week",
        ResetCreditsUnavailable = "Reset credits unavailable",
        ResetCreditsSummaryFormat = "Resets {0} · first expires {1}",
        ResetCreditsTraySummaryFormat = "Resets {0} · {1}",
        ResetCreditsDetailHeaderFormat = "Resets {0}",
        ResetCreditsDetailLineFormat = "{0} · expires {1}"
    };

    private static LocalizedStrings French { get; } = new()
    {
        ContextTitle = "Contexte",
        FiveHourTitle = "Limite 5 h",
        WeeklyTitle = "Limite hebdo",
        Remaining = "restant",
        WaitingData = "En attente",
        Show = "Afficher",
        Exit = "Quitter",
        HideToTray = "Réduire dans la zone de notification",
        ExpandFullMode = "Mode complet",
        ContextShort = "Contexte",
        FiveHourShort = "5 h",
        WeeklyShort = "Hebdo",
        ResetCreditsUnavailable = "Réinitialisations indisponibles",
        ResetCreditsSummaryFormat = "Réinitialisations {0} · première expiration {1}",
        ResetCreditsTraySummaryFormat = "Réinit. {0} · {1}",
        ResetCreditsDetailHeaderFormat = "Réinitialisations {0}",
        ResetCreditsDetailLineFormat = "{0} · expire le {1}"
    };
}
