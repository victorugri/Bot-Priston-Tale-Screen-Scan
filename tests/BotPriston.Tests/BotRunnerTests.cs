using BotPriston.Core.Bot;
using BotPriston.Core.Config;
using Microsoft.Extensions.Time.Testing;

namespace BotPriston.Tests;

public class BotRunnerTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeInput _input = new();
    private readonly FakeWindow _window = new();
    private readonly FakeCapture _capture = new();
    private readonly FakeVision _vision = new();
    private readonly BotControl _control = new();
    private readonly BotConfig _config = new()
    {
        Potions = new PotionsConfig
        {
            Hp = new PotionConfig { Key = "2", BelowPercent = 50, CooldownMs = 2000 },
            Mp = new PotionConfig { Key = "3", BelowPercent = 30, CooldownMs = 2000 },
            Stm = new PotionConfig { Key = "1", BelowPercent = 20, CooldownMs = 2000 },
        },
        Safety = new SafetyConfig { WatchdogSeconds = 120, HudLostStopSeconds = 20 },
    };

    private BotRunner Runner() => new(_config, _capture, _vision, _input, _window, _control, _time, TestLog.Silent);

    [Fact]
    public void HealthyBars_RunningAndNoInput()
    {
        var runner = Runner();

        runner.Tick();

        Assert.Equal(RunnerState.Running, runner.State);
        Assert.Empty(_input.Actions);
    }

    [Fact]
    public void LowHp_PressesHpPotionKey_OncePerCooldown()
    {
        var runner = Runner();
        _vision.Next = FakeVision.Snapshot(hp: 40, mp: 100, stm: 100);

        runner.Tick();
        runner.Tick();
        _time.Advance(TimeSpan.FromSeconds(2));
        runner.Tick();

        Assert.Equal(["key 2", "key 2"], _input.Actions);
    }

    [Fact]
    public void RefusedKeyPress_IsNotCountedAsUsed()
    {
        var runner = Runner();
        _vision.Next = FakeVision.Snapshot(hp: 40, mp: 100, stm: 100);
        _input.Refuse = true;
        runner.Tick();

        _input.Refuse = false;
        runner.Tick(); // no cooldown was started by the refused press

        Assert.Equal(["key 2"], _input.Actions);
    }

    [Fact]
    public void PauseHotkey_StopsActing_AndReleasesInput()
    {
        var runner = Runner();
        _vision.Next = FakeVision.Snapshot(hp: 40, mp: 100, stm: 100);
        _control.SetPaused(true);

        runner.Tick();

        Assert.Equal(RunnerState.Paused, runner.State);
        Assert.Empty(_input.Actions);
        Assert.True(_input.ReleaseAllCalls > 0);

        _control.SetPaused(false);
        runner.Tick();
        Assert.Equal(RunnerState.Running, runner.State);
        Assert.Equal(["key 2"], _input.Actions);
    }

    [Fact]
    public void FocusLoss_PausesAutomatically_AndResumesWithFocus()
    {
        var runner = Runner();
        _vision.Next = FakeVision.Snapshot(hp: 40, mp: 100, stm: 100);
        _window.IsForeground = false;

        runner.Tick();
        Assert.Equal(RunnerState.FocusLost, runner.State);
        Assert.Empty(_input.Actions);

        _window.IsForeground = true;
        runner.Tick();
        Assert.Equal(RunnerState.Running, runner.State);
        Assert.Equal(["key 2"], _input.Actions);
    }

    [Fact]
    public void MinimizedGame_CountsAsFocusLost()
    {
        var runner = Runner();
        _window.IsMinimized = true;

        runner.Tick();

        Assert.Equal(RunnerState.FocusLost, runner.State);
    }

    [Fact]
    public void ResizedWindow_PausesUntilSizeIsBack()
    {
        var runner = Runner();
        _vision.Next = FakeVision.Snapshot(hp: 40, mp: 100, stm: 100);
        _window.ClientSize = (1637, 1000);

        runner.Tick();
        Assert.Equal(RunnerState.WrongWindowSize, runner.State);
        Assert.Empty(_input.Actions);

        _window.ClientSize = (1600, 900);
        runner.Tick();
        Assert.Equal(RunnerState.Running, runner.State);
        Assert.Equal(["key 2"], _input.Actions);
    }

    [Fact]
    public void QuitHotkey_Stops()
    {
        var runner = Runner();
        _control.RequestQuit();

        runner.Tick();

        Assert.Equal(RunnerState.Stopped, runner.State);
        Assert.Equal("quit hotkey", runner.StopReason);
    }

    [Fact]
    public void ClosedWindow_Stops()
    {
        var runner = Runner();
        _window.Exists = false;

        runner.Tick();

        Assert.Equal(RunnerState.Stopped, runner.State);
    }

    [Fact]
    public void HudHidden_NoInput_ThenStopsAfterTimeout()
    {
        var runner = Runner();
        runner.Tick();
        _vision.Next = FakeVision.HudHidden();

        runner.Tick();
        Assert.Equal(RunnerState.HudHidden, runner.State);

        _time.Advance(TimeSpan.FromSeconds(19));
        runner.Tick();
        Assert.Equal(RunnerState.HudHidden, runner.State);

        _time.Advance(TimeSpan.FromSeconds(1));
        runner.Tick();
        Assert.Equal(RunnerState.Stopped, runner.State);
        Assert.Empty(_input.Actions);
    }

    [Fact]
    public void HudBackInTime_KeepsRunning()
    {
        var runner = Runner();
        runner.Tick();
        _vision.Next = FakeVision.HudHidden();
        runner.Tick();
        _time.Advance(TimeSpan.FromSeconds(15));

        _vision.Next = FakeVision.Snapshot(100, 100, 100);
        runner.Tick();
        _time.Advance(TimeSpan.FromSeconds(15));
        _vision.Next = FakeVision.HudHidden();
        runner.Tick();

        Assert.Equal(RunnerState.HudHidden, runner.State);
    }

    [Fact]
    public void TimePaused_DoesNotCountTowardsHudTimeout()
    {
        var runner = Runner();
        runner.Tick();
        _vision.Next = FakeVision.HudHidden();
        runner.Tick();

        _control.SetPaused(true);
        runner.Tick();
        _time.Advance(TimeSpan.FromMinutes(10));
        _control.SetPaused(false);
        runner.Tick();

        Assert.Equal(RunnerState.HudHidden, runner.State);
    }

    [Fact]
    public void NoFrames_StopAfterHudTimeout()
    {
        var runner = Runner();
        runner.Tick();
        _capture.NoFrame = true;

        _time.Advance(TimeSpan.FromSeconds(20));
        runner.Tick();

        Assert.Equal(RunnerState.Stopped, runner.State);
    }
}

public class WatchdogTests
{
    [Fact]
    public void ExpiresAfterTimeout_AndKickResets()
    {
        var time = new FakeTimeProvider();
        var watchdog = new Watchdog(TimeSpan.FromSeconds(10), time);

        time.Advance(TimeSpan.FromSeconds(9));
        Assert.False(watchdog.Expired);
        watchdog.Kick();
        time.Advance(TimeSpan.FromSeconds(9));
        Assert.False(watchdog.Expired);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(watchdog.Expired);
    }
}
