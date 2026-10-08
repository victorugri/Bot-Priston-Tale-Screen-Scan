using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using OpenCvSharp;

namespace BotPriston.Core.Vision;

public sealed record HudState(bool Visible, double Score);

/// <summary>
/// Checks that a fixed HUD element is in place. When it isn't, every other HUD reading
/// is meaningless and the bot must not act.
/// </summary>
public sealed class HudDetector : IDetector<HudState>, IDisposable
{
    private readonly Mat _template;

    public HudDetector(HudConfig config)
    {
        Config = config;
        if (!File.Exists(config.TemplatePath))
            throw new FileNotFoundException($"HUD template not found: {Path.GetFullPath(config.TemplatePath)}", config.TemplatePath);

        _template = Cv2.ImRead(config.TemplatePath, ImreadModes.Color);
        if (_template.Empty())
            throw new InvalidDataException($"Could not decode HUD template {config.TemplatePath}");
        if (_template.Width != config.Roi.Width || _template.Height != config.Roi.Height)
            throw new InvalidDataException(
                $"HUD template is {_template.Width}x{_template.Height} but Vision.Hud.Roi is {config.Roi.Width}x{config.Roi.Height}");
    }

    public HudConfig Config { get; }

    public HudState Detect(Mat frame)
    {
        int m = Config.SearchMargin;
        var search = new PixelRect(Config.Roi.X - m, Config.Roi.Y - m, Config.Roi.Width + 2 * m, Config.Roi.Height + 2 * m)
            .Intersect(new PixelRect(0, 0, frame.Width, frame.Height));
        if (search.Width < _template.Width || search.Height < _template.Height)
            return new HudState(false, 0);

        using var region = new Mat(frame, search.ToCvRect());
        using var result = new Mat();
        Cv2.MatchTemplate(region, _template, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out double max);

        // A flat region yields NaN; treat as no match.
        if (double.IsNaN(max)) max = 0;
        return new HudState(max >= Config.MinScore, max);
    }

    public void Dispose() => _template.Dispose();
}
