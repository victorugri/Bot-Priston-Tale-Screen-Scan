using BotPriston.App;
using BotPriston.App.Commands;
using BotPriston.Core.Config;
using Serilog;
using Serilog.Events;

return Run(args);

static int Run(string[] args)
{
    CommandLine cmd;
    try
    {
        cmd = new CommandLine(args);
    }
    catch (UsageException ex)
    {
        Console.Error.WriteLine(ex.Message);
        PrintHelp();
        return 2;
    }

    if (cmd.Command is "help" || cmd.Flag("help", "h"))
    {
        PrintHelp();
        return 0;
    }

    bool verbose = cmd.Flag("verbose", "v");
    var configPath = ConfigLoader.Resolve(cmd.String("config"));

    BotConfig config;
    try
    {
        // botconfig.json (defaults) + settings.json (what the UI changed), if present.
        config = ConfigStore.Load(configPath);
    }
    catch (ConfigException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    var logDir = Path.GetFullPath(config.Paths.Logs);
    Directory.CreateDirectory(logDir);
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console(
            restrictedToMinimumLevel: verbose ? LogEventLevel.Debug : LogEventLevel.Information,
            outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(Path.Combine(logDir, "botpriston-.log"),
            rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    using var cancel = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cancel.Cancel();
    };

    try
    {
        Log.Debug("Command '{Command}' with config {Config}", cmd.Command, configPath);
        var ctx = new BotContext(config, configPath, Log.Logger);
        return cmd.Command switch
        {
            "windows" => WindowsCommand.Run(ctx, cmd),
            "info" => InfoCommand.Run(ctx, cmd),
            "capture" => CaptureCommand.Run(ctx, cmd, cancel.Token),
            "debug" => DebugCommand.Run(ctx, cmd, cancel.Token),
            "detect" => DetectCommand.Run(ctx, cmd),
            "crop" => CropCommand.Run(ctx, cmd),
            "run" => RunCommand.Run(ctx, cmd, cancel.Token),
            "press" => InputTestCommands.Press(ctx, cmd, cancel.Token),
            "mouse" => InputTestCommands.Mouse(ctx, cmd, cancel.Token),
            "layout" => LayoutCommand.Run(ctx, cmd),
            "motion" => MotionCommand.Run(ctx, cmd),
            "probe" => TargetingCommands.Probe(ctx, cmd, cancel.Token),
            "find" => TargetingCommands.Find(ctx, cmd, cancel.Token),
            _ => throw new UsageException($"Unknown command '{cmd.Command}'."),
        };
    }
    catch (UsageException ex)
    {
        Console.Error.WriteLine(ex.Message);
        PrintHelp();
        return 2;
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "{Message}", ex.Message);
        return 1;
    }
    finally
    {
        Log.CloseAndFlush();
    }
}

static void PrintHelp() => Console.WriteLine("""
    BotPriston - screen-reading farm bot for Priston Tale

    Usage: BotPriston <command> [options]

    Commands:
      windows                 List visible windows (to configure Window.ProcessName / TitleContains)
      layout                  Restore the game's client size and align it to the top-right of the screen
          --left                Align to the top-left instead (leaves room on the right)
      info                    Show game window geometry and test the capture backend
          --frames <n>          Frames to grab for the timing test (default 30)
      capture                 Save client-area screenshots (PNG) to the samples folder
          --delay <s>           Wait before the first shot (default 3; 0 with --hotkey)
          --count <n>           Number of shots (default 1)
          --interval <ms>       Time between shots (default 1000)
          --hotkey              Save a shot each time Hotkeys.Capture is pressed; Hotkeys.Quit stops
          --label <text>        Appended to file names, e.g. --label hp_low
          --window              Also save the uncropped window image (diagnostics)
      debug                   Window with detections drawn over the image
          --source <path>       Use saved screenshots (file or folder) instead of the live game
          --fps <n>             Live refresh rate (default 10)
                                Keys: q/Esc quit, s save frame, space pause/next, n next, b previous
      detect                  Run the detectors over screenshots and print a table
          --source <path>       File or folder (default: the samples folder)
          --csv <file>          Also write the results as CSV
          --overlay <dir>       Also save each image with the detections drawn
          --sweep               With --overlay: also draw the hover-sweep points and exclusions
      motion                  Replay a recording through the walking detector
          --source <folder>     e.g. samples/record_20261007_224549
      crop                    Cut a template out of a screenshot
          --source <image> --roi x,y,w,h --out <file.png>
      run                     Run the bot: find monsters, attack until they die, use potions, rest
          --dry-run             Detect and log decisions, send no input
          --no-combat           Potions only (same as Combat.Enabled = false)
          --no-right-skill      Never use the right-click skill (same as Combat.RightSkill.Enabled = false)
          --record              Save every frame (JPEG, with the bot's state drawn) to samples/record_*
                                Hotkeys: Hotkeys.PauseResume (F12) pause/resume, Hotkeys.Quit (Ctrl+F12) quit
      press                   Send a key to the game (waits until the game has focus)
          --key <key> [--count <n>] [--wait <s>]
      mouse                   Move the mouse inside the game, optionally clicking
          --x <px> --y <px> [--click left|right] [--hold <ms>] [--wait <s>]
      probe                   Calibration sweep: hover every probe point, save frames/CSV/map (no clicks)
          --points <n>          Limit the number of points
          --window <ms>         How long to watch the target panel at each point (default 300)
      find                    Find a monster with the hover sweep and leave the cursor on it (no clicks)
          --repeat <n> [--pause <ms>]

    Common options:
      --config <path>         Config file (default: ./botconfig.json)
      --backend <name>        Override Capture.Backend: auto | wgc | bitblt
      -v, --verbose           Debug output on the console (the log file always has it)
    """);
