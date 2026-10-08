using BotPriston.Core.Config;
using BotPriston.Core.Geometry;

namespace BotPriston.Core.Targeting;

/// <summary>Probe points for the hover sweep: a hexagonal grid inside an ellipse, nearest to the character first.</summary>
public static class SweepPattern
{
    public static IReadOnlyList<PixelPoint> Generate(TargetingConfig config, int clientWidth, int clientHeight)
    {
        var anchor = config.Anchor;
        int step = Math.Max(1, config.Step);
        double rowHeight = step * Math.Sqrt(3) / 2;
        var points = new List<(PixelPoint Point, double Distance)>();

        int rows = (int)Math.Ceiling(config.RadiusY / rowHeight);
        for (int row = -rows; row <= rows; row++)
        {
            int y = anchor.Y + (int)Math.Round(row * rowHeight);
            double offset = Math.Abs(row) % 2 == 1 ? step / 2.0 : 0; // hex: odd rows shifted half a step
            int cols = config.RadiusX / step + 1;
            for (int col = -cols; col <= cols; col++)
            {
                int x = anchor.X + (int)Math.Round(col * step + offset);
                var p = new PixelPoint(x, y);
                if (!Accept(p, config, clientWidth, clientHeight)) continue;
                double dx = (x - anchor.X) / (double)config.RadiusX;
                double dy = (y - anchor.Y) / (double)config.RadiusY;
                points.Add((p, Math.Sqrt(dx * dx + dy * dy)));
            }
        }

        return points.OrderBy(p => p.Distance).ThenBy(p => p.Point.Y).ThenBy(p => p.Point.X)
            .Select(p => p.Point).ToList();
    }

    public static bool IsExcluded(PixelPoint p, TargetingConfig config) =>
        config.Exclusions.Any(r => p.X >= r.X && p.X < r.Right && p.Y >= r.Y && p.Y < r.Bottom);

    private static bool Accept(PixelPoint p, TargetingConfig config, int width, int height)
    {
        int m = config.EdgeMargin;
        if (p.X < m || p.Y < m || p.X >= width - m || p.Y >= height - m) return false;

        double dx = (p.X - config.Anchor.X) / (double)config.RadiusX;
        double dy = (p.Y - config.Anchor.Y) / (double)config.RadiusY;
        if (dx * dx + dy * dy > 1) return false;

        double px = p.X - config.Anchor.X, py = p.Y - config.Anchor.Y;
        if (px * px + py * py < (double)config.MinRadius * config.MinRadius) return false;

        return !IsExcluded(p, config);
    }
}
