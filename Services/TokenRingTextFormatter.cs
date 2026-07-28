using System.Globalization;

namespace AgentCompanion.Services;

internal static class TokenRingTextFormatter
{
    private const string MissingResetText = "未取得";

    public static string FormatClaudeUsage(string primaryLabel, string secondaryLabel)
    {
        return $"Claude {primaryLabel} / {secondaryLabel}";
    }

    public static string FormatResetLine(
        DateTimeOffset? primaryReset,
        DateTimeOffset? secondaryReset,
        bool includeSecondary,
        TimeZoneInfo? timeZone = null)
    {
        if (!primaryReset.HasValue && (!includeSecondary || !secondaryReset.HasValue))
            return string.Empty;

        var zone = timeZone ?? TimeZoneInfo.Local;
        var primaryLabel = FormatResetTime(primaryReset, zone);
        if (!includeSecondary)
            return $"\nリセット日時 {primaryLabel}";

        var secondaryLabel = FormatResetTime(secondaryReset, zone);
        return $"\nリセット日時 {primaryLabel} / {secondaryLabel}";
    }

    private static string FormatResetTime(DateTimeOffset? resetsAt, TimeZoneInfo timeZone)
    {
        if (!resetsAt.HasValue)
            return MissingResetText;

        var local = TimeZoneInfo.ConvertTime(resetsAt.Value, timeZone);
        return local.ToString("M/d H:mm", CultureInfo.InvariantCulture);
    }
}
