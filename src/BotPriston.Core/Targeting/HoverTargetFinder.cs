using System.Diagnostics;
using BotPriston.Core.Capture;
using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using BotPriston.Core.Input;
using BotPriston.Core.Vision;
using Serilog;

namespace BotPriston.Core.Targeting;

/// <summary>
/// Moves the mouse over a grid of points, nearest to the character first, and stops where the
/// game's cursor gem turns red (a monster is under the cursor). Uses the game's own picking, so it
/// works for any monster on any background. The target panel is read afterwards for information only:
/// it keeps showing the last hovered monster, so it can't tell what is under the cursor now.
/// </summary>
public sealed class HoverTargetFinder : ITargetFinder
{
    private readonly TargetingConfig _config;
    private readonly IInputSink _input;
    private readonly ICaptureSource _capture;
    private readonly ICursorReader _cursor;
    private readonly TargetPanelDetector _panel;
    private readonly IReadOnlyList<PixelPoint> _points;
    private readonly Action<TimeSpan> _sleep;
    private readonly ILogger _log;

    public HoverTargetFinder(TargetingConfig config, int clientWidth, int clientHeight, IInputSink input,
        ICaptureSource capture, ICursorReader cursor, TargetPanelDetector panel, ILogger log, Action<TimeSpan>? sleep = null)
    {
        _config = config;
        _input = input;
        _capture = capture;
        _cursor = cursor;
        _panel = panel;
        _log = log;
        _sleep = sleep ?? Thread.Sleep;
        _points = SweepPattern.Generate(config, clientWidth, clientHeight);
    }

    public IReadOnlyList<PixelPoint> Points => _points;

    public FindResult Find(CancellationToken cancel, PixelPoint? near = null, int maxDistance = 0)
    {
        var watch = Stopwatch.StartNew();
        int probes = 0;
        TargetFound? outOfReach = null;
        foreach (var point in PointsFor(near, maxDistance))
        {
            if (cancel.IsCancellationRequested)
                return new FindResult(FindOutcome.Aborted, null, probes, watch.Elapsed, "cancelled");

            if (!_input.MoveMouse(point))
                return new FindResult(FindOutcome.Aborted, null, probes, watch.Elapsed, "mouse move refused (game not focused?)");
            probes++;

            _sleep(TimeSpan.FromMilliseconds(_config.HoverDelayMs));
            if (Read(point) is not { Cursor.Kind: CursorKind.Enemy })
                continue;

            // Confirm: the monster may have just walked through the cursor.
            _sleep(TimeSpan.FromMilliseconds(_config.ConfirmDelayMs));
            if (Read(point) is { Cursor.Kind: CursorKind.Enemy } confirmed)
            {
                var found = new TargetFound(point, confirmed.Cursor, confirmed.Panel, probes, watch.Elapsed);
                if (!_config.InReach(point))
                {
                    // Points in reach are probed first: in a full sweep nothing closer is left to find.
                    outOfReach ??= found;
                    if (near is null) break;
                    continue;
                }
                _log.Information("Target found at {Point} after {Probes} probes in {Ms:F0} ms (cursor {Cursor}, panel {Panel})",
                    point, probes, watch.Elapsed.TotalMilliseconds, confirmed.Cursor, confirmed.Panel);
                return new FindResult(FindOutcome.Found, found, probes, watch.Elapsed);
            }
            _log.Debug("Hit at {Point} not confirmed", point);
        }

        if (outOfReach is not null)
        {
            _log.Information("Monster at {Point} is out of attack reach ({Reach:F2} x reach): not clicking it ({Probes} probes, {Ms:F0} ms)",
                outOfReach.Point, _config.ReachDistance(outOfReach.Point), probes, watch.Elapsed.TotalMilliseconds);
            return new FindResult(FindOutcome.OutOfReach, outOfReach, probes, watch.Elapsed);
        }

        _log.Information("No target in {Probes} probes ({Ms:F0} ms)", probes, watch.Elapsed.TotalMilliseconds);
        return new FindResult(FindOutcome.NothingFound, null, probes, watch.Elapsed);
    }

    /// <summary>Full sweep, or the sweep points close to <paramref name="near"/> (the point itself first).</summary>
    private IEnumerable<PixelPoint> PointsFor(PixelPoint? near, int maxDistance)
    {
        if (near is not { } center)
            return _points;

        static double Dist(PixelPoint a, PixelPoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        var local = _points.Where(p => p != center && Dist(p, center) <= maxDistance).OrderBy(p => Dist(p, center));
        return SweepPattern.IsExcluded(center, _config) ? local : local.Prepend(center);
    }

    private (CursorReading Cursor, TargetPanel Panel)? Read(PixelPoint mouse)
    {
        using var frame = _capture.Grab();
        return frame is null ? null : (_cursor.Detect(frame.Image, mouse), _panel.Detect(frame.Image));
    }
}
