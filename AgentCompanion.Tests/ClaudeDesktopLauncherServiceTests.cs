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
    public void Normalize_ReturnsSupportedCanonicalValue(string? input, string expected)
    {
        Assert.Equal(expected, LauncherTargets.Normalize(input));
    }

    [Theory]
    [InlineData("Claude_pzs8sxrjxfjjc!Claude\r\n", "Claude_pzs8sxrjxfjjc!Claude")]
    [InlineData("Other_abc!App\nClaude_abc123!Claude\n", "Claude_abc123!Claude")]
    [InlineData("Claude_abc!Invalid Value", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractClaudeDesktopAppUserModelId_ReturnsOnlyValidClaudeAumid(string? output, string? expected)
    {
        Assert.Equal(expected, ClaudeDesktopLauncherService.ExtractClaudeDesktopAppUserModelId(output));
    }

    [Fact]
    public void MigrateLegacyTarget_MapsClaudeMonitoringFromCodexToVSCode()
    {
        var migrated = LauncherTargets.MigrateLegacyTarget("Claude", "Codex", configurationVersion: 0);

        Assert.Equal(LauncherTargets.VSCode, migrated);
    }

    [Fact]
    public void MigrateLegacyTarget_PreservesAnExplicitCurrentTarget()
    {
        var target = LauncherTargets.MigrateLegacyTarget("Claude", "Codex", LauncherTargets.CurrentConfigurationVersion);

        Assert.Equal(LauncherTargets.Codex, target);
    }
}
