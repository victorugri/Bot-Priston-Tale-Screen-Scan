using BotPriston.Core.Config;
using OpenCvSharp;

namespace BotPriston.Core.Vision;

public sealed record PlayerBars(BarReading Hp, BarReading Mp, BarReading Stm)
{
    public override string ToString() => $"HP {Hp.Percent:F1}%  MP {Mp.Percent:F1}%  STM {Stm.Percent:F1}%";
}

/// <summary>Reads the character's HP/MP/STM tubes (bottom-left HUD).</summary>
public sealed class PlayerBarsDetector(PlayerBarsConfig config) : IDetector<PlayerBars>
{
    public PlayerBarsConfig Config { get; } = config;

    public PlayerBars Detect(Mat frame) =>
        new(BarReader.Read(frame, Config.Hp), BarReader.Read(frame, Config.Mp), BarReader.Read(frame, Config.Stm));
}
