using BotPriston.Core.Config;
using OpenCvSharp;

namespace BotPriston.Core.Vision;

/// <summary>Everything the bot perceived in one frame. Null members were not evaluated.</summary>
public sealed record VisionSnapshot(HudState Hud, PlayerBars? Bars, TargetPanel? Target = null)
{
    public override string ToString() =>
        Hud.Visible ? $"HUD ok ({Hud.Score:F2})  {Bars}  target: {Target}" : $"HUD not visible ({Hud.Score:F2})";
}

/// <summary>Runs all detectors on a frame. Detectors that read the HUD only run when the HUD is visible.</summary>
public sealed class VisionPipeline : IVision, IDisposable
{
    public VisionPipeline(VisionConfig config)
    {
        Hud = new HudDetector(config.Hud);
        PlayerBars = new PlayerBarsDetector(config.PlayerBars);
        TargetPanel = new TargetPanelDetector(config.TargetPanel);
        Cursor = new CursorDetector(config.Cursor);
    }

    /// <summary>Not part of <see cref="Analyze"/>: it needs the mouse position.</summary>
    public CursorDetector Cursor { get; }

    public HudDetector Hud { get; }
    public PlayerBarsDetector PlayerBars { get; }
    public TargetPanelDetector TargetPanel { get; }

    public VisionSnapshot Analyze(Mat frame)
    {
        var hud = Hud.Detect(frame);
        if (!hud.Visible)
            return new VisionSnapshot(hud, null);
        return new VisionSnapshot(hud, PlayerBars.Detect(frame), TargetPanel.Detect(frame));
    }

    public void Dispose() => Hud.Dispose();
}
