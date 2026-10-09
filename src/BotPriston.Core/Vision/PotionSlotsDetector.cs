using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using OpenCvSharp;

namespace BotPriston.Core.Vision;

/// <summary>A potion's hotbar cell: empty, or holding a stack (whose size may be unreadable).</summary>
public readonly record struct PotionSlot(bool Empty, ItemCount Count)
{
    /// <summary>Potions left: 0 when empty, null when the number couldn't be read.</summary>
    public int? Left => Empty ? 0 : Count.Value;

    public override string ToString() => Empty ? "empty" : Count.ToString();
}

public sealed record PotionSlots(PotionSlot Hp, PotionSlot Mp, PotionSlot Stm)
{
    public override string ToString() => $"HP {Hp} MP {Mp} STM {Stm}";
}

/// <summary>Reads how many potions are left in each hotbar cell.</summary>
public sealed class PotionSlotsDetector : IDetector<PotionSlots>
{
    /// <summary>Brightness (HSV value) above which a pixel belongs to an icon rather than the dark cell background.</summary>
    private const int ItemBrightness = 70;

    private readonly PotionSlotsConfig _config;
    private readonly ItemCountReader _counts;

    public PotionSlotsDetector(PotionSlotsConfig config, ItemCountReader counts)
    {
        _config = config;
        _counts = counts;
    }

    public PotionSlotsConfig Config => _config;

    public PotionSlots Detect(Mat frame) =>
        new(Read(frame, _config.Hp), Read(frame, _config.Mp), Read(frame, _config.Stm));

    public PotionSlot Read(Mat frame, PixelRect cell)
    {
        var count = _counts.Read(frame, cell);
        bool empty = !count.HasDigits && ItemFraction(frame, cell) < _config.MinItemFraction;
        return new PotionSlot(empty, count);
    }

    /// <summary>
    /// The part of a cell that identifies the item: below the stack size, so the same potion matches
    /// whatever its count. Same geometry for hotbar and inventory cells.
    /// </summary>
    public static PixelRect IconArea(PixelRect cell) => new(cell.X + 1, cell.Y + 12, cell.Width - 2, cell.Height - 12);

    private static double ItemFraction(Mat frame, PixelRect cell)
    {
        var area = IconArea(cell).Intersect(new PixelRect(0, 0, frame.Width, frame.Height));
        if (area.IsEmpty) return 0;

        using var roi = new Mat(frame, area.ToCvRect());
        using var hsv = roi.CvtColor(ColorConversionCodes.BGR2HSV);
        using var value = hsv.ExtractChannel(2);
        using var bright = value.Threshold(ItemBrightness, 255, ThresholdTypes.Binary);
        return Cv2.CountNonZero(bright) / (double)(area.Width * area.Height);
    }
}
