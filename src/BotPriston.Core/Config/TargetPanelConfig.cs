using BotPriston.Core.Geometry;
using BotPriston.Core.Vision;

namespace BotPriston.Core.Config;

/// <summary>
/// The top-right panel showing the monster under the cursor (portrait + name) and, once it is
/// engaged, its HP bar.
/// </summary>
public sealed class TargetPanelConfig
{
    /// <summary>
    /// Rectangle whose 1-px outline is the panel border (the outline pixels are X, X+Width-1, Y, Y+Height-1).
    /// The border is semi-transparent, so it is detected as a "ridge": brighter or less saturated than
    /// the pixels on both sides of it.
    /// </summary>
    public PixelRect Border { get; set; }

    /// <summary>A border pixel is a ridge if its gray level exceeds both neighbors by at least this much...</summary>
    public int RidgeMinLumaDelta { get; set; } = 12;

    /// <summary>...or its saturation is below both neighbors by at least this much.</summary>
    public int RidgeMinSaturationDelta { get; set; } = 40;

    /// <summary>Each of the 4 edges needs at least this fraction of ridge pixels for the panel to count as visible.</summary>
    public double MinEdgeScore { get; set; } = 0.6;

    public TargetHpBarConfig HpBar { get; set; } = new();
}

/// <summary>Horizontal bar under the panel, filled from the left, with the percentage written over its middle.</summary>
public sealed class TargetHpBarConfig
{
    /// <summary>Rows of the black frame (top and bottom) used to tell whether the bar is shown at all.</summary>
    public List<PixelRect> FrameRows { get; set; } = [];

    /// <summary>Max V (0-255) for a frame pixel to count as black.</summary>
    public int FrameMaxValue { get; set; } = 20;

    /// <summary>Fraction of black pixels each frame row needs.</summary>
    public double MinFrameDarkFraction { get; set; } = 0.8;

    /// <summary>Inside of the bar: left column = first HP pixel, right column = 100%.</summary>
    public PixelRect Fill { get; set; }

    /// <summary>Colors of the remaining HP (it shifts green → yellow → red). The empty part is dark.</summary>
    public List<HsvRange> FillColors { get; set; } = [];

    /// <summary>Fraction of a column's pixels that must match for the column to count as filled.</summary>
    public double MinColumnCoverage { get; set; } = 0.5;

    /// <summary>Filled columns needed in a row to accept the right end (ignores isolated text pixels).</summary>
    public int MinRunColumns { get; set; } = 2;
}
