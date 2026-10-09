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
    public MotionConfig Motion { get; set; } = new();
    public PotionSlotsConfig PotionSlots { get; set; } = new();
    public ItemCountConfig ItemCount { get; set; } = new();
    public InventoryConfig Inventory { get; set; } = new();
}

/// <summary>Where each potion sits in the hotbar (one 22x22 item cell each; the stack size is printed on it).</summary>
public sealed class PotionSlotsConfig
{
    public PixelRect Hp { get; set; }
    public PixelRect Mp { get; set; }
    public PixelRect Stm { get; set; }

    /// <summary>Fraction of bright pixels in the lower part of a cell for it to hold an item (empty cells have ~0).</summary>
    public double MinItemFraction { get; set; } = 0.15;
}

/// <summary>The white stack size printed in the top-left corner of an item cell (hotbar and inventory).</summary>
public sealed class ItemCountConfig
{
    /// <summary>Rows from the top of the cell to the top of the digits.</summary>
    public int DigitsTop { get; set; } = 5;

    /// <summary>A pixel is part of a digit when all its channels are at least this bright...</summary>
    public int MinWhite { get; set; } = 232;

    /// <summary>...and its channels differ by at most this much (pure white; the icons' highlights are tinted).</summary>
    public int MaxWhiteSpread { get; set; } = 24;

    /// <summary>Minimum match (0-1) between a digit's shape and the pixels for it to be read.</summary>
    public double MinGlyphScore { get; set; } = 0.75;
}

/// <summary>The inventory window (bottom-left, toggled with a key) and its item grid.</summary>
public sealed class InventoryConfig
{
    /// <summary>Fixed piece of the open inventory (its "!" and "▲" buttons), cut from a screenshot.</summary>
    public string TemplatePath { get; set; } = "";
    public PixelRect Roi { get; set; }
    public int SearchMargin { get; set; } = 3;
    public double MinScore { get; set; } = 0.8;

    /// <summary>Item grid: the top-left cell starts at (X, Y); Width/Height cover all cells.</summary>
    public PixelRect Grid { get; set; }
    public int CellSize { get; set; } = 22;

    /// <summary>The page buttons: the one of the page on screen is lit (orange).</summary>
    public PixelRect Page1Button { get; set; }
    public PixelRect Page2Button { get; set; }

    /// <summary>Largest difference (0-1, normalized squared difference) between two icons of the same item.</summary>
    public double MaxIconDifference { get; set; } = 0.12;
}

/// <summary>Walking detection from the ground sliding between frames (see MotionDetector).</summary>
public sealed class MotionConfig
{
    /// <summary>Ground areas away from the character, monsters near it, and the HUD.</summary>
    public List<PixelRect> Boxes { get; set; } = [];

    /// <summary>Images are shrunk by this factor before comparing (speed).</summary>
    public double Downscale { get; set; } = 0.5;

    /// <summary>Minimum ground shift between two frames to count as movement.</summary>
    public double MinShiftPx { get; set; } = 2.5;

    /// <summary>A box agrees with the median shift if it is within this many pixels of it.</summary>
    public double AgreementPx { get; set; } = 2;

    /// <summary>Boxes that must agree (protects against a monster moving through one box).</summary>
    public int MinAgreeingBoxes { get; set; } = 3;

    /// <summary>Frames in a row with movement before it is called walking (hit effects shake 1 frame).</summary>
    public int ConsecutiveFrames { get; set; } = 3;

    /// <summary>
    /// After each attack click, the ground is watched this long: a monster out of reach makes the character
    /// step towards it (too short to count as walking). Logged to calibrate Targeting.ReachX/ReachY.
    /// </summary>
    public int AttackStepWatchMs { get; set; } = 800;

    /// <summary>Ground slide (sum over the watch) from which an attack counts as having made the character step.</summary>
    public double AttackStepMinPx { get; set; } = 10;
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
