using HultUI.Core.Accessibility;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Terminal.Tests;

/// <summary>The terminal for a screen reader: a document named after the title, whose value is
/// the screen, told of a change once the output pauses.</summary>
public class TerminalAccessibilityTests
{
    private static bool ToldOf(HultUI.Headless.HeadlessAccessibilityBridge bridge, string property) =>
        bridge.Diffs.Any(d => d.PropertyChanged.Any(p => p.PropertyName == property && p.Node.Role == AccessibilityRole.Document));

    [Fact]
    public void TheScreen_IsTheValue_AndAChangeIsToldOnceTheOutputPauses()
    {
        AccessibilityClients.Force = true;
        try
        {
            using var t = Term.Shown();
            var bridge = t.Window.InstallAccessibilityBridge();
            t.Emit("hello\r\nworld");
            Assert.Equal("hello\nworld", t.Window.FindByRole(AccessibilityRole.Document)!.Value);
            t.Session.PumpFrame(TerminalElement.ScreenSettle + TimeSpan.FromMilliseconds(50));
            t.Session.PumpFrames(2);
            bridge.ClearDiffs();

            t.Emit(" again");
            t.Session.PumpFrames(3);
            Assert.False(ToldOf(bridge, AccessibilityProperties.Value)); // not a change a frame

            t.Session.PumpFrame(TerminalElement.ScreenSettle + TimeSpan.FromMilliseconds(50));
            t.Session.PumpFrames(2);
            Assert.True(ToldOf(bridge, AccessibilityProperties.Value));
            Assert.Equal("hello\nworld again", bridge.CurrentTree is { } tree ? FindDocument(tree)?.Value : null);
        }
        finally { AccessibilityClients.Force = null; }
    }

    [Fact]
    public void UnbrokenOutput_IsStillToldOf_AfterTheLimit()
    {
        AccessibilityClients.Force = true;
        try
        {
            using var t = Term.Shown();
            var bridge = t.Window.InstallAccessibilityBridge();
            t.Session.PumpFrames(2);
            bridge.ClearDiffs();

            // Output every 400 ms, never pausing for the 500 the settle waits.
            var waited = TimeSpan.Zero;
            while (waited < TerminalElement.ScreenSettleLimit + TimeSpan.FromMilliseconds(600))
            {
                t.Emit("x");
                t.Session.PumpFrame(TimeSpan.FromMilliseconds(400));
                waited += TimeSpan.FromMilliseconds(400);
            }
            Assert.True(ToldOf(bridge, AccessibilityProperties.Value));
        }
        finally { AccessibilityClients.Force = null; }
    }

    [Fact]
    public void ATitle_RenamesTheDocument_AtOnce()
    {
        AccessibilityClients.Force = true;
        try
        {
            using var t = Term.Shown();
            var bridge = t.Window.InstallAccessibilityBridge();
            t.Session.PumpFrames(2);
            bridge.ClearDiffs();

            t.Emit("\x1b]2;make test\x07");
            t.Session.PumpFrames(2);
            Assert.True(ToldOf(bridge, AccessibilityProperties.Name));
            Assert.Equal("make test", t.Window.FindByRole(AccessibilityRole.Document)!.Name);
        }
        finally { AccessibilityClients.Force = null; }
    }

    [Fact]
    public void ATitleSetBeforeTheElementListened_StillNamesIt()
    {
        // The demo shell's title arrived while the window was being built, and a screen reader
        // was told "Terminal" for good.
        using var session = Headless.HeadlessSession.Create(400, 300);
        var connection = new Testing.FakeConnection();
        using var controller = new TerminalController(connection);
        var element = TerminalElement.New(controller); // made, not yet in a window

        connection.Emit("\x1b]0;Demo shell\x07");
        session.PumpUntil(() => controller.Title == "Demo shell", TimeSpan.FromSeconds(10));

        session.Window.SetRoot(element);
        session.PumpFrames(2);
        Assert.Equal("Demo shell", session.Window.FindByRole(AccessibilityRole.Document)!.Name);
    }

    // ──── The text (seam A1) ────

    private static AccessibleText TextOf(Term t) =>
        t.Window.FindByRole(AccessibilityRole.Document)!.Node.CurrentText!;

    private static void Settle(Term t)
    {
        t.Session.PumpFrame(TerminalElement.ScreenSettle + TimeSpan.FromMilliseconds(50));
        t.Session.PumpFrames(2);
    }

    [Fact]
    public void TheText_IsTheRows_WithTheCursorAsTheCaret()
    {
        using var t = Term.Shown();
        t.Emit("hello\r\nworld\r\n$ ");
        var text = TextOf(t);
        Assert.Equal("hello\nworld\n$", text.Text);
        Assert.Equal(3, text.LineCount);
        // The cursor is past the prompt's blank, beyond the row's text: the row's end.
        Assert.Equal(text.Length, text.Caret);
        Assert.True(text.IsReadOnly);
    }

    [Fact]
    public void TheGeometry_IsTheCellGrid_WideCharactersTwoCellsWide()
    {
        using var t = Term.Shown();
        t.Emit("ab日本c\r\nxy");
        var text = TextOf(t);
        var box = t.Element!.ScreenContentBox;
        var (w, h) = t.Element.CellSize;
        Assert.Equal(new Rect(box.X + 2 * w, box.Y, 2 * w, h), text.Geometry!.CharacterBounds(text, 2)); // 日
        Assert.Equal(new Rect(box.X + 6 * w, box.Y, w, h), text.Geometry.CharacterBounds(text, 4));     // c
        Assert.Equal(new Rect(box.X + w, box.Y + h, w, h), text.Geometry.CharacterBounds(text, 7));     // y
        // A point in 本's second cell finds 本; a point past a row's text finds its end.
        Assert.Equal(3, text.OffsetAt(new Point(box.X + 5.5f * w, box.Y + 0.5f * h)));
        Assert.Equal(5, text.OffsetAt(new Point(box.X + 20 * w, box.Y + 0.5f * h)));
    }

    [Fact]
    public void TheSelection_IsTheTextsSelection()
    {
        using var t = Term.Shown();
        t.Emit("one two three");
        var (x0, y) = t.CellCentre(4, 0);
        var (x1, _) = t.CellCentre(6, 0);
        t.Window.Drag(new Point(x0, y), new Point(x1, y));
        var text = TextOf(t);
        Assert.Equal("two", text.Text[text.SelectionStart..text.SelectionEnd]);
    }

    [Fact]
    public void TheSource_IsReadOffTheUiThread_AsTheScreenIsNow()
    {
        using var t = Term.Shown();
        t.Emit("first");
        var source = t.Window.FindByRole(AccessibilityRole.Document)!.Node.TextSource!;
        t.Emit("\r\nsecond");
        // No rebuild since: the source reads the screen itself, here from another thread.
        var text = Task.Run(() => source.AccessibleText).Result;
        Assert.Equal("first\nsecond", text!.Text);
        Assert.Same(text, source.AccessibleText); // unchanged screen, the same snapshot
    }

    [Fact]
    public void Output_TellsTheTextChanged_AndAMovedCursorTheCaret()
    {
        AccessibilityClients.Force = true;
        try
        {
            using var t = Term.Shown();
            var bridge = t.Window.InstallAccessibilityBridge();
            t.Emit("abc");
            Settle(t);
            bridge.ClearDiffs();

            t.Emit("d");
            Settle(t);
            Assert.True(ToldOf(bridge, AccessibilityProperties.Text));

            bridge.ClearDiffs();
            t.Emit("\x1b[2D"); // the cursor back two
            Settle(t);
            Assert.True(ToldOf(bridge, AccessibilityProperties.TextSelection));
            Assert.False(ToldOf(bridge, AccessibilityProperties.Text));
        }
        finally { AccessibilityClients.Force = null; }
    }

    // ──── Announcements (seam A2) ────

    private static Term Listening(out HultUI.Headless.HeadlessAccessibilityBridge bridge)
    {
        AccessibilityClients.Force = true;
        var t = Term.Shown();
        bridge = t.Window.InstallAccessibilityBridge();
        t.Emit("$ ");
        Settle(t); // the first settle takes the place: nothing before it is announced
        bridge.ClearAnnouncements();
        return t;
    }

    [Fact]
    public void NewOutput_IsAnnounced_OnceItSettles()
    {
        try
        {
            using var t = Listening(out var bridge);
            Assert.Empty(bridge.Announcements);
            t.Emit("building\r\n");
            t.Session.PumpFrames(2);
            Assert.Empty(bridge.Announcements); // not yet: the output has not paused
            Settle(t);
            var said = Assert.Single(bridge.Announcements);
            Assert.Equal("building", said.Text);
            Assert.Equal(AnnouncementPoliteness.Polite, said.Politeness);
            Assert.Equal(AccessibilityRole.Document, said.Source!.Role);

            t.Emit("done\r\n$ ");
            Settle(t);
            Assert.Equal("done\n$", bridge.Announcements[^1].Text);
        }
        finally { AccessibilityClients.Force = null; }
    }

    [Fact]
    public void WhatTheUserTyped_IsNotAnnouncedBack()
    {
        try
        {
            using var t = Listening(out var bridge);
            t.Window.TypeText("l");
            t.Emit("l"); // the shell's echo
            Settle(t);
            Assert.Empty(bridge.Announcements);

            t.Window.TypeText("s");
            t.Window.PressKey(Core.Events.KeyCode.Enter, text: "");
            t.Emit("s\r\nfile.txt\r\n$ ");
            Settle(t);
            Assert.Equal("file.txt\n$", Assert.Single(bridge.Announcements).Text);
        }
        finally { AccessibilityClients.Force = null; }
    }

    [Fact]
    public void AFullScreenProgram_IsNotAnnounced()
    {
        try
        {
            using var t = Listening(out var bridge);
            t.Emit("\x1b[?1049h\x1b[Hmenu item one\r\nmenu item two");
            Settle(t);
            t.Emit("\x1b[2;1Hmenu item 2!!");
            Settle(t);
            Assert.Empty(bridge.Announcements);

            t.Emit("\x1b[?1049lback\r\n");
            Settle(t); // the way back takes the place again
            t.Emit("after\r\n");
            Settle(t);
            Assert.Equal("after", Assert.Single(bridge.Announcements).Text);
        }
        finally { AccessibilityClients.Force = null; }
    }

    [Fact]
    public void AFlood_IsAnnouncedByItsLastScreenful()
    {
        try
        {
            using var t = Listening(out var bridge);
            int rows = t.Controller.Rows;
            t.Emit(string.Concat(Enumerable.Range(1, rows * 3).Select(i => $"line {i}\r\n")));
            Settle(t);
            var lines = Assert.Single(bridge.Announcements).Text.Split('\n');
            Assert.True(lines.Length <= rows);
            Assert.Equal($"line {rows * 3}", lines[^1]);
        }
        finally { AccessibilityClients.Force = null; }
    }

    [Fact]
    public void NobodyListening_NothingIsAnnounced()
    {
        AccessibilityClients.Force = false;
        try
        {
            using var t = Term.Shown();
            var bridge = t.Window.InstallAccessibilityBridge();
            t.Emit("$ ");
            Settle(t);
            t.Emit("output\r\n");
            Settle(t);
            Assert.Empty(bridge.Announcements);
        }
        finally { AccessibilityClients.Force = null; }
    }

    private static AccessibilityNode? FindDocument(AccessibilityNode node)
    {
        if (node.Role == AccessibilityRole.Document) return node;
        foreach (var child in node.Children)
            if (FindDocument(child) is { } found) return found;
        return null;
    }
}
