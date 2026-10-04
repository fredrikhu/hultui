using HultUI.Core.Events;
using Xunit;
using static HultUI.Terminal.Tests.TerminalControllerTests;

namespace HultUI.Terminal.Tests;

/// <summary>Search over the scrollback: what is found, what is tinted, what is revealed, and
/// how it follows output.</summary>
public class TerminalSearchTests
{
    private const string Hit = "#FFBF003D", Current = "#FFBF009E";

    private static List<string> Tints(Term t, string colour)
    {
        t.Window.RenderLog.Clear();
        t.Session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        return t.Window.RenderLog.Named("DrawRect").Select(o => o.Args).Where(a => a.EndsWith(colour)).ToList();
    }

    [Fact]
    public void AHitInTheScrollback_IsFound_AndScrolledIntoView()
    {
        using var t = Term.Shown();
        t.Emit(Lines(20) + "the needle is here\r\n" + Lines(300));
        Assert.DoesNotContain("needle", t.Controller.ReadScreen());

        t.Element!.Search.SetQuery("needle");
        t.Session.PumpFrames(2);

        Assert.Single(t.Element.Search.Hits);
        Assert.Contains("the needle is here", t.Controller.ReadScreen());
    }

    [Fact]
    public void AMatchAcrossTheWrap_IsOneHit_TintedOnBothRows()
    {
        using var t = Term.Shown(recording: true);
        int cols = t.Controller.Columns;
        t.Emit(new string('x', cols - 3) + "needle");

        t.Element!.Search.SetQuery("needle");
        Assert.Single(t.Element.Search.Hits);
        Assert.Equal(2, Tints(t, Current).Count);
    }

    [Fact]
    public void TheCurrentHit_HasItsOwnTint()
    {
        using var t = Term.Shown(recording: true);
        t.Emit("ab cd ab");
        t.Element!.Search.SetQuery("ab");
        Assert.Single(Tints(t, Current));
        Assert.Single(Tints(t, Hit));

        t.Element.Search.Move(1);
        var current = Assert.Single(Tints(t, Current));
        var (w, _) = t.Element.CellSize;
        Assert.StartsWith($"({Math.Round(t.Element.ScreenContentBox.X + 6 * w):0},", current);
    }

    [Fact]
    public void AfterWideCharacters_TheTintIsOnTheRightColumns()
    {
        using var t = Term.Shown(recording: true);
        t.Emit("日本needle");
        t.Element!.Search.SetQuery("needle");
        var tint = Assert.Single(Tints(t, Current));
        var (w, _) = t.Element.CellSize;
        // Two wide characters are four columns; the hit is six columns from there.
        Assert.StartsWith($"({Math.Round(t.Element.ScreenContentBox.X + 4 * w):0},", tint);
        Assert.Contains($" {Math.Round(6 * w):0}x", tint);
    }

    [Fact]
    public void Output_IsSearchedOnceItPauses_AndTheCurrentHitStaysPut()
    {
        using var t = Term.Shown();
        t.Emit("one x7\r\ntwo x7\r\n");
        var search = t.Element!.Search;
        search.SetQuery("x7");
        search.Move(1);
        Assert.Equal((2, 1), (search.Hits.Count, search.Index));

        t.Emit("three x7\r\n");
        Assert.Equal(2, search.Hits.Count); // not yet: the rebind waits for output to pause
        t.Session.PumpFrame(TerminalElement.SearchRebindDelay + TimeSpan.FromMilliseconds(20));

        Assert.Equal(3, search.Hits.Count);
        Assert.Equal(1, search.Index); // still "two x7"
    }

    [Fact]
    public void TheAlternateScreen_IsSearchedWhileItShows()
    {
        using var t = Term.Shown();
        t.Emit("normal x7\r\n");
        var search = t.Element!.Search;
        search.SetQuery("x7");
        Assert.Single(search.Hits);

        t.Emit("\x1b[?1049h\x1b[Halt x7 x7");
        Assert.Equal(2, search.Hits.Count);
        t.Emit("\x1b[?1049l");
        Assert.Single(search.Hits);
    }

    [Fact]
    public void CtrlShiftF_AsksForTheFindBar_OrGoesToTheProgram()
    {
        using var t = Term.Shown();
        t.Window.PressKey(KeyCode.F, KeyModifiers.Ctrl | KeyModifiers.Shift);
        Assert.NotEqual("", t.Settle()); // nobody asked for it: the program's key

        int asked = 0;
        t.Element!.FindRequested += () => asked++;
        t.Window.PressKey(KeyCode.F, KeyModifiers.Ctrl | KeyModifiers.Shift);
        Assert.Equal(1, asked);
        Assert.Equal("", t.Settle());
    }
}
