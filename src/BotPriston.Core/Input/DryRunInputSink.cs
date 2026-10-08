using BotPriston.Core.Geometry;
using Serilog;

namespace BotPriston.Core.Input;

/// <summary>Logs what would be sent and sends nothing. Always "succeeds" so decisions flow as in a real run.</summary>
public sealed class DryRunInputSink(ILogger log) : IInputSink
{
    public bool PressKey(KeyChord key)
    {
        log.Information("[dry-run] press {Key}", key);
        return true;
    }

    public bool MoveMouse(PixelPoint clientPoint)
    {
        log.Debug("[dry-run] move mouse to {Point}", clientPoint);
        return true;
    }

    public bool MouseDown(MouseButton button)
    {
        log.Information("[dry-run] {Button} button down", button);
        return true;
    }

    public bool MouseUp(MouseButton button)
    {
        log.Information("[dry-run] {Button} button up", button);
        return true;
    }

    public bool Click(MouseButton button)
    {
        log.Information("[dry-run] {Button} click", button.ToString());
        return true;
    }

    public void ReleaseAll() { }

    public void ForceReleaseButtons() => log.Debug("[dry-run] force release of both mouse buttons");
}
