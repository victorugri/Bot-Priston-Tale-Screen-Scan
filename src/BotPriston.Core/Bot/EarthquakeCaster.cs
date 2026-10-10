using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using BotPriston.Core.Input;
using BotPriston.Core.Targeting;
using BotPriston.Core.Vision;
using Serilog;

namespace BotPriston.Core.Bot;

/// <summary>
/// Earthquake mode (<see cref="EarthquakeConfig"/>). Every <see cref="EarthquakeConfig.IntervalMs"/>:
/// one skill key picked at random by weight (it switches which skill the right button casts), the mouse to
/// a random spot near the middle of the screen (it keeps moving slightly: casts with a still cursor
/// stopped counting), and a right click. No target to find: the skills hit everything around.
/// Optionally, every <see cref="EarthquakeConfig.LeftAttackEverySeconds"/> a monster in reach gets one left click.
/// </summary>
public sealed class EarthquakeCaster
{
    private readonly EarthquakeConfig _config;
    private readonly IInputSink _input;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly ITargetFinder? _finder;
    private readonly Random _random;
    private readonly (KeyChord Key, double Weight)[] _keys;
    private readonly double _totalWeight;
    private readonly Dictionary<string, int> _keyCounts = [];
    private DateTimeOffset _next = DateTimeOffset.MinValue;
    private DateTimeOffset _nextLeftAttack;
    private bool _lowMpLogged;

    /// <param name="finder">Finds the monster for the periodic left attack; null = no left attack.</param>
    /// <param name="random">Source of the random keys and mouse spots (fixed seed in tests).</param>
    public EarthquakeCaster(EarthquakeConfig config, IInputSink input, TimeProvider time, ILogger log,
        ITargetFinder? finder = null, Random? random = null)
    {
        _config = config;
        _input = input;
        _time = time;
        _log = log;
        _finder = finder;
        _random = random ?? Random.Shared;
        _keys = config.Keys.Select(k => (KeyChord.Parse(k.Key), k.Weight)).ToArray();
        _totalWeight = _keys.Sum(k => k.Weight);
        _nextLeftAttack = time.GetUtcNow() + TimeSpan.FromSeconds(config.LeftAttackEverySeconds);
    }

    /// <summary>Right clicks sent.</summary>
    public int Casts { get; private set; }

    /// <summary>Periodic left clicks on a monster.</summary>
    public int LeftAttacks { get; private set; }

    public string Summary =>
        $"earthquake cast {Casts}x" +
        (_keyCounts.Count > 0 ? $" (keys {string.Join(", ", _keyCounts.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}x"))})" : "") +
        (LeftAttacks > 0 ? $", left attacks {LeftAttacks}x" : "");

    public void Tick(PlayerBars bars, CancellationToken cancel)
    {
        var now = _time.GetUtcNow();
        if (LeftAttackDue(now))
        {
            LeftAttack(now, cancel);
            return;
        }

        if (now < _next) return;

        if (bars.Mp.Percent < _config.MinMpPercent)
        {
            if (!_lowMpLogged)
                _log.Information("Earthquake on hold: MP {Mp:F0}% < {Min}%", bars.Mp.Percent, _config.MinMpPercent);
            _lowMpLogged = true;
            return;
        }
        _lowMpLogged = false;

        // Start counting the interval now, so the time the input takes is part of it.
        _next = now + TimeSpan.FromMilliseconds(_config.IntervalMs);

        var key = PickKey();
        if (key is { } chord && !_input.PressKey(chord))
            return; // refused (game not focused): the runner pauses
        if (!_input.MoveMouse(Wander()) || !_input.Click(MouseButton.Right))
            return;

        Casts++;
        if (key is { } pressed)
            _keyCounts[pressed.ToString()] = _keyCounts.GetValueOrDefault(pressed.ToString()) + 1;
        if (Casts == 1 || Casts % 500 == 0)
            _log.Information("Earthquake cast #{Casts}: {Summary}", Casts, Summary);
    }

    /// <summary>One of the configured keys, by weight; null when none are configured.</summary>
    private KeyChord? PickKey()
    {
        if (_keys.Length == 0) return null;
        double roll = _random.NextDouble() * _totalWeight;
        foreach (var (key, weight) in _keys)
        {
            roll -= weight;
            if (roll < 0) return key;
        }
        return _keys[^1].Key;
    }

    /// <summary>A random spot within <see cref="EarthquakeConfig.JitterPx"/> of the middle (uniform over a disc).</summary>
    private PixelPoint Wander()
    {
        if (_config.JitterPx <= 0) return _config.Point;
        double angle = _random.NextDouble() * 2 * Math.PI;
        double radius = _config.JitterPx * Math.Sqrt(_random.NextDouble());
        return new PixelPoint(_config.Point.X + (int)Math.Round(radius * Math.Cos(angle)),
            _config.Point.Y + (int)Math.Round(radius * Math.Sin(angle)));
    }

    private bool LeftAttackDue(DateTimeOffset now) =>
        _finder is not null && _config.LeftAttackEverySeconds > 0 && now >= _nextLeftAttack;

    /// <summary>Hover sweep (no click) until the cursor gem is red on a monster in reach, then one left click.</summary>
    private void LeftAttack(DateTimeOffset now, CancellationToken cancel)
    {
        _input.ForceReleaseButtons();
        var result = _finder!.Find(cancel);
        switch (result.Outcome)
        {
            case FindOutcome.Found when _input.Click(MouseButton.Left):
                LeftAttacks++;
                _nextLeftAttack = now + TimeSpan.FromSeconds(_config.LeftAttackEverySeconds);
                _log.Information("Left attack #{Count} on the monster at {Point}, back to the earthquake", LeftAttacks, result.Target!.Point);
                break;
            case FindOutcome.Aborted:
                break; // paused / focus lost: try again when running
            default:
                _nextLeftAttack = now + TimeSpan.FromSeconds(_config.LeftAttackRetrySeconds);
                _log.Information("Left attack: no monster in reach, trying again in {Seconds}s", _config.LeftAttackRetrySeconds);
                break;
        }
    }
}
