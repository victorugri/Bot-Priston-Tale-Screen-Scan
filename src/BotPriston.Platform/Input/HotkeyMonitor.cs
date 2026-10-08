using BotPriston.Core.Bot;
using BotPriston.Core.Input;
using Serilog;

namespace BotPriston.Platform.Input;

/// <summary>
/// Background thread that polls the physical keyboard for the bot's own hotkeys. Polling
/// (instead of RegisterHotKey) works whatever window has focus and also for F12, which
/// Windows reserves for debuggers in RegisterHotKey.
/// </summary>
public sealed class HotkeyMonitor : IDisposable
{
    private readonly BotControl _control;
    private readonly ILogger _log;
    private readonly KeyPressDetector _pause;
    private readonly KeyPressDetector _quit;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;

    public HotkeyMonitor(BotControl control, KeyChord pauseResume, KeyChord quit, ILogger log)
    {
        _control = control;
        _log = log;
        _pause = new KeyPressDetector(pauseResume);
        _quit = new KeyPressDetector(quit);
        _thread = new Thread(Loop) { IsBackground = true, Name = "HotkeyMonitor" };
        _thread.Start();
    }

    private void Loop()
    {
        while (!_stop.IsCancellationRequested)
        {
            // Check quit first: Ctrl+F12 must not also count as F12 (KeyboardState requires exact modifiers).
            if (_quit.Pressed())
            {
                _log.Warning("Quit hotkey {Key} pressed", _quit.Chord);
                _control.RequestQuit();
            }
            else if (_pause.Pressed())
            {
                bool paused = _control.TogglePause();
                _log.Warning("{Key}: {State}", _pause.Chord, paused ? "PAUSED" : "RESUMED");
                Console.Beep(paused ? 600 : 1000, 80);
            }

            _stop.Token.WaitHandle.WaitOne(20);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread.Join(500);
        _stop.Dispose();
    }
}
