using BotPriston.Core.Capture;
using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using BotPriston.Core.Input;
using BotPriston.Core.Targeting;
using BotPriston.Core.Vision;
using OpenCvSharp;

namespace BotPriston.Tests;

public class SweepPatternTests
{
    private static TargetingConfig Config() => TestConfig.Load().Targeting;

    [Fact]
    public void Points_StayInsideClientEllipse_AndOutsideExclusions()
    {
        var config = Config();

        var points = SweepPattern.Generate(config, 1600, 900);

        Assert.NotEmpty(points);
        foreach (var p in points)
        {
            Assert.InRange(p.X, config.EdgeMargin, 1600 - config.EdgeMargin - 1);
            Assert.InRange(p.Y, config.EdgeMargin, 900 - config.EdgeMargin - 1);
            Assert.False(SweepPattern.IsExcluded(p, config), $"{p} is in an excluded area");
            double dx = (p.X - config.Anchor.X) / (double)config.RadiusX, dy = (p.Y - config.Anchor.Y) / (double)config.RadiusY;
            Assert.True(dx * dx + dy * dy <= 1.0001, $"{p} is outside the ellipse");
        }
    }

    [Fact]
    public void Points_SkipTheCharacter_AndStartNearIt()
    {
        var config = Config();

        var points = SweepPattern.Generate(config, 1600, 900);

        double Dist(PixelPoint p) => Math.Sqrt(Math.Pow(p.X - config.Anchor.X, 2) + Math.Pow(p.Y - config.Anchor.Y, 2));
        Assert.All(points, p => Assert.True(Dist(p) >= config.MinRadius));
        Assert.True(Dist(points[0]) < config.MinRadius + config.Step * 1.5, "sweep should start next to the character");
        Assert.True(Dist(points[0]) < Dist(points[^1]));
    }

    [Fact]
    public void Points_AreUnique_AndDenseEnough()
    {
        var config = Config();

        var points = SweepPattern.Generate(config, 1600, 900);

        Assert.Equal(points.Count, points.Distinct().Count());
        // A spot inside the search area always has a probe within one grid step.
        var spot = new PixelPoint(config.Anchor.X + config.RadiusX / 2 + 7, config.Anchor.Y - config.RadiusY / 3 - 3);
        Assert.Contains(points, p => Math.Sqrt(Math.Pow(p.X - spot.X, 2) + Math.Pow(p.Y - spot.Y, 2)) <= config.Step);
    }
}

public class CursorDetectorTests
{
    private readonly CursorDetector _detector = new(TestConfig.Load().Vision.Cursor);

    [Fact]
    public void GemAwayFromTheMouse_IsUnknown()
    {
        using var image = Cv2.ImRead(Path.Combine(TestPaths.Samples, "20261007_183857_cursor_ground.png"), ImreadModes.Color);

        // The real cursor is at (725,465); 200 px away there is only ground.
        Assert.Equal(CursorKind.Unknown, _detector.Detect(image, new PixelPoint(925, 465)).Kind);
    }

    [Fact]
    public void MouseAtTheEdge_DoesNotThrow()
    {
        using var image = new Mat(900, 1600, MatType.CV_8UC3, Scalar.All(0));

        Assert.Equal(CursorKind.Unknown, _detector.Detect(image, new PixelPoint(1599, 899)).Kind);
    }
}

public class HoverTargetFinderTests : IDisposable
{
    private static readonly PixelPoint GroundCursorAt = new(725, 465);
    private static readonly PixelPoint EnemyCursorAt = new(762, 400);
    private const int PatchW = 26, PatchH = 24;

    private readonly Mat _background;
    private readonly Mat _greenCursor;
    private readonly Mat _redCursor;
    private readonly VisionConfig _vision = TestConfig.Load().Vision;

    // A wide grid of its own, so these tests don't depend on how small the user tunes the search area.
    private readonly TargetingConfig _config = WideGrid();

    private static TargetingConfig WideGrid()
    {
        var config = TestConfig.Load().Targeting;
        config.RadiusX = 450;
        config.RadiusY = 300;
        config.MinRadius = 60;
        config.Step = 75;
        config.ReachX = 0; // the whole area in reach unless a test says otherwise
        config.ReachY = 0;
        return config;
    }

    public HoverTargetFinderTests()
    {
        using var ground = Load("20261007_183857_cursor_ground.png");
        using var enemy = Load("20261007_183857_cursor_enemy.png");
        _greenCursor = new Mat(ground, new Rect(GroundCursorAt.X, GroundCursorAt.Y, PatchW, PatchH)).Clone();
        _redCursor = new Mat(enemy, new Rect(EnemyCursorAt.X, EnemyCursorAt.Y, PatchW, PatchH)).Clone();
        _background = Load("20261007_112434_831_farm.png"); // no cursor gem anywhere near the probe points
    }

    private static Mat Load(string name) => Cv2.ImRead(Path.Combine(TestPaths.Samples, name), ImreadModes.Color);

    /// <summary>A screen with the real game cursor (green or red) pasted where the fake mouse is.</summary>
    private Mat Screen(PixelPoint? mouse, bool overMonster)
    {
        var screen = _background.Clone();
        if (mouse is { } m && m.X + PatchW <= screen.Width && m.Y + PatchH <= screen.Height)
            (overMonster ? _redCursor : _greenCursor).CopyTo(new Mat(screen, new Rect(m.X, m.Y, PatchW, PatchH)));
        return screen;
    }

    private sealed class FakeScreen(MouseTrackingInput input, Func<PixelPoint?, int, Mat> show) : ICaptureSource
    {
        private int _grabs;
        public string Name => "FakeScreen";
        public Frame? Grab() => new(show(input.Mouse, _grabs++), DateTimeOffset.Now, Name);
        public void Dispose() { }
    }

    private sealed class MouseTrackingInput : IInputSink
    {
        public PixelPoint? Mouse { get; private set; }
        public int Moves { get; private set; }
        public int RefuseAfter { get; set; } = int.MaxValue;
        public bool PressKey(KeyChord key) => true;
        public bool MoveMouse(PixelPoint p)
        {
            if (Moves >= RefuseAfter) return false;
            Moves++;
            Mouse = p;
            return true;
        }
        public bool MouseDown(MouseButton button) => true;
        public bool MouseUp(MouseButton button) => true;
        public bool Click(MouseButton button) => true;
        public void ReleaseAll() { }
        public void ForceReleaseButtons() { }
    }

    private HoverTargetFinder Finder(MouseTrackingInput input, ICaptureSource capture) =>
        new(_config, 1600, 900, input, capture, new CursorDetector(_vision.Cursor), new TargetPanelDetector(_vision.TargetPanel),
            TestLog.Silent, sleep: _ => { });

    [Fact]
    public void FindsMonster_WhereTheCursorTurnsRed()
    {
        var input = new MouseTrackingInput();
        var points = SweepPattern.Generate(_config, 1600, 900);
        var monster = points[17];
        var screen = new FakeScreen(input, (mouse, _) => Screen(mouse, overMonster: mouse == monster));

        var result = Finder(input, screen).Find(CancellationToken.None);

        Assert.Equal(FindOutcome.Found, result.Outcome);
        Assert.Equal(monster, result.Target!.Point);
        Assert.Equal(18, result.Probes);
        Assert.Equal(CursorKind.Enemy, result.Target.Cursor.Kind);
        Assert.Equal(monster, input.Mouse); // cursor left on the monster
    }

    [Fact]
    public void LingeringTargetPanel_IsNotAHit()
    {
        // The real failure seen in game: the panel kept showing a monster hovered earlier.
        using var lingering = Load("20261007_183857_cursor_ground.png");
        var input = new MouseTrackingInput();
        var screen = new FakeScreen(input, (_, _) => lingering.Clone());

        var result = Finder(input, screen).Find(CancellationToken.None);

        Assert.Equal(FindOutcome.NothingFound, result.Outcome);
    }

    [Fact]
    public void UnconfirmedHit_IsSkipped()
    {
        var input = new MouseTrackingInput();
        var points = SweepPattern.Generate(_config, 1600, 900);
        bool walkedAway = false;
        var screen = new FakeScreen(input, (mouse, _) =>
        {
            // A monster is under points[3] only for the first frame read there.
            if (mouse == points[3] && !walkedAway) { walkedAway = true; return Screen(mouse, overMonster: true); }
            return Screen(mouse, overMonster: mouse == points[9]);
        });

        var result = Finder(input, screen).Find(CancellationToken.None);

        Assert.Equal(FindOutcome.Found, result.Outcome);
        Assert.Equal(points[9], result.Target!.Point);
    }

    [Fact]
    public void EmptyScreen_ProbesEverything_AndFindsNothing()
    {
        var input = new MouseTrackingInput();
        var screen = new FakeScreen(input, (mouse, _) => Screen(mouse, overMonster: false));
        var finder = Finder(input, screen);

        var result = finder.Find(CancellationToken.None);

        Assert.Equal(FindOutcome.NothingFound, result.Outcome);
        Assert.Equal(finder.Points.Count, result.Probes);
    }

    [Fact]
    public void PointsInReach_AreProbedFirst()
    {
        _config.ReachX = 130;
        _config.ReachY = 95;

        var points = SweepPattern.Generate(_config, 1600, 900);

        int firstOut = points.ToList().FindIndex(p => !_config.InReach(p));
        Assert.True(firstOut > 0);
        Assert.All(points.Take(firstOut), p => Assert.True(_config.InReach(p)));
        Assert.All(points.Skip(firstOut), p => Assert.False(_config.InReach(p)));
    }

    [Fact]
    public void MonsterOutOfReach_IsReportedButNotATarget()
    {
        _config.ReachX = 130;
        _config.ReachY = 95;
        var input = new MouseTrackingInput();
        var points = SweepPattern.Generate(_config, 1600, 900);
        var far = points.First(p => !_config.InReach(p) && p.Y < _config.Anchor.Y - 100); // straight up: far on the map
        var screen = new FakeScreen(input, (mouse, _) => Screen(mouse, overMonster: mouse == far));

        var result = Finder(input, screen).Find(CancellationToken.None);

        Assert.Equal(FindOutcome.OutOfReach, result.Outcome);
        Assert.Equal(far, result.Target!.Point);
        Assert.Equal(points.ToList().IndexOf(far) + 1, result.Probes); // stops there: everything closer was probed
    }

    [Fact]
    public void WiderUnderAttackSearch_HasNoReachLimit()
    {
        _config.ReachX = 130;
        _config.ReachY = 95;

        var wide = _config.WithArea(450, 300, 75);

        Assert.True(wide.InReach(new PixelPoint(_config.Anchor.X, _config.Anchor.Y - 250)));
    }

    [Fact]
    public void RefusedMouseMove_Aborts()
    {
        var input = new MouseTrackingInput { RefuseAfter = 5 };
        var screen = new FakeScreen(input, (mouse, _) => Screen(mouse, overMonster: false));

        var result = Finder(input, screen).Find(CancellationToken.None);

        Assert.Equal(FindOutcome.Aborted, result.Outcome);
        Assert.Equal(5, result.Probes);
    }

    public void Dispose()
    {
        _background.Dispose();
        _greenCursor.Dispose();
        _redCursor.Dispose();
    }
}
