using HultUI.Core.Events;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Terminal.Tests;

/// <summary>OSC 8 links: the hover underline and hand, one link across its stretches, and
/// Ctrl+click handing it to the application.</summary>
public class TerminalLinkTests
{
    private const string E = "\x1b";
    private const string Text = "#CCCCCC";

    private static string Link(string url, string text, string? id = null) =>
        $"{E}]8;{(id is null ? "" : "id=" + id)};{url}{E}\\{text}{E}]8;;{E}\\";

    private static int Underlines(Term t)
    {
        t.Window.RenderLog.Clear();
        t.Session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        return t.Window.RenderLog.Named("DrawRect").Count(o => o.Args.EndsWith(Text));
    }

    private static void Hover(Term t, int column, int row)
    {
        var (x, y) = t.CellCentre(column, row);
        t.Window.MoveMouse(x, y);
    }

    [Fact]
    public void AHoveredLink_IsUnderlined_AndThePointerIsAHand()
    {
        using var t = Term.Shown(recording: true);
        t.Emit("see " + Link("https://example.com/", "example") + " now");

        Hover(t, 1, 0);
        Assert.Equal(0, Underlines(t));
        Assert.Equal(CursorType.Text, t.Element!.Inherited.Cursor);

        Hover(t, 6, 0);
        Assert.Equal(1, Underlines(t));
        Assert.Equal(CursorType.Pointer, t.Element.Inherited.Cursor);

        t.Window.MouseLeaveWindow();
        Assert.Equal(0, Underlines(t));
        Assert.Equal(CursorType.Text, t.Element.Inherited.Cursor);
    }

    [Fact]
    public void StretchesWithOneId_AreOneLink_AndWithoutAnId_Separate()
    {
        using var t = Term.Shown(recording: true);
        t.Emit(Link("https://a.test/", "one", "x") + " gap " + Link("https://a.test/", "two", "x") + "\r\n"
            + Link("https://b.test/", "three") + " gap " + Link("https://b.test/", "four"));

        Hover(t, 1, 0);
        Assert.Equal(2, Underlines(t)); // both stretches of the link with the id
        Hover(t, 1, 1);
        Assert.Equal(1, Underlines(t)); // the same address, but no id: only the one hovered
    }

    [Fact]
    public void CtrlClick_HandsTheLinkToTheApplication_AndAPlainClickSelects()
    {
        using var t = Term.Shown();
        var opened = new List<TerminalLink>();
        t.Controller.LinkActivated += opened.Add;
        t.Emit(Link("https://example.com/docs", "docs", "d1"));

        var (x, y) = t.CellCentre(1, 0);
        t.Window.Click(x, y);
        Assert.Empty(opened);

        t.Session.PumpFrame(TimeSpan.FromSeconds(1)); // out of the double-click window
        t.Window.Click(x, y, modifiers: KeyModifiers.Ctrl);
        Assert.Equal([new TerminalLink("https://example.com/docs", "d1")], opened);
        Assert.Equal("", t.Settle()); // nothing reached the program
    }

    [Fact]
    public void CtrlClick_IsTheElements_EvenWhenTheProgramTracksTheMouse()
    {
        using var t = Term.Shown();
        var opened = new List<TerminalLink>();
        t.Controller.LinkActivated += opened.Add;
        t.Emit(Link("https://example.com/", "here") + $"{E}[?1000h{E}[?1006h");

        var (x, y) = t.CellCentre(1, 0);
        t.Window.Click(x, y, modifiers: KeyModifiers.Ctrl);
        Assert.Single(opened);
    }

    [Fact]
    public void WithoutAHandler_NothingOpens()
    {
        using var t = Term.Shown();
        t.Emit(Link("file:///C:/Windows/System32/calc.exe", "calc"));
        var (x, y) = t.CellCentre(1, 0);
        t.Window.Click(x, y, modifiers: KeyModifiers.Ctrl); // must not throw, must not start anything
        Assert.Equal("", t.Settle());
    }
}
