using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using BotPriston.Core.Bot;
using BotPriston.Core.Config;
using BotPriston.Hosting;
using BotPriston.Platform.Window;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BotPriston.Ui;

/// <summary>
/// The whole UI: day-to-day settings (saved to settings.json on top of botconfig.json),
/// Play/Pause/Stop of a <see cref="BotSession"/>, live status, session stats and the log.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxLogLines = 400;

    private readonly string _configPath;
    private readonly DispatcherTimer _timer;
    private ILogger _log = Serilog.Core.Logger.None;
    private BotSession? _session;
    private DateTime _nextGameCheck = DateTime.MinValue;

    public MainViewModel(string configPath, BotConfig config)
    {
        _configPath = configPath;
        Config = config;
        IsAdmin = ProcessElevation.CurrentIsElevated;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    public void Attach(ILogger log) => _log = log;

    public ObservableCollection<string> LogLines { get; } = [];

    // ---------------------------------------------------------------- settings

    /// <summary>Working copy edited by the settings tab; saved to settings.json before every Play.</summary>
    [ObservableProperty]
    public partial BotConfig Config { get; set; }

    [ObservableProperty]
    public partial string SettingsMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool SettingsError { get; set; }

    /// <summary>Save every frame of the session (diagnostics; ~3 MB/s).</summary>
    [ObservableProperty]
    public partial bool Record { get; set; }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Save() => TrySave(out _);

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void ResetDefaults()
    {
        if (MessageBox.Show("Voltar todos os ajustes para os padrões do botconfig.json?", "BotPriston",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        ConfigStore.ResetToDefaults(_configPath);
        Config = ConfigStore.Load(_configPath);
        ShowSettingsMessage("Ajustes voltaram aos padrões.", error: false);
    }

    private bool TrySave(out BotConfig saved)
    {
        saved = Config;
        try
        {
            ConfigStore.Save(_configPath, Config);
            saved = ConfigStore.Load(_configPath);
            ShowSettingsMessage(File.Exists(ConfigStore.SettingsPathFor(_configPath))
                ? $"Salvo em {ConfigStore.SettingsFileName} ({DateTime.Now:HH:mm:ss})."
                : "Tudo igual aos padrões do botconfig.json.", error: false);
            return true;
        }
        catch (ConfigException ex)
        {
            ShowSettingsMessage(ex.Message, error: true);
            return false;
        }
    }

    private void ShowSettingsMessage(string text, bool error)
    {
        SettingsMessage = text;
        SettingsError = error;
    }

    // ---------------------------------------------------------------- bot control

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(PauseCommand), nameof(StopCommand), nameof(SaveCommand),
        nameof(ResetDefaultsCommand), nameof(FixWindowCommand))]
    public partial bool IsRunning { get; set; }

    public bool CanEdit => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task PlayAsync()
    {
        if (!TrySave(out var config))
        {
            StatusText = "Ajustes inválidos: veja a aba Ajustes";
            return;
        }

        // Starting takes a couple of seconds (capture setup): do it off the UI thread so the window stays responsive.
        IsRunning = true;
        LastStopReason = "";
        StatusText = "Iniciando...";
        BotSession session;
        try
        {
            session = await Task.Run(() =>
                BotSession.Start(config, _log, new BotSessionOptions { Record = Record, BringGameToFront = true }));
        }
        catch (Exception ex)
        {
            _log.Error("Could not start: {Message}", ex.Message);
            IsRunning = false;
            StatusText = "Parado";
            LastStopReason = "Não iniciou: " + ex.Message;
            return;
        }

        _session = session;
        session.Exited += s => Application.Current.Dispatcher.BeginInvoke(() => OnSessionExited(s));
        if (!session.IsRunning) OnSessionExited(session); // ended before we subscribed
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Pause() => _session?.TogglePause();

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop() => _session?.Stop("stopped from the UI");

    private void OnSessionExited(BotSession session)
    {
        if (!ReferenceEquals(_session, session)) return; // already handled
        LastStopReason = $"Parou: {Translate(session.Runner.StopReason)}";
        session.Dispose();
        _session = null;
        IsRunning = false;
        _nextGameCheck = DateTime.MinValue;
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void FixWindow()
    {
        var status = GameLocator.Check(Config);
        if (status.Window is null)
        {
            StatusText = "Jogo não encontrado";
            return;
        }
        var (ok, message) = WindowLayout.Apply(status.Window, Config.Window.ExpectedClientWidth, Config.Window.ExpectedClientHeight,
            WindowAnchor.TopRight);
        _log.Information("{Message}", message);
        _nextGameCheck = DateTime.MinValue;
        if (!ok) StatusText = "Não consegui ajustar a janela (precisa rodar como administrador?)";
    }

    [RelayCommand]
    private void RestartAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The user said no to the Windows prompt.
        }
    }

    // ---------------------------------------------------------------- status and stats

    [ObservableProperty] public partial bool IsAdmin { get; set; }
    [ObservableProperty] public partial bool GameReady { get; set; }
    [ObservableProperty] public partial string GameText { get; set; } = "Procurando o jogo...";
    [ObservableProperty] public partial string StatusText { get; set; } = "Parado";
    [ObservableProperty] public partial string ActivityText { get; set; } = "";
    [ObservableProperty] public partial string LastStopReason { get; set; } = "";
    [ObservableProperty] public partial string Elapsed { get; set; } = "0:00";
    [ObservableProperty] public partial int Kills { get; set; }
    [ObservableProperty] public partial string AverageKill { get; set; } = "-";
    [ObservableProperty] public partial int RightSkillCasts { get; set; }
    [ObservableProperty] public partial int GivenUp { get; set; }
    [ObservableProperty] public partial int WalkStops { get; set; }
    [ObservableProperty] public partial int DistantAttackers { get; set; }
    [ObservableProperty] public partial double Hp { get; set; }
    [ObservableProperty] public partial double Mp { get; set; }
    [ObservableProperty] public partial double Stm { get; set; }

    private void Refresh()
    {
        if (_session is { } session)
        {
            var runner = session.Runner;
            StatusText = runner.State switch
            {
                RunnerState.Starting => "Iniciando...",
                RunnerState.Running => "Rodando",
                RunnerState.Paused => "Pausado (F12 retoma)",
                RunnerState.FocusLost => "Pausado: clique no jogo",
                RunnerState.WrongWindowSize => "Pausado: janela fora do tamanho",
                RunnerState.HudHidden => "Interface do jogo não visível",
                _ => "Parando...",
            };
            ActivityText = runner.Brain?.State switch
            {
                BrainState.Recover => runner.State == RunnerState.Running ? "Recuperando" : "",
                BrainState.SearchTarget => "Procurando alvo",
                BrainState.Engage or BrainState.Attack => runner.Brain.AllowWalking ? "Atacando atacante distante" : "Atacando",
                _ => "",
            };
            if (runner.Brain is { } brain)
            {
                Kills = brain.Kills;
                AverageKill = brain.Kills == 0 ? "-" : $"{brain.AverageKillSeconds:F1} s";
                RightSkillCasts = brain.RightSkillUses;
                GivenUp = brain.GivenUp;
                WalkStops = brain.WalkStops;
                DistantAttackers = brain.WideTargets;
            }
            if (runner.LastSnapshot?.Bars is { } bars)
            {
                Hp = bars.Hp.Percent;
                Mp = bars.Mp.Percent;
                Stm = bars.Stm.Percent;
            }
            var elapsed = DateTimeOffset.Now - session.StartedAt;
            Elapsed = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"m\:ss");
            GameReady = true;
            GameText = "Jogo encontrado (1600x900)";
            return;
        }

        if (IsRunning) return; // starting: the session is being built in the background

        StatusText = "Parado";
        ActivityText = "";
        if (DateTime.Now < _nextGameCheck) return;
        _nextGameCheck = DateTime.Now.AddSeconds(2);

        var status = GameLocator.Check(Config);
        GameReady = status.Ready;
        GameText = status.Window is null ? "Jogo não encontrado"
            : status.Window.IsMinimized ? "Jogo minimizado"
            : status.Ready ? $"Jogo encontrado ({Config.Window.ExpectedClientWidth}x{Config.Window.ExpectedClientHeight})"
            : $"Janela do jogo em {status.Window.ClientSize.Width}x{status.Window.ClientSize.Height}: use \"Ajustar janela\"";
    }

    public void AddLogLine(string line)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            LogLines.Add(line);
            while (LogLines.Count > MaxLogLines) LogLines.RemoveAt(0);
        });
    }

    private static string Translate(string? reason) => reason switch
    {
        null => "?",
        "quit hotkey" => "tecla de encerrar (Ctrl+F12)",
        "stopped from the UI" => "botão Parar",
        "game window closed" => "o jogo foi fechado",
        var r when r.StartsWith("no progress") => "sem progresso por muito tempo (nada para atacar?)",
        var r when r.Contains("character died") => "personagem morreu",
        var r when r.StartsWith("HUD not visible") => "interface do jogo sumiu (morte, desconexão ou janela aberta)",
        var r => r,
    };

    public void Dispose()
    {
        _timer.Stop();
        if (_session is { } session)
        {
            session.Stop("UI closed");
            session.Wait();
            session.Dispose();
        }
    }
}
