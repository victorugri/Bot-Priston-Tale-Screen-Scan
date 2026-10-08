using OpenCvSharp;

namespace BotPriston.Core.Vision;

/// <summary>
/// Inclusive HSV range in OpenCV units: H 0-179, S 0-255, V 0-255.
/// If HMin &gt; HMax the hue range wraps around 180 (e.g. reds: HMin 170, HMax 10).
/// </summary>
public sealed record HsvRange
{
    public int HMin { get; init; }
    public int HMax { get; init; } = 179;
    public int SMin { get; init; }
    public int SMax { get; init; } = 255;
    public int VMin { get; init; }
    public int VMax { get; init; } = 255;

    public bool Wraps => HMin > HMax;

    /// <summary>Adds (bitwise OR) the pixels of <paramref name="hsv"/> inside this range to <paramref name="mask"/>.</summary>
    public void AddToMask(Mat hsv, Mat mask)
    {
        if (Wraps)
        {
            Accumulate(hsv, mask, HMin, 179);
            Accumulate(hsv, mask, 0, HMax);
        }
        else
        {
            Accumulate(hsv, mask, HMin, HMax);
        }
    }

    /// <summary>Binary mask (0/255) of the pixels matching any of the ranges.</summary>
    public static Mat Mask(Mat hsv, IEnumerable<HsvRange> ranges)
    {
        var mask = new Mat(hsv.Size(), MatType.CV_8UC1, Scalar.All(0));
        foreach (var range in ranges)
            range.AddToMask(hsv, mask);
        return mask;
    }

    private void Accumulate(Mat hsv, Mat mask, int hMin, int hMax)
    {
        using var part = new Mat();
        Cv2.InRange(hsv, new Scalar(hMin, SMin, VMin), new Scalar(hMax, SMax, VMax), part);
        Cv2.BitwiseOr(mask, part, mask);
    }

    public override string ToString() => $"H{HMin}-{HMax} S{SMin}-{SMax} V{VMin}-{VMax}";
}
