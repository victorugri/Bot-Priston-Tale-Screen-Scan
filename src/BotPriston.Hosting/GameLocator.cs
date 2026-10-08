using BotPriston.Core.Config;
using BotPriston.Platform.Window;
using Serilog;

namespace BotPriston.Hosting;

/// <summary>What is known about the game window right now (for status displays; never throws).</summary>
public sealed record GameStatus(GameWindow? Window, string Problem)
{
    public bool Found => Window is not null;
    public bool Ready => Window is not null && Problem.Length == 0;
}

public static class GameLocator
{
    /// <summary>
    /// Finds the game window or throws with a helpful message. Callers that act on the game pass
    /// <paramref name="requireExpectedSize"/>: with any other client size every ROI is wrong.
    /// </summary>
    public static GameWindow Find(BotConfig config, ILogger log, bool requireExpectedSize)
    {
        var matches = WindowFinder.FindMatches(config.Window);
        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Game window not found (ProcessName='{config.Window.ProcessName}', TitleContains='{config.Window.TitleContains}'). " +
                "Is the game running? Run the 'windows' command to see candidates and fix the Window section of the config.");
        }

        var window = matches[0];
        if (matches.Count > 1)
        {
            log.Warning("{Count} windows match the filters; using the largest: {Window}", matches.Count, window);
            foreach (var other in matches.Skip(1))
                log.Warning("  also matched: {Window}", other);
        }

        if (window.IsMinimized)
            throw new InvalidOperationException($"Game window {window} is minimized. Restore it and try again.");

        var client = window.ClientScreenRect;
        log.Information("Game window: {Window}, client {W}x{H} at screen ({X},{Y})",
            window, client.Width, client.Height, client.X, client.Y);

        if (SizeProblem(window, config) is { } problem)
        {
            if (requireExpectedSize)
                throw new InvalidOperationException(problem);
            log.Warning("{Message}", problem);
        }

        return window;
    }

    /// <summary>Quiet check for status displays: is the game there, restored and at the expected size?</summary>
    public static GameStatus Check(BotConfig config)
    {
        var window = WindowFinder.FindMatches(config.Window).FirstOrDefault();
        if (window is null) return new GameStatus(null, "game window not found");
        if (window.IsMinimized) return new GameStatus(window, "game window is minimized");
        return new GameStatus(window, SizeProblem(window, config) ?? "");
    }

    private static string? SizeProblem(GameWindow window, BotConfig config)
    {
        var (w, h) = window.ClientSize;
        if (w == config.Window.ExpectedClientWidth && h == config.Window.ExpectedClientHeight)
            return null;
        return $"Client area is {w}x{h} but {config.Window.ExpectedClientWidth}x{config.Window.ExpectedClientHeight} " +
               "is expected, so every screen position the bot uses is wrong. The window was probably resized or snapped; " +
               "use 'layout' (as administrator) to restore it.";
    }
}
