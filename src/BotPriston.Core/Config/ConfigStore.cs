using System.Text.Json;
using System.Text.Json.Nodes;

namespace BotPriston.Core.Config;

/// <summary>
/// Two config layers: botconfig.json holds every setting with its documentation (versioned), and
/// settings.json holds only what the user changed (from the UI; not versioned). The effective config
/// is botconfig.json with settings.json merged on top.
/// </summary>
public static class ConfigStore
{
    public const string SettingsFileName = "settings.json";

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>settings.json next to the given botconfig.json.</summary>
    public static string SettingsPathFor(string basePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(basePath))!, SettingsFileName);

    /// <summary>Defaults only (botconfig.json), validated.</summary>
    public static BotConfig LoadDefaults(string basePath) => ConfigLoader.Load(basePath);

    /// <summary>botconfig.json + settings.json (if present), validated.</summary>
    public static BotConfig Load(string basePath)
    {
        var settingsPath = SettingsPathFor(basePath);
        if (!File.Exists(settingsPath))
            return ConfigLoader.Load(basePath);

        var merged = ReadObject(basePath);
        JsonObject overrides;
        try
        {
            overrides = ReadObject(settingsPath);
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"Invalid JSON in {settingsPath}: {ex.Message}", ex);
        }
        Merge(merged, overrides);

        BotConfig? config;
        try
        {
            config = merged.Deserialize<BotConfig>(ConfigLoader.JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"Invalid setting in {settingsPath}: {ex.Message}", ex);
        }
        if (config is null)
            throw new ConfigException($"Config is empty: {basePath}");

        var errors = ConfigLoader.Validate(config);
        if (errors.Count > 0)
            throw new ConfigException($"Invalid config ({basePath} + {settingsPath}):\n  - " + string.Join("\n  - ", errors));
        return config;
    }

    /// <summary>
    /// Validates <paramref name="config"/> and writes to settings.json only the values that differ from
    /// botconfig.json. Nothing different = settings.json is removed.
    /// </summary>
    public static void Save(string basePath, BotConfig config)
    {
        var errors = ConfigLoader.Validate(config);
        if (errors.Count > 0)
            throw new ConfigException("Not saved:\n  - " + string.Join("\n  - ", errors));

        var defaults = JsonSerializer.SerializeToNode(LoadDefaults(basePath), ConfigLoader.JsonOptions)!.AsObject();
        var current = JsonSerializer.SerializeToNode(config, ConfigLoader.JsonOptions)!.AsObject();
        var diff = Diff(defaults, current);

        var settingsPath = SettingsPathFor(basePath);
        if (diff.Count == 0)
        {
            File.Delete(settingsPath);
            return;
        }
        File.WriteAllText(settingsPath, diff.ToJsonString(ConfigLoader.JsonOptions));
    }

    /// <summary>Deletes settings.json: back to botconfig.json defaults.</summary>
    public static void ResetToDefaults(string basePath) => File.Delete(SettingsPathFor(basePath));

    private static JsonObject ReadObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions)?.AsObject()
        ?? throw new ConfigException($"Config file is empty: {path}");

    /// <summary>Objects are merged key by key (case-insensitive); anything else in the overlay replaces the base.</summary>
    internal static void Merge(JsonObject target, JsonObject overlay)
    {
        foreach (var (key, value) in overlay.ToList())
        {
            var existingKey = target.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) ?? key;
            if (value is JsonObject overlayObject && target[existingKey] is JsonObject targetObject)
            {
                Merge(targetObject, overlayObject);
            }
            else
            {
                target.Remove(existingKey);
                target[existingKey] = value?.DeepClone();
            }
        }
    }

    /// <summary>The parts of <paramref name="current"/> that differ from <paramref name="defaults"/> (whole arrays/values).</summary>
    internal static JsonObject Diff(JsonObject defaults, JsonObject current)
    {
        var diff = new JsonObject();
        foreach (var (key, value) in current)
        {
            var baseline = defaults[key];
            if (value is JsonObject currentObject && baseline is JsonObject defaultObject)
            {
                var nested = Diff(defaultObject, currentObject);
                if (nested.Count > 0) diff[key] = nested;
            }
            else if (!JsonNode.DeepEquals(value, baseline))
            {
                diff[key] = value?.DeepClone();
            }
        }
        return diff;
    }
}
