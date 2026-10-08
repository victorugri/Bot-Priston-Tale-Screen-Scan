using BotPriston.Core.Capture;
using BotPriston.Core.Vision;
using OpenCvSharp;

namespace BotPriston.App.Commands;

/// <summary>
/// Opens a window with the detections drawn over the image. Live (default) or over saved
/// screenshots with --source. Keys: q/Esc quit, s save the raw frame to samples,
/// space pause (live) / next image (files), n next, b previous.
/// </summary>
public static class DebugCommand
{
    private const string WindowName = "BotPriston debug";

    public static int Run(BotContext ctx, CommandLine cmd, CancellationToken cancel)
    {
        var sourcePath = cmd.String("source");
        var backend = BotContext.ParseBackend(cmd.String("backend"));
        int fps = cmd.Int("fps", 10, min: 1);
        cmd.EnsureAllConsumed();

        using var vision = ctx.CreateVision();
        var overlay = new OverlayRenderer(ctx.Config.Vision);
        Cv2.NamedWindow(WindowName, WindowFlags.AutoSize);
        try
        {
            return sourcePath is null
                ? RunLive(ctx, vision, overlay, backend, fps, cancel)
                : RunFiles(ctx, vision, overlay, sourcePath, cancel);
        }
        finally
        {
            Cv2.DestroyAllWindows();
        }
    }

    private static int RunLive(BotContext ctx, VisionPipeline vision, OverlayRenderer overlay,
        Core.Config.CaptureBackend? backend, int fps, CancellationToken cancel)
    {
        var window = ctx.FindGameWindow();
        using var source = ctx.CreateCaptureSource(window, backend);
        ctx.Log.Information("Debug window open. Keys: q/Esc quit, s save frame, space pause");

        int delayMs = Math.Max(1, 1000 / fps);
        bool paused = false;
        Frame? current = null;
        string? lastSummary = null;
        try
        {
            while (!cancel.IsCancellationRequested && IsWindowOpen())
            {
                if (!paused)
                {
                    var next = source.Grab();
                    if (next is not null)
                    {
                        current?.Dispose();
                        current = next;
                    }
                }

                if (current is not null)
                {
                    var snapshot = vision.Analyze(current.Image);
                    var summary = snapshot.ToString();
                    if (summary != lastSummary)
                    {
                        ctx.Log.Debug("{Snapshot}", summary);
                        lastSummary = summary;
                    }
                    (Core.Geometry.PixelPoint, CursorReading)? cursor = window.CursorClientPosition is { } mouse
                        ? (mouse, vision.Cursor.Detect(current.Image, mouse))
                        : null;
                    using var canvas = overlay.Render(current.Image, snapshot, paused ? "LIVE (paused)" : "LIVE", cursor);
                    Cv2.ImShow(WindowName, canvas);
                }

                int key = Cv2.WaitKey(delayMs) & 0xFF;
                if (key is 'q' or 27) break;
                if (key == ' ') paused = !paused;
                if (key == 's' && current is not null) SaveFrame(ctx, current.Image);
            }
        }
        finally
        {
            current?.Dispose();
        }
        return 0;
    }

    private static int RunFiles(BotContext ctx, VisionPipeline vision, OverlayRenderer overlay, string path, CancellationToken cancel)
    {
        using var files = new FileCaptureSource(path);
        var list = files.Files;
        ctx.Log.Information("{Count} image(s). Keys: n/space next, b previous, q/Esc quit", list.Count);

        int index = 0;
        while (!cancel.IsCancellationRequested)
        {
            using var image = Cv2.ImRead(list[index], ImreadModes.Color);
            var snapshot = vision.Analyze(image);
            var name = Path.GetFileName(list[index]);
            ctx.Log.Information("[{Index}/{Count}] {File}: {Snapshot}", index + 1, list.Count, name, snapshot);

            using (var canvas = overlay.Render(image, snapshot, $"[{index + 1}/{list.Count}] {name}"))
                Cv2.ImShow(WindowName, canvas);

            int key;
            do
            {
                key = Cv2.WaitKey(100) & 0xFF;
                if (!IsWindowOpen() || cancel.IsCancellationRequested) return 0;
            } while (key == 0xFF);

            if (key is 'q' or 27) break;
            if (key is 'n' or ' ') index = (index + 1) % list.Count;
            if (key == 'b') index = (index - 1 + list.Count) % list.Count;
        }
        return 0;
    }

    private static void SaveFrame(BotContext ctx, Mat image)
    {
        Directory.CreateDirectory(ctx.SamplesDir);
        var path = Path.Combine(ctx.SamplesDir, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_debug.png");
        Cv2.ImWrite(path, image);
        ctx.Log.Information("Saved {File}", Path.GetFileName(path));
    }

    private static bool IsWindowOpen()
    {
        try { return Cv2.GetWindowProperty(WindowName, WindowPropertyFlags.Visible) >= 1; }
        catch (OpenCVException) { return false; }
    }
}
