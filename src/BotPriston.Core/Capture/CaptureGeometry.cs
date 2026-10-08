using BotPriston.Core.Geometry;

namespace BotPriston.Core.Capture;

public static class CaptureGeometry
{
    /// <summary>
    /// Maps the client area into the coordinate space of a captured image.
    /// </summary>
    /// <param name="capturedScreenRect">Screen rect the captured image covers (e.g. the window's visible frame bounds).</param>
    /// <param name="clientScreenRect">Client area in screen coordinates.</param>
    /// <param name="frameWidth">Actual width of the captured image.</param>
    /// <param name="frameHeight">Actual height of the captured image.</param>
    /// <returns>The client rect inside the image, clipped to the image bounds (empty if they don't overlap).</returns>
    public static PixelRect ClientRectInFrame(
        PixelRect capturedScreenRect, PixelRect clientScreenRect, int frameWidth, int frameHeight)
    {
        var inFrame = clientScreenRect.Offset(-capturedScreenRect.X, -capturedScreenRect.Y);
        return inFrame.Intersect(new PixelRect(0, 0, frameWidth, frameHeight));
    }
}
