using BotPriston.Core.Config;
using BotPriston.Core.Input;
using BotPriston.Core.Vision;
using Serilog;

namespace BotPriston.Core.Bot;

public enum PotionKind
{
    Hp,
    Mp,
    Stm,
}

/// <summary>
/// Decides when to drink. One potion per decision, priority HP &gt; MP &gt; STM. Tracks whether each
/// potion actually raised its bar; repeated failures mean "out of stock" and pause that potion.
/// </summary>
public sealed class PotionManager
{
    private static readonly PotionKind[] Priority = [PotionKind.Hp, PotionKind.Mp, PotionKind.Stm];

    private readonly PotionsConfig _config;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly Dictionary<PotionKind, Slot> _slots;

    public PotionManager(PotionsConfig config, TimeProvider time, ILogger log)
    {
        _config = config;
        _time = time;
        _log = log;
        _slots = new()
        {
            [PotionKind.Hp] = new Slot(config.Hp),
            [PotionKind.Mp] = new Slot(config.Mp),
            [PotionKind.Stm] = new Slot(config.Stm),
        };
    }

    public KeyChord KeyFor(PotionKind kind) => _slots[kind].Key;

    public bool IsOutOfStock(PotionKind kind) => _slots[kind].OutOfStockUntil > _time.GetUtcNow();

    /// <summary>The potion to use now, or null. Also updates the effect bookkeeping of past uses.</summary>
    public PotionKind? Decide(PlayerBars bars)
    {
        var now = _time.GetUtcNow();
        foreach (var kind in Priority)
            Evaluate(kind, _slots[kind], Read(bars, kind), now);

        foreach (var kind in Priority)
        {
            var slot = _slots[kind];
            if (!slot.Config.Enabled || Read(bars, kind) >= slot.Config.BelowPercent) continue;
            if (slot.OutOfStockUntil > now) continue;
            if (now - slot.LastUse < TimeSpan.FromMilliseconds(slot.Config.CooldownMs)) continue;
            return kind;
        }
        return null;
    }

    /// <summary>Call after the key was actually sent.</summary>
    public void MarkUsed(PotionKind kind, PlayerBars bars)
    {
        var slot = _slots[kind];
        slot.LastUse = _time.GetUtcNow();
        slot.PercentAtUse = Read(bars, kind);
        slot.PendingCheck = true;
        _log.Information("Potion {Kind} ({Key}) used at {Percent:F1}%", kind.ToString(), slot.Key.ToString(), slot.PercentAtUse);
    }

    /// <summary>Once the cooldown is over, compare the bar with its value at use time.</summary>
    private void Evaluate(PotionKind kind, Slot slot, double percent, DateTimeOffset now)
    {
        if (!slot.PendingCheck || now - slot.LastUse < TimeSpan.FromMilliseconds(slot.Config.CooldownMs))
            return;

        slot.PendingCheck = false;
        if (percent >= slot.PercentAtUse + _config.MinGainPercent)
        {
            slot.Failures = 0;
            return;
        }

        slot.Failures++;
        _log.Warning("Potion {Kind} had no visible effect ({Before:F1}% -> {After:F1}%), {Failures}/{Max}",
            kind.ToString(), slot.PercentAtUse, percent, slot.Failures, _config.MaxFailures);

        if (slot.Failures >= _config.MaxFailures)
        {
            slot.Failures = 0;
            slot.OutOfStockUntil = now + TimeSpan.FromSeconds(_config.RetryAfterSeconds);
            _log.Warning("Potion {Kind} looks out of stock; not using it for {Seconds}s", kind.ToString(), _config.RetryAfterSeconds);
        }
    }

    private static double Read(PlayerBars bars, PotionKind kind) => kind switch
    {
        PotionKind.Hp => bars.Hp.Percent,
        PotionKind.Mp => bars.Mp.Percent,
        _ => bars.Stm.Percent,
    };

    private sealed class Slot(PotionConfig config)
    {
        public PotionConfig Config { get; } = config;
        public KeyChord Key { get; } = KeyChord.Parse(config.Key);
        public DateTimeOffset LastUse { get; set; } = DateTimeOffset.MinValue;
        public double PercentAtUse { get; set; }
        public bool PendingCheck { get; set; }
        public int Failures { get; set; }
        public DateTimeOffset OutOfStockUntil { get; set; } = DateTimeOffset.MinValue;
    }
}
