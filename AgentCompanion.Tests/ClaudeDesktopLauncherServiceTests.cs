using AgentCompanion.Services;
using Xunit;

namespace AgentCompanion.Tests;

public sealed class ClaudeDesktopLauncherServiceTests
{
    [Theory]
    [InlineData("ClaudeDesktop", "ClaudeDesktop")]
    [InlineData("claudedesktop", "ClaudeDesktop")]
    [InlineData("VSCode", "VSCode")]
    [InlineData("Codex", "Codex")]
    [InlineData("unknown", "Codex")]
    [InlineData(null, "Codex")]
    public void NormalizeLauncherTarget_ReturnsSupportedCanonicalValue(string? input, string expected)
    {
        Assert.Equal(expected, ClaudeDesktopLauncherService.NormalizeLauncherTarget(input));
    }

    [Fact]
    public void ClaudeDesktopAppUserModelId_UsesRegisteredWindowsDesktopIdentity()
    {
        Assert.Equal("Claude_pzs8sxrjxfjjc!Claude", ClaudeDesktopLauncherService.ClaudeDesktopAppUserModelId);
    }
}
