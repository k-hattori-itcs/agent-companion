namespace AgentCompanion.Services;

internal enum ClaudeUsageWindow
{
    FiveHour,
    SevenDay
}

internal sealed record ClaudeUsageApiCache(
    string ClaudeHome,
    ClaudeUsageApiResponse Response,
    DateTimeOffset FetchedAtUtc);

internal readonly record struct ClaudeUsageValue(double Percent, bool IsExact);

internal static class ClaudeUsagePolicy
{
    private static readonly TimeSpan ApiUsageMaxAge = TimeSpan.FromMinutes(16);

    public static ClaudeUsageValue Resolve(
        ClaudeUsageApiCache? cache,
        string claudeHome,
        ClaudeUsageWindow window,
        double? statuslinePercent,
        double estimatedPercent,
        DateTimeOffset now)
    {
        var apiPercent = GetCurrentApiPercent(cache, claudeHome, window, now);
        if (apiPercent.HasValue)
            return new ClaudeUsageValue(apiPercent.Value, true);
        if (statuslinePercent.HasValue)
            return new ClaudeUsageValue(statuslinePercent.Value, true);

        var cachedPercent = GetCachedApiPercent(cache, claudeHome, window);
        return new ClaudeUsageValue(cachedPercent ?? estimatedPercent, false);
    }

    public static DateTimeOffset? GetFreshResetAt(
        ClaudeUsageApiCache? cache,
        string claudeHome,
        ClaudeUsageWindow window,
        DateTimeOffset now)
    {
        if (cache == null
            || !string.Equals(cache.ClaudeHome, claudeHome, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var resetsAt = window switch
        {
            ClaudeUsageWindow.FiveHour => cache.Response.FiveHourResetsAt,
            ClaudeUsageWindow.SevenDay => cache.Response.SevenDayResetsAt,
            _ => null
        };
        return resetsAt;
    }

    private static double? GetCurrentApiPercent(
        ClaudeUsageApiCache? cache,
        string claudeHome,
        ClaudeUsageWindow window,
        DateTimeOffset now)
    {
        return TryGetCurrentApiUsage(cache, claudeHome, window, now, out var percent, out _)
            ? percent
            : null;
    }

    private static double? GetCachedApiPercent(
        ClaudeUsageApiCache? cache,
        string claudeHome,
        ClaudeUsageWindow window)
    {
        if (cache == null
            || !string.Equals(cache.ClaudeHome, claudeHome, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return window switch
        {
            ClaudeUsageWindow.FiveHour => cache.Response.FiveHourPercent,
            ClaudeUsageWindow.SevenDay => cache.Response.SevenDayPercent,
            _ => null
        };
    }
    private static bool TryGetCurrentApiUsage(
        ClaudeUsageApiCache? cache,
        string claudeHome,
        ClaudeUsageWindow window,
        DateTimeOffset now,
        out double? percent,
        out DateTimeOffset? resetsAt)
    {
        percent = null;
        resetsAt = null;
        if (cache == null
            || !string.Equals(cache.ClaudeHome, claudeHome, StringComparison.OrdinalIgnoreCase)
            || now - cache.FetchedAtUtc > ApiUsageMaxAge)
        {
            return false;
        }

        (percent, resetsAt) = window switch
        {
            ClaudeUsageWindow.FiveHour => (cache.Response.FiveHourPercent, cache.Response.FiveHourResetsAt),
            ClaudeUsageWindow.SevenDay => (cache.Response.SevenDayPercent, cache.Response.SevenDayResetsAt),
            _ => (null, null)
        };
        return !resetsAt.HasValue || resetsAt.Value >= now.AddMinutes(-1);
    }
}

internal sealed class SingleFlightGate
{
    private int _entered;

    public bool TryEnter()
    {
        return Interlocked.CompareExchange(ref _entered, 1, 0) == 0;
    }

    public void Exit()
    {
        Interlocked.Exchange(ref _entered, 0);
    }
}
