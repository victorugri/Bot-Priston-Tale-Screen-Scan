namespace BotPriston.Core.Config;

/// <summary>
/// Root of botconfig.json. Every tunable (keys, ROIs, colors, thresholds, limits) lives here,
/// never hardcoded. Sections are added as each stage of the bot is built.
/// </summary>
public sealed class BotConfig
{
    public WindowConfig Window { get; set; } = new();
    public CaptureConfig Capture { get; set; } = new();
    public HotkeyConfig Hotkeys { get; set; } = new();
    public VisionConfig Vision { get; set; } = new();
    public InputConfig Input { get; set; } = new();
    public SafetyConfig Safety { get; set; } = new();
    public BotLoopConfig Bot { get; set; } = new();
    public PotionsConfig Potions { get; set; } = new();
    public TargetingConfig Targeting { get; set; } = new();
    public CombatConfig Combat { get; set; } = new();
    public PathsConfig Paths { get; set; } = new();
}

public sealed class WindowConfig
{
    /// <summary>Process name without ".exe". Empty = don't filter by process.</summary>
    public string ProcessName { get; set; } = "";

    /// <summary>Case-insensitive substring of the window title. Empty = don't filter by title.</summary>
    public string TitleContains { get; set; } = "";

    /// <summary>All ROIs assume this client size; a mismatch is reported loudly.</summary>
    public int ExpectedClientWidth { get; set; } = 1600;
    public int ExpectedClientHeight { get; set; } = 900;
}

public enum CaptureBackend
{
    /// <summary>Windows.Graphics.Capture, falling back to BitBlt if it can't start.</summary>
    Auto,
    WindowsGraphicsCapture,
    /// <summary>Copies the window's area from the desktop. Requires the window to be visible and unobstructed.</summary>
    BitBlt,
}

public sealed class CaptureConfig
{
    public CaptureBackend Backend { get; set; } = CaptureBackend.Auto;

    /// <summary>How long Grab() waits for the first frame before giving up.</summary>
    public int FrameTimeoutMs { get; set; } = 2000;
}

public sealed class HotkeyConfig
{
    /// <summary>Saves a screenshot while `capture --hotkey` is running.</summary>
    public string Capture { get; set; } = "F11";

    /// <summary>Pauses/resumes the bot (stage 3).</summary>
    public string PauseResume { get; set; } = "F12";

    /// <summary>Stops the bot / ends hotkey capture mode.</summary>
    public string Quit { get; set; } = "Ctrl+F12";
}

public sealed class PathsConfig
{
    /// <summary>Relative paths are resolved against the current working directory.</summary>
    public string Samples { get; set; } = "samples";
    public string Logs { get; set; } = "logs";
}
