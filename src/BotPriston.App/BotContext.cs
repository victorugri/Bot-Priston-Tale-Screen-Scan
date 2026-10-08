using BotPriston.Core.Config;
using BotPriston.Core.Input;
using BotPriston.Core.Vision;
using BotPriston.Platform.Input;
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
    public GameWindow FindGameWindow(bool requireExpectedSize = false)
    {
        var matches = WindowFinder.FindMatches(Config.Window);
        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Game window not found (ProcessName='{Config.Window.ProcessName}', TitleContains='{Config.Window.TitleContains}'). " +
                "Is the game running? Run the 'windows' command to see candidates and fix the Window section of the config.");
        }

        var window = matches[0];
        if (matches.Count > 1)
        {
            Log.Warning("{Count} windows match the filters; using the largest: {Window}", matches.Count, window);
            foreach (var other in matches.Skip(1))
                Log.Warning("  also matched: {Window}", other);
        }

        if (window.IsMinimized)
            throw new InvalidOperationException($"Game window {window} is minimized. Restore it and try again.");

        var client = window.ClientScreenRect;
        Log.Information("Game window: {Window}, client {W}x{H} at screen ({X},{Y})",
            window, client.Width, client.Height, client.X, client.Y);

        if (client.Width != Config.Window.ExpectedClientWidth || client.Height != Config.Window.ExpectedClientHeight)
        {
            var message = $"Client area is {client.Width}x{client.Height} but {Config.Window.ExpectedClientWidth}x{Config.Window.ExpectedClientHeight} " +
                          "is expected, so every screen position the bot uses is wrong. The window was probably resized or snapped; " +
                          "run the 'layout' command (as administrator) to restore it.";
            if (requireExpectedSize)
                throw new InvalidOperationException(message);
            Log.Warning("{Message}", message);
        }

        return window;
    }

    public WindowCaptureSource CreateCaptureSource(GameWindow window, CaptureBackend? backendOverride = null)
    {
        var captureConfig = Config.Capture;
        if (backendOverride is { } backend)
            captureConfig = new CaptureConfig { Backend = backend, FrameTimeoutMs = captureConfig.FrameTimeoutMs };

        var source = CaptureSourceFactory.Create(window, captureConfig, Log);
        Log.Information("Capture backend: {Backend}", source.Name);
        return source;
    }

    public VisionPipeline CreateVision() => new(Config.Vision);

    public IInputSink CreateInput(GameWindow window)
    {
        if (Config.Safety.DryRun)
        {
            Log.Warning("DRY-RUN: decisions are logged, no input is sent");
            return new DryRunInputSink(Log);
        }
        return new SendInputSink(window, Config.Input, Log);
    }

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

    public void WarnIfInputBlocked(GameWindow window)
    {
        if (ProcessElevation.InputBlockedWarning(window.ProcessId) is { } warning)
            Log.Warning("{Warning}", warning);
    }

    public static CaptureBackend? ParseBackend(string? text) => text?.ToLowerInvariant() switch
    {
        null => null,
        "auto" => CaptureBackend.Auto,
        "wgc" or "windowsgraphicscapture" => CaptureBackend.WindowsGraphicsCapture,
        "bitblt" => CaptureBackend.BitBlt,
        _ => throw new UsageException($"Unknown backend '{text}'. Use auto, wgc or bitblt."),
    };
}
