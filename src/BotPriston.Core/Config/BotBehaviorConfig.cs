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
