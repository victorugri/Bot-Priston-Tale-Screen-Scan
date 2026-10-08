using System.Text.Json.Serialization;
using OpenCvSharp;

namespace BotPriston.Core.Geometry;

/// <summary>
/// Integer rectangle in pixels. Used for screen rects, client rects and ROIs in config.
/// Unless stated otherwise, ROIs are relative to the game's client area.
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    [JsonIgnore] public int Right => X + Width;
    [JsonIgnore] public int Bottom => Y + Height;
    [JsonIgnore] public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect FromEdges(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);

    public PixelRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    public PixelRect Intersect(PixelRect other)
    {
        int left = Math.Max(X, other.X);
        int top = Math.Max(Y, other.Y);
        int right = Math.Min(Right, other.Right);
        int bottom = Math.Min(Bottom, other.Bottom);
        return right > left && bottom > top ? FromEdges(left, top, right, bottom) : default;
    }

    public bool Contains(PixelRect other) =>
        other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;

    public Rect ToCvRect() => new(X, Y, Width, Height);

    public override string ToString() => $"({X},{Y} {Width}x{Height})";
}
