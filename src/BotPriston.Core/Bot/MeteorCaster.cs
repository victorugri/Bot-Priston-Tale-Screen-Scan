using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using BotPriston.Core.Input;
using BotPriston.Core.Vision;
using Serilog;

namespace BotPriston.Core.Bot;

/// <summary>
/// Meteor mode (<see cref="MeteorConfig"/>): a right click near the middle of the screen every
/// <see cref="MeteorConfig.IntervalMs"/> casts the meteor (an area skill: no target to find). Every
/// <see cref="MeteorConfig.ShieldEverySeconds"/> (and first thing), the shield key, a right click to cast
/// the shield, and the meteor key again. The mouse moves slightly each click.
/// </summary>
public sealed class MeteorCaster
{
    private readonly MeteorConfig _config;
    private readonly IInputSink _input;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly Random _random;
    private readonly KeyChord _meteorKey, _shieldKey;
    private DateTimeOffset _nextCast = DateTimeOffset.MinValue;
    private DateTimeOffset _nextShield = DateTimeOffset.MinValue; // the shield goes up first
    private bool _lowMpLogged;

    /// <param name="random">Source of the mouse wandering (fixed seed in tests).</param>
    public MeteorCaster(MeteorConfig config, IInputSink input, TimeProvider time, ILogger log, Random? random = null)
    {
        _config = config;
        _input = input;
        _time = time;
        _log = log;
        _random = random ?? Random.Shared;
        _meteorKey = KeyChord.Parse(config.MeteorKey);
        _shieldKey = KeyChord.Parse(config.ShieldKey);
    }

    /// <summary>Meteor right clicks sent.</summary>
    public int Casts { get; private set; }

    /// <summary>Shield casts (key + right click + back to the meteor).</summary>
    public int Shields { get; private set; }

    public string Summary => $"meteor cast {Casts}x, shield {Shields}x";

    public void Tick(PlayerBars bars, CancellationToken cancel)
    {
        var now = _time.GetUtcNow();
        if (now < _nextCast && now < _nextShield) return;

        if (bars.Mp.Percent < _config.MinMpPercent)
        {
            if (!_lowMpLogged)
                _log.Information("Meteor on hold: MP {Mp:F0}% < {Min}%", bars.Mp.Percent, _config.MinMpPercent);
            _lowMpLogged = true;
            return;
        }
        _lowMpLogged = false;

        if (now >= _nextShield)
        {
            CastShield(now);
            return;
        }

        _nextCast = now + TimeSpan.FromMilliseconds(_config.IntervalMs);
        if (!RightClick()) return; // refused (game not focused): the runner pauses
        Casts++;
        if (Casts == 1 || Casts % 100 == 0)
            _log.Information("Meteor cast #{Casts} ({Summary})", Casts, Summary);
    }

    /// <summary>Shield key, right click, back to the meteor key. A refused step is retried next tick.</summary>
    private void CastShield(DateTimeOffset now)
    {
        if (!_input.PressKey(_shieldKey) || !RightClick())
            return;
        Shields++;
        _nextShield = now + TimeSpan.FromSeconds(_config.ShieldEverySeconds);
        if (!_input.PressKey(_meteorKey))
            _nextShield = now; // the meteor key didn't go out: do the whole switch again
        _nextCast = now + TimeSpan.FromMilliseconds(_config.IntervalMs);
        _log.Information("Shield #{Count} ({Shield} + right click), back to the meteor ({Meteor})", Shields, _shieldKey, _meteorKey);
    }

    private bool RightClick() => _input.MoveMouse(Wander()) && _input.Click(MouseButton.Right);

    /// <summary>A random spot within <see cref="MeteorConfig.JitterPx"/> of the middle (uniform over a disc).</summary>
    private PixelPoint Wander()
    {
        if (_config.JitterPx <= 0) return _config.Point;
        double angle = _random.NextDouble() * 2 * Math.PI;
        double radius = _config.JitterPx * Math.Sqrt(_random.NextDouble());
        return new PixelPoint(_config.Point.X + (int)Math.Round(radius * Math.Cos(angle)),
            _config.Point.Y + (int)Math.Round(radius * Math.Sin(angle)));
    }
}
