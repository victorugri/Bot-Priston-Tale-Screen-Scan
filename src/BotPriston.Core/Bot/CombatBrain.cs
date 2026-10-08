using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using BotPriston.Core.Input;
using BotPriston.Core.Targeting;
using BotPriston.Core.Vision;
using OpenCvSharp;
using Serilog;

namespace BotPriston.Core.Bot;

public enum BrainState
{
    /// <summary>Between fights: rest until HP/MP are back if they are low.</summary>
    Recover,
    /// <summary>Hover sweep looking for a monster.</summary>
    SearchTarget,
    /// <summary>Mouse is on a monster: press the attack button.</summary>
    Engage,
    /// <summary>Holding the attack button, following the target until its HP bar is empty.</summary>
    Attack,
    /// <summary>Pick up drops (not implemented; skipped).</summary>
    Loot,
}

/// <summary>
/// Combat state machine: Recover → SearchTarget → Engage → Attack → (Loot) → Recover.
/// Every transition is logged. The brain only decides; safety (pause, focus, HUD, potions) is the
/// runner's job, which calls <see cref="Suspend"/> whenever the bot stops acting.
/// </summary>
public sealed class CombatBrain
{
    private readonly CombatConfig _config;
    private readonly ITargetFinder _finder;
    private readonly ICursorReader _cursor;
    private readonly IInputSink _input;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly Action _onProgress;

    private PixelPoint? _aim;
    private bool _holding;
    private bool _seenAlive;
    private double? _lastTargetHp;
    private DateTimeOffset _targetSince, _lastDamage, _nextSearchAt;
    private DateTimeOffset? _cursorLostSince;
    private DateTimeOffset? _restingSince;
    private readonly List<TimeSpan> _killTimes = [];

    public CombatBrain(CombatConfig config, ITargetFinder finder, ICursorReader cursor, IInputSink input,
        TimeProvider time, ILogger log, Action onProgress)
    {
        _config = config;
        _finder = finder;
        _cursor = cursor;
        _input = input;
        _time = time;
        _log = log;
        _onProgress = onProgress;
    }

    public BrainState State { get; private set; } = BrainState.Recover;
    public int Kills => _killTimes.Count;
    public int GivenUp { get; private set; }
    public PixelPoint? Aim => _aim;

    public string Summary => Kills == 0
        ? $"0 kills, {GivenUp} targets given up"
        : $"{Kills} kills (avg {_killTimes.Average(t => t.TotalSeconds):F1}s each), {GivenUp} targets given up";

    /// <summary>One decision. <paramref name="frame"/> is the image <paramref name="snapshot"/> came from.</summary>
    public void Tick(VisionSnapshot snapshot, Mat frame, CancellationToken interrupt)
    {
        if (snapshot.Bars is null) return; // runner only calls with the HUD visible; be defensive anyway

        switch (State)
        {
            case BrainState.Recover: TickRecover(snapshot.Bars); break;
            case BrainState.SearchTarget: TickSearch(interrupt); break;
            case BrainState.Engage: TickEngage(); break;
            case BrainState.Attack: TickAttack(snapshot, frame, interrupt); break;
            case BrainState.Loot: Go(BrainState.Recover, "looting is disabled"); break;
        }
    }

    /// <summary>
    /// The runner stopped acting (pause, focus lost, HUD hidden...) and released all input.
    /// Forget the current target: the world will have moved on when we resume.
    /// </summary>
    public void Suspend(string reason)
    {
        _holding = false;
        _restingSince = null;
        if (State is BrainState.Engage or BrainState.Attack)
            Go(BrainState.SearchTarget, $"suspended: {reason}");
    }

    private void TickRecover(PlayerBars bars)
    {
        var rest = _config.Rest;
        var now = _time.GetUtcNow();
        bool low = bars.Hp.Percent < rest.HpBelow || bars.Mp.Percent < rest.MpBelow;

        if (_restingSince is null)
        {
            if (!low)
            {
                Go(BrainState.SearchTarget, "ready to fight");
                return;
            }
            _restingSince = now;
            _log.Information("Resting: HP {Hp:F0}%, MP {Mp:F0}% (until HP >= {HpUntil}% and MP >= {MpUntil}%, max {Max}s)",
                bars.Hp.Percent, bars.Mp.Percent, rest.HpUntil, rest.MpUntil, rest.MaxSeconds);
        }

        _onProgress(); // resting is deliberate, not a stall (bounded by MaxSeconds < watchdog)
        bool recovered = bars.Hp.Percent >= rest.HpUntil && bars.Mp.Percent >= rest.MpUntil;
        bool tooLong = now - _restingSince >= TimeSpan.FromSeconds(rest.MaxSeconds);
        if (recovered || tooLong)
        {
            _restingSince = null;
            Go(BrainState.SearchTarget, recovered ? "rested" : $"rested {rest.MaxSeconds}s (max)");
        }
    }

    private void TickSearch(CancellationToken interrupt)
    {
        if (_time.GetUtcNow() < _nextSearchAt) return;

        var result = _finder.Find(interrupt);
        switch (result.Outcome)
        {
            case FindOutcome.Found:
                _aim = result.Target!.Point;
                Go(BrainState.Engage, $"monster at {_aim} ({result.Probes} probes, {result.Elapsed.TotalMilliseconds:F0} ms)");
                break;
            case FindOutcome.NothingFound:
                _nextSearchAt = _time.GetUtcNow() + TimeSpan.FromMilliseconds(_config.SearchRetryMs);
                break;
            case FindOutcome.Aborted:
                break; // paused / focus lost: the runner takes it from here
        }
    }

    private void TickEngage()
    {
        if (!_input.MouseDown(MouseButton.Left))
        {
            Go(BrainState.SearchTarget, "attack click refused");
            return;
        }

        var now = _time.GetUtcNow();
        _holding = true;
        _seenAlive = false;
        _lastTargetHp = null;
        _cursorLostSince = null;
        _targetSince = now;
        _lastDamage = now;
        Go(BrainState.Attack, $"holding attack on {_aim}");
    }

    private void TickAttack(VisionSnapshot snapshot, Mat frame, CancellationToken interrupt)
    {
        var now = _time.GetUtcNow();
        var panel = snapshot.Target;

        if (panel is { Status: TargetStatus.Hovered or TargetStatus.Engaged })
            _seenAlive = true;

        // Death: the target's HP bar is empty. Ignore a "dead" panel left over from the previous kill.
        if (panel?.Status == TargetStatus.Dead && _seenAlive)
        {
            Release();
            var took = now - _targetSince;
            _killTimes.Add(took);
            _onProgress();
            _log.Information("Kill #{Kills} in {Seconds:F1}s", Kills, took.TotalSeconds);
            Go(_config.Loot.Enabled ? BrainState.Loot : BrainState.Recover, "target died");
            return;
        }

        if (panel is { Status: TargetStatus.Engaged, HpPercent: { } hp })
        {
            if (_lastTargetHp is null || hp < _lastTargetHp - 0.5)
            {
                _lastDamage = now;
                _onProgress();
            }
            _lastTargetHp = hp;
        }

        if (now - _lastDamage >= TimeSpan.FromSeconds(_config.NoProgressSeconds))
        {
            GiveUp($"no damage for {_config.NoProgressSeconds}s");
            return;
        }
        if (now - _targetSince >= TimeSpan.FromSeconds(_config.MaxTargetSeconds))
        {
            GiveUp($"still alive after {_config.MaxTargetSeconds}s");
            return;
        }

        // Is the monster still under the cursor? It moves, and the camera moves with the character.
        var cursor = _cursor.Detect(frame, _aim!.Value);
        if (cursor.Kind == CursorKind.Enemy)
        {
            _cursorLostSince = null;
            return;
        }

        _cursorLostSince ??= now;
        if (now - _cursorLostSince < TimeSpan.FromMilliseconds(_config.LostTargetGraceMs))
            return;

        // Let go before moving the mouse, or the character would walk to wherever the cursor goes.
        Release();
        var result = _finder.Find(interrupt, _aim, _config.ReacquireRadius);
        if (result.Outcome == FindOutcome.Found && _input.MouseDown(MouseButton.Left))
        {
            _holding = true;
            _cursorLostSince = null;
            _log.Information("Target moved: re-acquired at {Point} ({Probes} probes)", result.Target!.Point, result.Probes);
            _aim = result.Target.Point;
            return;
        }
        if (result.Outcome != FindOutcome.Aborted)
            Go(BrainState.SearchTarget, "lost the target");
    }

    private void GiveUp(string reason)
    {
        Release();
        GivenUp++;
        Go(BrainState.SearchTarget, $"giving up target: {reason}");
    }

    private void Release()
    {
        if (!_holding) return;
        _input.MouseUp(MouseButton.Left);
        _holding = false;
    }

    private void Go(BrainState next, string reason)
    {
        if (State == next) return;
        _log.Information("Brain {From} -> {To} ({Reason})", State.ToString(), next.ToString(), reason);
        State = next;
    }
}
