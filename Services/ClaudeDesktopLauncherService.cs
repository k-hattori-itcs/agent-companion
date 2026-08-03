using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace AgentCompanion.Services;

public sealed class ClaudeDesktopLauncherService
{
    private const int SW_RESTORE = 9;
    internal const string ClaudeDesktopAppUserModelId = "Claude_pzs8sxrjxfjjc!Claude";

    public void OpenOrFocus()
    {
        if (TryFocusClaudeDesktopWindow())
            return;

        if (!TryLaunchClaudeDesktopApp())
        {
            AppLogger.Warning("Claude Desktop could not be launched. Confirm that the Windows desktop app is installed.");
            return;
        }

        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 32; i++)
            {
                await Task.Delay(250).ConfigureAwait(false);
                if (TryFocusClaudeDesktopWindow())
                    break;
            }
        });
    }

    internal static string NormalizeLauncherTarget(string? launcherTarget)
    {
        if (string.Equals(launcherTarget, "VSCode", StringComparison.OrdinalIgnoreCase))
            return "VSCode";
        if (string.Equals(launcherTarget, "ClaudeDesktop", StringComparison.OrdinalIgnoreCase))
            return "ClaudeDesktop";
        return "Codex";
    }

    private static bool TryLaunchClaudeDesktopApp()
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
            startInfo.ArgumentList.Add($"shell:AppsFolder\\{ClaudeDesktopAppUserModelId}");
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
        var processIds = Process.GetProcessesByName("Claude")
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
