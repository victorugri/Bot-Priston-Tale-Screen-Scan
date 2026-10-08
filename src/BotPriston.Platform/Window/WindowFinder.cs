using System.Diagnostics;
using BotPriston.Core.Config;
using BotPriston.Platform.Native;

namespace BotPriston.Platform.Window;

public static class WindowFinder
{
    /// <summary>Visible, non-tool, non-cloaked top-level windows with a non-empty client area.</summary>
    public static IReadOnlyList<GameWindow> ListTopLevelWindows()
    {
        var handles = new List<IntPtr>();
        User32.EnumWindows((hwnd, _) =>
        {
            handles.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        int ownPid = Environment.ProcessId;
        var processNames = new Dictionary<uint, string>();
        var result = new List<GameWindow>();

        foreach (var hwnd in handles)
        {
            if (!User32.IsWindowVisible(hwnd)) continue;
            if ((User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE).ToInt64() & User32.WS_EX_TOOLWINDOW) != 0) continue;
            if (Dwm.DwmGetWindowAttribute(hwnd, Dwm.DWMWA_CLOAKED, out int cloaked, sizeof(int)) >= 0 && cloaked != 0) continue;

            User32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownPid) continue;

            if (!processNames.TryGetValue(pid, out var processName))
            {
                processName = TryGetProcessName(pid);
                processNames[pid] = processName;
            }

            var window = new GameWindow(hwnd, (int)pid, processName, User32.GetWindowText(hwnd));
            if (!window.IsMinimized && window.ClientScreenRect.IsEmpty) continue;
            result.Add(window);
        }

        return result;
    }

    /// <summary>
    /// Windows matching every non-empty filter in <paramref name="config"/>, largest client area first.
    /// </summary>
    public static IReadOnlyList<GameWindow> FindMatches(WindowConfig config) =>
        ListTopLevelWindows()
            .Where(w => Matches(w, config))
            .OrderByDescending(w => (long)w.ClientScreenRect.Width * w.ClientScreenRect.Height)
            .ToList();

    public static bool Matches(GameWindow window, WindowConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.ProcessName))
        {
            var wanted = config.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? config.ProcessName[..^4]
                : config.ProcessName;
            if (!string.Equals(window.ProcessName, wanted, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(config.TitleContains) &&
            !window.Title.Contains(config.TitleContains, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static string TryGetProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return "?";
        }
    }
}
