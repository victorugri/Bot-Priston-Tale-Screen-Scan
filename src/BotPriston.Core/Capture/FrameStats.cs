using OpenCvSharp;

namespace BotPriston.Core.Capture;

/// <summary>Cheap sanity checks on captured frames (e.g. detecting black frames from a failing backend).</summary>
public static class FrameStats
{
    /// <summary>
    /// True if almost every pixel is near-black. DirectX windows captured with an unsuitable
    /// backend typically come back fully black.
    /// </summary>
    public static bool LooksBlack(Mat image, double maxMeanIntensity = 4.0)
    {
        if (image.Empty()) return true;
        var mean = Cv2.Mean(image);
        return (mean.Val0 + mean.Val1 + mean.Val2) / 3.0 <= maxMeanIntensity;
    }
}
