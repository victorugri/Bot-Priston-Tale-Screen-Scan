using BotPriston.Core.Bot;
using BotPriston.Core.Config;
using BotPriston.Core.Input;
using BotPriston.Core.Targeting;
using BotPriston.Core.Vision;
using BotPriston.Platform.Capture;
using BotPriston.Platform.Input;
using BotPriston.Platform.Window;
using Serilog;

namespace BotPriston.Hosting;

public sealed class BotSessionOptions
{
    /// <summary>Save every frame with the bot's state drawn on it (samples/record_*).</summary>
    public bool Record { get; init; }

    public CaptureBackend? Backend { get; init; }

    /// <summary>Put the game in the foreground right after starting (the UI has focus when Play is clicked).</summary>
    public bool BringGameToFront { get; init; }
}

/// <summary>
/// A running bot: finds the game, builds capture/vision/input/brain/runner and runs the loop on its own
/// thread, so the console or the UI stays responsive. Stop with <see cref="Stop"/>, the quit hotkey, or the token.
/// </summary>
public sealed class BotSession : IDisposable
{
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cancel;
    private readonly WindowCaptureSource _capture;
    private readonly VisionPipeline _vision;
    private readonly HotkeyMonitor _hotkeys;
    private readonly ILogger _log;

    private BotSession(BotConfig config, GameWindow window, WindowCaptureSource capture, VisionPipeline vision,
        BotRunner runner, BotControl control, HotkeyMonitor hotkeys, SessionRecorder? recorder,
        CancellationToken external, ILogger log)
    {
        Config = config;
        Window = window;
        _capture = capture;
        _vision = vision;
        Runner = runner;
        Control = control;
        _hotkeys = hotkeys;
        Recorder = recorder;
        _log = log;
        _cancel = CancellationTokenSource.CreateLinkedTokenSource(external);
        _thread = new Thread(RunLoop) { IsBackground = true, Name = "BotRunner" };
    }

    public BotConfig Config { get; }
    public GameWindow Window { get; }
    public BotRunner Runner { get; }
    public BotControl Control { get; }
    public SessionRecorder? Recorder { get; }
    public DateTimeOffset StartedAt { get; private set; }
    public bool IsRunning => _thread.IsAlive;

    /// <summary>Raised on the bot thread when the loop has ended (whatever the reason).</summary>
    public event Action<BotSession>? Exited;

    /// <summary>Builds everything and starts the loop. Throws with a readable message if the game isn't ready.</summary>
    public static BotSession Start(BotConfig config, ILogger log, BotSessionOptions? options = null, CancellationToken cancel = default)
    {
        options ??= new BotSessionOptions();
        var window = GameLocator.Find(config, log, requireExpectedSize: true);
        BotFactory.WarnIfInputBlocked(window, log);

        var capture = BotFactory.CreateCaptureSource(window, config.Capture, log, options.Backend);
        var vision = BotFactory.CreateVision(config);
        var input = BotFactory.CreateInput(window, config, log);
        var control = new BotControl();
        var hotkeys = new HotkeyMonitor(control,
            KeyChord.Parse(config.Hotkeys.PauseResume), KeyChord.Parse(config.Hotkeys.Quit), log);

        foreach (var (name, potion) in new[] { ("HP", config.Potions.Hp), ("MP", config.Potions.Mp), ("STM", config.Potions.Stm) })
        {
            log.Information("Potion {Name}: {State}, key {Key} below {Below}% (cooldown {Cooldown} ms)",
                name, potion.Enabled ? "on" : "off", potion.Key, potion.BelowPercent, potion.CooldownMs);
        }

        var runner = new BotRunner(config, capture, vision, input, window, control, TimeProvider.System, log,
            CreateBrain(config, input, capture, vision, log));

        SessionRecorder? recorder = null;
        if (options.Record)
        {
            var dir = Path.Combine(Path.GetFullPath(config.Paths.Samples), $"record_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(dir);
            recorder = new SessionRecorder(dir, runner, window);
            runner.AfterTick = recorder.Record;
            log.Information("Recording every frame to {Dir}", dir);
        }

        var session = new BotSession(config, window, capture, vision, runner, control, hotkeys, recorder, cancel, log);
        if (options.BringGameToFront)
            window.BringToFront();
        session.StartedAt = DateTimeOffset.Now;
        session._thread.Start();
        return session;
    }

    private static Func<Action, CombatBrain>? CreateBrain(BotConfig config, IInputSink input,
        WindowCaptureSource capture, VisionPipeline vision, ILogger log)
    {
        if (!config.Combat.Enabled)
        {
            log.Information("Combat off (Combat.Enabled = false): potions only");
            return null;
        }

        int width = config.Window.ExpectedClientWidth, height = config.Window.ExpectedClientHeight;
        var finder = new HoverTargetFinder(config.Targeting, width, height, input, capture, vision.Cursor, vision.TargetPanel, log);
        var ua = config.Combat.UnderAttack;
        HoverTargetFinder? wideFinder = ua.Enabled
            ? new HoverTargetFinder(config.Targeting.WithArea(ua.RadiusX, ua.RadiusY, ua.Step), width, height,
                input, capture, vision.Cursor, vision.TargetPanel, log)
            : null;
        log.Information("Combat on: hover sweep of {Points} points ({Wide}), right skill {RightSkill}, give up a target after {NoProgress}s without damage",
            finder.Points.Count,
            wideFinder is null ? "no wider search" : $"{wideFinder.Points.Count} when losing HP with nothing in reach",
            config.Combat.RightSkill.Enabled ? "on" : "off", config.Combat.NoProgressSeconds);

        return onProgress => new CombatBrain(config.Combat, finder, vision.Cursor, input, TimeProvider.System, log,
            onProgress, wideFinder);
    }

    private void RunLoop()
    {
        try
        {
            Runner.Run(_cancel.Token);
        }
        catch (Exception ex)
        {
            _log.Fatal(ex, "Bot loop crashed: {Message}", ex.Message);
        }
        finally
        {
            if (Recorder is not null)
                _log.Information("Recorded {Frames} frames to {Dir}", Recorder.Frames, Recorder.Directory);
            Exited?.Invoke(this);
        }
    }

    public void TogglePause() => Control.TogglePause();

    public void Stop(string reason = "stopped") => Control.RequestQuit(reason);

    /// <summary>Blocks until the loop has ended.</summary>
    public void Wait() => _thread.Join();

    public void Dispose()
    {
        if (_thread.IsAlive)
        {
            Stop("session disposed");
            _thread.Join(TimeSpan.FromSeconds(5));
        }
        _hotkeys.Dispose();
        _vision.Dispose();
        _capture.Dispose();
        _cancel.Dispose();
    }
}
