using BotPriston.Core.Config;
using BotPriston.Core.Input;
using BotPriston.Core.Vision;
using BotPriston.Hosting;
using BotPriston.Platform.Capture;
using BotPriston.Platform.Window;
using Serilog;

namespace BotPriston.App;

/// <summary>Things every command needs: config, logger, and the located game window.</summary>
public sealed class BotContext(BotConfig config, string configPath, ILogger log)
{
    public BotConfig Config { get; } = config;
    public string ConfigPath { get; } = configPath;
    public ILogger Log { get; } = log;

    public string SamplesDir => Path.GetFullPath(Config.Paths.Samples);

    /// <summary>
    /// Finds the game window or throws with a helpful message. Commands that act on the game pass
    /// <paramref name="requireExpectedSize"/>: with any other client size every ROI is wrong, so they refuse to start.
    /// </summary>
    public GameWindow FindGameWindow(bool requireExpectedSize = false) =>
        GameLocator.Find(Config, Log, requireExpectedSize);

    public WindowCaptureSource CreateCaptureSource(GameWindow window, CaptureBackend? backendOverride = null) =>
        BotFactory.CreateCaptureSource(window, Config.Capture, Log, backendOverride);

    public VisionPipeline CreateVision() => BotFactory.CreateVision(Config);

    public IInputSink CreateInput(GameWindow window) => BotFactory.CreateInput(window, Config, Log);

    /// <summary>Waits until the user puts the game in the foreground. Input is never sent to another window.</summary>
    public bool WaitForFocus(GameWindow window, int seconds, CancellationToken cancel)
    {
        if (window.IsForeground) return true;
        Log.Information("Click on the game window within {Seconds}s (input is only sent to the focused game)", seconds);
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline && !cancel.IsCancellationRequested)
        {
            if (window.IsForeground)
            {
                cancel.WaitHandle.WaitOne(500); // let the click that focused the game finish
                return true;
            }
            cancel.WaitHandle.WaitOne(100);
        }
        Log.Error("The game did not get focus; nothing was sent.");
        return false;
    }

    public void WarnIfInputBlocked(GameWindow window) => BotFactory.WarnIfInputBlocked(window, Log);

    public static CaptureBackend? ParseBackend(string? text) => text?.ToLowerInvariant() switch
    {
        null => null,
        "auto" => CaptureBackend.Auto,
        "wgc" or "windowsgraphicscapture" => CaptureBackend.WindowsGraphicsCapture,
        "bitblt" => CaptureBackend.BitBlt,
        _ => throw new UsageException($"Unknown backend '{text}'. Use auto, wgc or bitblt."),
    };
}
