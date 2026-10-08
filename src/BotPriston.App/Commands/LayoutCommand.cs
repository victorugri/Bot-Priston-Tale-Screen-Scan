using BotPriston.Platform.Window;

namespace BotPriston.App.Commands;

/// <summary>layout [--left]: restore the game's client size and align it to the top-right (or top-left) of the screen.</summary>
public static class LayoutCommand
{
    public static int Run(BotContext ctx, CommandLine cmd)
    {
        var anchor = cmd.Flag("left") ? WindowAnchor.TopLeft : WindowAnchor.TopRight;
        cmd.EnsureAllConsumed();

        var window = ctx.FindGameWindow();
        ctx.WarnIfInputBlocked(window);
        var (ok, message) = WindowLayout.Apply(window,
            ctx.Config.Window.ExpectedClientWidth, ctx.Config.Window.ExpectedClientHeight, anchor);

        if (ok) ctx.Log.Information("{Message}", message);
        else ctx.Log.Error("{Message}", message);
        return ok ? 0 : 1;
    }
}
