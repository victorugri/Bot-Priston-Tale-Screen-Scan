using BotPriston.Core.Bot;
using BotPriston.Core.Geometry;
using BotPriston.Platform.Native;

namespace BotPriston.Platform.Window;

/// <summary>
/// A top-level window plus live geometry queries. All rects are in physical screen pixels
/// (the app manifest declares per-monitor DPI awareness).
/// </summary>
public sealed class GameWindow(IntPtr handle, int processId, string processName, string title) : IGameWindowState
{
    public IntPtr Handle { get; } = handle;
    public int ProcessId { get; } = processId;
    public string ProcessName { get; } = processName;
    public string Title { get; } = title;

    public bool Exists => User32.IsWindow(Handle);
    public bool IsMinimized => User32.IsIconic(Handle);
    public bool IsForeground => User32.GetForegroundWindow() == Handle;
    public uint Dpi => User32.GetDpiForWindow(Handle);

    /// <summary>Mouse position in client coordinates, or null if it is outside the client area.</summary>
    public PixelPoint? CursorClientPosition
    {
        get
        {
            if (!InputNative.GetCursorPos(out var p)) return null;
            var client = ClientScreenRect;
            int x = p.X - client.X, y = p.Y - client.Y;
            return x >= 0 && y >= 0 && x < client.Width && y < client.Height ? new PixelPoint(x, y) : null;
        }
    }

    public (int Width, int Height) ClientSize
    {
        get
        {
            var client = ClientScreenRect;
            return (client.Width, client.Height);
        }
    }

    /// <summary>Client area in screen coordinates (origin of every coordinate the bot uses).</summary>
    public PixelRect ClientScreenRect
    {
        get
        {
            if (!User32.GetClientRect(Handle, out var client))
                return default;
            var origin = new POINT();
            if (!User32.ClientToScreen(Handle, ref origin))
                return default;
            return new PixelRect(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
        }
    }

    /// <summary>GetWindowRect: includes the invisible resize borders on Windows 10/11.</summary>
    public PixelRect WindowRect =>
        User32.GetWindowRect(Handle, out var r) ? PixelRect.FromEdges(r.Left, r.Top, r.Right, r.Bottom) : default;

    /// <summary>Visible window bounds (what Windows.Graphics.Capture captures for a window).</summary>
    public PixelRect VisibleFrameRect
    {
        get
        {
            unsafe
            {
                int hr = Dwm.DwmGetWindowAttribute(Handle, Dwm.DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, sizeof(RECT));
                return hr >= 0 ? PixelRect.FromEdges(r.Left, r.Top, r.Right, r.Bottom) : WindowRect;
            }
        }
    }

    public override string ToString() => $"'{Title}' [{ProcessName}.exe pid={ProcessId} hwnd=0x{Handle:X}]";
}
