using BotPriston.Core.Bot;
using BotPriston.Core.Capture;
using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using BotPriston.Core.Input;
using BotPriston.Core.Vision;
using Microsoft.Extensions.Time.Testing;
using OpenCvSharp;

namespace BotPriston.Tests;

/// <summary>Inventory vision on real screenshots: item counts, empty cells, finding a hotbar potion.</summary>
public class InventoryVisionTests : IDisposable
{
    private const string Page1 = "20261008_142444_589_inv.png";
    private const string Page2 = "20261008_142500_373_inv.png";
    private const string Closed = "20261008_142318_518_inv_closed.png";

    private readonly BotConfig _config = TestConfig.Load();
    private readonly VisionPipeline _vision;

    public InventoryVisionTests() => _vision = new VisionPipeline(_config.Vision);

    private static Mat Load(string name) => Cv2.ImRead(Path.Combine(TestPaths.Samples, name), ImreadModes.Color);

    private PixelRect Cell(int row, int column)
    {
        var inv = _config.Vision.Inventory;
        return new PixelRect(inv.Grid.X + column * inv.CellSize, inv.Grid.Y + row * inv.CellSize, inv.CellSize, inv.CellSize);
    }

    [Theory]
    [InlineData(0, 0, 53)]
    [InlineData(0, 1, 89)]
    [InlineData(0, 2, 8)]
    [InlineData(0, 3, 12)]
    [InlineData(0, 4, 12)]
    [InlineData(0, 5, 24)]
    [InlineData(1, 0, 19)]
    [InlineData(3, 3, 6)]
    [InlineData(4, 1, 4)]
    [InlineData(5, 0, 1)]
    public void InventoryStackSizes_AreRead(int row, int column, int expected)
    {
        using var frame = Load(Page1);

        var slot = _vision.PotionSlots.Read(frame, Cell(row, column));

        Assert.Equal(expected, slot.Left);
    }

    [Theory]
    [InlineData(Page1, 1, 1)]   // a non-stackable item (crystal): no number, not empty
    [InlineData(Page1, 0, 10)]  // empty
    [InlineData(Page2, 0, 0)]   // empty page
    public void CellsWithoutNumber(string sample, int row, int column)
    {
        using var frame = Load(sample);

        var slot = _vision.PotionSlots.Read(frame, Cell(row, column));

        Assert.False(slot.Count.HasDigits);
        Assert.Equal(sample == Page1 && row == 1, !slot.Empty);
    }

    [Theory]
    [InlineData(Page1)]
    [InlineData(Closed)]  // icons taken from another screenshot
    public void HotbarPotions_AreFoundInTheInventory_OnlyTheSameKind(string iconSource)
    {
        using var source = Load(iconSource);
        using var frame = Load(Page1);
        var restockVision = new PipelineRestockVision(_vision);
        using var stm = restockVision.Icon(source, PotionKind.Stm);
        using var hp = restockVision.Icon(source, PotionKind.Hp);
        using var mp = restockVision.Icon(source, PotionKind.Mp);

        var stmItem = _vision.Inventory.Find(frame, stm);
        var hpItem = _vision.Inventory.Find(frame, hp);
        var mpItem = _vision.Inventory.Find(frame, mp);

        // The hotbar's green flask and red star are on the first row; its mana (round flask) is a
        // different potion from the blue stars in the inventory.
        Assert.Equal((0, 0), (stmItem?.Row, stmItem?.Column));
        Assert.Equal((0, 1), (hpItem?.Row, hpItem?.Column));
        Assert.Null(mpItem);
        Assert.Equal(new PixelPoint(33, 746), stmItem!.Center);
    }

    [Fact]
    public void NothingIsFoundOnAnEmptyPage()
    {
        using var frame = Load(Page2);
        using var stm = new PipelineRestockVision(_vision).Icon(frame, PotionKind.Stm);

        Assert.Null(_vision.Inventory.Find(frame, stm));
    }

    public void Dispose() => _vision.Dispose();
}

/// <summary>The restock routine against a simulated game (inventory, pages, Shift+key).</summary>
public class PotionRestockerTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly SimGame _game = new();
    private readonly FakeCapture _capture = new();
    private readonly PotionsConfig _config = new()
    {
        Hp = new PotionConfig { Key = "2" },
        Mp = new PotionConfig { Key = "3" },
        Stm = new PotionConfig { Key = "1" },
        Restock = new RestockConfig
        {
            Enabled = true, BelowCount = 5, ConfirmTicks = 3, StepDelayMs = 300, ToggleTimeoutMs = 2000,
            RetryAfterSeconds = 300, MouseRest = new PixelPoint(230, 882),
        },
    };
    private readonly PotionRestocker _restocker;

    public PotionRestockerTests()
    {
        _restocker = new PotionRestocker(_config, _game, _capture, _game, _time, TestLog.Silent, d => _time.Advance(d));
    }

    private void ObserveTicks(int ticks)
    {
        using var frame = new Mat(4, 4, MatType.CV_8UC3);
        for (int i = 0; i < ticks; i++)
            _restocker.Observe(frame, _game.Slots());
    }

    [Fact]
    public void LowStack_MustBeSeenSeveralTicks()
    {
        _game.Hotbar[PotionKind.Hp] = 3;

        ObserveTicks(2);
        Assert.Empty(_restocker.Due());

        ObserveTicks(1);
        Assert.Equal([PotionKind.Hp], _restocker.Due());
    }

    [Fact]
    public void FullStacks_NothingDue()
    {
        ObserveTicks(5);

        Assert.Empty(_restocker.Due());
    }

    [Fact]
    public void Refill_FromPage1_OpensHoversShiftKeyAndCloses()
    {
        _game.Hotbar[PotionKind.Hp] = 3;
        _game.Put(page: 1, row: 0, column: 1, PotionKind.Hp, 89);
        ObserveTicks(3);

        var outcome = _restocker.Run(_restocker.Due(), CancellationToken.None);

        Assert.Equal(RestockOutcome.Done, outcome);
        Assert.Equal(92, _game.Hotbar[PotionKind.Hp]);
        Assert.False(_game.Open);
        Assert.Equal(1, _restocker.Refills);
        Assert.Equal(["key V", "move (230,882)", "move (55,746)", "key Shift+2", "move (230,882)", "move (230,882)", "key V"],
            _game.Actions);
        ObserveTicks(3);
        Assert.Empty(_restocker.Due());
    }

    [Fact]
    public void Refill_LooksOnPage2()
    {
        _game.Hotbar[PotionKind.Mp] = 0;
        _game.Put(page: 2, row: 2, column: 3, PotionKind.Mp, 40);
        ObserveTicks(1);                          // the icon is learned while the slot has potions...
        _game.Hotbar[PotionKind.Mp] = 2;
        ObserveTicks(3);

        _restocker.Run(_restocker.Due(), CancellationToken.None);

        Assert.Equal(42, _game.Hotbar[PotionKind.Mp]);
        Assert.Contains("key E", _game.Actions);
        Assert.False(_game.Open);
    }

    [Fact]
    public void EmptySlot_IsRefilledWithThePotionSeenThereBefore()
    {
        _game.Put(page: 1, row: 0, column: 0, PotionKind.Stm, 20);
        ObserveTicks(1);                          // slot full: icon learned
        _game.Hotbar[PotionKind.Stm] = 0;         // ran out: the cell is empty
        ObserveTicks(3);

        _restocker.Run(_restocker.Due(), CancellationToken.None);

        Assert.Equal(20, _game.Hotbar[PotionKind.Stm]);
    }

    [Fact]
    public void SlotEmptySinceStart_IsPutAside()
    {
        _game.Hotbar[PotionKind.Stm] = 0;
        _game.Put(page: 1, row: 0, column: 0, PotionKind.Stm, 20);
        ObserveTicks(3);

        _restocker.Run(_restocker.Due(), CancellationToken.None);

        Assert.Equal(0, _game.Hotbar[PotionKind.Stm]);
        Assert.Equal(1, _restocker.Failures);
        Assert.False(_game.Open);
    }

    [Fact]
    public void NotInInventory_PutAsideUntilRetryTime()
    {
        _game.Hotbar[PotionKind.Hp] = 4;
        ObserveTicks(3);

        _restocker.Run(_restocker.Due(), CancellationToken.None);

        Assert.Equal(4, _game.Hotbar[PotionKind.Hp]);
        Assert.Equal(1, _restocker.Failures);
        Assert.False(_game.Open);
        ObserveTicks(3);
        Assert.Empty(_restocker.Due());

        _time.Advance(TimeSpan.FromSeconds(301));
        ObserveTicks(3);
        Assert.Equal([PotionKind.Hp], _restocker.Due());
    }

    [Fact]
    public void DisabledPotion_IsNeverDue()
    {
        _config.Mp.Enabled = false;
        _game.Hotbar[PotionKind.Mp] = 1;

        ObserveTicks(5);

        Assert.Empty(_restocker.Due());
    }

    [Fact]
    public void InventoryDoesNotOpen_PutsTheRequestAside()
    {
        _game.Hotbar[PotionKind.Hp] = 3;
        _game.Put(page: 1, row: 0, column: 1, PotionKind.Hp, 89);
        _game.IgnoreKeys = true;
        ObserveTicks(3);

        var outcome = _restocker.Run(_restocker.Due(), CancellationToken.None);

        Assert.Equal(RestockOutcome.Done, outcome);
        Assert.Equal(3, _game.Hotbar[PotionKind.Hp]);
        Assert.Empty(_restocker.Due());
    }

    [Fact]
    public void Interrupted_InventoryLeftOpen_IsClosedAfterwards()
    {
        _game.Hotbar[PotionKind.Hp] = 3;
        _game.Put(page: 1, row: 0, column: 1, PotionKind.Hp, 89);
        ObserveTicks(3);
        using var cancel = new CancellationTokenSource();
        _game.OnKey = key => { if (key == "V") cancel.Cancel(); }; // paused right after opening

        var outcome = _restocker.Run(_restocker.Due(), cancel.Token);

        Assert.Equal(RestockOutcome.Aborted, outcome);
        Assert.True(_game.Open);
        Assert.True(_restocker.MustClose(_game.Inventory(null!)));

        _game.OnKey = null;
        _restocker.Close(CancellationToken.None);

        Assert.False(_game.Open);
        Assert.False(_restocker.MustClose(_game.Inventory(null!)));
    }

    [Fact]
    public void ShiftKeyWithoutEffect_IsPutAside()
    {
        _game.Hotbar[PotionKind.Hp] = 3;
        _game.Put(page: 1, row: 0, column: 1, PotionKind.Hp, 89);
        _game.ShiftKeyWorks = false;
        ObserveTicks(3);

        _restocker.Run(_restocker.Due(), CancellationToken.None);

        Assert.Equal(1, _restocker.Failures);
        Assert.Equal(0, _restocker.Refills);
        Assert.False(_game.Open);
    }

    [Fact]
    public void Runner_RestocksBetweenFightsOnly_AfterPotions()
    {
        var config = new BotConfig { Potions = _config };
        var vision = new FakeVision();
        var runner = new BotRunner(config, _capture, vision, _game, new FakeWindow(), new BotControl(), _time,
            TestLog.Silent, restocker: _restocker);
        _game.Hotbar[PotionKind.Hp] = 3;
        _game.Put(page: 1, row: 0, column: 1, PotionKind.Hp, 89);

        for (int i = 0; i < 3; i++)
        {
            vision.Next = FakeVision.Snapshot(100, 100, 100) with { Potions = _game.Slots(), Inventory = _game.Inventory(null!) };
            runner.Tick();
        }

        Assert.Equal(92, _game.Hotbar[PotionKind.Hp]);
        Assert.False(_game.Open);
    }

    [Fact]
    public void Runner_RestocksMidFight_DroppingTheTarget()
    {
        var config = new BotConfig { Potions = _config };
        var vision = new FakeVision();
        var finder = new ScriptedFinder();
        finder.WillFind(new PixelPoint(875, 465));
        var runner = new BotRunner(config, _capture, vision, _game, new FakeWindow(), new BotControl(), _time, TestLog.Silent,
            onProgress => new CombatBrain(config.Combat, finder, new FakeCursor(), _game, _time, TestLog.Silent, onProgress),
            _restocker);
        _game.Put(page: 1, row: 0, column: 1, PotionKind.Hp, 89);

        void Tick()
        {
            vision.Next = FakeVision.Snapshot(100, 100, 100, FakeVision.Target(TargetStatus.Engaged, 80))
                with { Potions = _game.Slots(), Inventory = _game.Inventory(null!) };
            runner.Tick();
        }

        for (int i = 0; i < 3; i++) Tick();
        Assert.Equal(BrainState.Attack, runner.Brain!.State);

        _game.Hotbar[PotionKind.Hp] = 3; // monsters keep coming: no gap between fights
        for (int i = 0; i < 3; i++) Tick();

        Assert.Equal(92, _game.Hotbar[PotionKind.Hp]);
        Assert.False(_game.Open);
        Assert.Equal(BrainState.SearchTarget, runner.Brain.State);
    }

    public void Dispose() => _restocker.Dispose();

    /// <summary>
    /// A tiny model of the game: the hotbar stacks, the inventory (2 pages of cells) and how V, E and
    /// Shift+key change them. It is both the input sink and the vision of the restocker.
    /// </summary>
    private sealed class SimGame : IInputSink, IRestockVision
    {
        private readonly Dictionary<(int Page, int Row, int Column), (PotionKind Kind, int Count)> _cells = [];
        private PixelPoint _mouse;
        private int _page = 1;

        public Dictionary<PotionKind, int> Hotbar { get; } = new()
        {
            [PotionKind.Hp] = 30, [PotionKind.Mp] = 30, [PotionKind.Stm] = 30,
        };

        public bool Open { get; private set; }
        public bool IgnoreKeys { get; set; }
        public bool ShiftKeyWorks { get; set; } = true;
        public Action<string>? OnKey { get; set; }
        public List<string> Actions { get; } = [];

        public void Put(int page, int row, int column, PotionKind kind, int count) => _cells[(page, row, column)] = (kind, count);

        public PotionSlots Slots() => new(Slot(PotionKind.Hp), Slot(PotionKind.Mp), Slot(PotionKind.Stm));

        private PotionSlot Slot(PotionKind kind) =>
            Hotbar[kind] == 0 ? new PotionSlot(true, ItemCount.None) : new PotionSlot(false, new ItemCount(true, Hotbar[kind]));

        private static PixelPoint Center(int row, int column) => new(22 + column * 22 + 11, 735 + row * 22 + 11);

        // IRestockVision
        public InventoryState Inventory(Mat frame) => new(Open, Open ? 1 : 0.2, Open ? _page : null);

        public InventoryItem? Find(Mat frame, Mat icon)
        {
            var kind = (PotionKind)icon.At<byte>(0, 0);
            if (!Open) return null;
            foreach (var ((page, row, column), item) in _cells)
            {
                if (page == _page && item.Kind == kind)
                    return new InventoryItem(row, column, Center(row, column), 0.01);
            }
            return null;
        }

        public PotionSlot Slot(Mat frame, PotionKind kind) => Slot(kind);

        public Mat Icon(Mat frame, PotionKind kind) => new(1, 1, MatType.CV_8UC1, Scalar.All((int)kind));

        // IInputSink
        public bool PressKey(KeyChord key)
        {
            Actions.Add($"key {key}");
            OnKey?.Invoke(key.ToString());
            if (IgnoreKeys) return true;

            switch (key.ToString())
            {
                case "V": Open = !Open; break;
                case "E" when Open: _page = 3 - _page; break;
                case var k when k.StartsWith("Shift+") && Open && ShiftKeyWorks:
                    var slot = k[^1] switch { '1' => PotionKind.Stm, '2' => PotionKind.Hp, _ => PotionKind.Mp };
                    var hovered = _cells.FirstOrDefault(c => c.Key.Page == _page && Center(c.Key.Row, c.Key.Column) == _mouse);
                    if (hovered.Value.Count > 0 && hovered.Value.Kind == slot)
                    {
                        Hotbar[slot] += hovered.Value.Count;
                        _cells.Remove(hovered.Key);
                    }
                    break;
            }
            return true;
        }

        public bool MoveMouse(PixelPoint clientPoint)
        {
            Actions.Add($"move {clientPoint}");
            _mouse = clientPoint;
            return true;
        }

        public bool MouseDown(MouseButton button) => true;
        public bool MouseUp(MouseButton button) => true;
        public bool Click(MouseButton button) => true;
        public void ReleaseAll() { }
        public void ForceReleaseButtons() { }
    }
}
