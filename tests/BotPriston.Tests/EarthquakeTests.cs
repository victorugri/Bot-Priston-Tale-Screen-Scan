using BotPriston.Core.Bot;
using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using Microsoft.Extensions.Time.Testing;

namespace BotPriston.Tests;

public class EarthquakeTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeInput _input = new();
    private readonly FakeVision _vision = new();
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
            // No keys and a still mouse: the basic rhythm. The tests below add keys and the wandering.
            Earthquake = new EarthquakeConfig { Enabled = true, Point = new PixelPoint(800, 450), IntervalMs = 1000, JitterPx = 0 },
        },
    };

    private BotRunner Runner(out EarthquakeCaster caster)
    {
        caster = new EarthquakeCaster(_config.Combat.Earthquake, _input, _time, TestLog.Silent);
        return new BotRunner(_config, new FakeCapture(), _vision, _input, new FakeWindow(), new BotControl(), _time,
            TestLog.Silent, earthquake: caster);
    }

    [Fact]
    public void RightClicksTheMiddleOfTheScreen_OncePerInterval()
    {
        var runner = Runner(out var caster);

        runner.Tick();
        _time.Advance(TimeSpan.FromMilliseconds(500));
        runner.Tick();
        _time.Advance(TimeSpan.FromMilliseconds(500));
        runner.Tick();

        Assert.Equal(["move (800,450)", "click Right", "move (800,450)", "click Right"], _input.Actions);
        Assert.Equal(2, caster.Casts);
    }

    [Fact]
    public void EveryTwoMinutes_FindsAMonster_HitsItOnceWithTheLeftButton()
    {
        _config.Safety.WatchdogSeconds = 600; // the test jumps 2 minutes in one step
        _config.Combat.Earthquake.LeftAttackEverySeconds = 120;
        var finder = new ScriptedFinder();
        var caster = new EarthquakeCaster(_config.Combat.Earthquake, _input, _time, TestLog.Silent, finder);
        var runner = new BotRunner(_config, new FakeCapture(), _vision, _input, new FakeWindow(), new BotControl(), _time,
            TestLog.Silent, earthquake: caster);

        runner.Tick();                                   // first earthquake
        _time.Advance(TimeSpan.FromSeconds(120));
        finder.WillFind(new PixelPoint(860, 465));
        runner.Tick();                                   // 2 min: hover sweep + one left click
        runner.Tick();                                   // back to the earthquake

        Assert.Equal(["move (800,450)", "click Right", "click Left", "move (800,450)", "click Right"], _input.Actions);
        Assert.Equal(1, caster.LeftAttacks);
        Assert.Single(finder.Calls);
    }

    [Fact]
    public void LeftAttack_NoMonsterInReach_RetriesSoon_WithoutClicking()
    {
        _config.Combat.Earthquake.LeftAttackEverySeconds = 120;
        var finder = new ScriptedFinder(); // finds nothing
        var caster = new EarthquakeCaster(_config.Combat.Earthquake, _input, _time, TestLog.Silent, finder);
        _time.Advance(TimeSpan.FromSeconds(120));

        caster.Tick(FakeVision.Bars(100, 100, 100), CancellationToken.None);
        Assert.DoesNotContain("click Left", _input.Actions);

        _time.Advance(TimeSpan.FromSeconds(9));
        caster.Tick(FakeVision.Bars(100, 100, 100), CancellationToken.None);
        Assert.Single(finder.Calls);                     // not yet

        _time.Advance(TimeSpan.FromSeconds(1));
        caster.Tick(FakeVision.Bars(100, 100, 100), CancellationToken.None);
        Assert.Equal(2, finder.Calls.Count);
    }

    [Fact]
    public void EachCycle_OneWeightedRandomKey_ThenTheRightClick()
    {
        var quake = _config.Combat.Earthquake;
        quake.IntervalMs = 200;
        quake.Keys = [new() { Key = "F2", Weight = 0.2 }, new() { Key = "F3", Weight = 0.4 }, new() { Key = "F4", Weight = 0.4 }];
        var caster = new EarthquakeCaster(quake, _input, _time, TestLog.Silent, random: new Random(7));

        for (int i = 0; i < 1000; i++)
        {
            caster.Tick(FakeVision.Bars(100, 100, 100), CancellationToken.None);
            _time.Advance(TimeSpan.FromMilliseconds(200));
        }

        Assert.Equal(1000, caster.Casts);
        var keys = _input.Actions.Where(a => a.StartsWith("key ")).ToList();
        Assert.Equal(1000, keys.Count);
        Assert.InRange(keys.Count(k => k == "key F2"), 150, 250);   // ~20%
        Assert.InRange(keys.Count(k => k == "key F3"), 350, 450);   // ~40%
        Assert.InRange(keys.Count(k => k == "key F4"), 350, 450);   // ~40%
        Assert.Equal(["key", "move", "click"], _input.Actions.Take(3).Select(a => a.Split(' ')[0]));
        Assert.Equal("click Right", _input.Actions[2]);
    }

    [Fact]
    public void TheMouseWandersSlightly_AroundTheMiddle()
    {
        var quake = _config.Combat.Earthquake;
        quake.JitterPx = 25;
        var caster = new EarthquakeCaster(quake, _input, _time, TestLog.Silent, random: new Random(3));

        for (int i = 0; i < 200; i++)
        {
            caster.Tick(FakeVision.Bars(100, 100, 100), CancellationToken.None);
            _time.Advance(TimeSpan.FromMilliseconds(quake.IntervalMs));
        }

        var spots = _input.Actions.Where(a => a.StartsWith("move ")).Distinct().ToList();
        Assert.True(spots.Count > 50, "the cursor keeps moving");
        foreach (var spot in spots)
        {
            var xy = spot[6..^1].Split(',').Select(int.Parse).ToArray();
            Assert.True(Math.Sqrt(Math.Pow(xy[0] - 800, 2) + Math.Pow(xy[1] - 450, 2)) <= 25.5, spot);
        }
    }

    [Fact]
    public void PotionComesFirst()
    {
        var runner = Runner(out var caster);
        _vision.Next = FakeVision.Snapshot(hp: 40, mp: 100, stm: 100);

        runner.Tick();

        Assert.Equal(["key 2"], _input.Actions);
        Assert.Equal(0, caster.Casts);
    }

    [Fact]
    public void LowMana_HoldsTheCasts()
    {
        _config.Combat.Earthquake.MinMpPercent = 40;
        _config.Potions.Mp.Enabled = false;
        var runner = Runner(out var caster);
        _vision.Next = FakeVision.Snapshot(hp: 100, mp: 35, stm: 100);

        runner.Tick();

        Assert.Empty(_input.Actions);
        Assert.Equal(0, caster.Casts);
    }

    [Fact]
    public void NoTargets_IsNotAStall()
    {
        _config.Safety.WatchdogSeconds = 120;
        var runner = Runner(out _);

        for (int i = 0; i < 200; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            runner.Tick();
        }

        Assert.Equal(RunnerState.Running, runner.State);
    }
}
