using BotPriston.Core.Geometry;
using BotPriston.Core.Vision;

namespace BotPriston.Core.Targeting;

/// <param name="Point">Client-area point where the monster was hovered (the cursor is left there).</param>
/// <param name="Cursor">The cursor reading that confirmed the hit.</param>
/// <param name="Panel">The target panel at that moment (name/HP of the hovered monster, informative only).</param>
/// <param name="Probes">Mouse positions tried.</param>
public sealed record TargetFound(PixelPoint Point, CursorReading Cursor, TargetPanel Panel, int Probes, TimeSpan Elapsed);

public enum FindOutcome
{
    Found,
    /// <summary>The whole search area was probed without a hit.</summary>
    NothingFound,
    /// <summary>Input was refused (game lost focus) or the search was cancelled.</summary>
    Aborted,
}

public sealed record FindResult(FindOutcome Outcome, TargetFound? Target, int Probes, TimeSpan Elapsed, string? Reason = null);

/// <summary>
/// Locates a monster to attack and leaves the mouse over it. Strategies are interchangeable;
/// the current one sweeps the mouse and watches the target panel (HoverTargetFinder).
/// </summary>
public interface ITargetFinder
{
    /// <summary>
    /// Searches the whole area, nearest to the character first; or, with <paramref name="near"/>, only
    /// within <paramref name="maxDistance"/> of that point, nearest to it first (re-acquiring a monster that moved).
    /// </summary>
    FindResult Find(CancellationToken cancel, PixelPoint? near = null, int maxDistance = 0);
}
