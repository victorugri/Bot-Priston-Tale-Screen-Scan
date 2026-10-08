using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using OpenCvSharp;

namespace BotPriston.Core.Vision;

public enum TargetStatus
{
    /// <summary>No panel: nothing under the cursor and no current target.</summary>
    None,
    /// <summary>Panel without HP bar: a monster is under the cursor but not engaged.</summary>
    Hovered,
    /// <summary>Panel with HP bar above 0%.</summary>
    Engaged,
    /// <summary>Panel with an empty HP bar: the target died.</summary>
    Dead,
}

/// <param name="BorderScore">Lowest per-edge ridge score (0..1) of the panel border.</param>
/// <param name="HpPercent">HP of the target when its bar is shown, else null.</param>
public sealed record TargetPanel(TargetStatus Status, double BorderScore, double? HpPercent)
{
    public bool Visible => Status != TargetStatus.None;

    public override string ToString() => Status switch
    {
        TargetStatus.None => "no target",
        TargetStatus.Hovered => "hovered",
        _ => $"{Status.ToString().ToLowerInvariant()} {HpPercent:F1}%",
    };
}

/// <summary>Reads the top-right target panel: is it shown, is the HP bar shown, and how full is it.</summary>
public sealed class TargetPanelDetector(TargetPanelConfig config) : IDetector<TargetPanel>
{
    public TargetPanelConfig Config { get; } = config;

    public TargetPanel Detect(Mat frame)
    {
        double score = BorderScore(frame);
        if (score < Config.MinEdgeScore)
            return new TargetPanel(TargetStatus.None, score, null);

        if (!HpBarVisible(frame))
            return new TargetPanel(TargetStatus.Hovered, score, null);

        double hp = HpPercent(frame);
        return new TargetPanel(hp > 0 ? TargetStatus.Engaged : TargetStatus.Dead, score, hp);
    }

    /// <summary>Lowest fraction of ridge pixels over the 4 edges (corners skipped).</summary>
    public double BorderScore(Mat frame)
    {
        var b = Config.Border;
        int left = b.X, top = b.Y, right = b.Right - 1, bottom = b.Bottom - 1;
        if (left < 1 || top < 1 || right + 1 >= frame.Width || bottom + 1 >= frame.Height)
            return 0;

        using var gray = new Mat();
        using var hsv = new Mat();
        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.CvtColor(frame, hsv, ColorConversionCodes.BGR2HSV);
        byte G(int r, int c) => gray.At<byte>(r, c);
        byte S(int r, int c) => hsv.At<Vec3b>(r, c).Item1;

        const int skip = 3; // corners blend two edges
        double topScore = EdgeScore(left + skip, right - skip, x => (top, x), (1, 0));
        double bottomScore = EdgeScore(left + skip, right - skip, x => (bottom, x), (1, 0));
        double leftScore = EdgeScore(top + skip, bottom - skip, y => (y, left), (0, 1));
        double rightScore = EdgeScore(top + skip, bottom - skip, y => (y, right), (0, 1));
        return Math.Min(Math.Min(topScore, bottomScore), Math.Min(leftScore, rightScore));

        // (pr, pc) points across the line: horizontal edges compare the rows above/below,
        // vertical edges the columns left/right.
        double EdgeScore(int from, int to, Func<int, (int Row, int Col)> at, (int pr, int pc) across)
        {
            var (pr, pc) = across;
            int hits = 0, total = 0;
            for (int i = from; i <= to; i++)
            {
                var (r, c) = at(i);
                int gl = G(r, c), ga = G(r - pr, c - pc), gb = G(r + pr, c + pc);
                int sl = S(r, c), sa = S(r - pr, c - pc), sb = S(r + pr, c + pc);
                if (gl - Math.Max(ga, gb) >= Config.RidgeMinLumaDelta || Math.Min(sa, sb) - sl >= Config.RidgeMinSaturationDelta)
                    hits++;
                total++;
            }
            return total == 0 ? 0 : (double)hits / total;
        }
    }

    public bool HpBarVisible(Mat frame)
    {
        var bar = Config.HpBar;
        if (bar.FrameRows.Count == 0) return false;
        foreach (var row in bar.FrameRows)
        {
            if (!Inside(frame, row)) return false;
            using var region = new Mat(frame, row.ToCvRect());
            using var hsv = new Mat();
            Cv2.CvtColor(region, hsv, ColorConversionCodes.BGR2HSV);
            using var dark = new Mat();
            Cv2.InRange(hsv, new Scalar(0, 0, 0), new Scalar(179, 255, bar.FrameMaxValue), dark);
            if ((double)Cv2.CountNonZero(dark) / dark.Total() < bar.MinFrameDarkFraction)
                return false;
        }
        return true;
    }

    /// <summary>Position of the right end of the colored part, as a percentage of the bar width.</summary>
    public double HpPercent(Mat frame)
    {
        var bar = Config.HpBar;
        if (!Inside(frame, bar.Fill)) return 0;

        using var region = new Mat(frame, bar.Fill.ToCvRect());
        using var hsv = new Mat();
        Cv2.CvtColor(region, hsv, ColorConversionCodes.BGR2HSV);
        using var mask = HsvRange.Mask(hsv, bar.FillColors);
        using var columns = new Mat();
        Cv2.Reduce(mask, columns, ReduceDimension.Row, ReduceTypes.Avg, MatType.CV_64F);

        int width = bar.Fill.Width;
        int run = 0;
        for (int c = width - 1; c >= 0; c--)
        {
            bool filled = columns.At<double>(0, c) / 255.0 >= bar.MinColumnCoverage;
            run = filled ? run + 1 : 0;
            if (run >= bar.MinRunColumns)
                return 100.0 * (c + run) / width;
        }
        return 0;
    }

    private static bool Inside(Mat frame, PixelRect r) =>
        !r.IsEmpty && r.X >= 0 && r.Y >= 0 && r.Right <= frame.Width && r.Bottom <= frame.Height;
}
