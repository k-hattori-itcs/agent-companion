using System.Globalization;
using AgentCompanion.Services;
using Xunit;

namespace AgentCompanion.Tests;

public sealed class CodexStatusServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AgentCompanion.CodexStatusService.Tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Fact]
    public void Poll_ReadsPrimaryRateLimitResetFromTokenCount()
    {
        const long resetsAtUnixSeconds = 1785711518;
        var rolloutPath = Path.Combine(_root, "sessions", "2026", "07", "27", "rollout-test.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(rolloutPath)!);
        const string rolloutTemplate = """
            {"timestamp":"2026-07-27T12:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":32.0,"window_minutes":10080,"resets_at":RESET_VALUE},"secondary":null},"info":{"last_token_usage":{"total_tokens":1234}}}}
            """;
        File.WriteAllText(rolloutPath, rolloutTemplate.Replace("RESET_VALUE", resetsAtUnixSeconds.ToString(CultureInfo.InvariantCulture)));

        var snapshot = new CodexStatusService(_root).Poll();

        Assert.Equal(32, snapshot.TokenUsagePercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(resetsAtUnixSeconds), snapshot.TokenUsageResetsAt);
    }


    [Fact]
    public void Poll_UsesStatuslineResetTimesWhenUsageApiIsDisabled()
    {
        var claudeHome = Path.Combine(_root, ".claude");
        Directory.CreateDirectory(Path.Combine(claudeHome, "projects"));
        var fiveHourReset = DateTimeOffset.UtcNow.AddHours(2).ToString("O", CultureInfo.InvariantCulture);
        var weeklyReset = DateTimeOffset.UtcNow.AddDays(3).ToString("O", CultureInfo.InvariantCulture);
        var rateLimits = $$"""
            {
              "rate_limits": {
                "five_hour": { "used_percentage": 18, "resets_at": "{{fiveHourReset}}" },
                "seven_day": { "used_percentage": 51, "resets_at": "{{weeklyReset}}" }
              }
            }
            """;
        File.WriteAllText(Path.Combine(claudeHome, "agentcompanion-rate-limits.json"), rateLimits);

        var snapshot = new ClaudeStatusService(claudeHome).Poll(useUsageApi: false);

        Assert.Equal(18, snapshot.TokenUsagePercent);
        Assert.Equal(51, snapshot.SecondaryTokenUsagePercent);
        Assert.Equal(DateTimeOffset.Parse(fiveHourReset, CultureInfo.InvariantCulture), snapshot.TokenUsageResetsAt);
        Assert.Equal(DateTimeOffset.Parse(weeklyReset, CultureInfo.InvariantCulture), snapshot.SecondaryTokenUsageResetsAt);
    }
    [Fact]
    public void Poll_UsesRestoredApiCacheWithoutBackgroundApiRefresh()
    {
        var claudeHome = Path.Combine(_root, ".claude");
        var fetchedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var fiveHourReset = DateTimeOffset.UtcNow.AddMinutes(-20);
        var weeklyReset = DateTimeOffset.UtcNow.AddDays(3);
        var service = new ClaudeStatusService(claudeHome);

        service.RestoreCachedApiUsage(claudeHome, 18, fiveHourReset, 51, weeklyReset, fetchedAt);
        var snapshot = service.Poll(claudeHome, useUsageApi: false);

        Assert.Equal(18, snapshot.TokenUsagePercent);
        Assert.Equal(51, snapshot.SecondaryTokenUsagePercent);
        Assert.Equal(fiveHourReset, snapshot.TokenUsageResetsAt);
        Assert.Equal(weeklyReset, snapshot.SecondaryTokenUsageResetsAt);
    }
}
