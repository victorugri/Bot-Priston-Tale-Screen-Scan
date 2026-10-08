using BotPriston.Core.Config;
using OpenCvSharp;

namespace BotPriston.Core.Vision;

/// <summary>Fill level of one bar.</summary>
/// <param name="Percent">0..100.</param>
/// <param name="FilledRows">Rows counted as liquid, from the bottom.</param>
/// <param name="TotalRows">Height of the tube.</param>
/// <param name="TopY">Client-area y of the liquid surface (bottom of the ROI when empty).</param>
public readonly record struct BarReading(double Percent, int FilledRows, int TotalRows, int TopY)
{
    public override string ToString() => $"{Percent:F1}% ({FilledRows}/{TotalRows})";
}

public static class BarReader
{
    /// <summary>
    /// Measures a bottom-up bar: walks rows from the bottom of the ROI while they are filled
    /// (allowing small gaps). Anything above the liquid surface — including same-colored
    /// scenery — is ignored because the scan stops at the first real gap.
    /// </summary>
    public static BarReading Read(Mat frame, BarConfig bar)
    {
        var roi = bar.Roi;
        int rows = roi.Height;
        if (roi.IsEmpty || roi.X < 0 || roi.Y < 0 || roi.Right > frame.Width || roi.Bottom > frame.Height)
            return new BarReading(0, 0, Math.Max(rows, 0), roi.Bottom);

        using var region = new Mat(frame, roi.ToCvRect());
        using var hsv = new Mat();
        Cv2.CvtColor(region, hsv, ColorConversionCodes.BGR2HSV);
        using var mask = HsvRange.Mask(hsv, bar.Colors);
        using var rowSums = new Mat();
        Cv2.Reduce(mask, rowSums, ReduceDimension.Column, ReduceTypes.Avg, MatType.CV_64F);

        int filled = 0;
        int gap = 0;
        for (int r = rows - 1; r >= 0; r--)
        {
            double coverage = rowSums.At<double>(r, 0) / 255.0;
            if (coverage >= bar.MinRowCoverage)
            {
                filled = rows - r;
                gap = 0;
            }
            else if (filled == 0 && rows - r > bar.MaxGapRows + 1)
            {
                break; // bottom is empty: the bar is at 0%
            }
            else if (filled > 0 && ++gap > bar.MaxGapRows)
            {
                break;
            }
        }

        return new BarReading(100.0 * filled / rows, filled, rows, roi.Bottom - filled);
    }
}
