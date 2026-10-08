using BotPriston.Core.Bot;
using BotPriston.Core.Config;
using Microsoft.Extensions.Time.Testing;

namespace BotPriston.Tests;

public class PotionManagerTests
{
    private readonly FakeTimeProvider _time = new();

    private static PotionsConfig Config() => new()
    {
        Hp = new PotionConfig { Key = "2", BelowPercent = 50, CooldownMs = 2000 },
        Mp = new PotionConfig { Key = "3", BelowPercent = 30, CooldownMs = 2000 },
        Stm = new PotionConfig { Key = "1", BelowPercent = 20, CooldownMs = 2000 },
        MinGainPercent = 1,
        MaxFailures = 3,
        RetryAfterSeconds = 60,
    };

    private PotionManager Manager(PotionsConfig? config = null) => new(config ?? Config(), _time, TestLog.Silent);

    [Fact]
    public void NothingBelowThresholds_NoPotion()
    {
        Assert.Null(Manager().Decide(FakeVision.Bars(50, 30, 20)));
    }

    [Theory]
    [InlineData(49, 100, 100, PotionKind.Hp)]
    [InlineData(100, 29, 100, PotionKind.Mp)]
    [InlineData(100, 100, 19, PotionKind.Stm)]
    [InlineData(10, 10, 10, PotionKind.Hp)]   // HP has priority
    [InlineData(100, 10, 10, PotionKind.Mp)]  // then MP
    public void PicksLowBar_ByPriority(double hp, double mp, double stm, PotionKind expected)
    {
        Assert.Equal(expected, Manager().Decide(FakeVision.Bars(hp, mp, stm)));
    }

    [Fact]
    public void KeysComeFromConfig()
    {
        var manager = Manager();

        Assert.Equal(0x32, manager.KeyFor(PotionKind.Hp).VirtualKey);  // '2'
        Assert.Equal(0x33, manager.KeyFor(PotionKind.Mp).VirtualKey);  // '3'
        Assert.Equal(0x31, manager.KeyFor(PotionKind.Stm).VirtualKey); // '1'
    }

    [Fact]
    public void Cooldown_PreventsImmediateReuse()
    {
        var manager = Manager();
        var low = FakeVision.Bars(40, 100, 100);

        manager.MarkUsed(manager.Decide(low)!.Value, low);
        _time.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.Null(manager.Decide(low));

        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(PotionKind.Hp, manager.Decide(low));
    }

    [Fact]
    public void CooldownOfOnePotion_DoesNotBlockAnother()
    {
        var manager = Manager();
        var lowHp = FakeVision.Bars(40, 100, 100);
        manager.MarkUsed(PotionKind.Hp, lowHp);

        Assert.Equal(PotionKind.Mp, manager.Decide(FakeVision.Bars(40, 10, 100)));
    }

    [Fact]
    public void DisabledPotion_IsNeverUsed()
    {
        var config = Config();
        config.Hp.Enabled = false;

        Assert.Null(Manager(config).Decide(FakeVision.Bars(1, 100, 100)));
    }

    [Fact]
    public void RepeatedUsesWithoutEffect_MarkOutOfStock_ThenRetryLater()
    {
        var manager = Manager();
        var low = FakeVision.Bars(40, 100, 100);

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(PotionKind.Hp, manager.Decide(low));
            manager.MarkUsed(PotionKind.Hp, low);
            _time.Advance(TimeSpan.FromSeconds(2));
        }

        Assert.Null(manager.Decide(low));
        Assert.True(manager.IsOutOfStock(PotionKind.Hp));

        _time.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(PotionKind.Hp, manager.Decide(low));
    }

    [Fact]
    public void PotionThatWorks_ResetsFailures()
    {
        var manager = Manager();
        var low = FakeVision.Bars(40, 100, 100);

        // Two failures...
        for (int i = 0; i < 2; i++)
        {
            manager.MarkUsed(PotionKind.Hp, low);
            _time.Advance(TimeSpan.FromSeconds(2));
            manager.Decide(low);
        }

        // ...then one that works (bar rose), then two more failures: still not out of stock.
        manager.MarkUsed(PotionKind.Hp, low);
        _time.Advance(TimeSpan.FromSeconds(2));
        manager.Decide(FakeVision.Bars(45, 100, 100));
        for (int i = 0; i < 2; i++)
        {
            manager.MarkUsed(PotionKind.Hp, low);
            _time.Advance(TimeSpan.FromSeconds(2));
            manager.Decide(low);
        }

        Assert.False(manager.IsOutOfStock(PotionKind.Hp));
    }
}
