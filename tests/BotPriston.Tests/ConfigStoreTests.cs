using System.Text.Json.Nodes;
using BotPriston.Core.Config;

namespace BotPriston.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly TempDir _dir = TempDir.Create();
    private readonly string _basePath;

    public ConfigStoreTests()
    {
        _basePath = Path.Combine(_dir.Path, ConfigLoader.DefaultFileName);
        File.Copy(TestConfig.RepoConfigPath, _basePath);
    }

    private string SettingsPath => ConfigStore.SettingsPathFor(_basePath);

    [Fact]
    public void WithoutSettings_LoadsDefaults()
    {
        var config = ConfigStore.Load(_basePath);

        Assert.Equal(ConfigLoader.Load(_basePath).Potions.Hp.BelowPercent, config.Potions.Hp.BelowPercent);
    }

    [Fact]
    public void Save_WritesOnlyWhatChanged_AndLoadMergesItBack()
    {
        var config = ConfigStore.Load(_basePath);
        config.Potions.Hp.BelowPercent = 55;
        config.Combat.RightSkill.Enabled = false;

        ConfigStore.Save(_basePath, config);

        var saved = JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();
        Assert.Equal(["Potions", "Combat"], saved.Select(p => p.Key).Order().Reverse().ToArray());
        Assert.Equal(55, saved["Potions"]!["Hp"]!["BelowPercent"]!.GetValue<double>());
        Assert.Single(saved["Potions"]!.AsObject()); // only Hp
        Assert.Single(saved["Potions"]!["Hp"]!.AsObject()); // only BelowPercent

        var reloaded = ConfigStore.Load(_basePath);
        Assert.Equal(55, reloaded.Potions.Hp.BelowPercent);
        Assert.False(reloaded.Combat.RightSkill.Enabled);
        Assert.Equal(config.Potions.Mp.BelowPercent, reloaded.Potions.Mp.BelowPercent); // untouched values from defaults
    }

    [Fact]
    public void Save_WithNothingChanged_RemovesSettings()
    {
        File.WriteAllText(SettingsPath, """{ "Potions": { "Hp": { "BelowPercent": 55 } } }""");
        var config = ConfigLoader.Load(_basePath); // defaults

        ConfigStore.Save(_basePath, config);

        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void DefaultsChangingLater_StillApplyToUntouchedValues()
    {
        File.WriteAllText(SettingsPath, """{ "Potions": { "Hp": { "BelowPercent": 55 } } }""");
        var text = File.ReadAllText(_basePath);
        File.WriteAllText(_basePath, text.Replace("\"TickMs\": 100", "\"TickMs\": 150"));

        var config = ConfigStore.Load(_basePath);

        Assert.Equal(55, config.Potions.Hp.BelowPercent);
        Assert.Equal(150, config.Bot.TickMs);
    }

    [Fact]
    public void InvalidSettings_AreRejectedWithTheFileName()
    {
        File.WriteAllText(SettingsPath, """{ "Potions": { "Hp": { "BelowPercent": 150 } } }""");

        var ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(_basePath));
        Assert.Contains(ConfigStore.SettingsFileName, ex.Message);
        Assert.Contains("Potions.Hp.BelowPercent", ex.Message);
    }

    [Fact]
    public void Save_RefusesInvalidValues()
    {
        var config = ConfigStore.Load(_basePath);
        config.Potions.Mp.Key = "NotAKey";

        Assert.Throws<ConfigException>(() => ConfigStore.Save(_basePath, config));
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void ResetToDefaults_DeletesSettings()
    {
        File.WriteAllText(SettingsPath, """{ "Bot": { "TickMs": 200 } }""");

        ConfigStore.ResetToDefaults(_basePath);

        Assert.False(File.Exists(SettingsPath));
        Assert.Equal(100, ConfigStore.Load(_basePath).Bot.TickMs);
    }

    public void Dispose() => _dir.Dispose();
}
