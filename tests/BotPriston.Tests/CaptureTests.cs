using BotPriston.Core.Capture;
using BotPriston.Core.Geometry;
using OpenCvSharp;

namespace BotPriston.Tests;

public class CaptureGeometryTests
{
    [Fact]
    public void ClientRect_IsOffsetByTitleBarAndBorder()
    {
        // Typical Windows 11 window: 1px visible border, ~31px title bar.
        var visibleFrame = new PixelRect(100, 50, 1602, 932);
        var client = new PixelRect(101, 81, 1600, 900);

        var crop = CaptureGeometry.ClientRectInFrame(visibleFrame, client, 1602, 932);

        Assert.Equal(new PixelRect(1, 31, 1600, 900), crop);
    }

    [Fact]
    public void ClientRect_IsClippedToFrame()
    {
        var crop = CaptureGeometry.ClientRectInFrame(
            new PixelRect(0, 0, 800, 600), new PixelRect(10, 30, 800, 600), 800, 600);

        Assert.Equal(new PixelRect(10, 30, 790, 570), crop);
    }

    [Fact]
    public void ClientRect_EmptyWhenNoOverlap()
    {
        var crop = CaptureGeometry.ClientRectInFrame(
            new PixelRect(0, 0, 100, 100), new PixelRect(500, 500, 100, 100), 100, 100);

        Assert.True(crop.IsEmpty);
    }
}

public class FrameStatsTests
{
    [Fact]
    public void LooksBlack_DetectsBlackAndNonBlack()
    {
        using var black = new Mat(90, 160, MatType.CV_8UC3, Scalar.All(1));
        using var scene = new Mat(90, 160, MatType.CV_8UC3, new Scalar(40, 80, 120));

        Assert.True(FrameStats.LooksBlack(black));
        Assert.False(FrameStats.LooksBlack(scene));
    }
}

public class FileCaptureSourceTests
{
    [Fact]
    public void ReplaysImagesInNameOrder_AndLoops()
    {
        using var dir = TempDir.Create();
        WriteSolid(Path.Combine(dir.Path, "b.png"), 200);
        WriteSolid(Path.Combine(dir.Path, "a.png"), 100);
        File.WriteAllText(Path.Combine(dir.Path, "notes.txt"), "ignored");

        using var source = new FileCaptureSource(dir.Path);

        Assert.Equal(2, source.Files.Count);
        Assert.Equal(100, FirstBlue(source.Grab()));
        Assert.Equal(200, FirstBlue(source.Grab()));
        Assert.Equal(100, FirstBlue(source.Grab()));
    }

    [Fact]
    public void WithoutLoop_ReturnsNullAtEnd()
    {
        using var dir = TempDir.Create();
        var file = Path.Combine(dir.Path, "one.png");
        WriteSolid(file, 50);

        using var source = new FileCaptureSource(file, loop: false);

        using (var frame = source.Grab())
        {
            Assert.NotNull(frame);
            Assert.Equal(MatType.CV_8UC3, frame.Image.Type());
        }
        Assert.Null(source.Grab());
    }

    private static void WriteSolid(string path, byte blue)
    {
        using var mat = new Mat(9, 16, MatType.CV_8UC3, new Scalar(blue, 0, 0));
        Cv2.ImWrite(path, mat);
    }

    private static byte FirstBlue(Frame? frame)
    {
        Assert.NotNull(frame);
        using (frame)
            return frame.Image.At<Vec3b>(0, 0).Item0;
    }
}
