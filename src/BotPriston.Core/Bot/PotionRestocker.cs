using BotPriston.Core.Capture;
using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using BotPriston.Core.Input;
using BotPriston.Core.Vision;
using OpenCvSharp;
using Serilog;

namespace BotPriston.Core.Bot;

/// <summary>What the restocker needs to see. Implemented by <see cref="PipelineRestockVision"/>; faked in tests.</summary>
public interface IRestockVision
{
    InventoryState Inventory(Mat frame);
    InventoryItem? Find(Mat frame, Mat icon);
    PotionSlot Slot(Mat frame, PotionKind kind);

    /// <summary>Copy of the part of the potion's hotbar cell that identifies it (the caller disposes it).</summary>
    Mat Icon(Mat frame, PotionKind kind);
}

public sealed class PipelineRestockVision(VisionPipeline vision) : IRestockVision
{
    public InventoryState Inventory(Mat frame) => vision.Inventory.Detect(frame);
    public InventoryItem? Find(Mat frame, Mat icon) => vision.Inventory.Find(frame, icon);
    public PotionSlot Slot(Mat frame, PotionKind kind) => vision.PotionSlots.Read(frame, Cell(kind));
    public Mat Icon(Mat frame, PotionKind kind) => new Mat(frame, PotionSlotsDetector.IconArea(Cell(kind)).ToCvRect()).Clone();

    private PixelRect Cell(PotionKind kind) => vision.PotionSlots.Config.For(kind);
}

public static class PotionKindExtensions
{
    public static PotionSlot For(this PotionSlots slots, PotionKind kind) => kind switch
    {
        PotionKind.Hp => slots.Hp,
        PotionKind.Mp => slots.Mp,
        _ => slots.Stm,
    };

    public static PixelRect For(this PotionSlotsConfig slots, PotionKind kind) => kind switch
    {
        PotionKind.Hp => slots.Hp,
        PotionKind.Mp => slots.Mp,
        _ => slots.Stm,
    };

    public static PotionConfig For(this PotionsConfig potions, PotionKind kind) => kind switch
    {
        PotionKind.Hp => potions.Hp,
        PotionKind.Mp => potions.Mp,
        _ => potions.Stm,
    };
}

public enum RestockOutcome
{
    /// <summary>Went through every potion asked for (each one refilled or put aside) and closed the inventory.</summary>
    Done,
    /// <summary>Interrupted (pause, focus lost...). The inventory may still be open: see <see cref="PotionRestocker.MustClose"/>.</summary>
    Aborted,
}

/// <summary>
/// Refills the hotbar from the inventory when a potion stack runs low. The hotbar icon of the potion is
/// what gets looked for in the inventory, so whatever potion sits in a slot is the one refilled. With the
/// inventory open, hovering the potion and pressing Shift + its key adds that stack to the slot.
/// The runner calls <see cref="Run"/> as soon as a stack is low, dropping the current target; it blocks for a few seconds.
/// </summary>
public sealed class PotionRestocker : IDisposable
{
    private static readonly PotionKind[] Kinds = [PotionKind.Hp, PotionKind.Mp, PotionKind.Stm];
    private const int MaxCloseAttempts = 3;

    private readonly RestockConfig _config;
    private readonly PotionsConfig _potions;
    private readonly IRestockVision _vision;
    private readonly ICaptureSource _capture;
    private readonly IInputSink _input;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly Action<TimeSpan> _sleep;
    private readonly KeyChord _inventoryKey, _pageKey;
    private readonly Dictionary<PotionKind, Mat> _icons = [];
    private readonly Dictionary<PotionKind, int> _lowTicks = [];
    private readonly Dictionary<PotionKind, DateTimeOffset> _blockedUntil = [];
    private bool _openedByUs;
    private int _closeAttempts;

    public PotionRestocker(PotionsConfig potions, IRestockVision vision, ICaptureSource capture, IInputSink input,
        TimeProvider time, ILogger log, Action<TimeSpan>? sleep = null)
    {
        _config = potions.Restock;
        _potions = potions;
        _vision = vision;
        _capture = capture;
        _input = input;
        _time = time;
        _log = log;
        _sleep = sleep ?? Thread.Sleep;
        _inventoryKey = KeyChord.Parse(_config.InventoryKey);
        _pageKey = KeyChord.Parse(_config.PageKey);
    }

    /// <summary>Potions moved from the inventory to the hotbar.</summary>
    public int Refills { get; private set; }

    /// <summary>Times a potion was looked for and not found (or didn't refill).</summary>
    public int Failures { get; private set; }

    public string Summary => $"potions refilled from the inventory {Refills}x ({Failures} failed)";

    /// <summary>A restock is in progress (for display).</summary>
    public bool Busy { get; private set; }

    /// <summary>
    /// Every tick with the HUD up: remember which potion each hotbar cell holds (its icon is what gets
    /// looked for in the inventory, and an empty cell has none) and count ticks with a low stack.
    /// </summary>
    public void Observe(Mat frame, PotionSlots slots)
    {
        foreach (var kind in Kinds)
        {
            var slot = slots.For(kind);
            if (!slot.Empty && slot.Count.Value is not null)
            {
                var icon = _vision.Icon(frame, kind);
                if (_icons.Remove(kind, out var old)) old.Dispose();
                _icons[kind] = icon;
            }

            bool low = _potions.For(kind).Enabled && slot.Left is { } left && left < _config.BelowCount;
            _lowTicks[kind] = low ? _lowTicks.GetValueOrDefault(kind) + 1 : 0;
        }
    }

    /// <summary>Potions to refill now: low for <see cref="RestockConfig.ConfirmTicks"/> ticks and not put aside.</summary>
    public IReadOnlyList<PotionKind> Due()
    {
        var now = _time.GetUtcNow();
        return Kinds.Where(k => _lowTicks.GetValueOrDefault(k) >= _config.ConfirmTicks
            && _blockedUntil.GetValueOrDefault(k) <= now).ToList();
    }

    /// <summary>The inventory this restocker opened is still open (it was interrupted): close it before doing anything else.</summary>
    public bool MustClose(InventoryState? inventory)
    {
        if (_openedByUs && inventory is { Open: false })
        {
            _openedByUs = false;
            _closeAttempts = 0;
        }
        return _openedByUs && inventory is { Open: true };
    }

    public RestockOutcome Run(IReadOnlyList<PotionKind> kinds, CancellationToken cancel)
    {
        Busy = true;
        try
        {
            return RunSteps(kinds, cancel);
        }
        finally
        {
            Busy = false;
        }
    }

    private RestockOutcome RunSteps(IReadOnlyList<PotionKind> kinds, CancellationToken cancel)
    {
        _input.ForceReleaseButtons();
        _log.Information("Restocking {Potions} from the inventory", string.Join(", ", kinds.Select(Describe)));

        switch (Open(cancel))
        {
            case null:
                return RestockOutcome.Aborted;
            case false:
                foreach (var kind in kinds)
                    PutAside(kind, $"the inventory did not open ({_inventoryKey})");
                return RestockOutcome.Done;
        }

        foreach (var kind in kinds)
        {
            if (cancel.IsCancellationRequested || !Refill(kind, cancel))
                return RestockOutcome.Aborted;
        }

        return Close(cancel);
    }

    /// <summary>Parks the mouse and closes the inventory, checking that it did.</summary>
    public RestockOutcome Close(CancellationToken cancel)
    {
        _closeAttempts++;
        if (!Park(cancel) || !Press(_inventoryKey, cancel))
            return RestockOutcome.Aborted;

        switch (WaitForInventory(open: false, cancel))
        {
            case null:
                return RestockOutcome.Aborted;
            case true:
                _openedByUs = false;
                _closeAttempts = 0;
                return RestockOutcome.Done;
        }

        if (_closeAttempts >= MaxCloseAttempts)
        {
            _log.Error("The inventory does not close ({Key} pressed {Attempts}x): leaving it open", _inventoryKey, _closeAttempts);
            _openedByUs = false;
            _closeAttempts = 0;
        }
        else
        {
            _log.Warning("The inventory did not close; trying again");
        }
        return RestockOutcome.Done;
    }

    /// <summary>True = open, false = didn't open, null = interrupted.</summary>
    private bool? Open(CancellationToken cancel)
    {
        using (var frame = _capture.Grab())
        {
            if (frame is not null && _vision.Inventory(frame.Image).Open)
            {
                _openedByUs = true;
                return Park(cancel) ? true : null;
            }
        }

        if (!_input.PressKey(_inventoryKey)) return null;
        // From here it may be open, even if interrupted right now: MustClose checks the screen later.
        _openedByUs = true;
        if (!Step(true, cancel)) return null;

        var opened = WaitForInventory(open: true, cancel);
        if (opened == true && !Park(cancel)) return null;
        return opened;
    }

    /// <summary>Looks for the potion on the page on screen, then on the other page. False = interrupted.</summary>
    private bool Refill(PotionKind kind, CancellationToken cancel)
    {
        if (!_icons.TryGetValue(kind, out var icon))
        {
            PutAside(kind, "its hotbar cell has been empty since the bot started, so it isn't known which potion goes there");
            return true;
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            InventoryItem? item;
            int? page, before;
            using (var frame = _capture.Grab())
            {
                if (frame is null) return false;
                var inventory = _vision.Inventory(frame.Image);
                if (!inventory.Open)
                {
                    PutAside(kind, "the inventory closed by itself");
                    return true;
                }
                item = _vision.Find(frame.Image, icon);
                page = inventory.Page;
                before = _vision.Slot(frame.Image, kind).Left;
            }

            if (item is not null)
                return Use(kind, item, page, before, cancel);

            if (attempt == 0 && !Press(_pageKey, cancel))
                return false;
        }

        PutAside(kind, "no potion like the one in the hotbar on either inventory page");
        return true;
    }

    /// <summary>Hover the potion, Shift + its key, and check the hotbar stack grew. False = interrupted.</summary>
    private bool Use(PotionKind kind, InventoryItem item, int? page, int? before, CancellationToken cancel)
    {
        var key = KeyChord.Parse("Shift+" + _potions.For(kind).Key);
        if (!Move(item.Center, cancel) || !Press(key, cancel) || !Park(cancel))
            return false;

        PotionSlot after;
        using (var frame = _capture.Grab())
        {
            if (frame is null) return false;
            after = _vision.Slot(frame.Image, kind);
        }

        if (after.Left is not { } now)
        {
            _log.Information("Restock {Potion}: {Key} on inventory page {Page} {Item}; the hotbar now reads {After}",
                Describe(kind), key, page?.ToString() ?? "?", item, after);
            _lowTicks[kind] = 0;
            return true;
        }
        if (before is not null && now <= before)
        {
            PutAside(kind, $"{key} over the potion (page {page?.ToString() ?? "?"}, {item}) did not refill the hotbar ({before} -> {now})");
            return true;
        }

        Refills++;
        _lowTicks[kind] = 0;
        _log.Information("Restocked {Potion}: {Before} -> {After} (inventory page {Page}, {Item})",
            Describe(kind), before?.ToString() ?? "?", now, page?.ToString() ?? "?", item);
        if (now < _config.BelowCount)
            PutAside(kind, $"still only {now} after refilling (the inventory had few)");
        return true;
    }

    private void PutAside(PotionKind kind, string why)
    {
        Failures++;
        _lowTicks[kind] = 0;
        _blockedUntil[kind] = _time.GetUtcNow() + TimeSpan.FromSeconds(_config.RetryAfterSeconds);
        _log.Warning("Restock {Potion}: {Why}; not trying again for {Seconds}s", Describe(kind), why, _config.RetryAfterSeconds);
    }

    /// <summary>True when the inventory reached the wanted state, false on timeout, null when interrupted.</summary>
    private bool? WaitForInventory(bool open, CancellationToken cancel)
    {
        var deadline = _time.GetUtcNow() + TimeSpan.FromMilliseconds(_config.ToggleTimeoutMs);
        while (true)
        {
            using (var frame = _capture.Grab())
            {
                if (frame is not null && _vision.Inventory(frame.Image).Open == open)
                    return true;
            }
            if (cancel.IsCancellationRequested) return null;
            if (_time.GetUtcNow() >= deadline) return false;
            _sleep(TimeSpan.FromMilliseconds(100));
        }
    }

    private bool Park(CancellationToken cancel) => Move(_config.MouseRest, cancel);

    private bool Move(PixelPoint point, CancellationToken cancel) => Step(_input.MoveMouse(point), cancel);

    private bool Press(KeyChord key, CancellationToken cancel) => Step(_input.PressKey(key), cancel);

    /// <summary>After an action, give the game time to react. False when the action was refused or the bot was interrupted.</summary>
    private bool Step(bool sent, CancellationToken cancel)
    {
        if (!sent || cancel.IsCancellationRequested) return false;
        _sleep(TimeSpan.FromMilliseconds(_config.StepDelayMs));
        return !cancel.IsCancellationRequested;
    }

    private string Describe(PotionKind kind) => $"{kind.ToString().ToUpperInvariant()} ({_potions.For(kind).Key})";

    public void Dispose()
    {
        foreach (var icon in _icons.Values) icon.Dispose();
        _icons.Clear();
    }
}
