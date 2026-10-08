namespace BotPriston.Core.Input;

/// <summary>Name → Windows virtual-key code table for keys usable in config.</summary>
public static class VirtualKeys
{
    private static readonly Dictionary<string, int> ByName = Build();

    public static bool TryGet(string name, out int vk) => ByName.TryGetValue(name, out vk);

    private static Dictionary<string, int> Build()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backspace"] = 0x08, ["Tab"] = 0x09, ["Enter"] = 0x0D, ["Pause"] = 0x13,
            ["CapsLock"] = 0x14, ["Escape"] = 0x1B, ["Esc"] = 0x1B, ["Space"] = 0x20,
            ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["End"] = 0x23, ["Home"] = 0x24,
            ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
            ["Insert"] = 0x2D, ["Delete"] = 0x2E,
            ["Multiply"] = 0x6A, ["Add"] = 0x6B, ["Subtract"] = 0x6D, ["Decimal"] = 0x6E, ["Divide"] = 0x6F,
            ["ScrollLock"] = 0x91,
            ["Semicolon"] = 0xBA, ["Equals"] = 0xBB, ["Comma"] = 0xBC, ["Minus"] = 0xBD,
            ["Period"] = 0xBE, ["Slash"] = 0xBF, ["Backquote"] = 0xC0,
            ["LeftBracket"] = 0xDB, ["Backslash"] = 0xDC, ["RightBracket"] = 0xDD, ["Quote"] = 0xDE,
        };

        for (int i = 0; i <= 9; i++)
        {
            map[i.ToString()] = 0x30 + i;
            map[$"D{i}"] = 0x30 + i;
            map[$"NumPad{i}"] = 0x60 + i;
        }

        for (char c = 'A'; c <= 'Z'; c++)
            map[c.ToString()] = c;

        for (int i = 1; i <= 24; i++)
            map[$"F{i}"] = 0x70 + i - 1;

        return map;
    }
}
