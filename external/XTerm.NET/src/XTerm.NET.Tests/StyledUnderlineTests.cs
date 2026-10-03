using System.Runtime.CompilerServices;
using XTerm;
using XTerm.Buffer;
using XTerm.Common;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// Styled underlines (SGR 4:1–4:5, 21) and underline colour (SGR 58/59).
/// </summary>
/// <remarks>
/// The squiggly underline an LSP puts under an error. The style enum and sub-parameter parsing
/// already existed — the sub-parameters were being read and then dropped, so a program asking for a
/// curly underline got a straight one.
/// </remarks>
public class StyledUnderlineTests
{
    private const string Esc = "\u001b";

    private static Terminal Fresh() => new(new TerminalOptions { Cols = 20, Rows = 3 });

    private static BufferCell FirstCell(Terminal terminal)
        => terminal.Buffer.Lines[terminal.Buffer.YBase]![0];

    [Theory]
    [InlineData("4", UnderlineStyle.Single)]
    [InlineData("4:0", UnderlineStyle.None)]
    [InlineData("4:1", UnderlineStyle.Single)]
    [InlineData("4:2", UnderlineStyle.Double)]
    [InlineData("4:3", UnderlineStyle.Curly)]
    [InlineData("4:4", UnderlineStyle.Dotted)]
    [InlineData("4:5", UnderlineStyle.Dashed)]
    [InlineData("21", UnderlineStyle.Double)]
    public void Sgr_selects_the_underline_style(string sgr, UnderlineStyle expected)
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[{sgr}mx");

        Assert.Equal(expected, FirstCell(terminal).Attributes.GetUnderlineStyle());
    }

    /// <summary>
    /// A style nobody has defined is still an underline. Drawing a plain one is closer to what the
    /// program asked for than drawing nothing at all.
    /// </summary>
    [Fact]
    public void An_unknown_style_still_underlines()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[4:9mx");

        Assert.Equal(UnderlineStyle.Single, FirstCell(terminal).Attributes.GetUnderlineStyle());
    }

    /// <summary>
    /// The style is the single source of truth, so a cell underlined by any of these reports it.
    /// Keeping a separate flag beside the style is how a cell ends up underlined by one and not
    /// the other.
    /// </summary>
    [Fact]
    public void IsUnderline_follows_the_style()
    {
        var terminal = Fresh();

        terminal.Write($"{Esc}[4:3mx");
        Assert.True(FirstCell(terminal).Attributes.IsUnderline());

        terminal.Write($"{Esc}[24m{Esc}[1;1Hy");
        Assert.False(FirstCell(terminal).Attributes.IsUnderline());
        Assert.Equal(UnderlineStyle.None, FirstCell(terminal).Attributes.GetUnderlineStyle());
    }

    // ---- colour ---------------------------------------------------------------------------------

    [Fact]
    public void Sgr58_sets_a_truecolor_underline_as_subparameters()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[4:3;58:2::255:0:0mx");

        var attr = FirstCell(terminal).Attributes;
        Assert.True(attr.TryGetUnderlineColor(out var color, out var mode));
        Assert.Equal((255 << 16) | (0 << 8) | 0, color);
        Assert.Equal(1, mode);
        Assert.Equal(UnderlineStyle.Curly, attr.GetUnderlineStyle());
    }

    /// <summary>
    /// Both spellings are in use, and a terminal that takes only one looks broken to half its
    /// callers.
    /// </summary>
    [Fact]
    public void Sgr58_also_accepts_separate_parameters()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[58;2;0;128;255mx");

        Assert.True(FirstCell(terminal).Attributes.TryGetUnderlineColor(out var color, out var mode));
        Assert.Equal((0 << 16) | (128 << 8) | 255, color);
        Assert.Equal(1, mode);
    }

    [Fact]
    public void Sgr58_accepts_an_indexed_colour()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[58:5:196mx");

        Assert.True(FirstCell(terminal).Attributes.TryGetUnderlineColor(out var color, out var mode));
        Assert.Equal(196, color);
        Assert.Equal(0, mode);
    }

    [Fact]
    public void Sgr59_puts_the_underline_back_to_the_foreground()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[58:2::255:0:0m{Esc}[59mx");

        Assert.False(FirstCell(terminal).Attributes.TryGetUnderlineColor(out _, out _));
    }

    [Fact]
    public void A_reset_clears_the_style_and_the_colour()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[4:3;58:2::255:0:0m{Esc}[0mx");

        var attr = FirstCell(terminal).Attributes;
        Assert.Equal(UnderlineStyle.None, attr.GetUnderlineStyle());
        Assert.False(attr.TryGetUnderlineColor(out _, out _));
    }

    /// <summary>
    /// The same colour used twice is one entry, which is what keeps twenty bits of id enough.
    /// </summary>
    [Fact]
    public void The_same_colour_interns_once()
    {
        var terminal = Fresh();

        terminal.Write($"{Esc}[58:2::10:20:30mx");
        var first = FirstCell(terminal).Attributes.GetUnderlineColorId();

        terminal.Write($"{Esc}[0m{Esc}[58:2::10:20:30m{Esc}[1;1Hy");
        var second = FirstCell(terminal).Attributes.GetUnderlineColorId();

        Assert.Equal(first, second);
        Assert.NotEqual(0, first);
    }

    // ---- an abandoned sequence must not poison the next one --------------------------------------
    //
    // Raised in review. The sub-parameter accumulator is parser-lifetime state, and nothing cleared
    // it when a sequence was abandoned rather than dispatched -- so every digit of the NEXT sequence
    // up to its first separator was swallowed into the stale sub-parameter and its first parameter
    // read as 0. Worse than a dropped sequence, because 0 means something for most of them.

    private static AttributeData AttrAt(Terminal terminal)
        => terminal.Buffer.Lines[terminal.Buffer.YBase]![0].Attributes;

    /// <summary>What a clean SGR 31 gives, to compare a poisoned one against.</summary>
    private static int RedForeground()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[31mx");
        return AttrAt(terminal).GetFgColor();
    }

    [Theory]
    [InlineData("\u001b[4:3")]            // ESC begins the next sequence and abandons this one
    [InlineData("\u001b[4:3\u0018")]      // CAN
    [InlineData("\u001b[4:3\u001a")]      // SUB
    [InlineData("\u001b[4:3\u001bc")]     // RIS
    public void An_abandoned_sequence_does_not_swallow_the_next_one(string abandoned)
    {
        var terminal = Fresh();
        terminal.Write(abandoned);
        terminal.Write($"{Esc}[31mx");

        Assert.Equal(RedForeground(), AttrAt(terminal).GetFgColor());
    }

    /// <summary>
    /// Not only SGR: a lost first parameter homes the cursor instead of moving it.
    /// </summary>
    [Fact]
    public void An_abandoned_sequence_does_not_swallow_a_cursor_move()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[4:3");
        terminal.Write($"{Esc}[2;5H");
        terminal.Write("z");

        Assert.Equal("z", terminal.Buffer.Lines[terminal.Buffer.YBase + 1]![4].Content);
    }

    // ---- the reason this is stored as an id ------------------------------------------------------

    /// <summary>
    /// The whole feature had to fit in bits the cell already owned.
    /// </summary>
    /// <remarks>
    /// A full RGB underline colour plus its mode is more bits than were left, and growing
    /// AttributeData grows every cell in the buffer — the thing measured as costing most on fills.
    /// So the cell carries an interned id, and this asserts the cost of the feature is zero.
    /// </remarks>
    [Fact]
    public void The_cell_did_not_grow()
    {
        Assert.Equal(12, Unsafe.SizeOf<AttributeData>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AttributeData>());
    }

    /// <summary>
    /// Style and colour live in the same int as the boolean attributes and must not disturb them.
    /// </summary>
    [Fact]
    public void The_style_and_colour_do_not_disturb_the_other_attributes()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[1;3;4:3;58:2::255:0:0;9mx");

        var attr = FirstCell(terminal).Attributes;
        Assert.True(attr.IsBold());
        Assert.True(attr.IsItalic());
        Assert.True(attr.IsStrikethrough());
        Assert.Equal(UnderlineStyle.Curly, attr.GetUnderlineStyle());
        Assert.True(attr.TryGetUnderlineColor(out _, out _));
    }
}
