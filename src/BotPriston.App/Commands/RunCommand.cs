using BotPriston.Core.Bot;
using BotPriston.Core.Input;
using BotPriston.Core.Targeting;
using BotPriston.Platform.Input;

namespace BotPriston.App.Commands;

/// <summary>Runs the bot loop. Stage 3: survival only (potions), with all safety mechanisms.</summary>
public static class RunCommand
{
    public static int Run(BotContext ctx, CommandLine cmd, CancellationToken cancel)
    {
        var backend = BotContext.ParseBackend(cmd.String("backend"));
        if (cmd.Flag("dry-run")) ctx.Config.Safety.DryRun = true;
        if (cmd.Flag("no-combat")) ctx.Config.Combat.Enabled = false;
        cmd.EnsureAllConsumed();

        var config = ctx.Config;
        var window = ctx.FindGameWindow(requireExpectedSize: true);
        ctx.WarnIfInputBlocked(window);

        using var capture = ctx.CreateCaptureSource(window, backend);
        using var vision = ctx.CreateVision();
        var input = ctx.CreateInput(window);
        var control = new BotControl();
        using var hotkeys = new HotkeyMonitor(control,
            KeyChord.Parse(config.Hotkeys.PauseResume), KeyChord.Parse(config.Hotkeys.Quit), ctx.Log);

        foreach (var (name, potion) in new[] { ("HP", config.Potions.Hp), ("MP", config.Potions.Mp), ("STM", config.Potions.Stm) })
        {
            ctx.Log.Information("Potion {Name}: {State}, key {Key} below {Below}% (cooldown {Cooldown} ms)",
                name, potion.Enabled ? "on" : "off", potion.Key, potion.BelowPercent, potion.CooldownMs);
        }

        Func<Action, CombatBrain>? brain = null;
        if (config.Combat.Enabled)
        {
            var finder = new HoverTargetFinder(config.Targeting, config.Window.ExpectedClientWidth, config.Window.ExpectedClientHeight,
                input, capture, vision.Cursor, vision.TargetPanel, ctx.Log);
            brain = onProgress => new CombatBrain(config.Combat, finder, vision.Cursor, input, TimeProvider.System, ctx.Log, onProgress);
            ctx.Log.Information("Combat on: hover sweep of {Points} points, give up a target after {NoProgress}s without damage",
                finder.Points.Count, config.Combat.NoProgressSeconds);
        }
        else
        {
            ctx.Log.Information("Combat off (Combat.Enabled = false): potions only");
        }

        var runner = new BotRunner(config, capture, vision, input, window, control, TimeProvider.System, ctx.Log, brain);
        runner.Run(cancel);
        return runner.StopReason is "quit hotkey" or "cancelled (Ctrl+C)" ? 0 : 1;
    }
}
