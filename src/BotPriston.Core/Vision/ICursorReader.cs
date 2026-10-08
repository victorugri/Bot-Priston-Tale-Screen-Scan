using BotPriston.Core.Geometry;
using OpenCvSharp;

namespace BotPriston.Core.Vision;

/// <summary>What the game's cursor says about the spot under the mouse. Implemented by <see cref="CursorDetector"/>.</summary>
public interface ICursorReader
{
    CursorReading Detect(Mat frame, PixelPoint mouse);
}
