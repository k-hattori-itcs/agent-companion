using AgentCompanion.Services;
using Xunit;

namespace AgentCompanion.Tests;

public sealed class TokenRingTextFormatterTests
{
    [Fact]
    public void FormatClaudeUsage_IncludesProviderName()
    {
        var result = TokenRingTextFormatter.FormatClaudeUsage("5h 4%", "W 73%");

        Assert.Equal("Claude 5h 4% / W 73%", result);
    }

    [Fact]
    public void FormatResetLine_UsesDescriptiveJapaneseLabel()
    {
        var result = TokenRingTextFormatter.FormatResetLine(
            new DateTimeOffset(2026, 7, 27, 14, 50, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero),
            includeSecondary: true,
            TimeZoneInfo.Utc);

        Assert.Equal("\nリセット日時 7/27 14:50 / 7/28 0:00", result);
    }

    [Fact]
    public void FormatResetLine_ShowsMissingSideAsNotAvailable()
    {
        var result = TokenRingTextFormatter.FormatResetLine(
            null,
            new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero),
            includeSecondary: true,
            TimeZoneInfo.Utc);

        Assert.Equal("\nリセット日時 未取得 / 7/28 0:00", result);
    }

    [Fact]
    public void FormatResetLine_ReturnsEmptyWhenNoResetIsAvailable()
    {
        var result = TokenRingTextFormatter.FormatResetLine(
            null,
            null,
            includeSecondary: false,
            TimeZoneInfo.Utc);

        Assert.Equal(string.Empty, result);
    }
}
