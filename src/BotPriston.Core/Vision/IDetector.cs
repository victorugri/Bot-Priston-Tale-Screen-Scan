using OpenCvSharp;

namespace BotPriston.Core.Vision;

/// <summary>
/// One detector per concern. Input is a BGR client-area frame; output is a typed result.
/// Detectors are stateless with respect to frames so they can be tested on saved screenshots.
/// </summary>
public interface IDetector<out TResult>
{
    TResult Detect(Mat frame);
}
