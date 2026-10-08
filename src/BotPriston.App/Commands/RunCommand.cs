using BotPriston.Core.Bot;
using BotPriston.Core.Input;
using BotPriston.Core.Targeting;
using BotPriston.Platform.Input;

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
            int width = config.Window.ExpectedClientWidth, height = config.Window.ExpectedClientHeight;
            var finder = new HoverTargetFinder(config.Targeting, width, height, input, capture, vision.Cursor, vision.TargetPanel, ctx.Log);
            var ua = config.Combat.UnderAttack;
            HoverTargetFinder? wideFinder = ua.Enabled
                ? new HoverTargetFinder(config.Targeting.WithArea(ua.RadiusX, ua.RadiusY, ua.Step), width, height,
                    input, capture, vision.Cursor, vision.TargetPanel, ctx.Log)
                : null;
            brain = onProgress => new CombatBrain(config.Combat, finder, vision.Cursor, input, TimeProvider.System, ctx.Log,
                onProgress, wideFinder);
            ctx.Log.Information("Combat on: hover sweep of {Points} points ({Wide}), right skill {RightSkill}, give up a target after {NoProgress}s without damage",
                finder.Points.Count,
                wideFinder is null ? "no wider search" : $"{wideFinder.Points.Count} when losing HP with nothing in reach",
                config.Combat.RightSkill.Enabled ? "on" : "off", config.Combat.NoProgressSeconds);
        }
        else
        {
            ctx.Log.Information("Combat off (Combat.Enabled = false): potions only");
        }

        var runner = new BotRunner(config, capture, vision, input, window, control, TimeProvider.System, ctx.Log, brain);

        SessionRecorder? recorder = null;
        if (record)
        {
            var dir = Path.Combine(ctx.SamplesDir, $"record_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(dir);
            recorder = new SessionRecorder(dir, runner, window);
            runner.AfterTick = recorder.Record;
            ctx.Log.Information("Recording every frame to {Dir}", dir);
        }

        runner.Run(cancel);
        if (recorder is not null)
            ctx.Log.Information("Recorded {Frames} frames to {Dir}", recorder.Frames, recorder.Directory);
        return runner.StopReason is "quit hotkey" or "cancelled (Ctrl+C)" ? 0 : 1;
    }
}
