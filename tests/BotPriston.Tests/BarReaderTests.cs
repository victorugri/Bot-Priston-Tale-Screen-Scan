using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using BotPriston.Core.Vision;
using OpenCvSharp;

namespace BotPriston.Tests;

/// <summary>Synthetic tubes: exact expectations independent of real screenshots.</summary>
public class BarReaderTests
{
    private static readonly Scalar Red = new(10, 10, 220);      // BGR
    private static readonly Scalar Glass = new(20, 25, 30);     // dark empty tube
    private static readonly Scalar Highlight = new(200, 200, 230);

    private static BarConfig RedBar() => new()
    {
        Roi = new PixelRect(10, 20, 8, 100),
        Colors = [new HsvRange { HMin = 170, HMax = 10, SMin = 120, VMin = 70 }],
        MinRowCoverage = 0.5,
        MaxGapRows = 2,
    };

    private static Mat Tube(int filledRows)
    {
        var image = new Mat(140, 40, MatType.CV_8UC3, Glass);
        if (filledRows > 0)
            image[new Rect(10, 120 - filledRows, 8, filledRows)].SetTo(Red);
        return image;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(99)]
    [InlineData(100)]
    public void Read_ReturnsExactFill(int filled)
    {
        using var image = Tube(filled);

        var reading = BarReader.Read(image, RedBar());

        Assert.Equal(filled, reading.FilledRows);
        Assert.Equal(filled, reading.Percent, precision: 6);
        Assert.Equal(120 - filled, reading.TopY);
    }

    [Fact]
    public void Read_IgnoresSameColorScenery_AboveTheSurface()
    {
        using var image = Tube(40);
        image[new Rect(10, 20, 8, 30)].SetTo(Red); // red "leaves" at the top of the ROI, separated by glass

        Assert.Equal(40, BarReader.Read(image, RedBar()).FilledRows);
    }

    [Fact]
    public void Read_ToleratesSmallHighlightGaps()
    {
        using var image = Tube(60);
        image[new Rect(10, 85, 8, 2)].SetTo(Highlight); // 2-row reflection inside the liquid

        Assert.Equal(60, BarReader.Read(image, RedBar()).FilledRows);
    }

    [Fact]
    public void Read_StopsAtGapsLargerThanTolerance()
    {
        using var image = Tube(60);
        image[new Rect(10, 85, 8, 3)].SetTo(Glass);

        Assert.Equal(120 - 88, BarReader.Read(image, RedBar()).FilledRows);
    }

    [Fact]
    public void Read_IgnoresThinVerticalHighlightStripe()
    {
        using var image = Tube(50);
        image[new Rect(12, 70, 1, 50)].SetTo(Highlight); // 1 of 8 columns is a reflection

        Assert.Equal(50, BarReader.Read(image, RedBar()).FilledRows);
    }

    [Fact]
    public void Read_RoiOutsideFrame_ReturnsZero()
    {
        using var image = new Mat(50, 50, MatType.CV_8UC3, Red);

        Assert.Equal(0, BarReader.Read(image, RedBar()).Percent);
    }
}

public class HsvRangeTests
{
    [Fact]
    public void WrappingHue_MatchesBothEndsOfTheCircle()
    {
        using var hsv = new Mat(1, 3, MatType.CV_8UC3);
        hsv.Set(0, 0, new Vec3b(2, 200, 200));   // red, low hue
        hsv.Set(0, 1, new Vec3b(175, 200, 200)); // red, high hue
        hsv.Set(0, 2, new Vec3b(60, 200, 200));  // green
        var red = new HsvRange { HMin = 170, HMax = 10, SMin = 120, VMin = 70 };

        using var mask = HsvRange.Mask(hsv, [red]);

        Assert.Equal(255, mask.At<byte>(0, 0));
        Assert.Equal(255, mask.At<byte>(0, 1));
        Assert.Equal(0, mask.At<byte>(0, 2));
    }
}

public class OverlayRendererTests
{
    [Fact]
    public void Render_ReturnsNewImage_AndLeavesInputUntouched()
    {
        var config = TestConfig.Load();
        using var vision = new VisionPipeline(config.Vision);
        using var image = Cv2.ImRead(Path.Combine(TestPaths.Samples, "20261007_173954_389_farm.png"), ImreadModes.Color);
        using var before = image.Clone();

        using var canvas = new OverlayRenderer(config.Vision).Render(image, vision.Analyze(image), "test");

        Assert.Equal(image.Size(), canvas.Size());
        using var diff = new Mat();
        Cv2.Absdiff(image, before, diff);
        Assert.Equal(0, Cv2.CountNonZero(diff.Reshape(1)));
    }
}
