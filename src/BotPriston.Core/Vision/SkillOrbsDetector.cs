using BotPriston.Core.Config;
using OpenCvSharp;

namespace BotPriston.Core.Vision;

/// <param name="ColoredFraction">Fraction (0..1) of the icon's pixels that are colored.</param>
public readonly record struct SkillOrb(bool Ready, double ColoredFraction)
{
    public override string ToString() => Ready ? "ready" : "cooling down";
}

public sealed record SkillOrbs(SkillOrb Left, SkillOrb Right)
{
    public override string ToString() => $"left {Left}, right {Right}";
}

/// <summary>
/// The two round skill icons between the HP and MP tubes (left = left-click skill, right =
/// right-click skill). An icon is in color when the skill can be used and gray while it recharges.
/// </summary>
public sealed class SkillOrbsDetector(SkillOrbsConfig config) : IDetector<SkillOrbs>
{
    public SkillOrbsConfig Config { get; } = config;

    public SkillOrbs Detect(Mat frame) => new(Read(frame, Config.Left), Read(frame, Config.Right));

    public static SkillOrb Read(Mat frame, SkillOrbConfig orb)
    {
        int r = orb.Radius;
        var box = new Rect(orb.Center.X - r, orb.Center.Y - r, 2 * r + 1, 2 * r + 1);
        if (r <= 0 || box.X < 0 || box.Y < 0 || box.Right > frame.Width || box.Bottom > frame.Height)
            return new SkillOrb(false, 0);

        using var region = new Mat(frame, box);
        using var hsv = new Mat();
        Cv2.CvtColor(region, hsv, ColorConversionCodes.BGR2HSV);
        using var circle = new Mat(box.Size, MatType.CV_8UC1, Scalar.All(0));
        Cv2.Circle(circle, new Point(r, r), r, Scalar.All(255), -1);

        using var colored = new Mat();
        Cv2.InRange(hsv, new Scalar(0, orb.MinSaturation, 0), new Scalar(179, 255, 255), colored);
        Cv2.BitwiseAnd(colored, circle, colored);

        double fraction = (double)Cv2.CountNonZero(colored) / Cv2.CountNonZero(circle);
        return new SkillOrb(fraction >= orb.MinColoredFraction, fraction);
    }
}
