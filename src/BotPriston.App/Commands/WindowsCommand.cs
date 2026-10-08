using BotPriston.Platform.Window;

namespace BotPriston.App.Commands;

/// <summary>Lists top-level windows so the user can fill in the Window section of the config.</summary>
public static class WindowsCommand
{
    public static int Run(BotContext ctx, CommandLine cmd)
    {
        cmd.EnsureAllConsumed();

        var windows = WindowFinder.ListTopLevelWindows();
        Console.WriteLine($"{"Process",-24} {"PID",7}  {"Client",-11} {"Match",-5}  Title");
        foreach (var w in windows.OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            var client = w.ClientScreenRect;
            var size = w.IsMinimized ? "minimized" : $"{client.Width}x{client.Height}";
            var match = WindowFinder.Matches(w, ctx.Config.Window) ? "  *" : "";
            Console.WriteLine($"{Truncate(w.ProcessName, 24),-24} {w.ProcessId,7}  {size,-11} {match,-5}  {w.Title}");
        }

        Console.WriteLine();
        Console.WriteLine($"'*' = matches the current config (ProcessName='{ctx.Config.Window.ProcessName}', TitleContains='{ctx.Config.Window.TitleContains}').");
        return 0;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
