using BotPriston.Core.Bot;
using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using Microsoft.Extensions.Time.Testing;

namespace BotPriston.Tests;

public class MeteorTests
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
            // A still mouse keeps the expected actions simple; the wandering has its own test.
            Meteor = new MeteorConfig
            {
                Enabled = true, Point = new PixelPoint(800, 450), JitterPx = 0,
                IntervalMs = 2000, MeteorKey = "F4", ShieldKey = "F2", ShieldEverySeconds = 90,
            },
        },
    };

    private BotRunner Runner(out MeteorCaster caster)
    {
        caster = new MeteorCaster(_config.Combat.Meteor, _input, _time, TestLog.Silent);
        return new BotRunner(_config, new FakeCapture(), _vision, _input, new FakeWindow(), new BotControl(), _time,
            TestLog.Silent, meteor: caster);
    }

    private const string Click = "move (800,450)";

    [Fact]
    public void Starts_WithTheShield_ThenAMeteorEveryTwoSeconds()
    {
        var runner = Runner(out var caster);

        runner.Tick();                                         // shield first
        _time.Advance(TimeSpan.FromMilliseconds(1000));
        runner.Tick();                                         // too early
        _time.Advance(TimeSpan.FromMilliseconds(1000));
        runner.Tick();                                         // meteor
        _time.Advance(TimeSpan.FromMilliseconds(2000));
        runner.Tick();                                         // meteor

        Assert.Equal(
            ["key F2", Click, "click Right", "key F4",
             Click, "click Right",
             Click, "click Right"],
            _input.Actions);
        Assert.Equal((1, 2), (caster.Shields, caster.Casts));
    }

    [Fact]
    public void EveryMinuteAndAHalf_SwitchesToTheShield_AndBack()
    {
        _config.Safety.WatchdogSeconds = 600;
        var runner = Runner(out var caster);

        for (int i = 0; i <= 100; i++)                         // 200 s, one tick every 2 s
        {
            runner.Tick();
            _time.Advance(TimeSpan.FromSeconds(2));
        }

        Assert.Equal(3, caster.Shields);                       // at 0 s, 90 s and 180 s
        Assert.Equal(caster.Shields, _input.Actions.Count(a => a == "key F2"));
        Assert.Equal(caster.Shields, _input.Actions.Count(a => a == "key F4"));
        // Every shield is followed by its right click and then the meteor key.
        for (int i = 0; i < _input.Actions.Count; i++)
        {
            if (_input.Actions[i] != "key F2") continue;
            Assert.Equal([Click, "click Right", "key F4"], _input.Actions.Skip(i + 1).Take(3));
        }
        Assert.Equal(98, caster.Casts);                        // 101 ticks, 3 of them for the shield
    }

    [Fact]
    public void TheMouseMovesSlightly_AroundTheMiddle()
    {
        var meteor = _config.Combat.Meteor;
        meteor.JitterPx = 25;
        var caster = new MeteorCaster(meteor, _input, _time, TestLog.Silent, new Random(3));

        for (int i = 0; i < 200; i++)
        {
            caster.Tick(FakeVision.Bars(100, 100, 100), CancellationToken.None);
            _time.Advance(TimeSpan.FromMilliseconds(meteor.IntervalMs));
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
        Assert.Equal(0, caster.Casts + caster.Shields);
    }

    [Fact]
    public void LowMana_HoldsTheCasts()
    {
        _config.Combat.Meteor.MinMpPercent = 40;
        _config.Potions.Mp.Enabled = false;
        var runner = Runner(out var caster);
        _vision.Next = FakeVision.Snapshot(hp: 100, mp: 35, stm: 100);

        runner.Tick();

        Assert.Empty(_input.Actions);
    }

    [Fact]
    public void NoTargets_IsNotAStall()
    {
        var runner = Runner(out _);

        for (int i = 0; i < 200; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            runner.Tick();
        }

        Assert.Equal(RunnerState.Running, runner.State);
    }
}
