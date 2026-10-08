using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using OpenCvSharp;

namespace BotPriston.Core.Vision;

/// <param name="ShiftX">Median ground shift since the previous frame, in pixels.</param>
/// <param name="Moving">The ground moved this frame (the camera follows the character, so it walked).</param>
/// <param name="Walking">It moved for several frames in a row: a real walk, not a hit shake.</param>
public readonly record struct MotionReading(double ShiftX, double ShiftY, bool Moving, bool Walking, int Agreeing)
{
    public double Magnitude => Math.Sqrt(ShiftX * ShiftX + ShiftY * ShiftY);
    public override string ToString() => $"shift ({ShiftX:F1},{ShiftY:F1}) px, {Agreeing} boxes agree{(Walking ? ", WALKING" : "")}";
}

/// <summary>
/// Detects the character walking: the camera follows it, so the whole ground slides between frames.
/// Phase correlation on a few ground boxes away from the character and the HUD; the median shift is
/// trusted only when most boxes agree (a monster moving through one box doesn't count). A single
/// shaky frame (hit effects) is not a walk: it takes <see cref="MotionConfig.ConsecutiveFrames"/>.
/// Stateful: feed it every frame in order.
/// </summary>
public sealed class MotionDetector(MotionConfig config) : IDisposable
{
    private Mat[]? _previous;
    private int _movingStreak;
    private readonly Dictionary<int, Mat> _windows = [];

    /// <summary>Hanning window per box: tapers the edges so the image borders don't dominate the correlation.</summary>
    private Mat Window(int box, Size size)
    {
        if (_windows.TryGetValue(box, out var window) && window.Size() == size)
            return window;
        window?.Dispose();
        window = new Mat();
        Cv2.CreateHanningWindow(window, size, MatType.CV_32FC1);
        _windows[box] = window;
        return window;
    }

    public MotionConfig Config { get; } = config;

    public MotionReading Update(Mat frame)
    {
        var current = Config.Boxes.Select(b => Prepare(frame, b)).ToArray();
        if (_previous is null || current.Any(m => m is null) || _previous.Any(m => m is null))
        {
            Replace(current);
            return default;
        }

        var shifts = new List<Point2d>();
        for (int i = 0; i < current.Length; i++)
            shifts.Add(Cv2.PhaseCorrelate(_previous[i], current[i]!, Window(i, current[i]!.Size()), out _));
        Replace(current);

        double scale = 1.0 / Config.Downscale;
        double mx = Median(shifts.Select(s => s.X)) * scale;
        double my = Median(shifts.Select(s => s.Y)) * scale;
        int agreeing = shifts.Count(s => Math.Abs(s.X * scale - mx) <= Config.AgreementPx && Math.Abs(s.Y * scale - my) <= Config.AgreementPx);

        bool moving = Math.Sqrt(mx * mx + my * my) >= Config.MinShiftPx && agreeing >= Config.MinAgreeingBoxes;
        _movingStreak = moving ? _movingStreak + 1 : 0;
        return new MotionReading(mx, my, moving, _movingStreak >= Config.ConsecutiveFrames, agreeing);
    }

    /// <summary>Forget history (e.g. after a pause, when frames are not consecutive).</summary>
    public void Reset()
    {
        Replace(null);
        _movingStreak = 0;
    }

    private Mat? Prepare(Mat frame, PixelRect box)
    {
        if (box.IsEmpty || box.X < 0 || box.Y < 0 || box.Right > frame.Width || box.Bottom > frame.Height)
            return null;
        using var region = new Mat(frame, box.ToCvRect());
        using var gray = new Mat();
        Cv2.CvtColor(region, gray, ColorConversionCodes.BGR2GRAY);
        using var small = new Mat();
        Cv2.Resize(gray, small, new Size(), Config.Downscale, Config.Downscale, InterpolationFlags.Area);
        var result = new Mat();
        small.ConvertTo(result, MatType.CV_32FC1);
        return result;
    }

    private void Replace(Mat?[]? next)
    {
        if (_previous is not null)
            foreach (var m in _previous) m?.Dispose();
        _previous = next!;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        int n = sorted.Length;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
    }

    public void Dispose()
    {
        Replace(null);
        foreach (var window in _windows.Values) window.Dispose();
        _windows.Clear();
    }
}
