namespace BotPriston.Core.Input;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
}

/// <summary>
/// A key plus optional modifiers, parsed from config strings like "F12", "Ctrl+F12", "Shift+Alt+Q".
/// The key is stored as a Windows virtual-key code; the platform layer maps it to a scan code.
/// </summary>
public readonly record struct KeyChord(KeyModifiers Modifiers, int VirtualKey, string KeyName)
{
    public static KeyChord Parse(string text) =>
        TryParse(text, out var chord, out var error) ? chord : throw new FormatException(error);

    public static bool TryParse(string? text, out KeyChord chord, out string error)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Key is empty.";
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var modifiers = KeyModifiers.None;
        foreach (var mod in parts[..^1])
        {
            switch (mod.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= KeyModifiers.Ctrl; break;
                case "shift": modifiers |= KeyModifiers.Shift; break;
                case "alt": modifiers |= KeyModifiers.Alt; break;
                default:
                    error = $"Unknown modifier '{mod}' in '{text}'. Use Ctrl, Shift or Alt.";
                    return false;
            }
        }

        var keyName = parts[^1];
        if (!VirtualKeys.TryGet(keyName, out var vk))
        {
            error = $"Unknown key '{keyName}' in '{text}'.";
            return false;
        }

        chord = new KeyChord(modifiers, vk, keyName);
        error = "";
        return true;
    }

    public override string ToString() =>
        Modifiers == KeyModifiers.None ? KeyName : $"{Modifiers.ToString().Replace(", ", "+")}+{KeyName}";
}
