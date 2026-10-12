using BotPriston.Core.Bot;
using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using BotPriston.Core.Vision;
using Microsoft.Extensions.Time.Testing;
using OpenCvSharp;

namespace BotPriston.Tests;

public class MeteorTests : IDisposable
{
    private static readonly PixelPoint Monster = new(875, 465);
    private const string ShieldClick = "move (800,450)";

    private readonly FakeTimeProvider _time = new();
    private readonly FakeInput _input = new();
    private readonly FakeVision _vision = new();
    private readonly ScriptedFinder _finder = new();
    private readonly FakeCursor _cursor = new() { Kind = CursorKind.Neutral };
    private readonly Mat _frame = new(4, 4, MatType.CV_8UC3, Scalar.All(0));
    private readonly BotConfig _config = new()
    {
        Potions = new PotionsConfig
        {
            Hp = new PotionConfig { Key = "2", BelowPercent = 50, CooldownMs = 2000 },
            Mp = new PotionConfig { Key = "3", BelowPercent = 30, CooldownMs = 2000 },
            Stm = new PotionConfig { Key = "1", BelowPercent = 20, CooldownMs = 2000 },
        },
        Combat = new CombatConfig
        {
            Meteor = new MeteorConfig
            {
                // A fixed 2 s interval keeps the timing simple; the random one has its own test.
                Point = new PixelPoint(800, 450), JitterPx = 0, IntervalMs = 2000, IntervalMaxMs = 2000, SearchRetryMs = 500,
                MeteorKey = "F4", ShieldKey = "F2", ShieldEverySeconds = 90,
            },
        },
    };

    private MeteorCaster Caster() =>
        new(_config.Combat.Meteor, _input, _time, TestLog.Silent, _finder, _cursor);

    private void Tick(MeteorCaster caster) => caster.Tick(FakeVision.Bars(100, 100, 100), _frame, CancellationToken.None);

    /// <summary>The first tick casts the shield; the clock then moves to the first meteor.</summary>
    private MeteorCaster AfterShield()
    {
        var caster = Caster();
        Tick(caster);
        _time.Advance(TimeSpan.FromMilliseconds(2000));
        _input.Actions.Clear();
        return caster;
    }

    [Fact]
    public void Starts_WithTheShield_InTheMiddle()
    {
        var caster = Caster();

        Tick(caster);

        Assert.Equal(["key F2", ShieldClick, "click Right", "key F4"], _input.Actions);
        Assert.Equal(1, caster.Shields);
        Assert.Empty(_finder.Calls);
    }

    [Fact]
    public void Meteor_SearchesAMonster_ThenRightClicksIt()
    {
        var caster = AfterShield();
        _finder.WillFind(Monster);

        Tick(caster);

        Assert.Single(_finder.Calls);
        Assert.Equal(["move (875,465)", "click Right"], _input.Actions); // where the finder saw the gem red
        Assert.Equal(1, caster.Casts);
    }

    [Fact]
    public void Meteor_SameMonsterStillUnderTheCursor_NoNewSearch()
    {
        var caster = AfterShield();
        _finder.WillFind(Monster);
        Tick(caster);

        _cursor.Kind = CursorKind.Enemy;                // still red on that spot
        _time.Advance(TimeSpan.FromMilliseconds(2000));
        Tick(caster);

        Assert.Single(_finder.Calls);
        Assert.Equal(["move (875,465)", "click Right", "move (875,465)", "click Right"], _input.Actions);
    }

    [Fact]
    public void Meteor_MonsterGone_SearchesAgain()
    {
        var caster = AfterShield();
        _finder.WillFind(Monster);
        Tick(caster);

        _cursor.Kind = CursorKind.Neutral;              // it died or moved
        _finder.WillFind(new PixelPoint(740, 465));
        _time.Advance(TimeSpan.FromMilliseconds(2000));
        Tick(caster);

        Assert.Equal(2, _finder.Calls.Count);
        Assert.Equal(2, caster.Casts);
    }

    [Fact]
    public void WhileRecharging_FindsTheNextMonster_ThenClicksAsSoonAsReady()
    {
        var caster = AfterShield();
        _finder.WillFind(Monster);
        Tick(caster);                                   // meteor #1
        Assert.Equal(1, caster.Casts);

        _cursor.Kind = CursorKind.Neutral;              // that monster died
        var next = new PixelPoint(740, 465);
        _finder.WillFind(next);
        _time.Advance(TimeSpan.FromMilliseconds(300));
        Tick(caster);                                   // still recharging: aims at the next one, no click
        Assert.Equal(2, _finder.Calls.Count);
        Assert.Equal(1, caster.Casts);
        Assert.Equal(next, caster.Aim);

        _cursor.Kind = CursorKind.Enemy;                // still there when the meteor is ready
        _time.Advance(TimeSpan.FromMilliseconds(1700));
        Tick(caster);

        Assert.Equal(2, _finder.Calls.Count);           // no search at cast time
        Assert.Equal(2, caster.Casts);
        Assert.Equal(["move (740,465)", "click Right"], _input.Actions.TakeLast(2));
    }

    [Fact]
    public void NoMonsterInReach_NoRightClick_SearchesAgainSoon()
    {
        var caster = AfterShield();

        Tick(caster);                                   // finds nothing
        _time.Advance(TimeSpan.FromMilliseconds(499));
        Tick(caster);
        Assert.Single(_finder.Calls);                   // not yet

        _time.Advance(TimeSpan.FromMilliseconds(1));
        Tick(caster);

        Assert.Equal(2, _finder.Calls.Count);
        Assert.Empty(_input.Actions);                   // never clicked without a red gem
        Assert.Equal(0, caster.Casts);
    }

    [Fact]
    public void EveryMinuteAndAHalf_SwitchesToTheShield_AndBack()
    {
        var caster = Caster();
        _cursor.Kind = CursorKind.Enemy;
        for (int i = 0; i < 3; i++)
            _finder.WillFind(Monster);                  // found again after each shield

        for (int i = 0; i <= 100; i++)                  // 200 s, one tick every 2 s
        {
            Tick(caster);
            _time.Advance(TimeSpan.FromSeconds(2));
        }

        Assert.Equal(3, caster.Shields);                // at 0 s, 90 s and 180 s
        for (int i = 0; i < _input.Actions.Count; i++)
        {
            if (_input.Actions[i] != "key F2") continue;
            Assert.Equal([ShieldClick, "click Right", "key F4"], _input.Actions.Skip(i + 1).Take(3));
        }
        Assert.Equal(3, _finder.Calls.Count);           // after each shield the mouse left the monster
        Assert.Equal(98, caster.Casts);
    }

    [Fact]
    public void Runner_PotionComesFirst_AndNoTargetsIsNotAStall()
    {
        var caster = Caster();
        var runner = new BotRunner(_config, new FakeCapture(), _vision, _input, new FakeWindow(), new BotControl(), _time,
            TestLog.Silent, meteor: caster);
        _vision.Next = FakeVision.Snapshot(hp: 40, mp: 100, stm: 100);

        runner.Tick();
        Assert.Equal(["key 2"], _input.Actions);

        _vision.Next = FakeVision.Snapshot(hp: 100, mp: 100, stm: 100);
        for (int i = 0; i < 200; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            runner.Tick();
        }
        Assert.Equal(RunnerState.Running, runner.State);
    }

    [Fact]
    public void TimeBetweenMeteors_IsRandom_BetweenTwoAndThreeSeconds()
    {
        var meteor = _config.Combat.Meteor;
        meteor.IntervalMaxMs = 3000;
        meteor.ShieldEverySeconds = 100_000;
        _cursor.Kind = CursorKind.Enemy;
        _finder.WillFind(Monster);
        var caster = new MeteorCaster(meteor, _input, _time, TestLog.Silent, _finder, _cursor, new Random(5));
        Tick(caster);                                   // shield

        var castTimes = new List<double>();
        for (int ms = 0; ms < 300_000; ms += 10)        // 5 minutes, a tick every 10 ms
        {
            _time.Advance(TimeSpan.FromMilliseconds(10));
            int before = caster.Casts;
            Tick(caster);
            if (caster.Casts > before) castTimes.Add(ms);
        }

        var gaps = castTimes.Zip(castTimes.Skip(1), (a, b) => b - a).ToList();
        Assert.True(gaps.Count > 90, $"{gaps.Count} gaps");
        Assert.All(gaps, g => Assert.InRange(g, 2000, 3010));
        Assert.True(gaps.Max() - gaps.Min() > 700, "the intervals vary");
    }

    [Fact]
    public void LowMana_HoldsTheCasts()
    {
        _config.Combat.Meteor.MinMpPercent = 40;
        var caster = Caster();

        caster.Tick(FakeVision.Bars(100, 35, 100), _frame, CancellationToken.None);

        Assert.Empty(_input.Actions);
    }

    public void Dispose() => _frame.Dispose();
}
