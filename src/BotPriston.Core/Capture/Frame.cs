using OpenCvSharp;

namespace BotPriston.Core.Capture;

/// <summary>
/// One captured image of the game's client area. <see cref="Image"/> is always BGR (CV_8UC3)
/// and pixel (0,0) is the top-left of the client area. The frame owns the Mat.
/// </summary>
public sealed class Frame(Mat image, DateTimeOffset capturedAt, string source) : IDisposable
{
    public Mat Image { get; } = image;
    public DateTimeOffset CapturedAt { get; } = capturedAt;

    /// <summary>Human-readable origin (backend name or file path), for logs.</summary>
    public string Source { get; } = source;

    public int Width => Image.Width;
    public int Height => Image.Height;

    public void Dispose() => Image.Dispose();
}
