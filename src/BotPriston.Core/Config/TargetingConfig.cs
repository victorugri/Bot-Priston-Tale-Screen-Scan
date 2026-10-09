using BotPriston.Core.Geometry;

namespace BotPriston.Core.Config;

/// <summary>Hover sweep: where to probe with the mouse and how long to wait for the game to react.</summary>
public sealed class TargetingConfig
{
    /// <summary>Where the character stands on screen (the camera follows it). Probing starts here and goes outwards.</summary>
    public PixelPoint Anchor { get; set; } = new(800, 465);

    /// <summary>Half-axes of the elliptical search area around the anchor.</summary>
    public int RadiusX { get; set; } = 700;
    public int RadiusY { get; set; } = 400;

    /// <summary>
    /// Half-axes of the attack reach: monsters inside this ellipse are attacked from where the character
    /// stands; clicking one farther away makes the game walk the character to it, so those are only seen,
    /// never clicked. Smaller vertically than horizontally on screen (the camera looks down at an angle).
    /// 0 = the whole search area is in reach.
    /// </summary>
    public int ReachX { get; set; }
    public int ReachY { get; set; }

    /// <summary>Points closer than this to the anchor are skipped (the character itself).</summary>
    public int MinRadius { get; set; } = 60;

    /// <summary>Spacing of the hexagonal probe grid. Should be smaller than a monster on screen.</summary>
    public int Step { get; set; } = 75;

    /// <summary>Keep probes at least this far from the client edges.</summary>
    public int EdgeMargin { get; set; } = 15;

    /// <summary>Wait after moving the mouse before reading the target panel.</summary>
    public int HoverDelayMs { get; set; } = 80;

    /// <summary>A hit is re-checked after this delay to reject monsters that just walked past the cursor.</summary>
    public int ConfirmDelayMs { get; set; } = 60;

    /// <summary>HUD areas where the mouse must never go (bars, hotbar, chat, minimap, target panel...).</summary>
    public List<PixelRect> Exclusions { get; set; } = [];

    /// <summary>Whether a monster at this point can be attacked without the character walking.</summary>
    public bool InReach(PixelPoint p) => ReachX <= 0 || ReachY <= 0 || ReachDistance(p) <= 1;

    /// <summary>Distance from the anchor in units of the reach ellipse (1 = on its edge).</summary>
    public double ReachDistance(PixelPoint p)
    {
        double rx = ReachX > 0 ? ReachX : RadiusX, ry = ReachY > 0 ? ReachY : RadiusY;
        double dx = (p.X - Anchor.X) / rx, dy = (p.Y - Anchor.Y) / ry;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Same settings with a different search area (the wider "under attack" search). Everything found
    /// there may be attacked: that search exists to go after distant attackers, walking if needed.
    /// </summary>
    public TargetingConfig WithArea(int radiusX, int radiusY, int step)
    {
        var copy = (TargetingConfig)MemberwiseClone();
        copy.RadiusX = radiusX;
        copy.RadiusY = radiusY;
        copy.Step = step;
        copy.ReachX = 0;
        copy.ReachY = 0;
        return copy;
    }
}
