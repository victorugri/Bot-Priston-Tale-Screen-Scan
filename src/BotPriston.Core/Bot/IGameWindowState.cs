namespace BotPriston.Core.Bot;

/// <summary>What the bot needs to know about the game window to decide whether it may act.</summary>
public interface IGameWindowState
{
    bool Exists { get; }
    bool IsMinimized { get; }
    bool IsForeground { get; }

    /// <summary>Current client-area size; every ROI is only valid at the configured size.</summary>
    (int Width, int Height) ClientSize { get; }
}
