namespace BotPriston.Core.Capture;

/// <summary>
/// Produces frames of the game's client area. Implementations: live window capture
/// (Windows.Graphics.Capture / BitBlt) and saved screenshots for offline work.
/// </summary>
public interface ICaptureSource : IDisposable
{
    string Name { get; }

    /// <summary>
    /// Returns the most recent frame, or null if none could be obtained within the
    /// implementation's timeout. The caller owns (and must dispose) the returned frame.
    /// </summary>
    Frame? Grab();
}
