using BotPriston.Core.Geometry;
using BotPriston.Core.Input;
using BotPriston.Platform.Window;

namespace BotPriston.App.Commands;

/// <summary>
/// Manual input checks: does the game react to our keys and mouse? Both wait until the user
/// puts the game in focus (input is never sent to another window).
/// </summary>
public static class InputTestCommands
{
    /// <summary>press --key 2 [--count n] [--wait s]</summary>
    public static int Press(BotContext ctx, CommandLine cmd, CancellationToken cancel)
    {
        var key = KeyChord.Parse(cmd.String("key") ?? throw new UsageException("press needs --key, e.g. --key 2"));
        int count = cmd.Int("count", 1, min: 1);
        int waitSeconds = cmd.Int("wait", 15, min: 1);
        if (cmd.Flag("dry-run")) ctx.Config.Safety.DryRun = true;
        cmd.EnsureAllConsumed();

        var window = ctx.FindGameWindow();
        ctx.WarnIfInputBlocked(window);
        var input = ctx.CreateInput(window);
        if (!WaitForFocus(ctx, window, waitSeconds, cancel)) return 1;

        int sent = 0;
        for (int i = 0; i < count && !cancel.IsCancellationRequested; i++)
            if (input.PressKey(key)) sent++;

        ctx.Log.Information("Sent {Sent}/{Count} press(es) of {Key}. Check in the game whether it reacted.", sent, count, key);
        return sent == count ? 0 : 1;
    }

    /// <summary>mouse --x 800 --y 450 [--click left|right] [--hold ms] [--wait s]</summary>
    public static int Mouse(BotContext ctx, CommandLine cmd, CancellationToken cancel)
    {
        int x = cmd.Int("x", -1, min: 0);
        int y = cmd.Int("y", -1, min: 0);
        if (x < 0 || y < 0) throw new UsageException("mouse needs --x and --y (client-area pixels).");
        var clickText = cmd.String("click");
        MouseButton? button = clickText?.ToLowerInvariant() switch
        {
            null => null,
            "left" => MouseButton.Left,
            "right" => MouseButton.Right,
            _ => throw new UsageException("--click must be left or right."),
        };
        int holdMs = cmd.Int("hold", 80, min: 0);
        int waitSeconds = cmd.Int("wait", 15, min: 1);
        if (cmd.Flag("dry-run")) ctx.Config.Safety.DryRun = true;
        cmd.EnsureAllConsumed();

        var window = ctx.FindGameWindow(requireExpectedSize: true);
        ctx.WarnIfInputBlocked(window);
        var input = ctx.CreateInput(window);
        if (!WaitForFocus(ctx, window, waitSeconds, cancel)) return 1;

        var point = new PixelPoint(x, y);
        if (!input.MoveMouse(point)) return 1;
        ctx.Log.Information("Mouse moved to client {Point}", point);

        if (button is { } b)
        {
            try
            {
                if (!input.MouseDown(b)) return 1;
                cancel.WaitHandle.WaitOne(holdMs);
            }
            finally
            {
                input.MouseUp(b);
            }
            ctx.Log.Information("{Button} click held {Hold} ms", b, holdMs);
        }
        return 0;
    }

    private static bool WaitForFocus(BotContext ctx, GameWindow window, int seconds, CancellationToken cancel) =>
        ctx.WaitForFocus(window, seconds, cancel);
}
