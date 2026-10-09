using System.Text.Json;
using System.Text.Json.Serialization;
using BotPriston.Core.Geometry;
using BotPriston.Core.Input;

namespace BotPriston.Core.Config;

public sealed class ConfigException(string message, Exception? inner = null) : Exception(message, inner);

public static class ConfigLoader
{
    public const string DefaultFileName = "botconfig.json";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Finds the config file: explicit path, else ./botconfig.json, else next to the executable or in
    /// one of its parent folders (an app started from bin/ finds the repository's config).
    /// </summary>
    public static string Resolve(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath);

        var inCurrent = Path.Combine(Environment.CurrentDirectory, DefaultFileName);
        if (File.Exists(inCurrent)) return inCurrent;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, DefaultFileName);
            if (File.Exists(candidate)) return candidate;
        }
        return inCurrent;
    }

    public static BotConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new ConfigException($"Config file not found: {path}");

        BotConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<BotConfig>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"Invalid JSON in {path}: {ex.Message}", ex);
        }

        if (config is null)
            throw new ConfigException($"Config file is empty: {path}");

        var errors = Validate(config);
        if (errors.Count > 0)
            throw new ConfigException($"Invalid config {path}:\n  - " + string.Join("\n  - ", errors));

        return config;
    }

    public static IReadOnlyList<string> Validate(BotConfig config)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.Window.ProcessName) && string.IsNullOrWhiteSpace(config.Window.TitleContains))
            errors.Add("Window: set ProcessName and/or TitleContains so the game window can be found.");
        if (config.Window.ExpectedClientWidth <= 0 || config.Window.ExpectedClientHeight <= 0)
            errors.Add("Window: ExpectedClientWidth/Height must be positive.");

        if (config.Capture.FrameTimeoutMs <= 0)
            errors.Add("Capture.FrameTimeoutMs must be positive.");

        CheckKey(config.Hotkeys.Capture, "Hotkeys.Capture");
        CheckKey(config.Hotkeys.PauseResume, "Hotkeys.PauseResume");
        CheckKey(config.Hotkeys.Quit, "Hotkeys.Quit");

        if (string.IsNullOrWhiteSpace(config.Paths.Samples)) errors.Add("Paths.Samples is empty.");
        if (string.IsNullOrWhiteSpace(config.Paths.Logs)) errors.Add("Paths.Logs is empty.");

        var client = new PixelRect(0, 0, config.Window.ExpectedClientWidth, config.Window.ExpectedClientHeight);
        var hud = config.Vision.Hud;
        if (string.IsNullOrWhiteSpace(hud.TemplatePath)) errors.Add("Vision.Hud.TemplatePath is empty.");
        CheckRoi(hud.Roi, "Vision.Hud.Roi");
        if (hud.SearchMargin < 0) errors.Add("Vision.Hud.SearchMargin must be >= 0.");
        if (hud.MinScore is <= 0 or > 1) errors.Add("Vision.Hud.MinScore must be in (0, 1].");

        CheckBar(config.Vision.PlayerBars.Hp, "Vision.PlayerBars.Hp");
        CheckBar(config.Vision.PlayerBars.Mp, "Vision.PlayerBars.Mp");
        CheckBar(config.Vision.PlayerBars.Stm, "Vision.PlayerBars.Stm");

        var panel = config.Vision.TargetPanel;
        CheckRoi(panel.Border, "Vision.TargetPanel.Border");
        if (!panel.Border.IsEmpty && (panel.Border.X < 1 || panel.Border.Y < 1 ||
            panel.Border.Right >= client.Width || panel.Border.Bottom >= client.Height))
            errors.Add("Vision.TargetPanel.Border needs a 1-px margin inside the client area.");
        if (panel.MinEdgeScore is <= 0 or > 1) errors.Add("Vision.TargetPanel.MinEdgeScore must be in (0, 1].");
        var hpBar = panel.HpBar;
        if (hpBar.FrameRows.Count == 0) errors.Add("Vision.TargetPanel.HpBar.FrameRows needs at least one row.");
        foreach (var row in hpBar.FrameRows) CheckRoi(row, "Vision.TargetPanel.HpBar.FrameRows");
        CheckRoi(hpBar.Fill, "Vision.TargetPanel.HpBar.Fill");
        if (hpBar.FillColors.Count == 0) errors.Add("Vision.TargetPanel.HpBar.FillColors needs at least one HSV range.");
        if (hpBar.MinColumnCoverage is <= 0 or > 1) errors.Add("Vision.TargetPanel.HpBar.MinColumnCoverage must be in (0, 1].");
        if (hpBar.MinRunColumns < 1) errors.Add("Vision.TargetPanel.HpBar.MinRunColumns must be >= 1.");

        var cursor = config.Vision.Cursor;
        if (cursor.GemBox.IsEmpty) errors.Add("Vision.Cursor.GemBox must be non-empty (offsets from the mouse position).");
        if (cursor.EnemyColors.Count == 0 || cursor.NeutralColors.Count == 0)
            errors.Add("Vision.Cursor needs EnemyColors and NeutralColors.");
        if (cursor.MinEnemyPixels < 1 || cursor.MinNeutralPixels < 1) errors.Add("Vision.Cursor.Min*Pixels must be >= 1.");

        var motion = config.Vision.Motion;
        foreach (var box in motion.Boxes) CheckRoi(box, "Vision.Motion.Boxes");
        if (config.Safety.StopWalking && motion.Boxes.Count < motion.MinAgreeingBoxes)
            errors.Add("Vision.Motion: Safety.StopWalking needs at least MinAgreeingBoxes boxes.");
        if (motion.Downscale is <= 0 or > 1) errors.Add("Vision.Motion.Downscale must be in (0, 1].");
        if (motion.MinShiftPx <= 0 || motion.AgreementPx <= 0) errors.Add("Vision.Motion.MinShiftPx/AgreementPx must be positive.");
        if (motion.ConsecutiveFrames < 1 || motion.MinAgreeingBoxes < 1) errors.Add("Vision.Motion.ConsecutiveFrames/MinAgreeingBoxes must be >= 1.");

        var targeting = config.Targeting;
        if (targeting.Anchor.X < 0 || targeting.Anchor.Y < 0 || targeting.Anchor.X >= client.Width || targeting.Anchor.Y >= client.Height)
            errors.Add("Targeting.Anchor must be inside the client area.");
        if (targeting.RadiusX <= 0 || targeting.RadiusY <= 0) errors.Add("Targeting.RadiusX/RadiusY must be positive.");
        if (targeting.Step < 10) errors.Add("Targeting.Step must be >= 10.");
        if (targeting.ReachX < 0 || targeting.ReachY < 0 || (targeting.ReachX == 0) != (targeting.ReachY == 0))
            errors.Add("Targeting.ReachX/ReachY must both be positive (or both 0 = the whole search area).");
        if (targeting.MinRadius < 0 || targeting.EdgeMargin < 0) errors.Add("Targeting.MinRadius/EdgeMargin must be >= 0.");
        if (targeting.HoverDelayMs < 0 || targeting.ConfirmDelayMs < 0) errors.Add("Targeting delays must be >= 0.");
        foreach (var rect in targeting.Exclusions) CheckRoi(rect, "Targeting.Exclusions");

        var combat = config.Combat;
        if (combat.LostTargetGraceMs < 0 || combat.ReacquireRadius < 0 || combat.SearchRetryMs < 0)
            errors.Add("Combat: LostTargetGraceMs, ReacquireRadius and SearchRetryMs must be >= 0.");
        if (combat.ReclickMs <= 0) errors.Add("Combat.ReclickMs must be positive.");
        if (combat.OutOfReachRetryMs < 0) errors.Add("Combat.OutOfReachRetryMs must be >= 0.");
        if (motion.AttackStepWatchMs < 0 || motion.AttackStepMinPx <= 0)
            errors.Add("Vision.Motion.AttackStepWatchMs must be >= 0 and AttackStepMinPx positive.");
        if (combat.NoProgressSeconds <= 0 || combat.MaxTargetSeconds <= 0)
            errors.Add("Combat: NoProgressSeconds and MaxTargetSeconds must be positive.");
        var rest = combat.Rest;
        if (rest.HpBelow > rest.HpUntil || rest.MpBelow > rest.MpUntil)
            errors.Add("Combat.Rest: each *Below must be <= its *Until.");
        if (rest.MaxSeconds < 0) errors.Add("Combat.Rest.MaxSeconds must be >= 0.");
        if (combat.Rest.MaxSeconds >= config.Safety.WatchdogSeconds)
            errors.Add("Combat.Rest.MaxSeconds must be shorter than Safety.WatchdogSeconds.");
        if (combat.Loot.Enabled) errors.Add("Combat.Loot.Enabled: looting is not implemented yet.");
        if (combat.RightSkill.MinMpPercent is < 0 or > 100) errors.Add("Combat.RightSkill.MinMpPercent must be 0-100.");
        var underAttack = combat.UnderAttack;
        if (underAttack.HpDropPercent <= 0 || underAttack.WindowSeconds <= 0)
            errors.Add("Combat.UnderAttack.HpDropPercent and WindowSeconds must be positive.");
        if (underAttack.Enabled && (underAttack.RadiusX <= config.Targeting.RadiusX || underAttack.RadiusY <= config.Targeting.RadiusY))
            errors.Add("Combat.UnderAttack.RadiusX/RadiusY must be larger than Targeting.RadiusX/RadiusY.");
        if (underAttack.Step < 10) errors.Add("Combat.UnderAttack.Step must be >= 10.");
        if (combat.RightSkill.HoldMaxMs <= 0) errors.Add("Combat.RightSkill.HoldMaxMs must be positive.");
        if (combat.RightSkill.RetryMs < 0) errors.Add("Combat.RightSkill.RetryMs must be >= 0.");

        foreach (var (orb, name) in new[] { (config.Vision.SkillOrbs.Left, "Left"), (config.Vision.SkillOrbs.Right, "Right") })
        {
            if (orb.Radius <= 0 || !client.Contains(new PixelRect(orb.Center.X - orb.Radius, orb.Center.Y - orb.Radius, 2 * orb.Radius + 1, 2 * orb.Radius + 1)))
                errors.Add($"Vision.SkillOrbs.{name}: the circle must be inside the client area.");
            if (orb.MinColoredFraction is <= 0 or > 1) errors.Add($"Vision.SkillOrbs.{name}.MinColoredFraction must be in (0, 1].");
        }
        if (config.Safety.DeadHpSeconds <= 0) errors.Add("Safety.DeadHpSeconds must be positive.");

        var input = config.Input;
        if (input.KeyHoldMs < 10) errors.Add("Input.KeyHoldMs must be >= 10 (the game may miss shorter presses).");
        if (input.ActionDelayMs < 0 || input.JitterMs < 0 || input.MouseSettleMs < 0)
            errors.Add("Input delays must be >= 0.");

        if (config.Safety.WatchdogSeconds <= 0) errors.Add("Safety.WatchdogSeconds must be positive.");
        if (config.Safety.HudLostStopSeconds <= 0) errors.Add("Safety.HudLostStopSeconds must be positive.");
        if (config.Bot.TickMs < 10) errors.Add("Bot.TickMs must be >= 10.");

        CheckPotion(config.Potions.Hp, "Potions.Hp");
        CheckPotion(config.Potions.Mp, "Potions.Mp");
        CheckPotion(config.Potions.Stm, "Potions.Stm");
        if (config.Potions.MaxFailures < 1) errors.Add("Potions.MaxFailures must be >= 1.");
        if (config.Potions.RetryAfterSeconds < 0) errors.Add("Potions.RetryAfterSeconds must be >= 0.");

        var slots = config.Vision.PotionSlots;
        CheckRoi(slots.Hp, "Vision.PotionSlots.Hp");
        CheckRoi(slots.Mp, "Vision.PotionSlots.Mp");
        CheckRoi(slots.Stm, "Vision.PotionSlots.Stm");
        if (slots.MinItemFraction is <= 0 or > 1) errors.Add("Vision.PotionSlots.MinItemFraction must be in (0, 1].");
        var count = config.Vision.ItemCount;
        if (count.DigitsTop < 0) errors.Add("Vision.ItemCount.DigitsTop must be >= 0.");
        if (count.MinWhite is < 0 or > 255 || count.MaxWhiteSpread is < 0 or > 255)
            errors.Add("Vision.ItemCount.MinWhite/MaxWhiteSpread must be 0-255.");
        if (count.MinGlyphScore is <= 0 or > 1) errors.Add("Vision.ItemCount.MinGlyphScore must be in (0, 1].");

        var inventory = config.Vision.Inventory;
        if (string.IsNullOrWhiteSpace(inventory.TemplatePath)) errors.Add("Vision.Inventory.TemplatePath is empty.");
        CheckRoi(inventory.Roi, "Vision.Inventory.Roi");
        CheckRoi(inventory.Grid, "Vision.Inventory.Grid");
        CheckRoi(inventory.Page1Button, "Vision.Inventory.Page1Button");
        CheckRoi(inventory.Page2Button, "Vision.Inventory.Page2Button");
        if (inventory.MinScore is <= 0 or > 1) errors.Add("Vision.Inventory.MinScore must be in (0, 1].");
        if (inventory.CellSize < 8) errors.Add("Vision.Inventory.CellSize must be >= 8.");
        if (inventory.MaxIconDifference is <= 0 or > 1) errors.Add("Vision.Inventory.MaxIconDifference must be in (0, 1].");
        foreach (var (slot, name) in new[] { (slots.Hp, "Hp"), (slots.Mp, "Mp"), (slots.Stm, "Stm") })
        {
            if (slot.Width != inventory.CellSize || slot.Height != inventory.CellSize)
                errors.Add($"Vision.PotionSlots.{name} must be {inventory.CellSize}x{inventory.CellSize} (Vision.Inventory.CellSize).");
        }

        var restock = config.Potions.Restock;
        CheckKey(restock.InventoryKey, "Potions.Restock.InventoryKey");
        CheckKey(restock.PageKey, "Potions.Restock.PageKey");
        if (restock.BelowCount is < 1 or > 99) errors.Add("Potions.Restock.BelowCount must be 1-99.");
        if (restock.ConfirmTicks < 1) errors.Add("Potions.Restock.ConfirmTicks must be >= 1.");
        if (restock.StepDelayMs < 0 || restock.RetryAfterSeconds < 0) errors.Add("Potions.Restock.StepDelayMs/RetryAfterSeconds must be >= 0.");
        if (restock.ToggleTimeoutMs <= 0) errors.Add("Potions.Restock.ToggleTimeoutMs must be positive.");
        if (!client.Contains(new PixelRect(restock.MouseRest.X, restock.MouseRest.Y, 1, 1)))
            errors.Add("Potions.Restock.MouseRest must be inside the client area.");
        else if (inventory.Grid.Contains(new PixelRect(restock.MouseRest.X, restock.MouseRest.Y, 1, 1)))
            errors.Add("Potions.Restock.MouseRest must not be over the inventory grid.");

        return errors;

        void CheckPotion(PotionConfig potion, string name)
        {
            CheckKey(potion.Key, name + ".Key");
            if (potion.BelowPercent is < 0 or > 100) errors.Add($"{name}.BelowPercent must be 0-100.");
            if (potion.CooldownMs < 0) errors.Add($"{name}.CooldownMs must be >= 0.");
        }

        void CheckRoi(PixelRect roi, string name)
        {
            if (roi.IsEmpty || !client.Contains(roi))
                errors.Add($"{name} {roi} must be non-empty and inside the {client.Width}x{client.Height} client area.");
        }

        void CheckBar(BarConfig bar, string name)
        {
            CheckRoi(bar.Roi, name + ".Roi");
            if (bar.Colors.Count == 0) errors.Add($"{name}.Colors needs at least one HSV range.");
            foreach (var c in bar.Colors)
            {
                if (c.HMin is < 0 or > 179 || c.HMax is < 0 or > 179 || c.SMin > c.SMax || c.VMin > c.VMax ||
                    c.SMin < 0 || c.SMax > 255 || c.VMin < 0 || c.VMax > 255)
                    errors.Add($"{name}.Colors: invalid range {c} (H 0-179, S/V 0-255, min <= max).");
            }
            if (bar.MinRowCoverage is <= 0 or > 1) errors.Add($"{name}.MinRowCoverage must be in (0, 1].");
            if (bar.MaxGapRows < 0) errors.Add($"{name}.MaxGapRows must be >= 0.");
        }

        void CheckKey(string value, string name)
        {
            if (!KeyChord.TryParse(value, out _, out var error))
                errors.Add($"{name}: {error}");
        }
    }
}
