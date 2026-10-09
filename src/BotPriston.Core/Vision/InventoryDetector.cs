using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using OpenCvSharp;

namespace BotPriston.Core.Vision;

/// <summary>Whether the inventory is open and which page it shows (null = couldn't tell).</summary>
public sealed record InventoryState(bool Open, double Score, int? Page)
{
    public override string ToString() => Open ? $"open, page {Page?.ToString() ?? "?"} ({Score:F2})" : $"closed ({Score:F2})";
}

/// <summary>An item found in the inventory grid. <see cref="Center"/> is where to put the mouse.</summary>
public sealed record InventoryItem(int Row, int Column, PixelPoint Center, double Difference)
{
    public override string ToString() => $"row {Row + 1} col {Column + 1} (diff {Difference:F3})";
}

/// <summary>Reads the inventory window: open or not, current page, and where a given item icon is.</summary>
public sealed class InventoryDetector : IDetector<InventoryState>, IDisposable
{
    /// <summary>Minimum difference in mean saturation between the two page buttons to tell which one is lit.</summary>
    private const double MinPageContrast = 15;

    private readonly Mat _template;

    public InventoryDetector(InventoryConfig config)
    {
        Config = config;
        if (!File.Exists(config.TemplatePath))
            throw new FileNotFoundException($"Inventory template not found: {Path.GetFullPath(config.TemplatePath)}", config.TemplatePath);

        _template = Cv2.ImRead(config.TemplatePath, ImreadModes.Color);
        if (_template.Empty())
            throw new InvalidDataException($"Could not decode inventory template {config.TemplatePath}");
        if (_template.Width != config.Roi.Width || _template.Height != config.Roi.Height)
            throw new InvalidDataException(
                $"Inventory template is {_template.Width}x{_template.Height} but Vision.Inventory.Roi is {config.Roi.Width}x{config.Roi.Height}");
    }

    public InventoryConfig Config { get; }

    public InventoryState Detect(Mat frame)
    {
        double score = MatchScore(frame);
        bool open = score >= Config.MinScore;
        return new InventoryState(open, score, open ? Page(frame) : null);
    }

    /// <summary>
    /// The grid cell holding the same item as <paramref name="icon"/> (an <see cref="PotionSlotsDetector.IconArea"/>
    /// crop, e.g. from the hotbar), or null. Call with the mouse away from the grid: hovering lights a cell up.
    /// </summary>
    public InventoryItem? Find(Mat frame, Mat icon)
    {
        const int margin = 2;
        var grid = Config.Grid;
        var first = PotionSlotsDetector.IconArea(new PixelRect(grid.X, grid.Y, Config.CellSize, Config.CellSize));
        var search = new PixelRect(first.X - margin, first.Y - margin,
                grid.Width - Config.CellSize + first.Width + 2 * margin, grid.Height - Config.CellSize + first.Height + 2 * margin)
            .Intersect(new PixelRect(0, 0, frame.Width, frame.Height));
        if (search.Width < icon.Width || search.Height < icon.Height)
            return null;

        using var region = new Mat(frame, search.ToCvRect());
        using var result = new Mat();
        Cv2.MatchTemplate(region, icon, result, TemplateMatchModes.SqDiffNormed);
        Cv2.MinMaxLoc(result, out double min, out _, out var at, out _);
        if (double.IsNaN(min) || min > Config.MaxIconDifference)
            return null;

        int column = (int)Math.Round((search.X + at.X - first.X) / (double)Config.CellSize);
        int row = (int)Math.Round((search.Y + at.Y - first.Y) / (double)Config.CellSize);
        int columns = grid.Width / Config.CellSize, rows = grid.Height / Config.CellSize;
        if (column < 0 || row < 0 || column >= columns || row >= rows)
            return null;

        var center = new PixelPoint(grid.X + column * Config.CellSize + Config.CellSize / 2,
            grid.Y + row * Config.CellSize + Config.CellSize / 2);
        return new InventoryItem(row, column, center, min);
    }

    private double MatchScore(Mat frame)
    {
        int m = Config.SearchMargin;
        var search = new PixelRect(Config.Roi.X - m, Config.Roi.Y - m, Config.Roi.Width + 2 * m, Config.Roi.Height + 2 * m)
            .Intersect(new PixelRect(0, 0, frame.Width, frame.Height));
        if (search.Width < _template.Width || search.Height < _template.Height)
            return 0;

        using var region = new Mat(frame, search.ToCvRect());
        using var result = new Mat();
        Cv2.MatchTemplate(region, _template, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out double max);
        return double.IsNaN(max) ? 0 : max;
    }

    /// <summary>The lit page button is orange; the other one is gray.</summary>
    private int? Page(Mat frame)
    {
        double one = MeanSaturation(frame, Config.Page1Button), two = MeanSaturation(frame, Config.Page2Button);
        if (Math.Abs(one - two) < MinPageContrast) return null;
        return one > two ? 1 : 2;
    }

    private static double MeanSaturation(Mat frame, PixelRect rect)
    {
        using var roi = new Mat(frame, rect.ToCvRect());
        using var hsv = roi.CvtColor(ColorConversionCodes.BGR2HSV);
        return hsv.Mean().Val1;
    }

    public void Dispose() => _template.Dispose();
}
