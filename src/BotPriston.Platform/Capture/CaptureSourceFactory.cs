using BotPriston.Core.Config;
using BotPriston.Platform.Window;
using Serilog;

namespace BotPriston.Platform.Capture;

public static class CaptureSourceFactory
{
    public static WindowCaptureSource Create(GameWindow window, CaptureConfig config, ILogger log)
    {
        var timeout = TimeSpan.FromMilliseconds(config.FrameTimeoutMs);

        switch (config.Backend)
        {
            case CaptureBackend.WindowsGraphicsCapture:
                return new WgcCaptureSource(window, timeout, log);

            case CaptureBackend.BitBlt:
                return new BitBltCaptureSource(window, log);

            default:
                try
                {
                    return new WgcCaptureSource(window, timeout, log);
                }
                catch (Exception ex)
                {
                    log.Warning("Windows.Graphics.Capture unavailable ({Error}); falling back to BitBlt", ex.Message);
                    return new BitBltCaptureSource(window, log);
                }
        }
    }
}
