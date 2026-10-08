using BotPriston.Core.Geometry;

namespace BotPriston.Core.Input;

public enum MouseButton
{
    Left,
    Right,
}

/// <summary>
/// Where the bot's actions go. Coordinates are client-area pixels. Every method returns false
/// when the action was refused (e.g. the game is not in focus) — callers must not assume it happened.
/// </summary>
public interface IInputSink
{
    bool PressKey(KeyChord key);
    bool MoveMouse(PixelPoint clientPoint);
    bool MouseDown(MouseButton button);
    bool MouseUp(MouseButton button);

    /// <summary>Press and release (held long enough for the game to register it).</summary>
    bool Click(MouseButton button);

    /// <summary>Releases any key or button this sink is holding. Safe to call at any time.</summary>
    void ReleaseAll();

    /// <summary>
    /// Sends "button up" for both mouse buttons even if this sink believes they are up, so the game
    /// can't be left thinking a button is still held (e.g. an up that arrived while it had no focus).
    /// Only sent while the game is in the foreground.
    /// </summary>
    void ForceReleaseButtons();
}
