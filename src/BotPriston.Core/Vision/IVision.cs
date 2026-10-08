using OpenCvSharp;

namespace BotPriston.Core.Vision;

/// <summary>Frame → everything perceived. Implemented by <see cref="VisionPipeline"/>; faked in tests.</summary>
public interface IVision
{
    VisionSnapshot Analyze(Mat frame);
}
