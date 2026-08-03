using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace AgentCompanion.Services;

public sealed class ClaudeDesktopLauncherService
{
    private const int SW_RESTORE = 9;
    private const int LaunchVerificationAttempts = 32;
    private const int LaunchVerificationDelayMilliseconds = 250;
    private const int StartAppsQueryTimeoutMilliseconds = 3_000;
    private const string ClaudeAumidQuery = "Get-StartApps | Where-Object { $_.AppID -match '^Claude_[A-Za-z0-9]+![A-Za-z0-9._-]+$' } | Select-Object -First 1 -ExpandProperty AppID";
    private static readonly Regex ClaudeAumidPattern = new(
        "^Claude_[A-Za-z0-9]+![A-Za-z0-9._-]+$",
        RegexOptions.CultureInvariant);

    public void OpenOrFocus(Action<string> reportFailure)
    {
        ArgumentNullException.ThrowIfNull(reportFailure);

        if (TryFocusClaudeDesktopWindow())
            return;

        var appUserModelId = FindClaudeDesktopAppUserModelId();
        if (appUserModelId == null)
        {
            ReportFailure(reportFailure, "Claude Desktop のインストールを確認してください。");
            return;
        }

        if (!TryLaunchClaudeDesktopApp(appUserModelId))
        {
            ReportFailure(reportFailure, "Claude Desktop を起動できませんでした。アプリの再インストールを確認してください。");
            return;
        }

        _ = Task.Run(async () =>
        {
            for (var i = 0; i < LaunchVerificationAttempts; i++)
            {
                await Task.Delay(LaunchVerificationDelayMilliseconds).ConfigureAwait(false);
                if (TryFocusClaudeDesktopWindow())
                    return;
            }

            ReportFailure(reportFailure, "Claude Desktop を起動または前面表示できませんでした。アプリを手動で起動してから、もう一度試してください。");
        });
    }

    internal static string? ExtractClaudeDesktopAppUserModelId(string? startAppsOutput)
    {
        if (string.IsNullOrWhiteSpace(startAppsOutput))
            return null;

        return startAppsOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(appUserModelId => ClaudeAumidPattern.IsMatch(appUserModelId));
    }

    private static string? FindClaudeDesktopAppUserModelId()
    {
        var powerShellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powerShellPath))
        {
            AppLogger.Warning("Windows PowerShell was not found while looking up Claude Desktop.");
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = powerShellPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(ClaudeAumidQuery);

            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(StartAppsQueryTimeoutMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                AppLogger.Warning("Claude Desktop lookup timed out.");
                return null;
            }

            Task.WaitAll(outputTask, errorTask);
            return process.ExitCode == 0 ? ExtractClaudeDesktopAppUserModelId(outputTask.Result) : null;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Claude Desktop lookup failed.", ex);
            return null;
        }
    }

    private static bool TryLaunchClaudeDesktopApp(string appUserModelId)
    {
        try
        {
            var explorerPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "explorer.exe");
            if (!File.Exists(explorerPath))
                return false;

            var startInfo = new ProcessStartInfo
            {
                FileName = explorerPath,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add($"shell:AppsFolder\\{appUserModelId}");
            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Claude Desktop launch failed.", ex);
            return false;
        }
    }

    private static bool TryFocusClaudeDesktopWindow()
    {
        var processIds = Process.GetProcesses()
            .Where(IsClaudeDesktopProcess)
            .Select(process => process.Id)
            .ToHashSet();
        if (processIds.Count == 0)
            return false;

        var handle = FindBestWindow(processIds);
        if (handle == IntPtr.Zero)
            return false;

        ShowWindow(handle, SW_RESTORE);
        SetForegroundWindow(handle);
        return true;
    }

    private static bool IsClaudeDesktopProcess(Process process)
    {
        try
        {
            if (process.ProcessName.Equals("Claude", StringComparison.OrdinalIgnoreCase))
                return true;

            var path = process.MainModule?.FileName ?? string.Empty;
            return path.Contains("\\WindowsApps\\Claude_", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static IntPtr FindBestWindow(HashSet<int> processIds)
    {
        var fallback = IntPtr.Zero;
        var titled = IntPtr.Zero;

        EnumWindows((handle, _) =>
        {
            var windowThreadId = GetWindowThreadProcessId(handle, out var windowProcessId);
            if (windowThreadId == 0 || !processIds.Contains(windowProcessId) || !IsWindowVisible(handle))
                return true;

            if (fallback == IntPtr.Zero)
                fallback = handle;
            if (GetWindowTextLength(handle) > 0)
            {
                titled = handle;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return titled != IntPtr.Zero ? titled : fallback;
    }

    private static void ReportFailure(Action<string> reportFailure, string message)
    {
        AppLogger.Warning(message);
        reportFailure(message);
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);
}
