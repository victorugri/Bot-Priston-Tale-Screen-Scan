using BotPriston.Core.Config;
using BotPriston.Core.Input;
using BotPriston.Core.Targeting;
using BotPriston.Core.Vision;
using Serilog;

namespace BotPriston.Core.Bot;

/// <summary>
/// Earthquake mode (<see cref="EarthquakeConfig"/>): a right click in the middle of the screen every
/// <see cref="EarthquakeConfig.IntervalMs"/>. No target to find: the skill hits everything around.
/// The mouse is put back on the point before every click (a restock or a left attack moves it).
/// Every <see cref="EarthquakeConfig.LeftAttackEverySeconds"/> a monster in reach gets one left click.
/// </summary>
public sealed class EarthquakeCaster
{
    private readonly EarthquakeConfig _config;
    private readonly IInputSink _input;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly ITargetFinder? _finder;
    private DateTimeOffset _next = DateTimeOffset.MinValue;
    private DateTimeOffset _nextLeftAttack;
    private bool _lowMpLogged;

    /// <param name="finder">Finds the monster for the periodic left attack; null = no left attack.</param>
    public EarthquakeCaster(EarthquakeConfig config, IInputSink input, TimeProvider time, ILogger log, ITargetFinder? finder = null)
    {
        _config = config;
        _input = input;
        _time = time;
        _log = log;
        _finder = finder;
        _nextLeftAttack = time.GetUtcNow() + TimeSpan.FromSeconds(config.LeftAttackEverySeconds);
    }

    /// <summary>Right clicks sent.</summary>
    public int Casts { get; private set; }

    /// <summary>Periodic left clicks on a monster.</summary>
    public int LeftAttacks { get; private set; }

    public string Summary => $"earthquake cast {Casts}x, left attacks {LeftAttacks}x";

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

        if (!_input.MoveMouse(_config.Point) || !_input.Click(MouseButton.Right))
            return; // refused (game not focused): the runner pauses

        Casts++;
        _next = now + TimeSpan.FromMilliseconds(_config.IntervalMs);
        if (Casts == 1 || Casts % 100 == 0)
            _log.Information("Earthquake cast #{Casts} (right click at {Point})", Casts, _config.Point);
        else
            _log.Debug("Earthquake cast #{Casts}", Casts);
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
