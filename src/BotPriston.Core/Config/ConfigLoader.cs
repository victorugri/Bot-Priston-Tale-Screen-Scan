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
    /// Finds the config file: explicit path, else ./botconfig.json, else next to the executable.
    /// </summary>
    public static string Resolve(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath);

        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, DefaultFileName),
            Path.Combine(AppContext.BaseDirectory, DefaultFileName),
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
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

        var targeting = config.Targeting;
        if (targeting.Anchor.X < 0 || targeting.Anchor.Y < 0 || targeting.Anchor.X >= client.Width || targeting.Anchor.Y >= client.Height)
            errors.Add("Targeting.Anchor must be inside the client area.");
        if (targeting.RadiusX <= 0 || targeting.RadiusY <= 0) errors.Add("Targeting.RadiusX/RadiusY must be positive.");
        if (targeting.Step < 10) errors.Add("Targeting.Step must be >= 10.");
        if (targeting.MinRadius < 0 || targeting.EdgeMargin < 0) errors.Add("Targeting.MinRadius/EdgeMargin must be >= 0.");
        if (targeting.HoverDelayMs < 0 || targeting.ConfirmDelayMs < 0) errors.Add("Targeting delays must be >= 0.");
        foreach (var rect in targeting.Exclusions) CheckRoi(rect, "Targeting.Exclusions");

        var combat = config.Combat;
        if (combat.LostTargetGraceMs < 0 || combat.ReacquireRadius < 0 || combat.SearchRetryMs < 0)
            errors.Add("Combat: LostTargetGraceMs, ReacquireRadius and SearchRetryMs must be >= 0.");
        if (combat.NoProgressSeconds <= 0 || combat.MaxTargetSeconds <= 0)
            errors.Add("Combat: NoProgressSeconds and MaxTargetSeconds must be positive.");
        var rest = combat.Rest;
        if (rest.HpBelow > rest.HpUntil || rest.MpBelow > rest.MpUntil)
            errors.Add("Combat.Rest: each *Below must be <= its *Until.");
        if (rest.MaxSeconds < 0) errors.Add("Combat.Rest.MaxSeconds must be >= 0.");
        if (combat.Rest.MaxSeconds >= config.Safety.WatchdogSeconds)
            errors.Add("Combat.Rest.MaxSeconds must be shorter than Safety.WatchdogSeconds.");
        if (combat.Loot.Enabled) errors.Add("Combat.Loot.Enabled: looting is not implemented yet.");
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
