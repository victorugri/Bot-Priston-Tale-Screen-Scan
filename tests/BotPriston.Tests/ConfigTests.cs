using BotPriston.Core.Config;

namespace BotPriston.Tests;

public class ConfigTests
{
    [Fact]
    public void RepositoryConfig_LoadsAndValidates()
    {
        var config = ConfigLoader.Load(TestConfig.RepoConfigPath);

        Assert.Equal(1600, config.Window.ExpectedClientWidth);
        Assert.Equal(900, config.Window.ExpectedClientHeight);
        Assert.Empty(ConfigLoader.Validate(config));
        Assert.True(File.Exists(Path.Combine(TestPaths.RepoRoot, config.Vision.Hud.TemplatePath)));
    }

    [Fact]
    public void Load_RejectsUnknownProperties()
    {
        var json = TestConfig.LoadJson();
        json["Window"]!["ProcesName"] = "game";
        using var file = TempFile.WithText(json.ToJsonString());

        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Load(file.Path));
        Assert.Contains("ProcesName", ex.Message);
    }

    [Fact]
    public void Load_ReportsInvalidHotkey()
    {
        var json = TestConfig.LoadJson();
        json["Hotkeys"]!["Quit"] = "Ctrl+Nope";
        using var file = TempFile.WithText(json.ToJsonString());

        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Load(file.Path));
        Assert.Contains("Hotkeys.Quit", ex.Message);
    }

    [Fact]
    public void Load_ReportsRoiOutsideClientArea()
    {
        var json = TestConfig.LoadJson();
        json["Vision"]!["PlayerBars"]!["Mp"]!["Roi"]!["X"] = 1595;
        using var file = TempFile.WithText(json.ToJsonString());

        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Load(file.Path));
        Assert.Contains("Vision.PlayerBars.Mp.Roi", ex.Message);
    }

    [Fact]
    public void Load_ReportsInvalidHsvRange()
    {
        var json = TestConfig.LoadJson();
        json["Vision"]!["PlayerBars"]!["Hp"]!["Colors"]![0]!["HMax"] = 200;
        using var file = TempFile.WithText(json.ToJsonString());

        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Load(file.Path));
        Assert.Contains("Vision.PlayerBars.Hp.Colors", ex.Message);
    }

    [Fact]
    public void Validate_RequiresSomeWindowFilter()
    {
        var config = new BotConfig();

        Assert.Contains(ConfigLoader.Validate(config), e => e.StartsWith("Window:"));
    }

    [Fact]
    public void Load_ParsesBackendEnumByName()
    {
        var json = TestConfig.LoadJson();
        json["Capture"]!["Backend"] = "BitBlt";
        using var file = TempFile.WithText(json.ToJsonString());

        Assert.Equal(CaptureBackend.BitBlt, ConfigLoader.Load(file.Path).Capture.Backend);
    }
}
