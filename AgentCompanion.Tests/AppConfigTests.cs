using AgentCompanion.Services;
using Xunit;

namespace AgentCompanion.Tests;

public sealed class AppConfigTests
{
    [Fact]
    public void ClaudeUsageApi_IsDisabledByDefault()
    {
        var config = new AppConfig();

        Assert.False(config.ClaudeUsageApiEnabled);
    }

    [Fact]
    public void ClaudeUsageApi_NextAttemptIsNotScheduledByDefault()
    {
        var config = new AppConfig();

        Assert.Null(config.ClaudeUsageApiNextAttemptAt);
    }
}
