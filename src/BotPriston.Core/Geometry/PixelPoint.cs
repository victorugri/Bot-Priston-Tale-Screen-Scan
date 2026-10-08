namespace BotPriston.Core.Geometry;

/// <summary>Integer point in pixels (client-area coordinates unless stated otherwise).</summary>
public readonly record struct PixelPoint(int X, int Y)
{
    public override string ToString() => $"({X},{Y})";
}
