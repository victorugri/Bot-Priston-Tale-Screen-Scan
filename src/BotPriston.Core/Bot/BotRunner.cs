using BotPriston.Core.Capture;
using BotPriston.Core.Config;
using BotPriston.Core.Input;
using BotPriston.Core.Vision;
using OpenCvSharp;
using Serilog;

namespace BotPriston.Core.Bot;

public enum RunnerState
{
    Starting,
    Running,
    /// <summary>Paused by the user (hotkey).</summary>
    Paused,
    /// <summary>Paused automatically because the game is not the foreground window.</summary>
    FocusLost,
    /// <summary>Paused automatically because the client area is not the configured size (window resized/snapped).</summary>
    WrongWindowSize,
    /// <summary>The game is up but the HUD isn't (map open, loading, death, disconnect): perceive only.</summary>
    HudHidden,
    Stopped,
}

/// <summary>
/// The perceive → decide → act loop. Safety checks run first on every tick; nothing is sent
/// unless the bot is running, the game has focus and the HUD is visible. Survival (potions) runs
/// before any other behavior.
/// </summary>
public sealed class BotRunner
{
    private readonly ICaptureSource _capture;
    private readonly IVision _vision;
    private readonly IInputSink _input;
    private readonly IGameWindowState _window;
    private readonly BotControl _control;
    private readonly PotionManager _potions;
    private readonly Watchdog _progressWatchdog;
    private readonly Watchdog _hudWatchdog;
    private readonly BotConfig _config;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly CombatBrain? _brain;
    private CancellationToken _runCancel = CancellationToken.None;
    private DateTimeOffset? _hpEmptySince;
    private readonly MotionDetector? _motion;
    private DateTimeOffset? _walkingSince;
    private double _walkDx, _walkDy;

    /// <param name="brainFactory">
    /// Builds the combat brain, given the callback it must call on progress (damage dealt, kill).
    /// Null = survival only (potions).
    /// </param>
    public BotRunner(BotConfig config, ICaptureSource capture, IVision vision, IInputSink input,
        IGameWindowState window, BotControl control, TimeProvider time, ILogger log,
        Func<Action, CombatBrain>? brainFactory = null)
    {
        _config = config;
        _capture = capture;
        _vision = vision;
        _input = input;
        _window = window;
        _control = control;
        _time = time;
        _log = log;
        _potions = new PotionManager(config.Potions, time, log);
        _progressWatchdog = new Watchdog(TimeSpan.FromSeconds(config.Safety.WatchdogSeconds), time);
        _hudWatchdog = new Watchdog(TimeSpan.FromSeconds(config.Safety.HudLostStopSeconds), time);
        _brain = brainFactory?.Invoke(_progressWatchdog.Kick);
        if (_brain is not null)
            _brain.PotionAvailable = _potions.IsAvailable;
        if (config.Safety.StopWalking && config.Vision.Motion.Boxes.Count > 0)
            _motion = new MotionDetector(config.Vision.Motion);
    }

    /// <summary>Times the character was caught walking.</summary>
    public int WalkEpisodes { get; private set; }

    public CombatBrain? Brain => _brain;

    /// <summary>Called at the end of every tick that saw the game (frame + what was perceived), e.g. to record a session.</summary>
    public Action<Mat, VisionSnapshot>? AfterTick { get; set; }
    public RunnerState State { get; private set; } = RunnerState.Starting;
    public string? StopReason { get; private set; }
    public VisionSnapshot? LastSnapshot { get; private set; }

    public void Run(CancellationToken cancel)
    {
        var tick = TimeSpan.FromMilliseconds(_config.Bot.TickMs);
        _runCancel = cancel;
        _log.Information("Bot started (combat: {Combat}, dry-run: {DryRun}). {Pause} pause/resume, {Quit} quit",
            _brain is not null, _config.Safety.DryRun, _config.Hotkeys.PauseResume, _config.Hotkeys.Quit);
        try
        {
            while (State != RunnerState.Stopped)
            {
                if (cancel.IsCancellationRequested)
                {
                    Stop("cancelled (Ctrl+C)");
                    break;
                }

                var started = _time.GetUtcNow();
                Tick();
                var remaining = tick - (_time.GetUtcNow() - started);
                if (remaining > TimeSpan.Zero && State != RunnerState.Stopped)
                    cancel.WaitHandle.WaitOne(remaining);
            }
        }
        finally
        {
            _input.ReleaseAll();
            _log.Information("Bot stopped: {Reason}", StopReason ?? "unknown");
            if (_brain is not null)
                _log.Information("Session: {Summary}", _brain.Summary);
            _motion?.Dispose();
        }
    }

    /// <summary>One cycle. Public so tests can drive the loop step by step.</summary>
    public void Tick()
    {
        if (State == RunnerState.Stopped) return;

        if (_control.QuitRequested) { Stop("quit hotkey"); return; }
        if (!_window.Exists) { Stop("game window closed"); return; }
        if (_control.Paused) { Enter(RunnerState.Paused, "pause hotkey"); return; }
        if (!_window.IsForeground || _window.IsMinimized) { Enter(RunnerState.FocusLost, "game lost focus"); return; }
        if (_window.ClientSize != (_config.Window.ExpectedClientWidth, _config.Window.ExpectedClientHeight))
        {
            Enter(RunnerState.WrongWindowSize, $"client area is {_window.ClientSize.Width}x{_window.ClientSize.Height}, " +
                $"expected {_config.Window.ExpectedClientWidth}x{_config.Window.ExpectedClientHeight} (run 'layout')");
            return;
        }

        using var frame = _capture.Grab();
        if (frame is null)
        {
            // A missing frame is treated like a hidden HUD so a dead capture ends up stopping the bot.
            CheckHudLost("no frame captured");
            return;
        }

        var snapshot = _vision.Analyze(frame.Image);
        LastSnapshot = snapshot;
        try
        {
            TickWithFrame(frame.Image, snapshot);
        }
        finally
        {
            AfterTick?.Invoke(frame.Image, snapshot);
        }
    }

    private void TickWithFrame(Mat image, VisionSnapshot snapshot)
    {
        if (!snapshot.Hud.Visible || snapshot.Bars is null)
        {
            CheckHudLost("HUD not visible");
            return;
        }

        _hudWatchdog.Kick();
        Enter(RunnerState.Running, "HUD visible, game focused");

        if (_progressWatchdog.Expired)
        {
            Stop($"no progress for {_progressWatchdog.Timeout.TotalSeconds:F0}s");
            return;
        }

        if (CharacterDied(snapshot.Bars))
            return;

        if (CheckWalking(image))
            return;

        // Survival first: a potion pre-empts anything else this tick.
        if (_potions.Decide(snapshot.Bars) is { } potion)
        {
            if (_input.PressKey(_potions.KeyFor(potion)))
                _potions.MarkUsed(potion, snapshot.Bars);
            return;
        }

        if (_brain is null)
        {
            // Survival-only mode: being alive with the HUD up counts as progress.
            _progressWatchdog.Kick();
            return;
        }

        using var interrupt = CancellationTokenSource.CreateLinkedTokenSource(_runCancel, _control.Interrupt);
        _brain.Tick(snapshot, image, interrupt.Token);
    }

    /// <summary>
    /// The character must never walk. When the ground keeps sliding (it walks), force both mouse buttons
    /// up, make the brain drop its target, and don't act until it stands still again. Returns true while walking.
    /// </summary>
    private bool CheckWalking(Mat image)
    {
        if (_motion is null) return false;
        var motion = _motion.Update(image);

        // Chasing a distant attacker found by the wider search: walking is allowed for that one target.
        if (_brain?.AllowWalking == true)
        {
            _walkingSince = null;
            return false;
        }

        if (_walkingSince is { } since)
        {
            if (motion.Moving)
            {
                _walkDx += motion.ShiftX;
                _walkDy += motion.ShiftY;
                _input.ForceReleaseButtons();
                return true;
            }
            _log.Information("Character stopped walking after {Ms:F0} ms (ground moved ~{Distance:F0} px)",
                (_time.GetUtcNow() - since).TotalMilliseconds, Math.Sqrt(_walkDx * _walkDx + _walkDy * _walkDy));
            _walkingSince = null;
            return false;
        }

        if (!motion.Walking) return false;

        _input.ForceReleaseButtons();
        WalkEpisodes++;
        _walkingSince = _time.GetUtcNow();
        _walkDx = motion.ShiftX;
        _walkDy = motion.ShiftY;
        var context = _brain is null
            ? "no combat"
            : $"brain {_brain.State}, held [{(_brain.HoldingLeft ? "L" : "")}{(_brain.HoldingRight ? "R" : "")}], aim {_brain.Aim}";
        _log.Warning("Character is WALKING ({Motion}) while {Context}: buttons forced up, target dropped", motion, context);
        _brain?.OnWalkingDetected(context);
        return true;
    }

    /// <summary>An empty HP bar for a while means the character is dead: stop before doing anything silly.</summary>
    private bool CharacterDied(PlayerBars bars)
    {
        if (bars.Hp.FilledRows > 0)
        {
            _hpEmptySince = null;
            return false;
        }

        var now = _time.GetUtcNow();
        _hpEmptySince ??= now;
        if (now - _hpEmptySince < TimeSpan.FromSeconds(_config.Safety.DeadHpSeconds))
            return false;

        Stop("the character's HP bar is empty: character died");
        return true;
    }

    private void CheckHudLost(string why)
    {
        Enter(RunnerState.HudHidden, why);
        if (_hudWatchdog.Expired)
            Stop($"{why} for {_hudWatchdog.Timeout.TotalSeconds:F0}s (death, disconnect or a full-screen window?)");
    }

    private void Enter(RunnerState next, string reason)
    {
        if (State == next) return;

        // While not running, the user must not end up with a held button.
        if (next != RunnerState.Running)
        {
            _input.ReleaseAll();
            _brain?.Suspend(reason);
        }

        // Paused/unfocused time doesn't count against the watchdogs.
        if (State is RunnerState.Paused or RunnerState.FocusLost or RunnerState.WrongWindowSize)
        {
            _progressWatchdog.Kick();
            _hudWatchdog.Kick();
        }

        _log.Information("State {From} -> {To} ({Reason})", State.ToString(), next.ToString(), reason);
        State = next;

        // Back in control with the game focused: make sure the game doesn't think a button is still
        // held (an "up" sent while it had no focus may have been lost), and restart motion history.
        if (next == RunnerState.Running)
        {
            _input.ForceReleaseButtons();
            _motion?.Reset();
            _walkingSince = null;
        }
    }

    private void Stop(string reason)
    {
        StopReason = reason;
        Enter(RunnerState.Stopped, reason);
    }
}
