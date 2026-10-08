namespace BotPriston.Core.Bot;

/// <summary>Expires when <see cref="Kick"/> hasn't been called for <see cref="Timeout"/>.</summary>
public sealed class Watchdog(TimeSpan timeout, TimeProvider time)
{
    private DateTimeOffset _lastKick = time.GetUtcNow();

    public TimeSpan Timeout { get; } = timeout;
    public TimeSpan SinceLastKick => time.GetUtcNow() - _lastKick;
    public bool Expired => SinceLastKick >= Timeout;

    public void Kick() => _lastKick = time.GetUtcNow();
}
