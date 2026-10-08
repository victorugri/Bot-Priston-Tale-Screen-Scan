using System.Diagnostics;
using BotPriston.Core.Capture;

namespace BotPriston.App.Commands;

/// <summary>
/// Prints window geometry and measures the capture backend. Output is meant to be pasted back
/// when something about capture looks wrong.
/// </summary>
public static class InfoCommand
{
    public static int Run(BotContext ctx, CommandLine cmd)
    {
        var backend = BotContext.ParseBackend(cmd.String("backend"));
        int samples = cmd.Int("frames", 30, min: 1);
        cmd.EnsureAllConsumed();

        var window = ctx.FindGameWindow();
        var log = ctx.Log;

        log.Information("Window rect (GetWindowRect):        {Rect}", window.WindowRect);
        log.Information("Visible frame (DWM extended bounds): {Rect}", window.VisibleFrameRect);
        log.Information("Client rect (screen):                {Rect}", window.ClientScreenRect);
        log.Information("Window DPI: {Dpi} (scale {Scale:P0})", window.Dpi, window.Dpi / 96.0);
        log.Information("Foreground: {Foreground}", window.IsForeground);

        using var source = ctx.CreateCaptureSource(window, backend);

        using (var first = source.Grab())
        {
            if (first is null)
            {
                log.Error("Could not grab a frame.");
                return 1;
            }

            var mean = OpenCvSharp.Cv2.Mean(first.Image);
            log.Information("Frame: {W}x{H}, client crop inside window image {Crop}", first.Width, first.Height, source.LastClientRectInFrame);
            log.Information("Mean BGR: ({B:F1}, {G:F1}, {R:F1})", mean.Val0, mean.Val1, mean.Val2);
            if (FrameStats.LooksBlack(first.Image))
                log.Warning("Frame is (almost) black. If the game is not on a black screen, try the other backend: --backend bitblt / --backend wgc");
        }

        var sw = Stopwatch.StartNew();
        int ok = 0;
        for (int i = 0; i < samples; i++)
        {
            using var frame = source.Grab();
            if (frame is not null) ok++;
        }
        sw.Stop();
        log.Information("Grabbed {Ok}/{Total} frames, {Ms:F1} ms per grab", ok, samples, sw.Elapsed.TotalMilliseconds / samples);
        return 0;
    }
}
