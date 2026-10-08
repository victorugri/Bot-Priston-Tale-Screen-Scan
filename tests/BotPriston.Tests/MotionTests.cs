using BotPriston.Core.Bot;
using BotPriston.Core.Capture;
using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using BotPriston.Core.Vision;
using Microsoft.Extensions.Time.Testing;
using OpenCvSharp;

namespace BotPriston.Tests;

/// <summary>Real screenshot moved by a whole number of pixels, like the camera following a walking character.</summary>
internal static class Scenes
{
    public static Mat Load() => Cv2.ImRead(Path.Combine(TestPaths.Samples, "20261007_183857_cursor_ground.png"), ImreadModes.Color);

    public static Mat Shifted(Mat scene, int dx, int dy)
    {
        using var m = new Mat(2, 3, MatType.CV_64FC1);
        m.Set(0, 0, 1.0); m.Set(0, 1, 0.0); m.Set(0, 2, (double)dx);
        m.Set(1, 0, 0.0); m.Set(1, 1, 1.0); m.Set(1, 2, (double)dy);
        var result = new Mat();
        Cv2.WarpAffine(scene, result, m, scene.Size(), InterpolationFlags.Linear, BorderTypes.Replicate);
        return result;
    }
}

public class MotionDetectorTests : IDisposable
{
    private readonly Mat _scene = Scenes.Load();
    private readonly MotionDetector _detector = new(TestConfig.Load().Vision.Motion);

    [Fact]
    public void StandingStill_IsNotMoving()
    {
        for (int i = 0; i < 5; i++)
        {
            var m = _detector.Update(_scene);
            Assert.False(m.Moving);
            Assert.False(m.Walking);
        }
    }

    [Fact]
    public void GroundSlidingEveryFrame_IsWalking_AfterThreeFrames()
    {
        var readings = new List<MotionReading>();
        for (int i = 0; i < 5; i++)
        {
            using var frame = Scenes.Shifted(_scene, -8 * i, 3 * i);
            readings.Add(_detector.Update(frame));
        }

        Assert.False(readings[0].Moving); // no previous frame yet
        Assert.All(readings.Skip(1), r => Assert.True(r.Moving));
        Assert.False(readings[2].Walking);
        Assert.True(readings[3].Walking);
        Assert.InRange(readings[4].ShiftX, -9, -7);
        Assert.InRange(readings[4].ShiftY, 2, 4);
    }

    [Fact]
    public void SingleShake_IsNotWalking()
    {
        using var shaken = Scenes.Shifted(_scene, 5, 0);

        _detector.Update(_scene);
        var shake = _detector.Update(shaken);
        var back = _detector.Update(_scene);
        var still = _detector.Update(_scene);

        Assert.True(shake.Moving);
        Assert.False(shake.Walking || back.Walking || still.Walking);
    }

    [Fact]
    public void Reset_ForgetsHistory()
    {
        _detector.Update(_scene);
        _detector.Reset();
        using var moved = Scenes.Shifted(_scene, 10, 0);

        Assert.False(_detector.Update(moved).Moving);
    }

    public void Dispose()
    {
        _scene.Dispose();
        _detector.Dispose();
    }
}

public class BotRunnerWalkingTests : IDisposable
{
    private readonly Mat _scene = Scenes.Load();
    private readonly FakeTimeProvider _time = new();
    private readonly FakeInput _input = new();
    private readonly FakeWindow _window = new();
    private readonly FakeVision _vision = new();
    private readonly BotControl _control = new();
    private readonly ScriptedFinder _finder = new();
    private readonly BotConfig _config = TestConfig.Load();

    /// <summary>Real-size frames; once <see cref="Walk"/> is set the ground slides 8 px per frame.</summary>
    private sealed class SlidingCapture(Mat scene) : ICaptureSource
    {
        private int _offset;
        public bool Walk { get; set; }
        public string Name => "Sliding";
        public Frame? Grab()
        {
            if (Walk) _offset += 8;
            return new Frame(Scenes.Shifted(scene, -_offset, 0), DateTimeOffset.Now, Name);
        }
        public void Dispose() { }
    }

    private BotRunner Runner(ICaptureSource capture) => new(_config, capture, _vision, _input, _window, _control, _time, TestLog.Silent,
        onProgress => new CombatBrain(_config.Combat, _finder, new FakeCursor(), _input, _time, TestLog.Silent, onProgress));

    [Fact]
    public void Walking_ForcesButtonsUp_AndDropsTheTarget()
    {
        var capture = new SlidingCapture(_scene);
        var runner = Runner(capture);
        _finder.WillFind(new PixelPoint(860, 465));
        for (int i = 0; i < 3; i++) runner.Tick(); // Recover, SearchTarget, Engage -> Attack
        Assert.Equal(BrainState.Attack, runner.Brain!.State);
        int forcedBefore = _input.ForceReleaseCalls;

        capture.Walk = true;
        for (int i = 0; i < 4; i++) runner.Tick();

        Assert.Equal(1, runner.WalkEpisodes);
        Assert.True(_input.ForceReleaseCalls > forcedBefore);
        Assert.Equal(BrainState.SearchTarget, runner.Brain.State);
        Assert.Equal(1, runner.Brain.WalkStops);
    }

    [Fact]
    public void WalkingToADistantAttacker_IsAllowed()
    {
        var capture = new SlidingCapture(_scene);
        var wide = new ScriptedFinder();
        wide.WillFind(new PixelPoint(560, 300));
        var runner = new BotRunner(_config, capture, _vision, _input, _window, _control, _time, TestLog.Silent,
            onProgress => new CombatBrain(_config.Combat, _finder, new FakeCursor(), _input, _time, TestLog.Silent, onProgress, wide));

        _vision.Next = FakeVision.Snapshot(hp: 90, mp: 100, stm: 100);
        runner.Tick(); // Recover -> SearchTarget
        _time.Advance(TimeSpan.FromSeconds(1));
        _vision.Next = FakeVision.Snapshot(hp: 80, mp: 100, stm: 100);
        runner.Tick(); // nothing close, losing HP -> wide search -> Engage
        runner.Tick(); // Attack
        Assert.True(runner.Brain!.AllowWalking);

        capture.Walk = true;
        for (int i = 0; i < 4; i++) runner.Tick();

        Assert.Equal(0, runner.WalkEpisodes);
        Assert.Equal(BrainState.Attack, runner.Brain.State);
    }

    [Fact]
    public void StandingStill_NeverTriggers()
    {
        var runner = Runner(new SlidingCapture(_scene));

        for (int i = 0; i < 10; i++) runner.Tick();

        Assert.Equal(0, runner.WalkEpisodes);
    }

    [Fact]
    public void EveryResumeAndEverySearch_ForceButtonsUp()
    {
        var runner = Runner(new SlidingCapture(_scene));

        runner.Tick(); // Starting -> Running
        Assert.Equal(1, _input.ForceReleaseCalls);

        runner.Tick(); // Recover -> SearchTarget
        runner.Tick(); // search
        Assert.Equal(2, _input.ForceReleaseCalls);
    }

    public void Dispose() => _scene.Dispose();
}
