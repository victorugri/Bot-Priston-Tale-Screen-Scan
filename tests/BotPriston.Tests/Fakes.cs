using BotPriston.Core.Bot;
using BotPriston.Core.Capture;
using BotPriston.Core.Geometry;
using BotPriston.Core.Input;
using BotPriston.Core.Vision;
using OpenCvSharp;
using Serilog;

namespace BotPriston.Tests;

internal sealed class FakeInput : IInputSink
{
    public List<string> Actions { get; } = [];
    public bool Refuse { get; set; }
    public int ReleaseAllCalls { get; private set; }

    public bool PressKey(KeyChord key) => Record($"key {key}");
    public bool MoveMouse(PixelPoint clientPoint) => Record($"move {clientPoint}");
    public bool MouseDown(MouseButton button) => Record($"down {button}");
    public bool MouseUp(MouseButton button) => Record($"up {button}");
    public bool Click(MouseButton button) => Record($"click {button}");
    public void ReleaseAll() => ReleaseAllCalls++;

    private bool Record(string action)
    {
        if (Refuse) return false;
        Actions.Add(action);
        return true;
    }
}

internal sealed class FakeWindow : IGameWindowState
{
    public bool Exists { get; set; } = true;
    public bool IsMinimized { get; set; }
    public bool IsForeground { get; set; } = true;
    public (int Width, int Height) ClientSize { get; set; } = (1600, 900);
}

/// <summary>Returns a tiny dummy frame (or nothing); the fake vision decides what is "seen".</summary>
internal sealed class FakeCapture : ICaptureSource
{
    public bool NoFrame { get; set; }
    public string Name => "Fake";
    public Frame? Grab() => NoFrame ? null : new Frame(new Mat(4, 4, MatType.CV_8UC3, Scalar.All(0)), DateTimeOffset.Now, Name);
    public void Dispose() { }
}

internal sealed class FakeVision : IVision
{
    public VisionSnapshot Next { get; set; } = Snapshot(100, 100, 100);

    public VisionSnapshot Analyze(Mat frame) => Next;

    public static VisionSnapshot Snapshot(double hp, double mp, double stm, TargetPanel? target = null, bool rightSkillReady = false) =>
        new(new HudState(true, 1.0), Bars(hp, mp, stm), target ?? new TargetPanel(TargetStatus.None, 0, null),
            new SkillOrbs(new SkillOrb(false, 0), new SkillOrb(rightSkillReady, rightSkillReady ? 1 : 0)));

    public static TargetPanel Target(TargetStatus status, double? hp = null) => new(status, 1.0, hp);

    public static VisionSnapshot HudHidden() => new(new HudState(false, 0.0), null);

    public static PlayerBars Bars(double hp, double mp, double stm) =>
        new(Reading(hp), Reading(mp), Reading(stm));

    private static BarReading Reading(double percent) => new(percent, (int)percent, 100, 0);
}

internal static class TestLog
{
    public static readonly ILogger Silent = new LoggerConfiguration().CreateLogger();
}
