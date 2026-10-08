using System.Globalization;
using BotPriston.Core.Capture;
using BotPriston.Core.Vision;
using OpenCvSharp;

namespace BotPriston.App.Commands;

/// <summary>Runs the vision pipeline over saved screenshots and prints one line per image.</summary>
public static class DetectCommand
{
    public static int Run(BotContext ctx, CommandLine cmd)
    {
        var path = cmd.String("source") ?? ctx.SamplesDir;
        var csvPath = cmd.String("csv");
        var overlayDir = cmd.String("overlay");
        bool drawSweep = cmd.Flag("sweep");
        cmd.EnsureAllConsumed();

        using var vision = ctx.CreateVision();
        using var files = new FileCaptureSource(path);
        var overlay = new OverlayRenderer(ctx.Config.Vision);
        if (overlayDir is not null) Directory.CreateDirectory(overlayDir);

        using var csv = csvPath is null ? null : new StreamWriter(csvPath);
        csv?.WriteLine("file,hud_visible,hud_score,hp,mp,stm,target,target_hp,target_border");

        Console.WriteLine($"{"File",-40} {"HUD",-10} {"HP",7} {"MP",7} {"STM",7}  Target");
        foreach (var file in files.Files)
        {
            using var image = Cv2.ImRead(file, ImreadModes.Color);
            var name = Path.GetFileName(file);
            if (image.Width != ctx.Config.Window.ExpectedClientWidth || image.Height != ctx.Config.Window.ExpectedClientHeight)
            {
                Console.WriteLine($"{name,-40} skipped ({image.Width}x{image.Height})");
                continue;
            }

            var s = vision.Analyze(image);
            var hud = s.Hud.Visible ? $"ok {s.Hud.Score:F2}" : $"-- {s.Hud.Score:F2}";
            string Pct(Func<Core.Vision.PlayerBars, double> pick) => s.Bars is null ? "-" : $"{pick(s.Bars):F1}";
            Console.WriteLine($"{name,-40} {hud,-10} {Pct(b => b.Hp.Percent),7} {Pct(b => b.Mp.Percent),7} {Pct(b => b.Stm.Percent),7}  {s.Target?.ToString() ?? "-"}");

            if (overlayDir is not null)
            {
                using var canvas = overlay.Render(image, s, name);
                if (drawSweep)
                {
                    var t = ctx.Config.Targeting;
                    OverlayRenderer.DrawSweep(canvas, t, Core.Targeting.SweepPattern.Generate(t, image.Width, image.Height));
                }
                Cv2.ImWrite(Path.Combine(overlayDir, name), canvas);
            }

            csv?.WriteLine(string.Join(',', name, s.Hud.Visible, s.Hud.Score.ToString("F3", CultureInfo.InvariantCulture),
                CsvPct(s.Bars?.Hp.Percent), CsvPct(s.Bars?.Mp.Percent), CsvPct(s.Bars?.Stm.Percent),
                s.Target?.Status.ToString() ?? "", CsvPct(s.Target?.HpPercent),
                s.Target?.BorderScore.ToString("F2", CultureInfo.InvariantCulture) ?? ""));
        }

        if (csvPath is not null)
            ctx.Log.Information("Wrote {Csv}", Path.GetFullPath(csvPath));
        return 0;
    }

    private static string CsvPct(double? value) => value?.ToString("F1", CultureInfo.InvariantCulture) ?? "";
}
