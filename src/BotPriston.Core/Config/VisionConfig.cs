using BotPriston.Core.Geometry;
using BotPriston.Core.Vision;

namespace BotPriston.Core.Config;

public sealed class VisionConfig
{
    public HudConfig Hud { get; set; } = new();
    public PlayerBarsConfig PlayerBars { get; set; } = new();
    public TargetPanelConfig TargetPanel { get; set; } = new();
    public CursorConfig Cursor { get; set; } = new();
    public SkillOrbsConfig SkillOrbs { get; set; } = new();
}

public sealed class SkillOrbsConfig
{
    public SkillOrbConfig Left { get; set; } = new();
    public SkillOrbConfig Right { get; set; } = new();
}

/// <summary>A round skill icon: in color when ready, gray while recharging.</summary>
public sealed class SkillOrbConfig
{
    public PixelPoint Center { get; set; }

    /// <summary>Radius of the circle sampled inside the icon (stay inside the metal ring).</summary>
    public int Radius { get; set; } = 11;

    /// <summary>A pixel counts as colored at or above this saturation (0-255).</summary>
    public int MinSaturation { get; set; } = 100;

    /// <summary>Fraction of colored pixels for the skill to count as ready (gray icons have ~0).</summary>
    public double MinColoredFraction { get; set; } = 0.5;
}

/// <summary>The gem on the game's cursor: green over the ground, red over a monster.</summary>
public sealed class CursorConfig
{
    /// <summary>Where the gem is, relative to the mouse position (the cursor's hot spot).</summary>
    public PixelRect GemBox { get; set; }

    public List<HsvRange> EnemyColors { get; set; } = [];
    public List<HsvRange> NeutralColors { get; set; } = [];

    /// <summary>Pixels of each color needed inside the gem box (the gem is about 20-50 pixels).</summary>
    public int MinEnemyPixels { get; set; } = 15;
    public int MinNeutralPixels { get; set; } = 10;
}

/// <summary>
/// A fixed piece of the in-game HUD. If it isn't where it should be, we're not looking at the
/// normal game view (login screen, full-screen map, loading, disconnected...).
/// </summary>
public sealed class HudConfig
{
    /// <summary>PNG cut from a real screenshot. Relative paths resolve against the working directory.</summary>
    public string TemplatePath { get; set; } = "";

    /// <summary>Where the template sits in the client area; Width/Height must equal the template size.</summary>
    public PixelRect Roi { get; set; }

    /// <summary>Extra pixels searched around <see cref="Roi"/> on each side.</summary>
    public int SearchMargin { get; set; } = 3;

    /// <summary>Minimum normalized correlation (TM_CCOEFF_NORMED, -1..1) to consider the HUD visible.</summary>
    public double MinScore { get; set; } = 0.8;
}

public sealed class PlayerBarsConfig
{
    public BarConfig Hp { get; set; } = new();
    public BarConfig Mp { get; set; } = new();
    public BarConfig Stm { get; set; } = new();
}

/// <summary>A vertical tube that fills from the bottom.</summary>
public sealed class BarConfig
{
    /// <summary>Inside of the tube: top row = 100%, bottom row = first row filled.</summary>
    public PixelRect Roi { get; set; }

    /// <summary>Colors of the liquid. A pixel matching any range counts as filled.</summary>
    public List<HsvRange> Colors { get; set; } = [];

    /// <summary>Fraction (0..1) of a row's pixels that must match for the row to count as filled.</summary>
    public double MinRowCoverage { get; set; } = 0.5;

    /// <summary>Unfilled rows tolerated inside the liquid (highlights, bubbles) before it counts as the top.</summary>
    public int MaxGapRows { get; set; } = 2;
}
