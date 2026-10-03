using HultUI.Core.Events;
using HultUI.Headless.Recording;
using Xunit;

namespace HultUI.Terminal.Tests;

/// <summary>
/// What the element draws over the text: the five underline styles in their colours, the
/// overline, and blinking text and cursors, on the frame loop's virtual time.
/// </summary>
public class TerminalDecorationTests
{
    private const string Text = "#CCCCCC";  // Campbell's foreground
    private const string Cursor = "#FFFFFF";

    private static List<string> Ops(Term t, string name) =>
        t.Window.RenderLog.Named(name).Select(o => o.Args).ToList();

    private static Term Drawn(string output)
    {
        var t = Term.Shown(recording: true);
        t.Emit(output);
        t.Window.RenderLog.Clear();
        t.Session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        return t;
    }

    // ──── Underlines ────

    [Theory]
    [InlineData("\x1b[58;2;255;0;0m\x1b[4m", "#FF0000")]   // RGB, before the underline
    [InlineData("\x1b[4m\x1b[58;2;255;0;0m", "#FF0000")]   // after it
    [InlineData("\x1b[58;5;196m\x1b[4m", "#FF0000")]       // the 256-colour cube's red
    [InlineData("\x1b[58:5:196m\x1b[4m", "#FF0000")]       // colon form
    [InlineData("\x1b[58;5;1m\x1b[4m", "#C50F1F")]         // the theme's red
    public void AnUnderline_TakesItsOwnColour(string sgr, string colour)
    {
        using var t = Drawn(sgr + "red\x1b[0m");
        Assert.Single(Ops(t, "DrawRect"), a => a.EndsWith(colour));
    }

    [Fact]
    public void AnUnderline_SitsWhereTheFaceSays()
    {
        using var t = Drawn("\x1b[4mab\x1b[0m");
        var font = t.Element!.Font;
        var box = t.Element.ScreenContentBox;
        var underline = Assert.Single(Ops(t, "DrawRect"), a => a.EndsWith(Text));
        float y = float.Parse(underline.Split(',')[1].Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(box.Y + font.Ascent + font.UnderlinePosition, y, 1f);
    }

    [Fact]
    public void ADoubleUnderline_IsTwoStrokes()
    {
        using var t = Drawn("\x1b[4:2mab\x1b[0m");
        Assert.Equal(2, Ops(t, "DrawRect").Count(a => a.EndsWith(Text)));
    }

    [Theory]
    [InlineData(4)] // dotted
    [InlineData(5)] // dashed
    public void DottedAndDashedUnderlines_AreRowsOfMarks(int style)
    {
        using var t = Drawn($"\x1b[4:{style}mabcdefgh\x1b[0m");
        Assert.True(Ops(t, "DrawRect").Count(a => a.EndsWith(Text)) > 8);
    }

    [Fact]
    public void ACurlyUnderline_IsAWave_OnePeriodACell()
    {
        using var t = Drawn("\x1b[4:3mab\x1b[0m");
        Assert.Equal(16, Ops(t, "DrawLine").Count);
    }

    [Fact]
    public void TheOverline_IsAlongTheTop()
    {
        using var t = Drawn("\x1b[53mab\x1b[0m");
        var box = t.Element!.ScreenContentBox;
        Assert.Single(Ops(t, "DrawRect"), a => a.EndsWith(Text) && a.StartsWith($"({box.X:0.##},{box.Y:0.##} "));
    }

    // ──── Blinking ────

    private static bool CursorDrawn(Term t) => Ops(t, "DrawRect").Any(a => a.EndsWith(Cursor));

    private static void Frame(Term t, int ms)
    {
        t.Window.RenderLog.Clear();
        t.Session.PumpFrame(TimeSpan.FromMilliseconds(ms), forceRender: true);
    }

    [Fact]
    public void ABlinkingCursor_Blinks_AndTypingShowsItAtOnce()
    {
        using var t = Term.Shown(recording: true);
        t.Emit("\x1b[1 q"); // DECSCUSR 1: a blinking block
        Frame(t, 16);
        Assert.True(CursorDrawn(t));
        Assert.True(t.Element!.BlinkClockRunning);

        Frame(t, 600);
        Assert.False(CursorDrawn(t));
        Frame(t, 600);
        Assert.True(CursorDrawn(t));
        Frame(t, 600);
        Assert.False(CursorDrawn(t));

        t.Window.TypeText("x");
        Frame(t, 16);
        Assert.True(CursorDrawn(t));
    }

    [Fact]
    public void ASteadyCursor_RunsNoClock()
    {
        using var t = Term.Shown(recording: true);
        t.Emit("\x1b[2 q"); // a steady block
        Frame(t, 600);
        Frame(t, 600);
        Assert.True(CursorDrawn(t));
        Assert.False(t.Element!.BlinkClockRunning);
    }

    [Fact]
    public void BlinkingText_IsHiddenInTheOffHalf()
    {
        using var t = Term.Shown(recording: true);
        t.Emit("\x1b[5mblink\x1b[0m solid");
        Frame(t, 16);
        Assert.Contains(Ops(t, "DrawText"), a => a.StartsWith("\"blink solid\""));

        Frame(t, 600);
        Assert.Contains(Ops(t, "DrawText"), a => a.StartsWith("\"      solid\""));

        t.Emit("\x1b[2J\x1b[Hplain"); // nothing blinks any more: the clock stops
        Frame(t, 600);
        Frame(t, 16);
        Assert.False(t.Element!.BlinkClockRunning);
    }
}
