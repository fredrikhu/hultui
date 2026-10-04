using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// A skin tone modifies an emoji. On its own it is a character in its own right — every terminal draws a
/// lone one as a two-column swatch — and it must not attach to something it cannot modify.
///
/// <para>Both halves were wrong. A standalone modifier measured 0, so the cursor never moved and the next
/// character printed over it; and the combining test said yes unconditionally, so "║🏼║" glued the tone
/// inside the box-drawing character and drew the pair as one unreadable cell.</para>
/// </summary>
public class SkinToneTests
{
    private const string Medium = "\U0001F3FD";
    private const string Person = "\U0001F9D1";
    private const string BoxVertical = "\u2551";

    private static Terminal Write(string text)
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 20, Rows = 3 });
        terminal.Write(text);
        return terminal;
    }

    /// <summary>A lone modifier is a two-column swatch, not a character that vanishes.</summary>
    [Fact]
    public void A_lone_modifier_occupies_two_columns()
    {
        var terminal = Write(Medium);
        var line = terminal.Buffer.Lines[0]!;

        Assert.Equal(Medium, line[0].Content);
        Assert.Equal(2, line[0].Width);
        Assert.Equal(0, line[1].Width);
        Assert.Equal(2, terminal.Buffer.X);
    }

    /// <summary>And it survives what comes after it, which it used to be erased by.</summary>
    [Fact]
    public void A_lone_modifier_is_not_overwritten_by_the_next_character()
    {
        var terminal = Write(Medium + "X");
        var line = terminal.Buffer.Lines[0]!;

        Assert.Equal(Medium, line[0].Content);
        Assert.Equal("X", line[2].Content);
        Assert.Equal(3, terminal.Buffer.X);
    }

    /// <summary>
    /// The reported case. A box-drawing character cannot take a skin tone, so the tone stands on its own
    /// and the box keeps its cell.
    /// </summary>
    [Fact]
    public void A_modifier_does_not_attach_to_a_character_it_cannot_modify()
    {
        var terminal = Write(BoxVertical + Medium + BoxVertical);
        var line = terminal.Buffer.Lines[0]!;

        Assert.Equal(BoxVertical, line[0].Content);
        Assert.Equal(1, line[0].Width);
        Assert.Equal(Medium, line[1].Content);
        Assert.Equal(2, line[1].Width);
        Assert.Equal(BoxVertical, line[3].Content);
    }

    /// <summary>Nor to a letter.</summary>
    [Fact]
    public void A_modifier_does_not_attach_to_a_letter()
    {
        var terminal = Write("a" + Medium);
        var line = terminal.Buffer.Lines[0]!;

        Assert.Equal("a", line[0].Content);
        Assert.Equal(Medium, line[1].Content);
        Assert.Equal(2, line[1].Width);
    }

    /// <summary>
    /// But it still does what it is for. This is the case the unconditional answer existed to serve, and
    /// the guard must not cost it.
    /// </summary>
    [Fact]
    public void A_modifier_still_attaches_to_an_emoji()
    {
        var terminal = Write(Person + Medium);
        var line = terminal.Buffer.Lines[0]!;

        Assert.Equal(Person + Medium, line[0].Content);
        Assert.Equal(2, line[0].Width);
        Assert.Equal(0, line[1].Width);
        Assert.Equal(2, terminal.Buffer.X);
    }
}
