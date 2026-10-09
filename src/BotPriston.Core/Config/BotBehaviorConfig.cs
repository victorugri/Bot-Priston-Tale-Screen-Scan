using BotPriston.Core.Geometry;

namespace BotPriston.Core.Config;

/// <summary>Timing of simulated input. Every action is followed by a pause; jitter avoids a fixed rhythm.</summary>
public sealed class InputConfig
{
    /// <summary>How long a key is held down for a single press.</summary>
    public int KeyHoldMs { get; set; } = 60;

    /// <summary>Minimum pause between two consecutive actions (key press, click, mouse move).</summary>
    public int ActionDelayMs { get; set; } = 120;

    /// <summary>Random 0..JitterMs added to every hold and delay.</summary>
    public int JitterMs { get; set; } = 40;

    /// <summary>Pause after moving the mouse so the game registers the new position before a click.</summary>
    public int MouseSettleMs { get; set; } = 40;
}

public sealed class SafetyConfig
{
    /// <summary>Detect and log decisions, but never send input.</summary>
    public bool DryRun { get; set; }

    /// <summary>Stop the bot if there is no progress for this long.</summary>
    public int WatchdogSeconds { get; set; } = 120;

    /// <summary>Stop the bot if the HUD stays invisible this long (death screen, disconnect, loading).</summary>
    public int HudLostStopSeconds { get; set; } = 20;

    /// <summary>Stop the bot if the character's HP bar reads empty for this long (the character died).</summary>
    public double DeadHpSeconds { get; set; } = 2;

    /// <summary>
    /// The character must never walk: when the ground slides for a few frames (Vision.Motion), force both
    /// mouse buttons up, drop the target and log what the bot was doing.
    /// </summary>
    public bool StopWalking { get; set; } = true;
}

public sealed class BotLoopConfig
{
    /// <summary>Target duration of one perceive → decide → act cycle.</summary>
    public int TickMs { get; set; } = 100;
}

public sealed class PotionsConfig
{
    public PotionConfig Hp { get; set; } = new() { Key = "2", BelowPercent = 60 };
    public PotionConfig Mp { get; set; } = new() { Key = "3", BelowPercent = 30 };
    public PotionConfig Stm { get; set; } = new() { Key = "1", BelowPercent = 20 };

    /// <summary>A potion "worked" if its bar rose at least this much by the end of its cooldown.</summary>
    public double MinGainPercent { get; set; } = 1.0;

    /// <summary>After this many uses in a row without effect the potion is assumed to be out of stock.</summary>
    public int MaxFailures { get; set; } = 3;

    /// <summary>How long an out-of-stock potion is left alone before trying again.</summary>
    public int RetryAfterSeconds { get; set; } = 60;

    public RestockConfig Restock { get; set; } = new();
}

/// <summary>
/// Refill the hotbar from the inventory: with the inventory open, hovering a potion and pressing
/// Shift + the potion's key adds that stack to the hotbar slot (same potion only).
/// </summary>
public sealed class RestockConfig
{
    public bool Enabled { get; set; }

    /// <summary>Refill a potion when its hotbar stack is below this (an empty slot counts as 0).</summary>
    public int BelowCount { get; set; } = 5;

    /// <summary>Opens/closes the inventory.</summary>
    public string InventoryKey { get; set; } = "V";

    /// <summary>Switches between inventory pages 1 and 2.</summary>
    public string PageKey { get; set; } = "E";

    /// <summary>Ticks in a row the low count must be read before acting (guards against a misread).</summary>
    public int ConfirmTicks { get; set; } = 3;

    /// <summary>Pause after each key press or mouse move before looking at the screen again.</summary>
    public int StepDelayMs { get; set; } = 350;

    /// <summary>How long to wait for the inventory to open or close.</summary>
    public int ToggleTimeoutMs { get; set; } = 2000;

    /// <summary>A potion that isn't in the inventory (or didn't refill) is left alone this long.</summary>
    public int RetryAfterSeconds { get; set; } = 300;

    /// <summary>Where the mouse waits while the inventory is read: away from the grid (no hover highlight).</summary>
    public PixelPoint MouseRest { get; set; }
}

public sealed class PotionConfig
{
    public bool Enabled { get; set; } = true;
    public string Key { get; set; } = "";

    /// <summary>Use the potion when the bar is strictly below this percentage.</summary>
    public double BelowPercent { get; set; }

    /// <summary>Minimum time between two uses of this potion.</summary>
    public int CooldownMs { get; set; } = 2000;
}
