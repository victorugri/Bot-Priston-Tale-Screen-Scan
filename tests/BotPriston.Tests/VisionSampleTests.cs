using System.Text.Json;
using BotPriston.Core.Config;
using BotPriston.Core.Vision;
using OpenCvSharp;

namespace BotPriston.Tests;

/// <summary>
/// Runs the real vision pipeline over real game screenshots listed in samples/labels.json.
/// Add a screenshot + its expected values there to cover a new situation.
/// </summary>
public class VisionSampleTests
{
    private sealed record CursorLabel(int X, int Y, CursorKind Kind);
    private sealed record Label(bool? Hud, double? Hp, double? Mp, double? Stm, TargetStatus? Target, double? TargetHp, CursorLabel? Cursor, bool? LeftSkill, bool? RightSkill,
        Dictionary<string, int>? Potions, int? InventoryPage, string? Note);
    private sealed record LabelFile(double Tolerance, double TargetHpTolerance, Dictionary<string, Label> Samples);

    private static readonly LabelFile Labels = LoadLabels();

    public static TheoryData<string> LabeledSamples()
    {
        var data = new TheoryData<string>();
        foreach (var name in Labels.Samples.Keys)
            data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(LabeledSamples))]
    public void Pipeline_MatchesLabels(string fileName)
    {
        var label = Labels.Samples[fileName];
        var config = TestConfig.Load();
        using var vision = new VisionPipeline(config.Vision);
        // A key may carry a '#suffix' to give one image several label entries.
        using var image = Cv2.ImRead(Path.Combine(TestPaths.Samples, fileName.Split('#')[0]), ImreadModes.Color);
        Assert.False(image.Empty(), $"missing sample {fileName}");

        var snapshot = vision.Analyze(image);

        if (label.Hud is { } hud)
            Assert.True(hud == snapshot.Hud.Visible, $"HUD expected {hud}, got {snapshot.Hud} ({label.Note})");

        if (label.Hp is not null || label.Mp is not null || label.Stm is not null)
        {
            Assert.NotNull(snapshot.Bars);
            AssertPercent("HP", label.Hp, snapshot.Bars.Hp.Percent, Labels.Tolerance);
            AssertPercent("MP", label.Mp, snapshot.Bars.Mp.Percent, Labels.Tolerance);
            AssertPercent("STM", label.Stm, snapshot.Bars.Stm.Percent, Labels.Tolerance);
        }

        if (label.Target is { } status)
        {
            Assert.NotNull(snapshot.Target);
            Assert.True(status == snapshot.Target.Status, $"target expected {status}, got {snapshot.Target} ({label.Note})");
        }

        if (label.Cursor is { } cursor)
        {
            var reading = vision.Cursor.Detect(image, new BotPriston.Core.Geometry.PixelPoint(cursor.X, cursor.Y));
            Assert.True(cursor.Kind == reading.Kind, $"cursor expected {cursor.Kind}, got {reading} ({label.Note})");
        }

        if (label.LeftSkill is { } left)
            Assert.True(left == snapshot.Skills?.Left.Ready, $"left skill expected ready={left}, got {snapshot.Skills} ({label.Note})");
        if (label.RightSkill is { } right)
            Assert.True(right == snapshot.Skills?.Right.Ready, $"right skill expected ready={right}, got {snapshot.Skills} ({label.Note})");

        if (label.Potions is { } potions)
        {
            Assert.NotNull(snapshot.Potions);
            foreach (var (slot, expected) in potions)
            {
                var read = slot switch
                {
                    "Hp" => snapshot.Potions.Hp,
                    "Mp" => snapshot.Potions.Mp,
                    "Stm" => snapshot.Potions.Stm,
                    _ => throw new InvalidDataException($"unknown potion slot {slot} in labels.json"),
                };
                Assert.True(expected == read.Left, $"{slot} potions: expected {expected}, read {read} ({label.Note})");
            }
        }

        if (label.InventoryPage is { } page)
        {
            Assert.NotNull(snapshot.Inventory);
            Assert.True((page > 0) == snapshot.Inventory.Open, $"inventory expected {(page > 0 ? "open" : "closed")}, got {snapshot.Inventory} ({label.Note})");
            if (page > 0)
                Assert.True(page == snapshot.Inventory.Page, $"inventory page expected {page}, got {snapshot.Inventory} ({label.Note})");
        }

        if (label.TargetHp is { } targetHp)
        {
            Assert.NotNull(snapshot.Target?.HpPercent);
            AssertPercent("target HP", targetHp, snapshot.Target.HpPercent.Value, Labels.TargetHpTolerance);
        }
    }

    [Fact]
    public void Labels_CoverAllStates()
    {
        Assert.Contains(Labels.Samples.Values, l => l.Hud == true);
        Assert.Contains(Labels.Samples.Values, l => l.Hud == false);
        foreach (var status in Enum.GetValues<TargetStatus>())
            Assert.Contains(Labels.Samples.Values, l => l.Target == status);
        Assert.Contains(Labels.Samples.Values, l => l.Cursor?.Kind == CursorKind.Enemy);
        Assert.Contains(Labels.Samples.Values, l => l.Cursor?.Kind == CursorKind.Neutral);
        Assert.Contains(Labels.Samples.Values, l => l.RightSkill == true);
        Assert.Contains(Labels.Samples.Values, l => l.RightSkill == false);
        foreach (var page in new[] { 0, 1, 2 })
            Assert.Contains(Labels.Samples.Values, l => l.InventoryPage == page);
        Assert.Contains(Labels.Samples.Values, l => l.Potions?.Values.Any(n => n < 10) == true);
    }

    private static void AssertPercent(string what, double? expected, double actual, double tolerance)
    {
        if (expected is null) return;
        Assert.True(Math.Abs(expected.Value - actual) <= tolerance,
            $"{what}: expected {expected:F1}% ± {tolerance}, got {actual:F1}%");
    }

    private static LabelFile LoadLabels()
    {
        var json = File.ReadAllText(Path.Combine(TestPaths.Samples, "labels.json"));
        return JsonSerializer.Deserialize<LabelFile>(json, ConfigLoader.JsonOptions)
               ?? throw new InvalidDataException("samples/labels.json is empty");
    }
}
