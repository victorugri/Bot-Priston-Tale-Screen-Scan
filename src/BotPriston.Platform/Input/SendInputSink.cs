using System.Runtime.InteropServices;
using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using BotPriston.Core.Input;
using BotPriston.Platform.Native;
using BotPriston.Platform.Window;
using Serilog;

namespace BotPriston.Platform.Input;

/// <summary>
/// Real input through SendInput. Keys are sent as hardware scan codes because the game
/// (old DirectX) ignores virtual-key events. Every action is refused unless the game window
/// is in the foreground, and mouse targets must lie inside its client area.
/// </summary>
public sealed class SendInputSink(GameWindow window, InputConfig config, ILogger log) : IInputSink
{
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;

    private readonly HashSet<MouseButton> _heldButtons = [];
    private DateTime _nextActionAt = DateTime.MinValue;

    public bool PressKey(KeyChord key)
    {
        if (!Allowed($"press {key}")) return false;

        var modifiers = ModifierKeys(key.Modifiers);
        foreach (var vk in modifiers) SendKey(vk, up: false);
        SendKey(key.VirtualKey, up: false);
        Thread.Sleep(Jittered(config.KeyHoldMs));
        SendKey(key.VirtualKey, up: true);
        foreach (var vk in modifiers.AsEnumerable().Reverse()) SendKey(vk, up: true);

        log.Debug("Pressed {Key}", key.ToString());
        Done();
        return true;
    }

    public bool MoveMouse(PixelPoint clientPoint)
    {
        var client = window.ClientScreenRect;
        if (clientPoint.X < 0 || clientPoint.Y < 0 || clientPoint.X >= client.Width || clientPoint.Y >= client.Height)
        {
            log.Warning("Refused mouse move to {Point}: outside the {W}x{H} client area", clientPoint, client.Width, client.Height);
            return false;
        }
        // Moves don't wait for nor reset the action pacing: the hover sweep moves often and has its own delays.
        if (!Allowed($"move mouse to {clientPoint}", pace: false)) return false;

        int screenX = client.X + clientPoint.X;
        int screenY = client.Y + clientPoint.Y;
        int vx = InputNative.GetSystemMetrics(InputNative.SM_XVIRTUALSCREEN);
        int vy = InputNative.GetSystemMetrics(InputNative.SM_YVIRTUALSCREEN);
        int vw = InputNative.GetSystemMetrics(InputNative.SM_CXVIRTUALSCREEN);
        int vh = InputNative.GetSystemMetrics(InputNative.SM_CYVIRTUALSCREEN);

        // Absolute coordinates are normalized to 0..65535 over the whole virtual desktop.
        var input = MouseInput(
            InputNative.MOUSEEVENTF_MOVE | InputNative.MOUSEEVENTF_ABSOLUTE | InputNative.MOUSEEVENTF_VIRTUALDESK,
            (int)Math.Round((screenX - vx) * 65535.0 / Math.Max(1, vw - 1)),
            (int)Math.Round((screenY - vy) * 65535.0 / Math.Max(1, vh - 1)));
        Send(input);
        Thread.Sleep(Jittered(config.MouseSettleMs));

        log.Verbose("Mouse at client {Point}", clientPoint);
        return true;
    }

    public bool MouseDown(MouseButton button)
    {
        if (!Allowed($"{button} down")) return false;
        Send(MouseInput(button == MouseButton.Left ? InputNative.MOUSEEVENTF_LEFTDOWN : InputNative.MOUSEEVENTF_RIGHTDOWN));
        _heldButtons.Add(button);
        log.Debug("{Button} button down", button.ToString());
        Done();
        return true;
    }

    public bool MouseUp(MouseButton button)
    {
        // Releasing is always allowed: it can only make things safer.
        Send(MouseInput(button == MouseButton.Left ? InputNative.MOUSEEVENTF_LEFTUP : InputNative.MOUSEEVENTF_RIGHTUP));
        _heldButtons.Remove(button);
        log.Debug("{Button} button up", button.ToString());
        Done();
        return true;
    }

    public bool Click(MouseButton button)
    {
        if (!MouseDown(button)) return false;
        Thread.Sleep(Jittered(config.KeyHoldMs));
        return MouseUp(button);
    }

    public void ReleaseAll()
    {
        foreach (var button in _heldButtons.ToList())
            MouseUp(button);
    }

    private bool Allowed(string action, bool pace = true)
    {
        if (!window.Exists || !window.IsForeground)
        {
            log.Debug("Refused '{Action}': game window is not in the foreground", action);
            return false;
        }

        if (pace)
        {
            var wait = _nextActionAt - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) Thread.Sleep(wait);
        }
        return true;
    }

    private void Done() => _nextActionAt = DateTime.UtcNow.AddMilliseconds(Jittered(config.ActionDelayMs));

    private int Jittered(int ms) => ms + (config.JitterMs > 0 ? Random.Shared.Next(config.JitterMs + 1) : 0);

    private static void SendKey(int virtualKey, bool up)
    {
        // MAPVK_VK_TO_VSC_EX returns 0xE0xx for extended keys (arrows, Insert, Home, ...).
        uint scan = InputNative.MapVirtualKeyW((uint)virtualKey, InputNative.MAPVK_VK_TO_VSC_EX);
        if (scan == 0)
            throw new InvalidOperationException($"No scan code for virtual key 0x{virtualKey:X2}");

        uint flags = InputNative.KEYEVENTF_SCANCODE;
        if ((scan & 0xFF00) == 0xE000) flags |= InputNative.KEYEVENTF_EXTENDEDKEY;
        if (up) flags |= InputNative.KEYEVENTF_KEYUP;

        Send(new INPUT
        {
            type = InputNative.INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wScan = (ushort)(scan & 0xFF), dwFlags = flags } },
        });
    }

    private static INPUT MouseInput(uint flags, int dx = 0, int dy = 0) => new()
    {
        type = InputNative.INPUT_MOUSE,
        u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags } },
    };

    private static void Send(INPUT input)
    {
        uint sent = InputNative.SendInput(1, [input], Marshal.SizeOf<INPUT>());
        if (sent != 1)
            throw new InvalidOperationException($"SendInput failed (error {Marshal.GetLastWin32Error()})");
    }

    private static List<int> ModifierKeys(KeyModifiers modifiers)
    {
        var keys = new List<int>();
        if (modifiers.HasFlag(KeyModifiers.Ctrl)) keys.Add(VK_CONTROL);
        if (modifiers.HasFlag(KeyModifiers.Shift)) keys.Add(VK_SHIFT);
        if (modifiers.HasFlag(KeyModifiers.Alt)) keys.Add(VK_MENU);
        return keys;
    }
}
