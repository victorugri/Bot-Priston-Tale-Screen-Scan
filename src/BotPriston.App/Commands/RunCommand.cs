using BotPriston.Hosting;

namespace BotPriston.App.Commands;

/// <summary>Runs the bot loop: survival (potions) plus, unless disabled, the combat brain.</summary>
public static class RunCommand
{
    public static int Run(BotContext ctx, CommandLine cmd, CancellationToken cancel)
    {
        var backend = BotContext.ParseBackend(cmd.String("backend"));
        if (cmd.Flag("dry-run")) ctx.Config.Safety.DryRun = true;
        if (cmd.Flag("no-combat")) ctx.Config.Combat.Enabled = false;
        if (cmd.Flag("no-right-skill")) ctx.Config.Combat.RightSkill.Enabled = false;
        bool record = cmd.Flag("record");
        cmd.EnsureAllConsumed();

        using var session = BotSession.Start(ctx.Config, ctx.Log,
            new BotSessionOptions { Record = record, Backend = backend }, cancel);
        session.Wait();
        return session.Runner.StopReason is "quit hotkey" or "cancelled (Ctrl+C)" ? 0 : 1;
    }
}
