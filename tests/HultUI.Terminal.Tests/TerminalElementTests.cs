using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Headless;
using HultUI.Terminal.Emulation;
using Xunit;
using static HultUI.Terminal.Tests.TerminalControllerTests;

namespace HultUI.Terminal.Tests;

/// <summary>
/// <see cref="TerminalElement"/> under the headless host: the grid it sizes, the keys and text it
/// turns into input, selection and the clipboard, the wheel, mouse and focus reports, and what it
/// draws.
/// </summary>
public class TerminalElementTests
{
    // ──── Grid ────

    [Fact]
    public void TheGrid_IsTheContentBoxDividedByTheCell()
    {
        using var t = Term.Shown();
        var box = t.Element!.Layout.AbsoluteContentBox;
        var (w, h) = t.Element.CellSize;
        Assert.True(w > 3 && h > 6, $"cell {w}×{h}");

        var expected = ((int)(box.Width / w), (int)(box.Height / h));
        Assert.Equal(expected, (t.Controller.Columns, t.Controller.Rows));
        Assert.Equal(expected, t.Connection.Resizes[^1]);
    }

    [Fact]
    public void TheGrid_FollowsTheWindow()
    {
        using var t = Term.Shown();
        var before = (t.Controller.Columns, t.Controller.Rows);

        t.Window.SetClientSize(200, 150);
        t.Session.PumpFrames(2);

        Assert.True(t.Controller.Columns < before.Columns && t.Controller.Rows < before.Rows);
        Assert.Equal((t.Controller.Columns, t.Controller.Rows), t.Connection.Resizes[^1]);
    }

    // ──── Keys and text ────

    [Fact]
    public void TypedText_ReachesTheProgram()
    {
        using var t = Term.Shown();
        t.Window.TypeText("ls ö");
        Assert.Equal("ls ö", t.Expect("ö"));
    }

    [Theory]
    [InlineData(KeyCode.Enter, KeyModifiers.None, "\r")]
    [InlineData(KeyCode.Backspace, KeyModifiers.None, "\x7f")]
    [InlineData(KeyCode.Escape, KeyModifiers.None, "\x1b")]
    [InlineData(KeyCode.Up, KeyModifiers.None, "\x1b[A")]
    [InlineData(KeyCode.Right, KeyModifiers.Ctrl, "\x1b[1;5C")]
    [InlineData(KeyCode.F5, KeyModifiers.None, "\x1b[15~")]
    [InlineData(KeyCode.C, KeyModifiers.Ctrl, "\x03")]
    [InlineData(KeyCode.B, KeyModifiers.Alt, "\x1b" + "b")]
    public void AKey_SendsItsSequence(KeyCode key, KeyModifiers mods, string expected)
    {
        using var t = Term.Shown();
        t.Window.PressKey(key, mods);
        Assert.Equal(Escape(expected), Escape(t.Expect(expected)));
    }

    [Fact]
    public void Tab_IsTheProgramsKey_AndFocusStays()
    {
        using var t = Term.Shown(around: e => Flex.Column().Children(Focusable("before"), e, Focusable("after")));
        t.Element!.Focus();
        t.Window.PressKey(KeyCode.Tab);
        t.Window.PressKey(KeyCode.Tab, KeyModifiers.Shift);
        Assert.Equal("\t\x1b[Z", t.Expect("\x1b[Z"));
        Assert.True(t.Element.IsFocused);
    }

    [Fact]
    public void ApplicationCursorMode_ChangesTheArrows()
    {
        using var t = Term.Shown();
        t.Emit("\x1b[?1h");
        t.Window.PressKey(KeyCode.Up);
        Assert.Equal("\x1bOA", t.Expect("A"));
    }

    [Fact]
    public void AltGr_SendsTheCharacterItTyped_NotTheChord()
    {
        // Windows reports AltGr+2 (@ on a Swedish keyboard) as Ctrl+Alt+2 followed by the text.
        using var t = Term.Shown();
        t.Window.KeyDown(KeyCode.Digit2, KeyModifiers.Ctrl | KeyModifiers.Alt);
        t.Window.TypeText("@");
        t.Window.KeyUp(KeyCode.Digit2, KeyModifiers.Ctrl | KeyModifiers.Alt);
        Assert.Equal("@", t.Settle());
    }

    [Fact]
    public void ACtrlAltChord_ThatTypesNothing_SendsTheChord()
    {
        using var t = Term.Shown();
        t.Window.PressKey(KeyCode.A, KeyModifiers.Ctrl | KeyModifiers.Alt);
        Assert.Equal(Escape("\x1b\x01"), Escape(t.Expect("\x01")));
    }

    [Fact]
    public void AShortcutTheAppHandles_NeverReachesTheProgram()
    {
        int handled = 0;
        using var t = Term.Shown(around: e => Flex.Column()
            .OnKeyDown(k => { if (k.Key == KeyCode.K && k.Modifiers == KeyModifiers.Ctrl) { handled++; k.Handled = true; } })
            .Children(e));

        t.Window.PressKey(KeyCode.K, KeyModifiers.Ctrl);
        t.Window.PressKey(KeyCode.L, KeyModifiers.Ctrl);

        Assert.Equal(1, handled);
        Assert.Equal("\x0c", t.Expect("\x0c")); // Ctrl+L went through, Ctrl+K did not
    }

    [Fact]
    public void AKeyTheTerminalTakes_DoesNotBubble()
    {
        int seen = 0;
        using var t = Term.Shown(around: e => Flex.Column().OnKeyDown(_ => seen++).Children(e));
        t.Window.PressKey(KeyCode.Enter);
        t.Window.PressKey(KeyCode.Up);
        t.Expect("\x1b[A");
        Assert.Equal(0, seen);
    }

    [Fact]
    public void AComposition_IsDrawnAtTheCursor_AndCommittedAsText()
    {
        using var t = Term.Shown(recording: true);
        t.Emit("$ ");
        t.Window.RenderLog.Clear();
        EventDispatcher.Dispatch(new TextEditingEvent { Text = "にほ", SegmentStart = 0, SegmentLength = 2 }, t.Element!);
        t.Session.PumpFrame();
        Assert.True(t.Window.RenderLog.Contains("\"にほ\""), t.Window.RenderLog.ToString());
        Assert.Equal("", t.Settle());

        t.Window.TypeText("日本");
        Assert.Equal("日本", t.Expect("本"));
    }

    [Fact]
    public void WhileFocused_TheInputMethodIsPlacedAtTheCursor()
    {
        using var t = Term.Shown();
        t.Emit("abc\r\nde");
        t.Session.PumpFrame();
        var area = t.Window.TextInputHost.LastInputArea!.Value;
        var box = t.Element!.ScreenContentBox;
        var (w, h) = t.Element.CellSize;
        Assert.Equal(box.X + 2 * w, area.X, 0.01f);
        Assert.Equal(box.Y + 1 * h, area.Y, 0.01f);
    }

    // ──── Selection and the clipboard ────

    [Fact]
    public void ADrag_Selects_AndCtrlShiftC_Copies()
    {
        using var t = Term.Shown();
        t.Emit("hello world\r\nsecond line");
        Drag(t, (6, 0), (5, 1));
        Assert.Equal("world\nsecond", t.Element!.SelectedText);

        t.Window.PressKey(KeyCode.C, KeyModifiers.Ctrl | KeyModifiers.Shift);
        Assert.Equal("world\nsecond", t.Window.Clipboard.GetText());
        Assert.Equal("", t.Settle()); // copying sends nothing
    }

    [Fact]
    public void ADoubleClick_SelectsAWord_AndATripleClick_ALine()
    {
        using var t = Term.Shown();
        t.Emit("git commit --amend");
        var (x, y) = t.CellCentre(5, 0);
        t.Window.DoubleClick(x, y);
        Assert.Equal("commit", t.Element!.SelectedText);

        t.Window.Click(x, y);
        Assert.Equal("git commit --amend", t.Element.SelectedText?.TrimEnd());
    }

    [Fact]
    public void AClick_ClearsTheSelection()
    {
        using var t = Term.Shown();
        t.Emit("hello world");
        Drag(t, (0, 0), (4, 0));
        Assert.Equal("hello", t.Element!.SelectedText);

        t.Session.PumpFrame(TimeSpan.FromSeconds(1)); // past the double-click window
        var (x, y) = t.CellCentre(8, 0);
        t.Window.Click(x, y);
        Assert.Null(t.Element.SelectedText);
    }

    [Fact]
    public void Typing_DropsTheSelection()
    {
        using var t = Term.Shown();
        t.Emit("hello world");
        Drag(t, (0, 0), (4, 0));
        t.Window.TypeText("x");
        t.Expect("x");
        Assert.Null(t.Element!.SelectedText);
    }

    [Fact]
    public void CtrlShiftV_Pastes_BracketedWhenAsked()
    {
        using var t = Term.Shown();
        t.Emit("\x1b[?2004h");
        t.Window.Clipboard.SetText("echo hi");
        t.Window.PressKey(KeyCode.V, KeyModifiers.Ctrl | KeyModifiers.Shift);
        Assert.Equal(Escape("\x1b[200~echo hi\x1b[201~"), Escape(t.Expect("\x1b[201~")));
    }

    [Fact]
    public void AProgramsClipboardWrite_ReachesTheWindowsClipboard()
    {
        using var t = Term.Shown();
        t.Emit("\x1b]52;c;" + Convert.ToBase64String("from vim"u8) + "\x07");
        Assert.Equal("from vim", t.Window.Clipboard.GetText());
    }

    // ──── Scrollback ────

    [Fact]
    public void TheWheel_ScrollsTheScrollback_AndTypingReturnsToTheBottom()
    {
        using var t = Term.Shown();
        t.Emit(Lines(200));
        var bottom = t.Snapshot();
        Assert.True(bottom.AtBottom);

        var (x, y) = t.CellCentre(2, 2);
        t.Window.Wheel(x, y, 0, 2);
        t.Session.PumpFrame();
        var up = t.Snapshot();
        Assert.Equal(bottom.ViewportTop - 6, up.ViewportTop);
        Assert.False(up.AtBottom);

        t.Window.TypeText("q");
        t.Expect("q");
        Assert.True(t.Snapshot().AtBottom);
    }

    [Fact]
    public void APreciseWheel_AddsUpToWholeNotches()
    {
        using var t = Term.Shown();
        t.Emit(Lines(200));
        int top = t.Snapshot().ViewportTop;
        var (x, y) = t.CellCentre(2, 2);
        t.Window.Wheel(x, y, 0, 0.4f);
        t.Window.Wheel(x, y, 0, 0.4f);
        Assert.Equal(top, t.Snapshot().ViewportTop);
        t.Window.Wheel(x, y, 0, 0.4f);
        Assert.Equal(top - 3, t.Snapshot().ViewportTop);
    }

    [Fact]
    public void ShiftPageUp_ScrollsAPage()
    {
        using var t = Term.Shown();
        t.Emit(Lines(200));
        var before = t.Snapshot();
        t.Window.PressKey(KeyCode.PageUp, KeyModifiers.Shift);
        Assert.Equal(before.ViewportTop - (before.Rows - 1), t.Snapshot().ViewportTop);
        Assert.Equal("", t.Settle());
    }

    [Fact]
    public void OnTheAlternateScreen_TheWheelSendsArrows()
    {
        using var t = Term.Shown();
        t.Emit("\x1b[?1049h");
        var (x, y) = t.CellCentre(2, 2);
        t.Window.Wheel(x, y, 0, -1);
        Assert.Equal(Escape("\x1b[B\x1b[B\x1b[B"), Escape(t.Expect("\x1b[B\x1b[B\x1b[B")));
    }

    // ──── Mouse and focus reports ────

    [Fact]
    public void WhenTheProgramTracksTheMouse_AClickIsReported()
    {
        using var t = Term.Shown();
        t.Emit("\x1b[?1000h\x1b[?1006h");
        var (x, y) = t.CellCentre(2, 1);
        t.Window.Click(x, y);
        Assert.Equal(Escape("\x1b[<0;3;2M\x1b[<0;3;2m"), Escape(t.Expect("m")));
        Assert.Null(t.Element!.SelectedText);
    }

    [Fact]
    public void WhenTheProgramTracksTheMouse_ShiftStillSelects()
    {
        using var t = Term.Shown();
        t.Emit("hello\x1b[?1000h\x1b[?1006h");
        Drag(t, (0, 0), (4, 0), KeyModifiers.Shift);
        Assert.Equal("hello", t.Element!.SelectedText);
        Assert.Equal("", t.Settle());
    }

    [Fact]
    public void WhenTheProgramTracksTheMouse_TheWheelIsReported()
    {
        using var t = Term.Shown();
        t.Emit("\x1b[?1000h\x1b[?1006h");
        var (x, y) = t.CellCentre(0, 0);
        t.Window.Wheel(x, y, 0, 1);
        Assert.Equal(Escape("\x1b[<64;1;1M"), Escape(t.Expect("M")));
    }

    [Fact]
    public void FocusReports_AreSent_WhenAsked()
    {
        using var t = Term.Shown(around: e => Flex.Column().Children(Focusable("other"), e));
        t.Element!.Focus();
        t.Session.PumpFrame();
        t.Emit("\x1b[?1004h");

        t.Session.Window.GetByText("other").Focus();
        t.Session.PumpFrame();
        Assert.Equal(Escape("\x1b[O"), Escape(t.Expect("\x1b[O")));

        t.Element.Focus();
        Assert.Equal(Escape("\x1b[I"), Escape(t.Expect("\x1b[I")));
    }

    // ──── Accessibility ────

    [Fact]
    public void TheProgramsTitle_NamesTheElement_UnlessTheAppNamedIt()
    {
        using var t = Term.Shown();
        Assert.Equal(AccessibilityRole.Document, t.Element!.Role);
        Assert.Equal("Terminal", t.Element.AccessibleName);

        t.Emit("\x1b]2;vim notes.md\x07");
        Assert.Equal("vim notes.md", t.Element.AccessibleName);

        t.Element.AccessibleName = "Build output";
        t.Emit("\x1b]2;make\x07");
        Assert.Equal("Build output", t.Element.AccessibleName);
    }

    // ──── Drawing ────

    [Fact]
    public void ARow_IsOneDrawText_WithASpanPerStyle()
    {
        using var t = Term.Shown(recording: true);
        t.Window.RenderLog.Clear();
        t.Emit("plain \x1b[31mred\x1b[0m done");

        var row = t.Window.RenderLog.Named("DrawText").Select(o => o.Args).Single(a => a.StartsWith("\"plain"));
        Assert.StartsWith("\"plain red done\"", row);
        Assert.Contains("spans=[0+6:", row);
        Assert.Contains("6+3:#C50F1F", row); // Campbell red
    }

    [Fact]
    public void BoxDrawing_IsGeometry_NotGlyphs()
    {
        using var t = Term.Shown(recording: true);
        t.Window.RenderLog.Clear();
        t.Emit("┌─┐\r\n│x│\r\n└─┘");
        var text = string.Join("\n", t.Window.RenderLog.Named("DrawText").Select(o => o.Args));
        Assert.DoesNotContain("┌", text);
        Assert.DoesNotContain("─", text);
        Assert.Contains("\" x\"", text); // the frame's columns are spaces in the row's text
        Assert.True(t.Window.RenderLog.Named("DrawRect").Count() >= 8);
    }

    [Fact]
    public void AWideCharacter_IsDrawnAtItsColumns_AndTheRestOfTheRowKeepsItsPlace()
    {
        using var t = Term.Shown(recording: true);
        t.Window.RenderLog.Clear();
        t.Emit("a日b");
        var ops = t.Window.RenderLog.Named("DrawText").Select(o => o.Args).ToList();
        Assert.Contains(ops, a => a.StartsWith("\"a  b\""));
        Assert.Contains(ops, a => a.StartsWith("\"日\""));
    }

    [Fact]
    public void ACharacterTheFaceLacks_IsDrawnAtItsColumn_AndOneItHas_StaysInTheRow()
    {
        // In the row's text a missing glyph is drawn from a fallback font, whose advance pushed
        // the rest of the row off its columns (the first sample run showed ≥ landing on the ★
        // after it). The face is asked, so this holds for whichever monospace face the platform
        // has: Cascadia Mono and Consolas lack ∀, DejaVu Sans Mono has it. No monospace face
        // has Tibetan.
        var style = TextMeasure.StyleFor(13f, FontFamilies.Monospace);
        using var t = Term.Shown(recording: true);
        bool hasForAll = TextMeasure.HasGlyph(style, '∀') == true;
        Assert.False(TextMeasure.HasGlyph(style, 'ༀ'));

        t.Window.RenderLog.Clear();
        t.Emit("x ∀ ༀ y — ok…");
        var ops = t.Window.RenderLog.Named("DrawText").Select(o => o.Args).ToList();

        Assert.Contains(ops, a => a.StartsWith(hasForAll ? "\"x ∀   y — ok…\"" : "\"x     y — ok…\""));
        Assert.Contains(ops, a => a.StartsWith("\"ༀ\""));
        Assert.Equal(!hasForAll, ops.Any(a => a.StartsWith("\"∀\"")));
    }

    [Fact]
    public void ARightToLeftLetter_IsDrawnAtItsColumn_EvenWhenTheFaceHasIt()
    {
        // In the row's text bidi would reorder it; the program placed it, so it stays put.
        using var t = Term.Shown(recording: true);
        t.Window.RenderLog.Clear();
        t.Emit("aשb");
        var ops = t.Window.RenderLog.Named("DrawText").Select(o => o.Args).ToList();
        Assert.Contains(ops, a => a.StartsWith("\"a b\""));
        Assert.Contains(ops, a => a.StartsWith("\"ש\""));
    }

    [Fact]
    public void TheCell_IsTheFacesCharacterWidth_AndLineHeight()
    {
        using var t = Term.Shown();
        var font = t.Element!.Font;
        Assert.True(font.IsFixedPitch, font.FamilyName);
        Assert.Equal(font.CharacterWidth, t.Element.CellSize.Width);
        Assert.True(t.Element.CellSize.Height >= font.LineHeight && t.Element.CellSize.Height < font.LineHeight + 1);
    }

    [Fact]
    public void AFamilyThatIsNotInstalled_FallsBackToMonospace()
    {
        using var t = Term.Shown(new TerminalOptions { FontFamily = "No Such Font 4711" });
        Assert.True(t.Element!.Font.IsFixedPitch, t.Element.Font.FamilyName);
    }

    [Fact]
    public void SynchronizedOutput_HoldsTheLastCompleteScreen_ForAtMost150ms()
    {
        using var t = Term.Shown(recording: true);
        t.Emit("before");
        t.Window.RenderLog.Clear();

        t.Emit("\x1b[?2026h\x1b[2J\x1b[Hafter");
        t.Session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        Assert.False(t.Window.RenderLog.Contains("\"after\""));
        Assert.True(t.Window.RenderLog.Contains("\"before\""));

        t.Session.PumpFrame(TimeSpan.FromMilliseconds(200));
        t.Session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        Assert.True(t.Window.RenderLog.Contains("\"after\""));
    }

    [Fact]
    public void SynchronizedOutput_ShowsTheUpdate_WhenItCloses()
    {
        using var t = Term.Shown(recording: true);
        t.Emit("before");
        t.Emit("\x1b[?2026h\x1b[2J\x1b[Hafter");
        t.Window.RenderLog.Clear();
        t.Emit("\x1b[?2026l");
        Assert.True(t.Window.RenderLog.Contains("\"after\""));
    }

    [Fact]
    public void TheCursor_IsHollow_WhenTheElementIsNotFocused()
    {
        using var t = Term.Shown(recording: true, around: e => Flex.Column().Children(Focusable("other"), e));
        t.Element!.Focus();
        t.Session.PumpFrame();
        t.Window.RenderLog.Clear();
        t.Session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        Assert.Empty(t.Window.RenderLog.Named("DrawRoundedRect"));

        t.Session.Window.GetByText("other").Focus();
        t.Session.PumpFrame();
        t.Window.RenderLog.Clear();
        t.Session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        Assert.Contains(t.Window.RenderLog.Named("DrawRoundedRect"), o => o.Args.Contains("stroke="));
    }

    // ──── Lifetime ────

    [Fact]
    public void Unmounting_StopsListening()
    {
        using var t = Term.Shown();
        Assert.True(t.Element!.IsListening);
        t.Window.SetRoot(Box.New());
        t.Session.PumpFrame();
        t.Emit("more");
        Assert.False(t.Element.IsListening);
    }

    [Fact]
    public void ADisposedController_LeavesTheLastScreenUp()
    {
        using var t = Term.Shown(recording: true);
        t.Emit("last words");
        t.Controller.Dispose();
        t.Window.RenderLog.Clear();
        t.Session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        Assert.True(t.Window.RenderLog.Contains("\"last words\""));
        t.Window.TypeText("ignored");
        t.Window.PressKey(KeyCode.Enter);
    }

    // ──── Helpers ────

    private static Element Focusable(string name)
    {
        var box = Box.New().Height(20f).FlexShrink(0);
        box.IsFocusable = true;
        box.Role = AccessibilityRole.Button;
        box.AccessibleName = name;
        return box;
    }

    private static void Drag(Term t, (int Col, int Row) from, (int Col, int Row) to, KeyModifiers mods = KeyModifiers.None)
    {
        var a = t.CellCentre(from.Col, from.Row);
        var b = t.CellCentre(to.Col, to.Row);
        t.Window.Drag(new Point(a.X, a.Y), new Point(b.X, b.Y), modifiers: mods);
        t.Session.PumpFrame();
    }
}
