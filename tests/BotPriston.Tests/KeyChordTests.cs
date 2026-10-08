using BotPriston.Core.Input;

namespace BotPriston.Tests;

public class KeyChordTests
{
    [Theory]
    [InlineData("F12", KeyModifiers.None, 0x7B)]
    [InlineData("ctrl+f12", KeyModifiers.Ctrl, 0x7B)]
    [InlineData("Shift + Alt + Q", KeyModifiers.Shift | KeyModifiers.Alt, 'Q')]
    [InlineData("1", KeyModifiers.None, 0x31)]
    [InlineData("NumPad3", KeyModifiers.None, 0x63)]
    [InlineData("End", KeyModifiers.None, 0x23)]
    public void Parse_ValidChords(string text, KeyModifiers modifiers, int vk)
    {
        var chord = KeyChord.Parse(text);

        Assert.Equal(modifiers, chord.Modifiers);
        Assert.Equal(vk, chord.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Win+F1")]
    [InlineData("F25")]
    public void TryParse_RejectsInvalid(string text)
    {
        Assert.False(KeyChord.TryParse(text, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void ToString_RoundTrips()
    {
        var chord = KeyChord.Parse("Ctrl+Shift+F5");

        Assert.Equal(chord, KeyChord.Parse(chord.ToString()));
    }
}
