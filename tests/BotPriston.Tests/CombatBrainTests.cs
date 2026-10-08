using BotPriston.Core.Bot;
using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using BotPriston.Core.Targeting;
using BotPriston.Core.Vision;
using Microsoft.Extensions.Time.Testing;
using OpenCvSharp;

namespace BotPriston.Tests;

internal sealed class ScriptedFinder : ITargetFinder
{
    public Queue<FindResult> Results { get; } = new();
    public List<(PixelPoint? Near, int MaxDistance)> Calls { get; } = [];

    public FindResult Find(CancellationToken cancel, PixelPoint? near = null, int maxDistance = 0)
    {
        Calls.Add((near, maxDistance));
        return Results.Count > 0 ? Results.Dequeue() : new FindResult(FindOutcome.NothingFound, null, 10, TimeSpan.FromMilliseconds(500));
    }

    public void WillFind(PixelPoint at) => Results.Enqueue(new FindResult(FindOutcome.Found,
        new TargetFound(at, new CursorReading(CursorKind.Enemy, 50, 0), new TargetPanel(TargetStatus.Hovered, 1, null), 3, TimeSpan.FromMilliseconds(300)),
        3, TimeSpan.FromMilliseconds(300)));
}

internal sealed class FakeCursor : ICursorReader
{
    public CursorKind Kind { get; set; } = CursorKind.Enemy;
    public CursorReading Detect(Mat frame, PixelPoint mouse) => new(Kind, Kind == CursorKind.Enemy ? 50 : 0, Kind == CursorKind.Neutral ? 23 : 0);
}

public class CombatBrainTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeInput _input = new();
    private readonly ScriptedFinder _finder = new();
    private readonly FakeCursor _cursor = new();
    private readonly Mat _frame = new(4, 4, MatType.CV_8UC3, Scalar.All(0));
    private readonly CombatConfig _config = new()
    {
        LostTargetGraceMs = 400,
        ReacquireRadius = 150,
        NoProgressSeconds = 15,
        MaxTargetSeconds = 60,
        SearchRetryMs = 1500,
        Rest = new RestConfig { HpBelow = 40, HpUntil = 80, MpBelow = 10, MpUntil = 50, MaxSeconds = 60 },
    };
    private int _progress;

    private static readonly PixelPoint Monster = new(875, 465);

    private CombatBrain Brain() => new(_config, _finder, _cursor, _input, _time, TestLog.Silent, () => _progress++);

    private void Tick(CombatBrain brain, TargetPanel? target = null, double hp = 100, double mp = 100, bool rightReady = false) =>
        brain.Tick(FakeVision.Snapshot(hp, mp, 100, target, rightReady), _frame, CancellationToken.None);

    /// <summary>Recover → SearchTarget → Engage → Attack.</summary>
    private CombatBrain Attacking()
    {
        var brain = Brain();
        _finder.WillFind(Monster);
        Tick(brain); // Recover -> SearchTarget
        Tick(brain); // search -> Engage
        Tick(brain); // press -> Attack
        Assert.Equal(BrainState.Attack, brain.State);
        return brain;
    }

    [Fact]
    public void HealthyCharacter_GoesStraightToSearching()
    {
        var brain = Brain();

        Tick(brain);

        Assert.Equal(BrainState.SearchTarget, brain.State);
    }

    [Fact]
    public void FoundMonster_IsAttackedByHoldingTheLeftButton()
    {
        var brain = Attacking();

        Assert.Equal(["down Left"], _input.Actions);
        Assert.Equal(Monster, brain.Aim);
    }

    [Fact]
    public void TargetDies_ReleasesButton_CountsKill_AndRecovers()
    {
        var brain = Attacking();
        Tick(brain, FakeVision.Target(TargetStatus.Engaged, 100));
        Tick(brain, FakeVision.Target(TargetStatus.Engaged, 40));

        Tick(brain, FakeVision.Target(TargetStatus.Dead, 0));

        Assert.Equal(1, brain.Kills);
        Assert.Equal(["down Left", "up Left"], _input.Actions);
        Assert.Equal(BrainState.Recover, brain.State);
        Assert.True(_progress >= 3, "damage and the kill count as progress");
    }

    [Fact]
    public void OneShotKill_HoveredThenDead_Counts()
    {
        var brain = Attacking();
        Tick(brain, FakeVision.Target(TargetStatus.Hovered));

        Tick(brain, FakeVision.Target(TargetStatus.Dead, 0));

        Assert.Equal(1, brain.Kills);
    }

    [Fact]
    public void DeadPanelLeftFromThePreviousKill_IsNotANewKill()
    {
        var brain = Attacking();

        Tick(brain, FakeVision.Target(TargetStatus.Dead, 0));

        Assert.Equal(0, brain.Kills);
        Assert.Equal(BrainState.Attack, brain.State);
    }

    [Fact]
    public void MonsterMoves_ReleaseThenReacquireNearby()
    {
        var brain = Attacking();
        _cursor.Kind = CursorKind.Neutral;
        Tick(brain, FakeVision.Target(TargetStatus.Engaged, 80));
        _time.Advance(TimeSpan.FromMilliseconds(399));
        Tick(brain, FakeVision.Target(TargetStatus.Engaged, 80));
        Assert.Single(_finder.Calls); // still within the grace period

        var moved = new PixelPoint(950, 465);
        _finder.WillFind(moved);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Tick(brain, FakeVision.Target(TargetStatus.Engaged, 80));

        Assert.Equal((Monster, 150), (_finder.Calls[^1].Near!.Value, _finder.Calls[^1].MaxDistance));
        Assert.Equal(["down Left", "up Left", "down Left"], _input.Actions);
        Assert.Equal(moved, brain.Aim);
        Assert.Equal(BrainState.Attack, brain.State);
    }

    [Fact]
    public void MonsterGone_BackToSearching()
    {
        var brain = Attacking();
        _cursor.Kind = CursorKind.Neutral;
        Tick(brain, FakeVision.Target(TargetStatus.Engaged, 80));
        _time.Advance(TimeSpan.FromMilliseconds(500));

        Tick(brain, FakeVision.Target(TargetStatus.Engaged, 80)); // local search finds nothing

        Assert.Equal(BrainState.SearchTarget, brain.State);
        Assert.Equal(["down Left", "up Left"], _input.Actions);
    }

    [Fact]
    public void NoDamage_GivesUpTarget()
    {
        var brain = Attacking();
        Tick(brain, FakeVision.Target(TargetStatus.Engaged, 70));

        _time.Advance(TimeSpan.FromSeconds(15));
        Tick(brain, FakeVision.Target(TargetStatus.Engaged, 70));

        Assert.Equal(BrainState.SearchTarget, brain.State);
        Assert.Equal(1, brain.GivenUp);
        Assert.Equal("up Left", _input.Actions[^1]);
    }

    [Fact]
    public void SlowButSteadyDamage_IsNotGivenUp_UntilMaxTargetTime()
    {
        var brain = Attacking();
        for (int s = 0; s < 59; s++)
        {
            Tick(brain, FakeVision.Target(TargetStatus.Engaged, 100 - s));
            _time.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.Equal(BrainState.Attack, brain.State);

        _time.Advance(TimeSpan.FromSeconds(1));
        Tick(brain, FakeVision.Target(TargetStatus.Engaged, 30));

        Assert.Equal(BrainState.SearchTarget, brain.State);
        Assert.Equal(1, brain.GivenUp);
    }

    [Fact]
    public void NothingFound_WaitsBeforeSearchingAgain()
    {
        var brain = Brain();
        Tick(brain);
        Tick(brain); // nothing found
        Tick(brain); // too soon
        Assert.Single(_finder.Calls);

        _time.Advance(TimeSpan.FromMilliseconds(1500));
        Tick(brain);

        Assert.Equal(2, _finder.Calls.Count);
    }

    [Fact]
    public void LowMana_RestsUntilRecovered()
    {
        var brain = Brain();

        Tick(brain, mp: 5);
        Assert.Equal(BrainState.Recover, brain.State);
        Tick(brain, mp: 30);
        Assert.Equal(BrainState.Recover, brain.State);

        Tick(brain, mp: 50);
        Assert.Equal(BrainState.SearchTarget, brain.State);
    }

    [Fact]
    public void Rest_EndsAfterMaxSeconds()
    {
        var brain = Brain();
        Tick(brain, hp: 20);

        _time.Advance(TimeSpan.FromSeconds(60));
        Tick(brain, hp: 30);

        Assert.Equal(BrainState.SearchTarget, brain.State);
    }

    [Fact]
    public void RightSkillReady_IsUsedOnTheTarget_ThenLeftAttackResumes()
    {
        var brain = Attacking();

        Tick(brain, FakeVision.Target(TargetStatus.Engaged, 90), rightReady: true);

        Assert.Equal(["down Left", "up Left", "click Right", "down Left"], _input.Actions);
        Assert.Equal(1, brain.RightSkillUses);
        Assert.Equal(BrainState.Attack, brain.State);
    }

    [Fact]
    public void RightSkill_NotSpammedWhileTheIconTurnsGray()
    {
        var brain = Attacking();
        Tick(brain, FakeVision.Target(TargetStatus.Engaged, 90), rightReady: true);

        _time.Advance(TimeSpan.FromMilliseconds(1000));
        Tick(brain, FakeVision.Target(TargetStatus.Engaged, 80), rightReady: true); // icon not gray yet
        Assert.Equal(1, brain.RightSkillUses);

        _time.Advance(TimeSpan.FromMilliseconds(500));
        Tick(brain, FakeVision.Target(TargetStatus.Engaged, 70), rightReady: true); // still in color: try again
        Assert.Equal(2, brain.RightSkillUses);
    }

    [Fact]
    public void RightSkill_NotUsedWhileRecharging()
    {
        var brain = Attacking();

        Tick(brain, FakeVision.Target(TargetStatus.Engaged, 90), rightReady: false);

        Assert.Equal(0, brain.RightSkillUses);
        Assert.Equal(["down Left"], _input.Actions);
    }

    [Fact]
    public void RightSkill_NotUsedWhenTheCursorIsOffTheMonster()
    {
        var brain = Attacking();
        _cursor.Kind = CursorKind.Neutral;

        Tick(brain, FakeVision.Target(TargetStatus.Engaged, 90), rightReady: true);

        Assert.Equal(0, brain.RightSkillUses);
    }

    [Fact]
    public void RightSkill_RespectsMinMpAndEnabled()
    {
        _config.RightSkill.MinMpPercent = 20;
        var brain = Attacking();
        Tick(brain, FakeVision.Target(TargetStatus.Engaged, 90), mp: 15, rightReady: true);
        Assert.Equal(0, brain.RightSkillUses);

        _config.RightSkill.MinMpPercent = 0;
        _config.RightSkill.Enabled = false;
        Tick(brain, FakeVision.Target(TargetStatus.Engaged, 85), mp: 15, rightReady: true);
        Assert.Equal(0, brain.RightSkillUses);
    }

    [Fact]
    public void Suspend_DuringAttack_ForgetsTarget()
    {
        var brain = Attacking();

        brain.Suspend("pause hotkey");

        Assert.Equal(BrainState.SearchTarget, brain.State);
        _cursor.Kind = CursorKind.Neutral;
        Tick(brain); // a fresh search, not a re-acquire around the old point
        Assert.Null(_finder.Calls[^1].Near);
    }

    public void Dispose() => _frame.Dispose();
}

public class BotRunnerCombatTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeInput _input = new();
    private readonly FakeWindow _window = new();
    private readonly FakeCapture _capture = new();
    private readonly FakeVision _vision = new();
    private readonly BotControl _control = new();
    private readonly ScriptedFinder _finder = new();
    private readonly BotConfig _config = new()
    {
        Potions = new PotionsConfig
        {
            Hp = new PotionConfig { Key = "2", BelowPercent = 50, CooldownMs = 2000 },
            Mp = new PotionConfig { Key = "3", BelowPercent = 30, CooldownMs = 2000 },
            Stm = new PotionConfig { Key = "1", BelowPercent = 20, CooldownMs = 2000 },
        },
        Safety = new SafetyConfig { WatchdogSeconds = 120, HudLostStopSeconds = 20, DeadHpSeconds = 2 },
    };

    private BotRunner Runner() => new(_config, _capture, _vision, _input, _window, _control, _time, TestLog.Silent,
        onProgress => new CombatBrain(_config.Combat, _finder, new FakeCursor(), _input, _time, TestLog.Silent, onProgress));

    [Fact]
    public void PauseWhileAttacking_ReleasesAndForgetsTarget()
    {
        var runner = Runner();
        _finder.WillFind(new PixelPoint(875, 465));
        for (int i = 0; i < 3; i++) runner.Tick();
        Assert.Equal(BrainState.Attack, runner.Brain!.State);

        _control.SetPaused(true);
        runner.Tick();

        Assert.Equal(RunnerState.Paused, runner.State);
        Assert.True(_input.ReleaseAllCalls > 0);
        Assert.Equal(BrainState.SearchTarget, runner.Brain.State);
    }

    [Fact]
    public void PauseHotkey_InterruptsALongSearch()
    {
        var token = _control.Interrupt;
        Assert.False(token.IsCancellationRequested);

        _control.SetPaused(true);
        Assert.True(token.IsCancellationRequested);

        _control.SetPaused(false);
        Assert.False(_control.Interrupt.IsCancellationRequested);
    }

    [Fact]
    public void WithoutKillsOrDamage_WatchdogStopsTheBot()
    {
        var runner = Runner();
        runner.Tick(); // searching, nothing found forever

        _time.Advance(TimeSpan.FromSeconds(120));
        runner.Tick();

        Assert.Equal(RunnerState.Stopped, runner.State);
        Assert.Contains("no progress", runner.StopReason);
    }

    [Fact]
    public void EmptyHpBar_StopsTheBot()
    {
        var runner = Runner();
        _vision.Next = FakeVision.Snapshot(hp: 0, mp: 100, stm: 100);

        runner.Tick();
        Assert.NotEqual(RunnerState.Stopped, runner.State); // one bad frame is not death

        _time.Advance(TimeSpan.FromSeconds(2));
        runner.Tick();

        Assert.Equal(RunnerState.Stopped, runner.State);
        Assert.Contains("died", runner.StopReason);
    }
}
