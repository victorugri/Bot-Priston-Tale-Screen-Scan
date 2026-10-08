using System.Runtime.InteropServices;
using BotPriston.Core.Geometry;
using BotPriston.Platform.Native;

namespace BotPriston.Platform.Window;

public enum WindowAnchor
{
    TopRight,
    TopLeft,
}

/// <summary>
/// Puts the game window back to the client size every ROI assumes, aligned to a corner of the
/// monitor's work area (so a terminal fits next to it). Requires the bot to have the same
/// privileges as the game (administrator), or Windows ignores the request.
/// </summary>
public static class WindowLayout
{
    public static (bool Ok, string Message) Apply(GameWindow window, int clientWidth, int clientHeight, WindowAnchor anchor)
    {
        var hwnd = window.Handle;
        if (WindowLayoutNative.IsZoomed(hwnd) || window.IsMinimized)
            WindowLayoutNative.ShowWindow(hwnd, WindowLayoutNative.SW_RESTORE);

        // Window size for the wanted client size (includes title bar and the invisible resize borders).
        var rect = new RECT { Left = 0, Top = 0, Right = clientWidth, Bottom = clientHeight };
        uint style = (uint)User32.GetWindowLongPtr(hwnd, WindowLayoutNative.GWL_STYLE).ToInt64();
        uint exStyle = (uint)User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE).ToInt64();
        WindowLayoutNative.AdjustWindowRectExForDpi(ref rect, style, false, exStyle, window.Dpi);
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;

        var work = WorkArea(hwnd);
        if (!SetPos(hwnd, work.X, work.Y, width, height))
            return (false, $"SetWindowPos failed (error {Marshal.GetLastWin32Error()}). Is the bot running as administrator like the game?");

        // Now that the real frame is known, shift so the *visible* frame touches the chosen corner.
        var visible = window.VisibleFrameRect;
        var windowRect = window.WindowRect;
        int targetLeft = anchor == WindowAnchor.TopRight ? work.Right - visible.Width : work.X;
        int x = windowRect.X + (targetLeft - visible.X);
        int y = windowRect.Y + (work.Y - visible.Y);
        SetPos(hwnd, x, y, width, height);

        var client = window.ClientScreenRect;
        bool ok = client.Width == clientWidth && client.Height == clientHeight;
        return (ok, ok
            ? $"Client area is now {client.Width}x{client.Height} at screen ({client.X},{client.Y}); free space on the {(anchor == WindowAnchor.TopRight ? "left" : "right")}: {work.Width - window.VisibleFrameRect.Width}px"
            : $"Client area is {client.Width}x{client.Height} after resizing (wanted {clientWidth}x{clientHeight}). The monitor may be too small, or the game may refuse the size.");
    }

    private static bool SetPos(IntPtr hwnd, int x, int y, int width, int height) =>
        WindowLayoutNative.SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height,
            WindowLayoutNative.SWP_NOZORDER | WindowLayoutNative.SWP_NOACTIVATE);

    private static PixelRect WorkArea(IntPtr hwnd)
    {
        var monitor = WindowLayoutNative.MonitorFromWindow(hwnd, WindowLayoutNative.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        WindowLayoutNative.GetMonitorInfoW(monitor, ref info);
        return PixelRect.FromEdges(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
    }
}
