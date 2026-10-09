using System.Text.Json;
using System.Text.Json.Nodes;
using BotPriston.Core.Config;

namespace BotPriston.Tests;

/// <summary>Access to the repository's real botconfig.json, with paths made absolute for tests.</summary>
internal static class TestConfig
{
    public static string RepoConfigPath => Path.Combine(TestPaths.RepoRoot, ConfigLoader.DefaultFileName);

    public static BotConfig Load()
    {
        var config = ConfigLoader.Load(RepoConfigPath);
        config.Vision.Hud.TemplatePath = Path.Combine(TestPaths.RepoRoot, config.Vision.Hud.TemplatePath);
        config.Vision.Inventory.TemplatePath = Path.Combine(TestPaths.RepoRoot, config.Vision.Inventory.TemplatePath);
        return config;
    }

    /// <summary>The repository config as a mutable JSON tree, for tests that tweak one field.</summary>
    public static JsonObject LoadJson() =>
        JsonNode.Parse(File.ReadAllText(RepoConfigPath), documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        })!.AsObject();
}
