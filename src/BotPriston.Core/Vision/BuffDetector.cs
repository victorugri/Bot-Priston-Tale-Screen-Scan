using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using OpenCvSharp;

namespace BotPriston.Core.Vision;

public sealed record BuffState(bool Active, double Score)
{
    public override string ToString() => $"{(Active ? "active" : "missing")} ({Score:F2})";
}

/// <summary>
/// Whether a timed buff is on: its round icon shows in the row at the top-left of the screen while it
/// lasts. The icons move along the row as other buffs come and go, so the whole row is searched; only the
/// middle of the icon is compared (the ring around it shows the time left and changes).
/// </summary>
public sealed class BuffDetector : IDetector<BuffState>, IDisposable
{
    private readonly Mat _template;

    public BuffDetector(BuffConfig config)
    {
        Config = config;
        if (!File.Exists(config.TemplatePath))
            throw new FileNotFoundException($"Buff icon template not found: {Path.GetFullPath(config.TemplatePath)}", config.TemplatePath);
        _template = Cv2.ImRead(config.TemplatePath, ImreadModes.Color);
        if (_template.Empty())
            throw new InvalidDataException($"Could not decode buff icon template {config.TemplatePath}");
    }

    public BuffConfig Config { get; }

    public BuffState Detect(Mat frame)
    {
        var area = Config.SearchArea.Intersect(new PixelRect(0, 0, frame.Width, frame.Height));
        if (area.Width < _template.Width || area.Height < _template.Height)
            return new BuffState(false, 0);

        using var region = new Mat(frame, area.ToCvRect());
        using var result = new Mat();
        Cv2.MatchTemplate(region, _template, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out double max);
        if (double.IsNaN(max)) max = 0;
        return new BuffState(max >= Config.MinScore, max);
    }

    public void Dispose() => _template.Dispose();
}
