using System.Text.RegularExpressions;
using BotPriston.Core.Capture;
using BotPriston.Core.Input;
using BotPriston.Platform.Capture;
using BotPriston.Platform.Input;
using OpenCvSharp;

namespace BotPriston.App.Commands;

/// <summary>
/// Saves client-area screenshots to the samples folder (lossless PNG). Three modes:
/// single shot after a delay, timed burst (--count/--interval), or --hotkey (press a key in-game).
/// </summary>
public static partial class CaptureCommand
{
    public static int Run(BotContext ctx, CommandLine cmd, CancellationToken cancel)
    {
        var backend = BotContext.ParseBackend(cmd.String("backend"));
        bool hotkey = cmd.Flag("hotkey");
        double delaySeconds = cmd.Double("delay", hotkey ? 0 : 3, min: 0);
        int count = cmd.Int("count", 1, min: 1);
        int intervalMs = cmd.Int("interval", 1000, min: 50);
        string label = SanitizeLabel(cmd.String("label") ?? "");
        bool saveWindow = cmd.Flag("window");
        cmd.EnsureAllConsumed();

        Directory.CreateDirectory(ctx.SamplesDir);
        var window = ctx.FindGameWindow();
        using var source = ctx.CreateCaptureSource(window, backend);
        var writer = new SampleWriter(ctx, source, label, saveWindow);

        return hotkey
            ? RunHotkeyMode(ctx, writer, cancel)
            : RunTimed(ctx, writer, delaySeconds, count, intervalMs, cancel);
    }

    private static int RunTimed(BotContext ctx, SampleWriter writer, double delaySeconds, int count, int intervalMs, CancellationToken cancel)
    {
        if (delaySeconds > 0)
        {
            ctx.Log.Information("Capturing in {Delay:F1}s — switch to the game now", delaySeconds);
            if (cancel.WaitHandle.WaitOne(TimeSpan.FromSeconds(delaySeconds))) return 130;
        }

        int saved = 0;
        for (int i = 0; i < count && !cancel.IsCancellationRequested; i++)
        {
            if (i > 0 && cancel.WaitHandle.WaitOne(intervalMs)) break;
            if (writer.SaveOne()) saved++;
        }

        ctx.Log.Information("Saved {Saved}/{Count} screenshot(s) to {Dir}", saved, count, ctx.SamplesDir);
        return saved == count ? 0 : 1;
    }

    private static int RunHotkeyMode(BotContext ctx, SampleWriter writer, CancellationToken cancel)
    {
        var captureKey = new KeyPressDetector(KeyChord.Parse(ctx.Config.Hotkeys.Capture));
        var quitKey = new KeyPressDetector(KeyChord.Parse(ctx.Config.Hotkeys.Quit));
        ctx.Log.Information("Hotkey mode: press {Capture} to save a screenshot, {Quit} (or Ctrl+C here) to stop",
            captureKey.Chord, quitKey.Chord);

        int saved = 0;
        while (!cancel.IsCancellationRequested)
        {
            if (quitKey.Pressed()) break;
            if (captureKey.Pressed() && writer.SaveOne())
            {
                saved++;
                Console.Beep(1200, 60);
            }
            Thread.Sleep(15);
        }

        ctx.Log.Information("Saved {Saved} screenshot(s) to {Dir}", saved, ctx.SamplesDir);
        return 0;
    }

    private static string SanitizeLabel(string label) =>
        InvalidLabelChars().Replace(label.Trim(), "_").Trim('_');

    [GeneratedRegex(@"[^A-Za-z0-9_\-]+")]
    private static partial Regex InvalidLabelChars();

    private sealed class SampleWriter(BotContext ctx, WindowCaptureSource source, string label, bool saveWindow)
    {
        public bool SaveOne()
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var baseName = label.Length > 0 ? $"{stamp}_{label}" : stamp;

            using var frame = source.Grab();
            if (frame is null)
            {
                ctx.Log.Error("No frame captured");
                return false;
            }

            var path = Path.Combine(ctx.SamplesDir, baseName + ".png");
            if (!Cv2.ImWrite(path, frame.Image))
            {
                ctx.Log.Error("Could not write {Path}", path);
                return false;
            }

            ctx.Log.Information("Saved {File} ({W}x{H}, {Backend}{Focus})", Path.GetFileName(path), frame.Width, frame.Height,
                frame.Source, source.Window.IsForeground ? "" : ", game NOT in focus");
            if (FrameStats.LooksBlack(frame.Image))
                ctx.Log.Warning("Screenshot is (almost) black — try the other backend (--backend bitblt / wgc)");

            if (saveWindow)
            {
                using var full = source.GrabWindow();
                if (full is not null)
                {
                    var fullPath = Path.Combine(ctx.SamplesDir, baseName + "_window.png");
                    Cv2.ImWrite(fullPath, full.Image);
                    ctx.Log.Information("Saved {File} (uncropped window, client at {Crop})", Path.GetFileName(fullPath), source.LastClientRectInFrame);
                }
            }

            return true;
        }
    }
}
