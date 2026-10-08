using BotPriston.Core.Bot;
using BotPriston.Core.Vision;
using BotPriston.Platform.Window;
using OpenCvSharp;

namespace BotPriston.Hosting;

/// <summary>
/// Saves every frame the bot acts on (JPEG) with its state drawn on top: runner/brain state, the
/// aimed point, which buttons are held and where the real mouse is. File names carry the time so
/// frames line up with the log.
/// </summary>
public sealed class SessionRecorder(string directory, BotRunner runner, GameWindow window)
{
    private static readonly int[] JpegParams = [(int)ImwriteFlags.JpegQuality, 85];
    private int _frames;

    public string Directory { get; } = directory;
    public int Frames => _frames;

    public void Record(Mat frame, VisionSnapshot snapshot)
    {
        using var canvas = frame.Clone();
        var brain = runner.Brain;

        if (brain?.Aim is { } aim)
        {
            var color = brain.HoldingLeft || brain.HoldingRight ? new Scalar(0, 0, 255) : new Scalar(0, 255, 255);
            Cv2.Circle(canvas, new Point(aim.X, aim.Y), 14, color, 2, LineTypes.AntiAlias);
        }
        if (window.CursorClientPosition is { } mouse)
        {
            Cv2.Line(canvas, new Point(mouse.X - 8, mouse.Y), new Point(mouse.X + 8, mouse.Y), new Scalar(255, 0, 255), 1);
            Cv2.Line(canvas, new Point(mouse.X, mouse.Y - 8), new Point(mouse.X, mouse.Y + 8), new Scalar(255, 0, 255), 1);
        }

        var now = DateTime.Now;
        string buttons = brain is null ? "-" : $"{(brain.HoldingLeft ? "L" : "")}{(brain.HoldingRight ? "R" : "")}";
        var lines = new[]
        {
            $"{now:HH:mm:ss.fff}  runner {runner.State}  brain {brain?.State}  held [{buttons}]",
            $"aim {brain?.Aim}  target {snapshot.Target}  skills {snapshot.Skills}",
            snapshot.Bars is { } b ? $"HP {b.Hp.Percent:F0}%  MP {b.Mp.Percent:F0}%  STM {b.Stm.Percent:F0}%" : "HUD not visible",
        };
        using (var panel = new Mat(canvas, new Rect(0, 0, 900, 70)))
        using (var dark = new Mat(panel.Size(), panel.Type(), Scalar.All(0)))
            Cv2.AddWeighted(panel, 0.4, dark, 0.6, 0, panel);
        for (int i = 0; i < lines.Length; i++)
            Cv2.PutText(canvas, lines[i], new Point(8, 20 + i * 20), HersheyFonts.HersheySimplex, 0.5, Scalar.All(255), 1, LineTypes.AntiAlias);

        var name = $"{now:HHmmss_fff}_{brain?.State.ToString() ?? runner.State.ToString()}{(buttons is "-" or "" ? "" : "_" + buttons)}.jpg";
        Cv2.ImWrite(Path.Combine(Directory, name), canvas, JpegParams);
        _frames++;
    }
}
