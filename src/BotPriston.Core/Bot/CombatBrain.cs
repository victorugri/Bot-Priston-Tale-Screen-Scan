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
    /// <summary>Between fights: rest until HP/MP are back if they are low and no potion can help.</summary>
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
    private bool _holdingLeft;
    private DateTimeOffset? _holdingRightSince; // right button held until the skill icon turns gray
    private DateTimeOffset _nextRightTryAt;
    private bool? _rightIconReady;              // last seen icon state, to log its transitions
    private bool _seenAlive;
    private double? _lastTargetHp;
    private DateTimeOffset _targetSince, _lastDamage, _nextSearchAt;
    private DateTimeOffset? _cursorLostSince;
    private DateTimeOffset? _restingSince;
    private readonly List<TimeSpan> _killTimes = [];
    private readonly ITargetFinder? _wideFinder;
    private readonly Queue<(DateTimeOffset At, double Hp)> _hpHistory = new();
    private bool _wideTarget; // current target came from the wider "under attack" search

    /// <param name="wideFinder">Wider search used only when losing HP with nothing in reach (Combat.UnderAttack).</param>
    public CombatBrain(CombatConfig config, ITargetFinder finder, ICursorReader cursor, IInputSink input,
        TimeProvider time, ILogger log, Action onProgress, ITargetFinder? wideFinder = null)
    {
        _config = config;
        _finder = finder;
        _wideFinder = wideFinder;
        _cursor = cursor;
        _input = input;
        _time = time;
        _log = log;
        _onProgress = onProgress;
    }

    public BrainState State { get; private set; } = BrainState.Recover;
    public int Kills => _killTimes.Count;
    public int GivenUp { get; private set; }

    /// <summary>Right-skill casts, confirmed by its icon turning gray.</summary>
    public int RightSkillUses { get; private set; }

    /// <summary>Times the right button was held for HoldMaxMs without the icon turning gray.</summary>
    public int RightSkillMisses { get; private set; }

    public PixelPoint? Aim => _aim;
    public bool HoldingLeft => _holdingLeft;
    public bool HoldingRight => _holdingRightSince is not null;

    /// <summary>Attacking a distant attacker found by the wider search: the character may walk to reach it.</summary>
    public bool AllowWalking => _wideTarget && State is BrainState.Engage or BrainState.Attack;

    /// <summary>Distant attackers engaged through the wider search.</summary>
    public int WideTargets { get; private set; }

    /// <summary>
    /// Whether a potion is taking care of a bar. Rest is only for bars no potion can refill
    /// (disabled or out of stock). Null = no potions at all.
    /// </summary>
    public Func<PotionKind, bool>? PotionAvailable { get; set; }

    public string Summary =>
        (Kills == 0 ? "0 kills" : $"{Kills} kills (avg {_killTimes.Average(t => t.TotalSeconds):F1}s each)") +
        $", {GivenUp} targets given up, right skill cast {RightSkillUses}x ({RightSkillMisses} attempts without effect)" +
        $", walking stopped {WalkStops}x, distant attackers engaged {WideTargets}x";

    /// <summary>One decision. <paramref name="frame"/> is the image <paramref name="snapshot"/> came from.</summary>
    public void Tick(VisionSnapshot snapshot, Mat frame, CancellationToken interrupt)
    {
        if (snapshot.Bars is null) return; // runner only calls with the HUD visible; be defensive anyway

        LogRightIcon(snapshot);
        RecordHp(snapshot.Bars.Hp.Percent);

        switch (State)
        {
            case BrainState.Recover: TickRecover(snapshot.Bars); break;
            case BrainState.SearchTarget: TickSearch(interrupt); break;
            case BrainState.Engage: TickEngage(frame); break;
            case BrainState.Attack: TickAttack(snapshot, frame, interrupt); break;
            case BrainState.Loot: Go(BrainState.Recover, "looting is disabled"); break;
        }
    }

    /// <summary>
    /// The runner stopped acting (pause, focus lost, HUD hidden...) and released all input.
    /// Forget the current target: the world will have moved on when we resume.
    /// </summary>
    /// <summary>Times the character was caught walking and the brain dropped what it was doing.</summary>
    public int WalkStops { get; private set; }

    /// <summary>
    /// The runner saw the character walking (the ground slides). The character must never walk: the
    /// runner has already forced both buttons up; drop the target and pause briefly before searching.
    /// </summary>
    public void OnWalkingDetected(string context)
    {
        WalkStops++;
        _holdingLeft = false;
        _holdingRightSince = null;
        _cursorLostSince = null;
        _nextSearchAt = _time.GetUtcNow() + TimeSpan.FromMilliseconds(_config.SearchRetryMs);
        if (State is BrainState.Engage or BrainState.Attack)
            Go(BrainState.SearchTarget, $"character was walking ({context})");
    }

    public void Suspend(string reason)
    {
        _holdingLeft = false;
        _holdingRightSince = null;
        _restingSince = null;
        if (State is BrainState.Engage or BrainState.Attack)
            Go(BrainState.SearchTarget, $"suspended: {reason}");
    }

    private void TickRecover(PlayerBars bars)
    {
        var rest = _config.Rest;
        var now = _time.GetUtcNow();
        bool hpByPotion = PotionAvailable?.Invoke(PotionKind.Hp) ?? false;
        bool mpByPotion = PotionAvailable?.Invoke(PotionKind.Mp) ?? false;
        bool low = (!hpByPotion && bars.Hp.Percent < rest.HpBelow) || (!mpByPotion && bars.Mp.Percent < rest.MpBelow);

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
        bool recovered = (hpByPotion || bars.Hp.Percent >= rest.HpUntil) && (mpByPotion || bars.Mp.Percent >= rest.MpUntil);
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

        // The sweep moves the mouse around: if the game believed a button was still held, the character
        // would follow the cursor. Make sure both are up first; the click only comes once the gem is red.
        _input.ForceReleaseButtons();
        var result = _finder.Find(interrupt);

        // Nothing in reach but losing HP: something attacks from a distance. Look farther, once.
        if (result.Outcome == FindOutcome.NothingFound && _wideFinder is not null && _config.UnderAttack.Enabled
            && HpDrop() is var drop && drop >= _config.UnderAttack.HpDropPercent)
        {
            _log.Information("Losing HP ({Drop:F0}% in {Window}s) with no monster in reach: searching a wider area",
                drop, _config.UnderAttack.WindowSeconds);
            result = _wideFinder.Find(interrupt);
            if (result.Outcome == FindOutcome.Found)
            {
                _wideTarget = true;
                WideTargets++;
            }
        }

        switch (result.Outcome)
        {
            case FindOutcome.Found:
                _aim = result.Target!.Point;
                Go(BrainState.Engage, $"{(_wideTarget ? "distant attacker" : "monster")} at {_aim} " +
                    $"({result.Probes} probes, {result.Elapsed.TotalMilliseconds:F0} ms)");
                break;
            case FindOutcome.NothingFound:
                _nextSearchAt = _time.GetUtcNow() + TimeSpan.FromMilliseconds(_config.SearchRetryMs);
                break;
            case FindOutcome.Aborted:
                break; // paused / focus lost: the runner takes it from here
        }
    }

    private void TickEngage(Mat frame)
    {
        // The monster may have moved since the sweep: never press on the ground (the character would walk there).
        if (_cursor.Detect(frame, _aim!.Value).Kind != CursorKind.Enemy)
        {
            Go(BrainState.SearchTarget, "monster moved away before the attack");
            return;
        }

        if (!PressLeft())
        {
            Go(BrainState.SearchTarget, "attack click refused");
            return;
        }

        var now = _time.GetUtcNow();
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
            ReleaseAll();
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
            // Back on the monster after a flicker: hold the attack again.
            if (!_holdingLeft && _holdingRightSince is null && !PressLeft())
            {
                Go(BrainState.SearchTarget, "attack click refused");
                return;
            }
            UpdateRightSkill(snapshot, now);
            return;
        }

        // Not over the monster: never keep a button down. Holding it over the ground makes the
        // character walk to the cursor (and the camera follows, so it keeps walking).
        ReleaseAll();
        _cursorLostSince ??= now;
        if (now - _cursorLostSince < TimeSpan.FromMilliseconds(_config.LostTargetGraceMs))
            return;

        // A distant target is re-acquired among the wide search points (the close ones don't reach it).
        var finder = _wideTarget && _wideFinder is not null ? _wideFinder : _finder;
        var result = finder.Find(interrupt, _aim, _config.ReacquireRadius);
        if (result.Outcome == FindOutcome.Found && PressLeft())
        {
            _cursorLostSince = null;
            _log.Information("Target moved: re-acquired at {Point} ({Probes} probes)", result.Target!.Point, result.Probes);
            _aim = result.Target.Point;
            return;
        }
        if (result.Outcome != FindOutcome.Aborted)
            Go(BrainState.SearchTarget, "lost the target");
    }

    /// <summary>
    /// Right-click skill whenever its icon is in color. A single short click from the bot rarely casts
    /// (it lands mid basic-attack and the game ignores it), so the right button is HELD — the game casts
    /// as soon as it can, like it repeats the basic attack while the left button is held — until the
    /// icon turns gray, then the left button takes over again.
    /// </summary>
    private void UpdateRightSkill(VisionSnapshot snapshot, DateTimeOffset now)
    {
        var skill = _config.RightSkill;

        if (_holdingRightSince is { } since)
        {
            if (snapshot.Skills is { Right.Ready: false })
            {
                RightSkillUses++;
                _log.Information("Right skill cast #{Count}: right button held {Ms:F0} ms (MP {Mp:F0}%)",
                    RightSkillUses, (now - since).TotalMilliseconds, snapshot.Bars!.Mp.Percent);
                BackToLeft();
            }
            else if (now - since >= TimeSpan.FromMilliseconds(skill.HoldMaxMs))
            {
                RightSkillMisses++;
                _nextRightTryAt = now + TimeSpan.FromMilliseconds(skill.RetryMs);
                _log.Information("Right skill not cast after holding {Ms} ms (icon still ready); back to the left attack",
                    skill.HoldMaxMs);
                BackToLeft();
            }
            return;
        }

        if (!skill.Enabled || now < _nextRightTryAt) return;
        if (snapshot.Skills is not { Right.Ready: true }) return;
        if (snapshot.Bars!.Mp.Percent < skill.MinMpPercent) return;

        ReleaseLeft();
        if (_input.MouseDown(MouseButton.Right))
        {
            _holdingRightSince = now;
            _log.Debug("Holding right button on {Point} (MP {Mp:F0}%)", _aim, snapshot.Bars.Mp.Percent);
        }
        else
        {
            BackToLeft();
        }
    }

    private void BackToLeft()
    {
        ReleaseRight();
        if (!PressLeft())
            Go(BrainState.SearchTarget, "attack click refused");
    }

    private void LogRightIcon(VisionSnapshot snapshot)
    {
        if (snapshot.Skills is not { } skills || skills.Right.Ready == _rightIconReady) return;
        _log.Debug("Right skill icon: {State}", skills.Right.Ready ? "ready (color)" : "recharging (gray)");
        _rightIconReady = skills.Right.Ready;
    }

    private void GiveUp(string reason)
    {
        ReleaseAll();
        GivenUp++;
        Go(BrainState.SearchTarget, $"giving up target: {reason}");
    }

    private bool PressLeft()
    {
        if (!_input.MouseDown(MouseButton.Left)) return false;
        _holdingLeft = true;
        return true;
    }

    private void ReleaseLeft()
    {
        if (!_holdingLeft) return;
        _input.MouseUp(MouseButton.Left);
        _holdingLeft = false;
    }

    private void ReleaseRight()
    {
        if (_holdingRightSince is null) return;
        _input.MouseUp(MouseButton.Right);
        _holdingRightSince = null;
    }

    private void ReleaseAll()
    {
        ReleaseRight();
        ReleaseLeft();
    }

    private void RecordHp(double hp)
    {
        var now = _time.GetUtcNow();
        _hpHistory.Enqueue((now, hp));
        var oldest = now - TimeSpan.FromSeconds(_config.UnderAttack.WindowSeconds);
        while (_hpHistory.Count > 0 && _hpHistory.Peek().At < oldest)
            _hpHistory.Dequeue();
    }

    /// <summary>How much HP was lost within the window: highest recent value minus the current one.</summary>
    private double HpDrop() =>
        _hpHistory.Count == 0 ? 0 : _hpHistory.Max(h => h.Hp) - _hpHistory.Last().Hp;

    private void Go(BrainState next, string reason)
    {
        if (State == next) return;
        if (next is BrainState.Recover or BrainState.SearchTarget) _wideTarget = false;
        _log.Information("Brain {From} -> {To} ({Reason})", State.ToString(), next.ToString(), reason);
        State = next;
    }
}
