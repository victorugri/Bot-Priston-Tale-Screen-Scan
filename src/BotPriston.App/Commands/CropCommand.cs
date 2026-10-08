using OpenCvSharp;

namespace BotPriston.App.Commands;

/// <summary>Cuts a region out of a screenshot, e.g. to create a template: crop --source a.png --roi 655,880,140,12 --out t.png</summary>
public static class CropCommand
{
    public static int Run(BotContext ctx, CommandLine cmd)
    {
        var source = cmd.String("source") ?? throw new UsageException("crop needs --source <image>.");
        var roiText = cmd.String("roi") ?? throw new UsageException("crop needs --roi x,y,width,height.");
        var output = cmd.String("out") ?? throw new UsageException("crop needs --out <file.png>.");
        cmd.EnsureAllConsumed();

        var parts = roiText.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4 || !parts.All(p => int.TryParse(p, out _)))
            throw new UsageException("--roi must be x,y,width,height.");
        var v = parts.Select(int.Parse).ToArray();
        var roi = new Rect(v[0], v[1], v[2], v[3]);

        using var image = Cv2.ImRead(source, ImreadModes.Color);
        if (image.Empty()) throw new InvalidOperationException($"Could not read {source}");
        if (roi.X < 0 || roi.Y < 0 || roi.Width <= 0 || roi.Height <= 0 || roi.Right > image.Width || roi.Bottom > image.Height)
            throw new UsageException($"ROI {roiText} is outside the {image.Width}x{image.Height} image.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        using var crop = new Mat(image, roi);
        Cv2.ImWrite(output, crop);
        ctx.Log.Information("Saved {Out} ({W}x{H}) from {Source}", output, roi.Width, roi.Height, Path.GetFileName(source));
        return 0;
    }
}
