using BotPriston.Core.Config;
using BotPriston.Core.Input;
using BotPriston.Core.Vision;
using Serilog;

namespace BotPriston.Core.Bot;

/// <summary>
/// Earthquake mode (<see cref="EarthquakeConfig"/>): a right click in the middle of the screen every
/// <see cref="EarthquakeConfig.IntervalMs"/>. No target to find: the skill hits everything around.
/// The mouse is put back on the point before every click (a restock moves it to the inventory).
/// </summary>
public sealed class EarthquakeCaster(EarthquakeConfig config, IInputSink input, TimeProvider time, ILogger log)
{
    private DateTimeOffset _next = DateTimeOffset.MinValue;
    private bool _lowMpLogged;

    /// <summary>Right clicks sent.</summary>
    public int Casts { get; private set; }

    public string Summary => $"earthquake cast {Casts}x";

    public void Tick(PlayerBars bars)
    {
        var now = time.GetUtcNow();
        if (now < _next) return;

        if (bars.Mp.Percent < config.MinMpPercent)
        {
            if (!_lowMpLogged)
                log.Information("Earthquake on hold: MP {Mp:F0}% < {Min}%", bars.Mp.Percent, config.MinMpPercent);
            _lowMpLogged = true;
            return;
        }
        _lowMpLogged = false;

        if (!input.MoveMouse(config.Point) || !input.Click(MouseButton.Right))
            return; // refused (game not focused): the runner pauses

        Casts++;
        _next = now + TimeSpan.FromMilliseconds(config.IntervalMs);
        if (Casts == 1 || Casts % 100 == 0)
            log.Information("Earthquake cast #{Casts} (right click at {Point})", Casts, config.Point);
        else
            log.Debug("Earthquake cast #{Casts}", Casts);
    }
}
