using BotPriston.Core.Config;
using BotPriston.Core.Input;
using BotPriston.Core.Vision;
using BotPriston.Platform.Capture;
using BotPriston.Platform.Input;
using BotPriston.Platform.Window;
using Serilog;

namespace BotPriston.Hosting;

/// <summary>Creates the pieces the bot is made of, from the config.</summary>
public static class BotFactory
{
    public static WindowCaptureSource CreateCaptureSource(GameWindow window, CaptureConfig config, ILogger log, CaptureBackend? backendOverride = null)
    {
        var captureConfig = backendOverride is { } backend
            ? new CaptureConfig { Backend = backend, FrameTimeoutMs = config.FrameTimeoutMs }
            : config;
        var source = CaptureSourceFactory.Create(window, captureConfig, log);
        log.Information("Capture backend: {Backend}", source.Name);
        return source;
    }

    public static VisionPipeline CreateVision(BotConfig config) => new(config.Vision);

    public static IInputSink CreateInput(GameWindow window, BotConfig config, ILogger log)
    {
        if (config.Safety.DryRun)
        {
            log.Warning("DRY-RUN: decisions are logged, no input is sent");
            return new DryRunInputSink(log);
        }
        return new SendInputSink(window, config.Input, log);
    }

    public static void WarnIfInputBlocked(GameWindow window, ILogger log)
    {
        if (ProcessElevation.InputBlockedWarning(window.ProcessId) is { } warning)
            log.Warning("{Warning}", warning);
    }
}
