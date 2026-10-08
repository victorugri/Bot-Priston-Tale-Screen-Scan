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
}
