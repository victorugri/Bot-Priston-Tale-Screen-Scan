namespace BotPriston.Core.Config;

/// <summary>Combat loop: find a monster, hold the attack button on it until it dies, recover, repeat.</summary>
public sealed class CombatConfig
{
    /// <summary>False = survival only (potions), as in stage 3.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The cursor may leave the monster briefly (animations); only after this long is the target considered
    /// lost and searched for again. The buttons are released immediately either way: holding a button over
    /// the ground makes the character walk.
    /// </summary>
    public int LostTargetGraceMs { get; set; } = 400;

    /// <summary>When the target is lost, first look for it within this distance of where it was.</summary>
    public int ReacquireRadius { get; set; } = 150;

    /// <summary>Give up a target whose HP hasn't dropped for this long (unreachable, immune, misdetected).</summary>
    public int NoProgressSeconds { get; set; } = 15;

    /// <summary>Give up a target after this long no matter what.</summary>
    public int MaxTargetSeconds { get; set; } = 60;

    /// <summary>Pause between searches when nothing was found.</summary>
    public int SearchRetryMs { get; set; } = 1500;

    /// <summary>Pause before searching again when the only monsters seen are out of attack reach (they come closer).</summary>
    public int OutOfReachRetryMs { get; set; } = 300;

    /// <summary>
    /// The game's continuous attack ("ATK contínuo", button next to the minimap) is on: one click on the
    /// monster and the character attacks it until it dies. The bot clicks once instead of holding the button.
    /// </summary>
    public bool AutoAttack { get; set; }

    /// <summary>With <see cref="AutoAttack"/>: click the target again when its HP hasn't dropped for this long.</summary>
    public int ReclickMs { get; set; } = 2000;

    public RightSkillConfig RightSkill { get; set; } = new();
    public UnderAttackConfig UnderAttack { get; set; } = new();
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

    /// <summary>
    /// The right button is held until the icon turns gray (the cast happened), at most this long.
    /// A short click from the bot was ignored by the game most of the time.
    /// </summary>
    public int HoldMaxMs { get; set; } = 2500;

    /// <summary>After a hold without effect, attack with the left button this long before trying again.</summary>
    public int RetryMs { get; set; } = 1000;
}

/// <summary>
/// Monsters that attack from a distance (e.g. Cão Abelha sending bees) stay outside the normal search
/// area. When nothing is in reach but the character is losing HP, search a wider area once and attack
/// what is found; during that attack the character is allowed to walk.
/// </summary>
public sealed class UnderAttackConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>"Under attack" = HP dropped at least this much (percentage points)...</summary>
    public double HpDropPercent { get; set; } = 3;

    /// <summary>...within this many seconds.</summary>
    public double WindowSeconds { get; set; } = 4;

    /// <summary>Half-axes and grid spacing of the wider search area.</summary>
    public int RadiusX { get; set; } = 450;
    public int RadiusY { get; set; } = 300;
    public int Step { get; set; } = 75;
}

/// <summary>
/// Between fights: if HP or MP is low and its potion can't help (disabled or out of stock), stand
/// still until it regenerates. Rest starts below *Below and ends when those bars reach *Until, or after MaxSeconds.
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
