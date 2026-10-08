using System.Diagnostics;
using System.Globalization;
using BotPriston.Core.Geometry;
using BotPriston.Core.Targeting;
using BotPriston.Core.Vision;
using OpenCvSharp;

namespace BotPriston.App.Commands;

/// <summary>
/// probe: calibration sweep — visits every probe point, measures how fast the target panel reacts
/// and saves frames + a CSV + a hit map for offline analysis. Never clicks.
/// find: runs the real target finder and leaves the cursor on the monster. Never clicks.
/// </summary>
public static class TargetingCommands
{
    public static int Probe(BotContext ctx, CommandLine cmd, CancellationToken cancel)
    {
        int maxPoints = cmd.Int("points", int.MaxValue, min: 1);
        int windowMs = cmd.Int("window", 300, min: 20);
        int waitSeconds = cmd.Int("wait", 15, min: 1);
        cmd.EnsureAllConsumed();

        var window = ctx.FindGameWindow(requireExpectedSize: true);
        ctx.WarnIfInputBlocked(window);
        using var capture = ctx.CreateCaptureSource(window);
        using var vision = ctx.CreateVision();
        var input = ctx.CreateInput(window);
        var points = SweepPattern.Generate(ctx.Config.Targeting, ctx.Config.Window.ExpectedClientWidth, ctx.Config.Window.ExpectedClientHeight)
            .Take(maxPoints).ToList();
        if (!ctx.WaitForFocus(window, waitSeconds, cancel)) return 1;

        var dir = Path.Combine(ctx.SamplesDir, $"probe_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        ctx.Log.Information("Probing {Count} points (window {Window} ms). Frames and results go to {Dir}", points.Count, windowMs, dir);

        using var csv = new StreamWriter(Path.Combine(dir, "probe.csv"));
        csv.WriteLine("index,x,y,cursor_at_move,first_enemy_ms,cursor,red_px,green_px,panel,panel_hp,saved");

        Mat? mapBase = null;
        var results = new Dictionary<PixelPoint, bool>();
        var latencies = new List<double>();
        CursorKind previous = CursorKind.Neutral;
        try
        {
            for (int i = 0; i < points.Count && !cancel.IsCancellationRequested; i++)
            {
                var point = points[i];
                if (!input.MoveMouse(point))
                {
                    ctx.Log.Warning("Mouse move refused at {Point} (game lost focus?); stopping", point);
                    break;
                }

                var watch = Stopwatch.StartNew();
                CursorReading? atMove = null, last = null;
                TargetPanel? panel = null;
                double firstEnemy = -1;
                Mat? lastImage = null;
                while (watch.ElapsedMilliseconds < windowMs)
                {
                    using var frame = capture.Grab();
                    if (frame is null) break;
                    var cursor = vision.Cursor.Detect(frame.Image, point);
                    atMove ??= cursor;
                    last = cursor;
                    if (firstEnemy < 0 && cursor.Kind == CursorKind.Enemy)
                        firstEnemy = watch.Elapsed.TotalMilliseconds;
                    lastImage?.Dispose();
                    lastImage = frame.Image.Clone();
                    Thread.Sleep(10);
                }

                if (last is null || lastImage is null)
                {
                    ctx.Log.Warning("No frames at {Point}", point);
                    continue;
                }

                panel = vision.TargetPanel.Detect(lastImage);
                mapBase ??= lastImage.Clone();
                bool hit = last.Kind == CursorKind.Enemy;
                results[point] = hit;
                if (firstEnemy >= 0) latencies.Add(firstEnemy);

                // Frames are saved unmarked (the file name has the mouse position) so they can be re-analyzed.
                bool save = last.Kind != CursorKind.Neutral || last.Kind != previous || i % 10 == 0;
                string saved = "";
                if (save)
                {
                    saved = $"{i:D3}_{point.X}_{point.Y}_{last.Kind}.png";
                    Cv2.ImWrite(Path.Combine(dir, saved), lastImage);
                }
                lastImage.Dispose();
                previous = last.Kind;

                csv.WriteLine(string.Join(',', i, point.X, point.Y, atMove?.Kind, firstEnemy.ToString("F0", CultureInfo.InvariantCulture),
                    last.Kind, last.EnemyPixels, last.NeutralPixels, panel.Status,
                    panel.HpPercent?.ToString("F1", CultureInfo.InvariantCulture) ?? "", saved));
                ctx.Log.Debug("[{I}] {Point}: {Cursor} (first enemy {Latency} ms)", i, point, last, firstEnemy);
            }
        }
        finally
        {
            if (mapBase is not null)
            {
                OverlayRenderer.DrawSweep(mapBase, ctx.Config.Targeting, points,
                    p => results.TryGetValue(p, out var hit) ? hit : null);
                Cv2.ImWrite(Path.Combine(dir, "map.png"), mapBase);
                mapBase.Dispose();
            }
        }

        latencies.Sort();
        ctx.Log.Information("Probed {Done}/{Count} points: {Hits} with a monster under the cursor. Cursor latency ms: min {Min}, median {Median}, max {Max}",
            results.Count, points.Count, results.Values.Count(h => h),
            latencies.Count > 0 ? latencies[0].ToString("F0") : "-",
            latencies.Count > 0 ? latencies[latencies.Count / 2].ToString("F0") : "-",
            latencies.Count > 0 ? latencies[^1].ToString("F0") : "-");
        ctx.Log.Information("Results: {Dir}", dir);
        return 0;
    }

    public static int Find(BotContext ctx, CommandLine cmd, CancellationToken cancel)
    {
        int repeat = cmd.Int("repeat", 1, min: 1);
        int pauseMs = cmd.Int("pause", 2000, min: 0);
        int waitSeconds = cmd.Int("wait", 15, min: 1);
        cmd.EnsureAllConsumed();

        var window = ctx.FindGameWindow(requireExpectedSize: true);
        ctx.WarnIfInputBlocked(window);
        using var capture = ctx.CreateCaptureSource(window);
        using var vision = ctx.CreateVision();
        var input = ctx.CreateInput(window);
        var finder = new HoverTargetFinder(ctx.Config.Targeting, ctx.Config.Window.ExpectedClientWidth,
            ctx.Config.Window.ExpectedClientHeight, input, capture, vision.Cursor, vision.TargetPanel, ctx.Log);
        ctx.Log.Information("Sweep has {Count} probe points", finder.Points.Count);
        if (!ctx.WaitForFocus(window, waitSeconds, cancel)) return 1;

        int found = 0;
        for (int i = 0; i < repeat && !cancel.IsCancellationRequested; i++)
        {
            if (i > 0 && cancel.WaitHandle.WaitOne(pauseMs)) break;
            var result = finder.Find(cancel);
            ctx.Log.Information("Search {N}/{Total}: {Outcome} — {Probes} probes, {Ms:F0} ms{Where}",
                i + 1, repeat, result.Outcome, result.Probes, result.Elapsed.TotalMilliseconds,
                result.Target is { } t ? $", at {t.Point}" : "");
            if (result.Outcome == FindOutcome.Found) found++;
            if (result.Outcome == FindOutcome.Aborted) break;
        }

        ctx.Log.Information("Found a target in {Found}/{Repeat} searches", found, repeat);
        return found > 0 ? 0 : 1;
    }
}
