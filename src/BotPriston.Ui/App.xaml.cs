using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using BotPriston.Core.Config;
using Serilog;

namespace BotPriston.Ui;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Fatal(args.Exception, "UI error");
            MessageBox.Show(args.Exception.Message, "BotPriston: erro", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Numbers typed in the UI follow the Windows language (e.g. 1,5 in Portuguese).
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        // botconfig.json is found next to the exe or in a parent folder (the repository when started from bin/).
        // Relative paths in it (templates, samples, logs) are relative to its folder.
        var configPath = ConfigLoader.Resolve(null);
        if (!File.Exists(configPath))
        {
            MessageBox.Show($"botconfig.json não encontrado.\nProcurado em: {configPath}", "BotPriston",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        Environment.CurrentDirectory = Path.GetDirectoryName(configPath)!;

        BotConfig config;
        try
        {
            config = ConfigStore.Load(configPath);
        }
        catch (ConfigException ex)
        {
            MessageBox.Show(ex.Message, "BotPriston: configuração inválida", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _viewModel = new MainViewModel(configPath, config);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(Path.Combine(Path.GetFullPath(config.Paths.Logs), "botpriston-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Sink(new UiLogSink(_viewModel.AddLogLine))
            .CreateLogger();
        _viewModel.Attach(Log.Logger);

        var window = new MainWindow { DataContext = _viewModel };
        window.Closing += (_, _) => _viewModel.Dispose();
        window.Show();
        Log.Information("BotPriston UI started (config {Config})", configPath);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
