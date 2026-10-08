using BotPriston.Core.Vision;
using OpenCvSharp;

namespace BotPriston.App.Commands;

/// <summary>
/// motion --source samples/record_X: replays a recording (frames in name order) through the walking
/// detector and lists every frame where the ground moved and every walking episode.
/// </summary>
public static class MotionCommand
{
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png"];

    public static int Run(BotContext ctx, CommandLine cmd)
    {
        var source = cmd.String("source") ?? throw new UsageException("motion needs --source <folder with consecutive frames>.");
        cmd.EnsureAllConsumed();

        var files = Directory.EnumerateFiles(source)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count < 2) throw new UsageException($"Need at least 2 frames in {source}.");

        using var detector = new MotionDetector(ctx.Config.Vision.Motion);
        int moving = 0, episodes = 0;
        bool walking = false;
        foreach (var file in files)
        {
            using var image = Cv2.ImRead(file, ImreadModes.Color);
            var m = detector.Update(image);
            if (m.Moving)
            {
                moving++;
                Console.WriteLine($"{Path.GetFileName(file),-40} {m}");
            }
            if (m.Walking && !walking) episodes++;
            walking = m.Walking || (walking && m.Moving);
        }

        ctx.Log.Information("{Frames} frames: ground moved in {Moving}, walking episodes: {Episodes}", files.Count, moving, episodes);
        return 0;
    }
}
