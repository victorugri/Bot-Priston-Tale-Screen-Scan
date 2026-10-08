using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using OpenCvSharp;

namespace BotPriston.Core.Vision;

public enum CursorKind
{
    /// <summary>Cursor gem not recognized (cursor hidden, over UI, or covered by an effect).</summary>
    Unknown,
    /// <summary>Green gem: nothing attackable under the cursor.</summary>
    Neutral,
    /// <summary>Red gem: a monster is under the cursor.</summary>
    Enemy,
}

public sealed record CursorReading(CursorKind Kind, int EnemyPixels, int NeutralPixels)
{
    public override string ToString() => $"{Kind} (red {EnemyPixels}, green {NeutralPixels})";
}

/// <summary>
/// Reads the gem of the game's own cursor, which turns from green to red while the cursor is
/// over a monster. Unlike the target panel (which keeps showing the last monster for a while),
/// this reflects what is under the cursor right now. Needs to know where the mouse is.
/// </summary>
public sealed class CursorDetector(CursorConfig config) : ICursorReader
{
    public CursorConfig Config { get; } = config;

    public CursorReading Detect(Mat frame, PixelPoint mouse)
    {
        var box = Config.GemBox.Offset(mouse.X, mouse.Y).Intersect(new PixelRect(0, 0, frame.Width, frame.Height));
        if (box.IsEmpty)
            return new CursorReading(CursorKind.Unknown, 0, 0);

        using var region = new Mat(frame, box.ToCvRect());
        using var hsv = new Mat();
        Cv2.CvtColor(region, hsv, ColorConversionCodes.BGR2HSV);
        using var enemy = HsvRange.Mask(hsv, Config.EnemyColors);
        using var neutral = HsvRange.Mask(hsv, Config.NeutralColors);
        int red = Cv2.CountNonZero(enemy);
        int green = Cv2.CountNonZero(neutral);

        // Green wins: a green gem is unambiguous, while red can also come from scenery (autumn leaves).
        var kind = green >= Config.MinNeutralPixels ? CursorKind.Neutral
            : red >= Config.MinEnemyPixels ? CursorKind.Enemy
            : CursorKind.Unknown;
        return new CursorReading(kind, red, green);
    }
}
