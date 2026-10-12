using BotPriston.Core.Config;
using BotPriston.Core.Geometry;
using BotPriston.Core.Input;
using BotPriston.Core.Targeting;
using BotPriston.Core.Vision;
using OpenCvSharp;
using Serilog;

namespace BotPriston.Core.Bot;

/// <summary>
/// Meteor mode (<see cref="MeteorConfig"/>).
/// <para>Meteor: every 2-3 s (random, <see cref="MeteorConfig.IntervalMs"/> to <see cref="MeteorConfig.IntervalMaxMs"/>)
/// a right click on a monster: on the same spot while the cursor gem is still red there, otherwise after a hover
/// sweep (no click) finds one within attack reach. The search also runs while the meteor recharges, so the cursor
/// is already on a monster when it is ready. A right click is never sent with the gem not red.</para>
/// <para>Shield: first thing, every <see cref="MeteorConfig.ShieldEverySeconds"/>, and as soon as its buff icon is
/// gone: the shield key, right clicks near the middle of the screen until its icon shows (a click during the
/// meteor animation is ignored), then the meteor key again.</para>
/// </summary>
public sealed class MeteorCaster
{
    private const int MissingTicksToRecast = 2; // the icon must be gone this many ticks in a row (a flicker isn't enough)
    private const int ShieldFailedRetrySeconds = 3;

    private readonly MeteorConfig _config;
    private readonly IInputSink _input;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly ITargetFinder _finder;
    private readonly ICursorReader _cursor;
    private readonly Func<Mat, bool>? _shieldOn;
    private readonly Random _random;
    private readonly KeyChord _meteorKey, _shieldKey;
    private DateTimeOffset _nextCast = DateTimeOffset.MinValue;
    private DateTimeOffset _nextShield = DateTimeOffset.MinValue; // the shield goes up first
    private DateTimeOffset _shieldRetryAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextSearch = DateTimeOffset.MinValue;
    private DateTimeOffset _lastMeteorClick = DateTimeOffset.MinValue;
    private DateTimeOffset _nextShieldClick;
    private PixelPoint? _aim;
    private bool _shieldInProgress;     // between the shield key and the meteor key
    private int _shieldClicks;          // right clicks sent in this shield cast
    private bool _shieldWasOn;          // the icon was already there when this cast started (a refresh)
    private int _shieldMissingTicks;
    private bool _needMeteorKey;        // interrupted mid-shield: the shield may still be selected
    private bool _lowMpLogged, _noMonsterLogged;

    /// <param name="shieldOn">Reads the shield's buff icon in a frame; null = don't verify (timer only).</param>
    /// <param name="random">Source of the cast intervals and the shield click spots (fixed seed in tests).</param>
    public MeteorCaster(MeteorConfig config, IInputSink input, TimeProvider time, ILogger log,
        ITargetFinder finder, ICursorReader cursor, Func<Mat, bool>? shieldOn = null, Random? random = null)
    {
        _config = config;
        _input = input;
        _time = time;
        _log = log;
        _finder = finder;
        _cursor = cursor;
        _shieldOn = shieldOn;
        _random = random ?? Random.Shared;
        _meteorKey = KeyChord.Parse(config.MeteorKey);
        _shieldKey = KeyChord.Parse(config.ShieldKey);
    }

    /// <summary>Meteor right clicks sent (each one on a monster).</summary>
    public int Casts { get; private set; }

    /// <summary>Shield casts done (confirmed by its icon when verifying).</summary>
    public int Shields { get; private set; }

    /// <summary>Shield casts whose icon never showed after <see cref="MeteorConfig.ShieldMaxClicks"/> clicks.</summary>
    public int ShieldFailures { get; private set; }

    /// <summary>Times the shield icon was gone and the shield was cast again before its timer.</summary>
    public int ShieldLost { get; private set; }

    /// <summary>Where the cursor is kept: the monster the next meteor goes to.</summary>
    public PixelPoint? Aim => _aim;

    /// <summary>Searches that found no monster in reach.</summary>
    public int EmptySearches { get; private set; }

    public string Summary =>
        $"meteor cast {Casts}x, shield {Shields}x (recast after it vanished {ShieldLost}x, failed attempts {ShieldFailures}x), " +
        $"searches without a monster in reach {EmptySearches}x";

    /// <summary>The runner stopped acting (pause, focus...): a shield cast in progress is dropped.</summary>
    public void Suspend()
    {
        if (_shieldInProgress) _needMeteorKey = true;
        _shieldInProgress = false;
        _aim = null;
    }

    /// <param name="frame">This tick's screenshot: the cursor gem under the aim, and the shield icon.</param>
    public void Tick(PlayerBars bars, Mat frame, CancellationToken cancel)
    {
        var now = _time.GetUtcNow();
        bool lowMp = bars.Mp.Percent < _config.MinMpPercent;
        if (lowMp != _lowMpLogged)
            _log.Information(lowMp ? "Meteor on hold: MP {Mp:F0}% < {Min}%" : "Meteor resumed: MP {Mp:F0}%", bars.Mp.Percent, _config.MinMpPercent);
        _lowMpLogged = lowMp;

        if (_needMeteorKey)
        {
            if (!_input.PressKey(_meteorKey)) return;
            _needMeteorKey = false;
        }

        bool? shieldOn = _shieldOn?.Invoke(frame);
        if (_shieldInProgress)
        {
            ContinueShield(now, shieldOn);
            return;
        }

        _shieldMissingTicks = shieldOn == false ? _shieldMissingTicks + 1 : 0;
        bool shieldGone = _shieldMissingTicks >= MissingTicksToRecast;
        if (!lowMp && now >= _shieldRetryAt && (now >= _nextShield || shieldGone))
        {
            // Not during the meteor's animation: the game would ignore the click.
            if (now - _lastMeteorClick < TimeSpan.FromMilliseconds(_config.ShieldAfterMeteorMs)) return;
            if (shieldGone && now < _nextShield)
            {
                ShieldLost++;
                _log.Warning("Shield icon is gone: casting the shield again");
            }
            StartShield(now, shieldOn == true);
            return;
        }

        // Keep the cursor on a monster all the time, also while the meteor recharges: when it is ready,
        // the right click goes out at once. Hover only; a right click on the ground would walk.
        if (_aim is not { } aim || _cursor.Detect(frame, aim).Kind != CursorKind.Enemy)
        {
            if (now < _nextSearch) return;
            _input.ForceReleaseButtons();
            var result = _finder.Find(cancel);
            if (result.Outcome == FindOutcome.Aborted) return; // paused / focus lost: the runner takes over
            if (result.Outcome != FindOutcome.Found)
            {
                _aim = null;
                EmptySearches++;
                _nextSearch = _time.GetUtcNow() + TimeSpan.FromMilliseconds(_config.SearchRetryMs);
                if (!_noMonsterLogged)
                    _log.Information("Meteor: no monster in reach, searching again every {Ms} ms", _config.SearchRetryMs);
                _noMonsterLogged = true;
                return;
            }
            _noMonsterLogged = false;
            aim = result.Target!.Point; // the finder leaves the mouse on it, gem confirmed red
            _aim = aim;
            now = _time.GetUtcNow();    // the search took a while
        }

        if (now < _nextCast || lowMp) return; // aimed, waiting for the meteor to recharge

        _nextCast = now + NextInterval();
        if (!_input.MoveMouse(aim) || !_input.Click(MouseButton.Right)) return; // refused: the runner pauses
        _lastMeteorClick = now;
        Casts++;
        if (Casts == 1 || Casts % 100 == 0)
            _log.Information("Meteor cast #{Casts} on the monster at {Point} ({Summary})", Casts, aim, Summary);
        else
            _log.Debug("Meteor cast #{Casts} on {Point}", Casts, aim);
    }

    private void StartShield(DateTimeOffset now, bool alreadyOn)
    {
        if (!_input.PressKey(_shieldKey)) return;
        _aim = null; // the mouse leaves the monster
        _shieldWasOn = alreadyOn;
        _shieldClicks = 0;
        _shieldInProgress = true;
        // Nothing to wait for without the icon to check, or when it was already on (a refresh).
        if (ClickShield(now) && (_shieldOn is null || alreadyOn))
            EndShield(now, ok: true, confirmed: false);
    }

    /// <summary>One right click for the shield. False when refused (game not focused): retried next tick.</summary>
    private bool ClickShield(DateTimeOffset now)
    {
        _nextShieldClick = now + TimeSpan.FromMilliseconds(_config.ShieldRetryMs);
        if (!_input.MoveMouse(Wander()) || !_input.Click(MouseButton.Right))
        {
            _nextShieldClick = now;
            return false;
        }
        _shieldClicks++;
        return true;
    }

    /// <summary>After the shield key: click until its icon shows (or give up), then back to the meteor.</summary>
    private void ContinueShield(DateTimeOffset now, bool? shieldOn)
    {
        // Without the icon to check, or when it was already on (a refresh), one click is all we can do
        // (only reached when that click was refused).
        if (_shieldOn is null || _shieldWasOn)
        {
            if (ClickShield(now)) EndShield(now, ok: true, confirmed: false);
            return;
        }
        if (shieldOn == true)
        {
            EndShield(now, ok: true, confirmed: true);
            return;
        }
        if (now < _nextShieldClick) return;
        if (_shieldClicks >= _config.ShieldMaxClicks)
        {
            EndShield(now, ok: false, confirmed: false);
            return;
        }
        ClickShield(now);
    }

    private void EndShield(DateTimeOffset now, bool ok, bool confirmed)
    {
        int clicks = _shieldClicks;
        _shieldInProgress = false;
        _shieldMissingTicks = 0;
        _needMeteorKey = !_input.PressKey(_meteorKey);
        _nextCast = DateTimeOffset.MinValue; // the meteor goes out as soon as a monster is under the cursor

        if (ok)
        {
            Shields++;
            _nextShield = now + TimeSpan.FromSeconds(_config.ShieldEverySeconds);
            _log.Information("Shield #{Count} {How} ({Clicks} click(s)), back to the meteor ({Meteor})",
                Shields, confirmed ? "is up: its icon shows" : (_shieldOn is null ? "cast (not verified)" : "refreshed"), clicks, _meteorKey);
        }
        else
        {
            ShieldFailures++;
            _shieldRetryAt = now + TimeSpan.FromSeconds(ShieldFailedRetrySeconds);
            _log.Warning("Shield icon did not show after {Clicks} clicks: back to the meteor, trying again in {Seconds}s",
                clicks, ShieldFailedRetrySeconds);
        }
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
