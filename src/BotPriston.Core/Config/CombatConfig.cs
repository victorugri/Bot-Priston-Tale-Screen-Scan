namespace BotPriston.Core.Config;

/// <summary>Combat loop: find a monster, hold the attack button on it until it dies, recover, repeat.</summary>
public sealed class CombatConfig
{
    /// <summary>False = survival only (potions), as in stage 3.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The cursor may leave the monster briefly (animations); only after this long is the target considered lost.</summary>
    public int LostTargetGraceMs { get; set; } = 400;

    /// <summary>When the target is lost, first look for it within this distance of where it was.</summary>
    public int ReacquireRadius { get; set; } = 150;

    /// <summary>Give up a target whose HP hasn't dropped for this long (unreachable, immune, misdetected).</summary>
    public int NoProgressSeconds { get; set; } = 15;

    /// <summary>Give up a target after this long no matter what.</summary>
    public int MaxTargetSeconds { get; set; } = 60;

    /// <summary>Pause between searches when nothing was found.</summary>
    public int SearchRetryMs { get; set; } = 1500;

    public RightSkillConfig RightSkill { get; set; } = new();
    public RestConfig Rest { get; set; } = new();
    public LootConfig Loot { get; set; } = new();
}

/// <summary>
/// The right-click skill, used on the current target whenever its icon (right skill orb) is in color.
/// A skill rotation can grow from here later.
/// </summary>
public sealed class RightSkillConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Don't use it below this much mana (0 = whenever it is ready).</summary>
    public double MinMpPercent { get; set; }

    /// <summary>After a use, wait this long before trying again (the icon takes a moment to turn gray).</summary>
    public int RecheckMs { get; set; } = 1500;
}

/// <summary>
/// Between fights: if HP or MP is low (e.g. potions ran out), stand still until they regenerate.
/// Rest starts below *Below and ends when every bar is at or above its *Until, or after MaxSeconds.
/// </summary>
public sealed class RestConfig
{
    public double HpBelow { get; set; } = 40;
    public double HpUntil { get; set; } = 80;
    public double MpBelow { get; set; } = 10;
    public double MpUntil { get; set; } = 50;
    public int MaxSeconds { get; set; } = 60;
}

/// <summary>Not implemented yet: looting needs clicking each item, and the user doesn't need it.</summary>
public sealed class LootConfig
{
    public bool Enabled { get; set; }
}
