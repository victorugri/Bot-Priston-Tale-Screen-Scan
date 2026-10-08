using Serilog.Core;
using Serilog.Events;

namespace BotPriston.Ui;

/// <summary>Forwards log lines (Information and above) to the UI. The file log keeps everything.</summary>
public sealed class UiLogSink(Action<string> publish) : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Information) return;
        var level = logEvent.Level switch
        {
            LogEventLevel.Warning => "AVISO ",
            LogEventLevel.Error or LogEventLevel.Fatal => "ERRO ",
            _ => "",
        };
        publish($"{logEvent.Timestamp:HH:mm:ss}  {level}{logEvent.RenderMessage()}");
    }
}
