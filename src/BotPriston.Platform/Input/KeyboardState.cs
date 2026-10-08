using BotPriston.Core.Input;
using BotPriston.Platform.Native;

namespace BotPriston.Platform.Input;

/// <summary>
/// Reads the global (physical) keyboard state. Works regardless of which window has focus,
/// so it is used for the bot's own hotkeys.
/// </summary>
public static class KeyboardState
{
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;

    public static bool IsDown(int virtualKey) => (User32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    /// <summary>True if the chord's key and exactly its modifiers are held.</summary>
    public static bool IsDown(KeyChord chord) =>
        IsDown(chord.VirtualKey)
        && IsDown(VK_CONTROL) == chord.Modifiers.HasFlag(KeyModifiers.Ctrl)
        && IsDown(VK_SHIFT) == chord.Modifiers.HasFlag(KeyModifiers.Shift)
        && IsDown(VK_MENU) == chord.Modifiers.HasFlag(KeyModifiers.Alt);
}

/// <summary>Edge detector: reports a chord once per press, not while it is held.</summary>
public sealed class KeyPressDetector(KeyChord chord)
{
    private bool _wasDown = KeyboardState.IsDown(chord);

    public KeyChord Chord { get; } = chord;

    public bool Pressed()
    {
        bool down = KeyboardState.IsDown(Chord);
        bool pressed = down && !_wasDown;
        _wasDown = down;
        return pressed;
    }
}
