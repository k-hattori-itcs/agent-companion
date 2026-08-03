namespace AgentCompanion.Services;

public static class LauncherTargets
{
    public const string Codex = "Codex";
    public const string VSCode = "VSCode";
    public const string ClaudeDesktop = "ClaudeDesktop";
    public const string ClaudeDesktopDisplayName = "Claude Desk";
    public const int CurrentConfigurationVersion = 1;

    public static string Normalize(string? launcherTarget)
    {
        if (string.Equals(launcherTarget, VSCode, StringComparison.OrdinalIgnoreCase))
            return VSCode;
        if (string.Equals(launcherTarget, ClaudeDesktop, StringComparison.OrdinalIgnoreCase))
            return ClaudeDesktop;
        return Codex;
    }

    public static string MigrateLegacyTarget(string? statusProvider, string? launcherTarget, int configurationVersion)
    {
        var normalizedTarget = Normalize(launcherTarget);
        return configurationVersion < CurrentConfigurationVersion
            && string.Equals(statusProvider, "Claude", StringComparison.OrdinalIgnoreCase)
            && normalizedTarget == Codex
            ? VSCode
            : normalizedTarget;
    }

    public static string GetDisplayName(string? launcherTarget)
    {
        return Normalize(launcherTarget) == ClaudeDesktop
            ? ClaudeDesktopDisplayName
            : Normalize(launcherTarget);
    }

    public static string FromDisplayName(string? displayName)
    {
        return string.Equals(displayName, ClaudeDesktopDisplayName, StringComparison.Ordinal)
            ? ClaudeDesktop
            : Normalize(displayName);
    }
}
