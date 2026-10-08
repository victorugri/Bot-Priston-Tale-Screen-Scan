namespace BotPriston.Core.Bot;

/// <summary>
/// User-facing switches shared between the hotkey thread and the bot loop. Thread-safe.
/// <see cref="Interrupt"/> is cancelled on pause and quit so long operations (a hover sweep)
/// stop immediately instead of at the next tick.
/// </summary>
public sealed class BotControl
{
    private readonly object _gate = new();
    private CancellationTokenSource _interrupt = new();
    private bool _paused;
    private bool _quitRequested;
    private string _quitReason = "quit hotkey";

    public bool Paused { get { lock (_gate) return _paused; } }
    public bool QuitRequested { get { lock (_gate) return _quitRequested; } }

    /// <summary>Why quitting was requested (shown as the bot's stop reason).</summary>
    public string QuitReason { get { lock (_gate) return _quitReason; } }
    public CancellationToken Interrupt { get { lock (_gate) return _interrupt.Token; } }

    /// <summary>Returns the new paused state.</summary>
    public bool TogglePause()
    {
        lock (_gate)
        {
            SetPausedLocked(!_paused);
            return _paused;
        }
    }

    public void SetPaused(bool paused)
    {
        lock (_gate) SetPausedLocked(paused);
    }

    public void RequestQuit(string reason = "quit hotkey")
    {
        lock (_gate)
        {
            _quitRequested = true;
            _quitReason = reason;
            _interrupt.Cancel();
        }
    }

    private void SetPausedLocked(bool paused)
    {
        _paused = paused;
        if (paused)
        {
            _interrupt.Cancel();
        }
        else if (_interrupt.IsCancellationRequested && !_quitRequested)
        {
            _interrupt.Dispose();
            _interrupt = new CancellationTokenSource();
        }
    }
}
