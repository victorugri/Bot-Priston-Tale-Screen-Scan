using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using OpenCvSharp;

namespace BotPriston.Core.Vision;

/// <summary>
/// The stack size printed on an item cell. <see cref="HasDigits"/> without a <see cref="Value"/> means
/// something white is there but it couldn't be read (e.g. the cursor is over it).
/// </summary>
public readonly record struct ItemCount(bool HasDigits, int? Value)
{
    public static readonly ItemCount None = new(false, null);

    public override string ToString() => Value?.ToString() ?? (HasDigits ? "?" : "-");
}

/// <summary>
/// Reads the white stack size in the top-left corner of a hotbar or inventory cell. The game's digit
/// font is a fixed bitmap (8 px tall, proportional), so each digit is matched against its exact shape.
/// The potion icon shines through below the digits' top rows, so only the top rows count white
/// pixels that don't belong to the digit against it.
/// </summary>
public sealed class ItemCountReader
{
    private const int GlyphHeight = 8;
    private const int StrictRows = 5;  // rows where stray white pixels count against a digit
    private const int SearchWidth = 18;

    // Cut from the game's screenshots (samples/*_inv.png and older ones). '#' = white pixel.
    private static readonly (int Digit, bool[,] Shape)[] Glyphs =
    [
        (0, Shape(".###. #...# #...# #...# #...# #...# #...# .###.")),
        (1, Shape(".#. ##. .#. .#. .#. .#. .#. ###")),
        (2, Shape(".###. #...# ....# ...#. ..#.. .#... #.... #####")),
        (3, Shape(".###. #...# ....# .###. ....# ....# #...# .###.")),
        (4, Shape("...#. ..##. .#.#. .#.#. #..#. ##### ...#. ...#.")),
        (5, Shape("##### #.... ####. #...# ....# ....# #...# .###.")),
        (6, Shape("...#. ..#.. .#... ####. #...# #...# #...# .###.")),
        (7, Shape("###### ....#. ...#.. ...#.. ..#... ..#... ..#... .#....")),
        (8, Shape(".###. #...# #...# .###. #...# #...# #...# .###.")),
        (9, Shape(".###. #...# #...# #...# .#### ....# ...#. .##..")),
    ];

    private readonly ItemCountConfig _config;

    public ItemCountReader(ItemCountConfig config) => _config = config;

    public ItemCount Read(Mat frame, PixelRect cell)
    {
        var band = new PixelRect(cell.X, cell.Y + _config.DigitsTop, SearchWidth, GlyphHeight);
        if (!new PixelRect(0, 0, frame.Width, frame.Height).Contains(band))
            return ItemCount.None;

        var white = WhiteMask(frame, band);

        // Every digit has white pixels in its top rows; nothing there = no number on this cell.
        bool any = false;
        for (int y = 0; y < 3 && !any; y++)
            for (int x = 0; x < SearchWidth && !any; x++)
                any = white[y, x];
        if (!any) return ItemCount.None;

        // Left to right: the first digit starts 1-3 px into the cell, the next one 0-2 px after the previous.
        int value = 0, digits = 0, from = 1, to = 3;
        while (from < SearchWidth)
        {
            var best = (Score: double.MinValue, Digit: -1, X: 0, Width: 0);
            foreach (var (digit, shape) in Glyphs)
            {
                for (int x = from; x <= to; x++)
                {
                    double score = Score(white, shape, x);
                    if (score > best.Score) best = (score, digit, x, shape.GetLength(1));
                }
            }
            if (best.Score < _config.MinGlyphScore) break;

            value = value * 10 + best.Digit;
            digits++;
            from = best.X + best.Width;
            to = from + 2;
        }

        return digits == 0 ? new ItemCount(true, null) : new ItemCount(true, value);
    }

    /// <summary>Fraction of the digit's pixels that are white, minus stray white pixels in its top rows.</summary>
    private static double Score(bool[,] white, bool[,] shape, int left)
    {
        int h = shape.GetLength(0), w = shape.GetLength(1);
        if (left + w > white.GetLength(1)) return double.MinValue;

        int on = 0, hits = 0, stray = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool px = white[y, left + x];
                if (shape[y, x])
                {
                    on++;
                    if (px) hits++;
                }
                else if (px && y < StrictRows)
                {
                    stray++;
                }
            }
        }
        return (hits - stray) / (double)on;
    }

    private bool[,] WhiteMask(Mat frame, PixelRect band)
    {
        var mask = new bool[band.Height, band.Width];
        for (int y = 0; y < band.Height; y++)
        {
            for (int x = 0; x < band.Width; x++)
            {
                var px = frame.At<Vec3b>(band.Y + y, band.X + x);
                int min = Math.Min(px.Item0, Math.Min(px.Item1, px.Item2));
                int max = Math.Max(px.Item0, Math.Max(px.Item1, px.Item2));
                mask[y, x] = min >= _config.MinWhite && max - min <= _config.MaxWhiteSpread;
            }
        }
        return mask;
    }

    private static bool[,] Shape(string rows)
    {
        var lines = rows.Split(' ');
        var shape = new bool[lines.Length, lines[0].Length];
        for (int y = 0; y < lines.Length; y++)
            for (int x = 0; x < lines[y].Length; x++)
                shape[y, x] = lines[y][x] == '#';
        return shape;
    }
}
