using BotPriston.Core.Capture;
using BotPriston.Core.Geometry;
using BotPriston.Platform.Window;
using OpenCvSharp;
using Serilog;

namespace BotPriston.Platform.Capture;

/// <summary>An image of the whole visible window and the screen rect it covers.</summary>
public sealed record WindowImage(Mat Image, PixelRect ScreenRect);

/// <summary>
/// Base for live capture backends. A backend only has to produce an image of the visible
/// window; cropping to the client area is done here so every backend behaves the same.
/// </summary>
public abstract class WindowCaptureSource(GameWindow window, ILogger log) : ICaptureSource
{
    private PixelRect _lastLoggedCrop;

    public GameWindow Window { get; } = window;
    public abstract string Name { get; }
    protected ILogger Log { get; } = log;

    /// <summary>Client rect inside the last full-window image (for diagnostics).</summary>
    public PixelRect LastClientRectInFrame { get; private set; }

    /// <summary>Returns a BGR image of the visible window, or null if no frame is available.</summary>
    protected abstract WindowImage? CaptureWindow();

    /// <summary>Client area only. This is what every detector consumes.</summary>
    public Frame? Grab()
    {
        var raw = CaptureWindow();
        if (raw is null) return null;

        using (raw.Image)
        {
            var crop = CaptureGeometry.ClientRectInFrame(
                raw.ScreenRect, Window.ClientScreenRect, raw.Image.Width, raw.Image.Height);
            LastClientRectInFrame = crop;

            if (crop.IsEmpty)
            {
                Log.Warning("{Backend}: client area does not overlap the captured image (image {W}x{H}, screen {Screen}, client {Client})",
                    Name, raw.Image.Width, raw.Image.Height, raw.ScreenRect, Window.ClientScreenRect);
                return null;
            }

            if (crop != _lastLoggedCrop)
            {
                Log.Debug("{Backend}: client crop inside window image = {Crop}", Name, crop);
                _lastLoggedCrop = crop;
            }

            var client = new Mat(raw.Image, crop.ToCvRect()).Clone();
            return new Frame(client, DateTimeOffset.Now, Name);
        }
    }

    /// <summary>Uncropped window image including title bar/borders (diagnostics only).</summary>
    public Frame? GrabWindow()
    {
        var raw = CaptureWindow();
        return raw is null ? null : new Frame(raw.Image, DateTimeOffset.Now, Name + "/window");
    }

    public virtual void Dispose() => GC.SuppressFinalize(this);
}
