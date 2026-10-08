using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using OpenCvSharp;

namespace BotPriston.Core.Vision;

/// <summary>Draws detections on top of a frame for the debug window.</summary>
public sealed class OverlayRenderer(VisionConfig config)
{
    private static readonly Scalar Ok = new(80, 220, 80);
    private static readonly Scalar Bad = new(60, 60, 255);
    private static readonly Scalar Roi = new(255, 255, 255);
    private static readonly Scalar Surface = new(0, 255, 255);

    /// <summary>
    /// Draws the hover sweep: probe points (optionally colored by result) and excluded areas.
    /// </summary>
    public static void DrawSweep(Mat canvas, TargetingConfig targeting, IEnumerable<PixelPoint> points,
        Func<PixelPoint, bool?>? result = null)
    {
        foreach (var r in targeting.Exclusions)
            Cv2.Rectangle(canvas, r.ToCvRect(), Bad, 1);
        Cv2.Ellipse(canvas, new Point(targeting.Anchor.X, targeting.Anchor.Y),
            new Size(targeting.RadiusX, targeting.RadiusY), 0, 0, 360, Roi, 1);
        Cv2.Circle(canvas, new Point(targeting.Anchor.X, targeting.Anchor.Y), targeting.MinRadius, Roi, 1);

        foreach (var p in points)
        {
            var hit = result?.Invoke(p);
            var color = hit switch { true => Ok, false => new Scalar(160, 160, 160), null => Surface };
            Cv2.Circle(canvas, new Point(p.X, p.Y), hit == true ? 6 : 3, color, hit == true ? -1 : 1, LineTypes.AntiAlias);
        }
    }

    /// <summary>Returns a new image (the input is not modified).</summary>
    public Mat Render(Mat frame, VisionSnapshot snapshot, string? caption = null,
        (PixelPoint Mouse, CursorReading Reading)? cursor = null)
    {
        var canvas = frame.Clone();

        if (cursor is { } c)
        {
            var box = config.Cursor.GemBox.Offset(c.Mouse.X, c.Mouse.Y);
            var color = c.Reading.Kind switch { CursorKind.Enemy => Bad, CursorKind.Neutral => Ok, _ => Roi };
            Cv2.Rectangle(canvas, new Rect(box.X - 1, box.Y - 1, box.Width + 2, box.Height + 2), color, 1);
        }

        var hudColor = snapshot.Hud.Visible ? Ok : Bad;
        Cv2.Rectangle(canvas, config.Hud.Roi.ToCvRect(), hudColor, 1);

        if (snapshot.Bars is { } bars)
        {
            DrawBar(canvas, config.PlayerBars.Stm, bars.Stm);
            DrawBar(canvas, config.PlayerBars.Hp, bars.Hp);
            DrawBar(canvas, config.PlayerBars.Mp, bars.Mp);
        }

        if (snapshot.Target is { } target)
        {
            var panel = config.TargetPanel;
            Cv2.Rectangle(canvas, panel.Border.ToCvRect(), target.Visible ? Ok : Bad, 1);
            if (target.HpPercent is { } hp)
            {
                var fill = panel.HpBar.Fill;
                int x = fill.X + (int)Math.Round(fill.Width * hp / 100.0);
                Cv2.Line(canvas, new Point(x, fill.Y - 3), new Point(x, fill.Bottom + 2), Surface, 1);
            }
        }

        var lines = new List<(string Text, Scalar Color)>();
        if (caption is not null) lines.Add((caption, Roi));
        lines.Add((snapshot.Hud.Visible ? $"HUD ok  score {snapshot.Hud.Score:F2}" : $"HUD NOT VISIBLE  score {snapshot.Hud.Score:F2}", hudColor));
        if (snapshot.Bars is { } b)
            lines.Add(($"HP {b.Hp.Percent,5:F1}%   MP {b.Mp.Percent,5:F1}%   STM {b.Stm.Percent,5:F1}%", Ok));
        if (snapshot.Target is { } t)
            lines.Add(($"Target: {t}  (border {t.BorderScore:F2})", t.Visible ? Ok : Roi));
        if (snapshot.Skills is { } skills)
        {
            foreach (var (orb, state) in new[] { (config.SkillOrbs.Left, skills.Left), (config.SkillOrbs.Right, skills.Right) })
                Cv2.Circle(canvas, new Point(orb.Center.X, orb.Center.Y), orb.Radius + 2, state.Ready ? Ok : Bad, 1, LineTypes.AntiAlias);
            lines.Add(($"Skills: {skills}", Roi));
        }
        if (cursor is { } cr)
            lines.Add(($"Cursor at {cr.Mouse}: {cr.Reading}", cr.Reading.Kind == CursorKind.Enemy ? Bad : Ok));

        DrawTextPanel(canvas, lines);
        return canvas;
    }

    private static void DrawBar(Mat canvas, BarConfig bar, BarReading reading)
    {
        var roi = bar.Roi;
        Cv2.Rectangle(canvas, new Rect(roi.X - 1, roi.Y - 1, roi.Width + 2, roi.Height + 2), Roi, 1);
        Cv2.Line(canvas, new Point(roi.X - 4, reading.TopY), new Point(roi.Right + 3, reading.TopY), Surface, 1);
        DrawText(canvas, $"{reading.Percent:F0}", new Point(roi.X - 2, roi.Y - 8), Surface, 0.4);
    }

    private static void DrawTextPanel(Mat canvas, List<(string Text, Scalar Color)> lines)
    {
        const int lineHeight = 22;
        var panel = new PixelRect(8, 8, 520, 10 + lineHeight * lines.Count);
        using (var roi = new Mat(canvas, panel.ToCvRect()))
        using (var dark = new Mat(roi.Size(), roi.Type(), Scalar.All(0)))
            Cv2.AddWeighted(roi, 0.35, dark, 0.65, 0, roi);

        for (int i = 0; i < lines.Count; i++)
            DrawText(canvas, lines[i].Text, new Point(16, 28 + i * lineHeight), lines[i].Color, 0.55);
    }

    private static void DrawText(Mat canvas, string text, Point origin, Scalar color, double scale)
    {
        Cv2.PutText(canvas, text, origin + new Point(1, 1), HersheyFonts.HersheySimplex, scale, Scalar.All(0), 2, LineTypes.AntiAlias);
        Cv2.PutText(canvas, text, origin, HersheyFonts.HersheySimplex, scale, color, 1, LineTypes.AntiAlias);
    }
}
