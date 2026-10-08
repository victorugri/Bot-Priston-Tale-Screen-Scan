using BotPriston.Core.Geometry;
using BotPriston.Platform.Native;
using BotPriston.Platform.Window;
using OpenCvSharp;
using Serilog;

namespace BotPriston.Platform.Capture;

/// <summary>
/// Fallback backend: copies the window's screen area from the desktop DC. Works with DirectX
/// windows, but whatever is on top of the game (other windows, tooltips) is captured too, so the
/// game must be visible and unobstructed — which it is whenever the bot is allowed to act.
/// </summary>
public sealed class BitBltCaptureSource(GameWindow window, ILogger log) : WindowCaptureSource(window, log)
{
    public override string Name => "BitBlt";

    protected override unsafe WindowImage? CaptureWindow()
    {
        if (!Window.Exists || Window.IsMinimized)
            return null;

        PixelRect rect = Window.VisibleFrameRect;
        if (rect.IsEmpty)
            return null;

        IntPtr screenDc = User32.GetDC(IntPtr.Zero);
        IntPtr memDc = Gdi32.CreateCompatibleDC(screenDc);
        IntPtr bitmap = Gdi32.CreateCompatibleBitmap(screenDc, rect.Width, rect.Height);
        try
        {
            IntPtr previous = Gdi32.SelectObject(memDc, bitmap);
            bool ok = Gdi32.BitBlt(memDc, 0, 0, rect.Width, rect.Height, screenDc, rect.X, rect.Y,
                Gdi32.SRCCOPY | Gdi32.CAPTUREBLT);
            Gdi32.SelectObject(memDc, previous);
            if (!ok)
            {
                Log.Warning("BitBlt failed for {Rect}", rect);
                return null;
            }

            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = rect.Width,
                biHeight = -rect.Height, // negative = top-down rows
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Gdi32.BI_RGB,
            };

            using var bgra = new Mat(rect.Height, rect.Width, MatType.CV_8UC4);
            int lines = Gdi32.GetDIBits(memDc, bitmap, 0, (uint)rect.Height, bgra.DataPointer, ref header, Gdi32.DIB_RGB_COLORS);
            if (lines != rect.Height)
            {
                Log.Warning("GetDIBits returned {Lines} of {Height} lines", lines, rect.Height);
                return null;
            }

            var bgr = new Mat();
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
            return new WindowImage(bgr, rect);
        }
        finally
        {
            Gdi32.DeleteObject(bitmap);
            Gdi32.DeleteDC(memDc);
            User32.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
