using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using BotPriston.Core.Input;
using BotPriston.Core.Targeting;
using BotPriston.Core.Vision;
using OpenCvSharp;
using Serilog;

namespace BotPriston.Core.Bot;

/// <summary>
/// Meteor mode (<see cref="MeteorConfig"/>). Every 2-3 s (random, <see cref="MeteorConfig.IntervalMs"/> to
/// <see cref="MeteorConfig.IntervalMaxMs"/>) a right click on a
/// monster casts the meteor: on the same spot while the cursor gem is still red there, otherwise after a
/// hover sweep (no click) finds one within attack reach. A right click is never sent with the gem not red.
/// Every <see cref="MeteorConfig.ShieldEverySeconds"/> (and first thing): the shield key, a right click near
/// the middle of the screen to cast it, and the meteor key again.
/// </summary>
public sealed class MeteorCaster
{
    private readonly MeteorConfig _config;
    private readonly IInputSink _input;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly ITargetFinder _finder;
    private readonly ICursorReader _cursor;
    private readonly Random _random;
    private readonly KeyChord _meteorKey, _shieldKey;
    private DateTimeOffset _nextCast = DateTimeOffset.MinValue;
    private DateTimeOffset _nextShield = DateTimeOffset.MinValue; // the shield goes up first
    private PixelPoint? _aim;
    private bool _lowMpLogged, _noMonsterLogged;

    /// <param name="random">Source of the cast intervals and the shield click spot (fixed seed in tests).</param>
    public MeteorCaster(MeteorConfig config, IInputSink input, TimeProvider time, ILogger log,
        ITargetFinder finder, ICursorReader cursor, Random? random = null)
    {
        _config = config;
        _input = input;
        _time = time;
        _log = log;
        _finder = finder;
        _cursor = cursor;
        _random = random ?? Random.Shared;
        _meteorKey = KeyChord.Parse(config.MeteorKey);
        _shieldKey = KeyChord.Parse(config.ShieldKey);
    }

    /// <summary>Meteor right clicks sent (each one on a monster).</summary>
    public int Casts { get; private set; }

    /// <summary>Shield casts (key + right click + back to the meteor).</summary>
    public int Shields { get; private set; }

    /// <summary>Searches that found no monster in reach.</summary>
    public int EmptySearches { get; private set; }

    public string Summary => $"meteor cast {Casts}x, shield {Shields}x, searches without a monster in reach {EmptySearches}x";

    /// <param name="frame">This tick's screenshot: tells whether the cursor is still on the last monster.</param>
    public void Tick(PlayerBars bars, Mat frame, CancellationToken cancel)
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

        if (_aim is not { } aim || _cursor.Detect(frame, aim).Kind != CursorKind.Enemy)
        {
            // Hover only: the right click comes once the gem is red (on the ground it would walk).
            _input.ForceReleaseButtons();
            var result = _finder.Find(cancel);
            if (result.Outcome == FindOutcome.Aborted) return; // paused / focus lost: the runner takes over
            if (result.Outcome != FindOutcome.Found)
            {
                _aim = null;
                EmptySearches++;
                _nextCast = now + TimeSpan.FromMilliseconds(_config.SearchRetryMs);
                if (!_noMonsterLogged)
                    _log.Information("Meteor: no monster in reach, searching again every {Ms} ms", _config.SearchRetryMs);
                _noMonsterLogged = true;
                return;
            }
            _noMonsterLogged = false;
            aim = result.Target!.Point; // the finder leaves the mouse on it, gem confirmed red
            _aim = aim;
        }
        else if (!_input.MoveMouse(aim))
        {
            return;
        }

        _nextCast = now + NextInterval();
        if (!_input.Click(MouseButton.Right)) return; // refused (game not focused): the runner pauses
        Casts++;
        if (Casts == 1 || Casts % 100 == 0)
            _log.Information("Meteor cast #{Casts} on the monster at {Point} ({Summary})", Casts, aim, Summary);
        else
            _log.Debug("Meteor cast #{Casts} on {Point}", Casts, aim);
    }

    /// <summary>Shield key, right click near the middle, back to the meteor key. A refused step is retried next tick.</summary>
    private void CastShield(DateTimeOffset now)
    {
        if (!_input.PressKey(_shieldKey) || !_input.MoveMouse(Wander()) || !_input.Click(MouseButton.Right))
            return;
        Shields++;
        _aim = null; // the mouse left the monster
        _nextShield = now + TimeSpan.FromSeconds(_config.ShieldEverySeconds);
        if (!_input.PressKey(_meteorKey))
            _nextShield = now; // the meteor key didn't go out: do the whole switch again
        _nextCast = now + NextInterval();
        _log.Information("Shield #{Count} ({Shield} + right click), back to the meteor ({Meteor})", Shields, _shieldKey, _meteorKey);
    }

    /// <summary>Random time until the next meteor, from IntervalMs to IntervalMaxMs.</summary>
    private TimeSpan NextInterval() =>
        TimeSpan.FromMilliseconds(_config.IntervalMs + _random.NextDouble() * (_config.IntervalMaxMs - _config.IntervalMs));

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
