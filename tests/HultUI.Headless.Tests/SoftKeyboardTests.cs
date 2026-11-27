using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using Xunit;
using Dialog = HultUI.Core.Dialogs.Dialog;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// The soft keyboard (docs/design/SOFT-KEYBOARD.md §2–3): each field tells the platform what it
/// holds, so a phone shows the keyboard for it, and the window keeps content and the focused
/// field above the keyboard (<c>Window.KeyboardInset</c>). Headless, the keyboard is simulated
/// with <c>SetKeyboardInset</c>, as the bars are with <c>SetSafeArea</c>.
/// </summary>
public sealed class SoftKeyboardTests
{
    // A phone's portrait keyboard, in layout units, and the window it covers the bottom of.
    private const float Keyboard = 120;
    private const int Width = 400, Height = 300;

    // ──── Input kinds (§2) ────

    [Theory]
    [InlineData(TextInputKind.Text)]
    [InlineData(TextInputKind.Number)]
    [InlineData(TextInputKind.Decimal)]
    [InlineData(TextInputKind.Phone)]
    [InlineData(TextInputKind.Email)]
    [InlineData(TextInputKind.Url)]
    [InlineData(TextInputKind.Username)]
    [InlineData(TextInputKind.Name)]
    [InlineData(TextInputKind.Search)]
    public void AFieldsKind_ReachesTheTextInputHost_AsOneLine(TextInputKind kind)
    {
        using var session = HeadlessSession.Create(Width, Height);
        var field = TextInput.New().Kind(kind);
        session.Window.SetRoot(Flex.Column().Children(field));
        session.PumpFrame();

        session.Window.Click(field);

        var host = session.Window.TextInputHost;
        Assert.True(host.IsTextInputActive);
        Assert.Equal(kind, host.Kind);
        Assert.False(host.Multiline, "a text input takes one line: a phone shows its action key, not a new line");
    }

    /// <summary>A field whose keyboard the system put away (Android's Back: SDL stops input by
    /// itself, and the field keeps the focus) gets it back from a press on it or a reader's double
    /// tap. Only a move to another field and back brought it (TalkBack on a Galaxy S25).</summary>
    [Fact]
    public void AFieldWhoseKeyboardWasPutAway_GetsItBack_FromAPressOrAReadersInvoke()
    {
        using var session = HeadlessSession.Create(Width, Height);
        var notes = TextArea.New("alpha bravo").Height(80);
        session.Window.SetRoot(Flex.Column().Children(notes));
        session.PumpFrame();
        session.Window.Click(notes);
        var host = session.Window.TextInputHost;
        Assert.True(host.IsTextInputActive);

        host.StopTextInput();   // the system's Back, as SDL takes it
        session.Window.Click(notes);
        Assert.True(host.IsTextInputActive, "a press on the focused field");

        host.StopTextInput();
        var router = session.Window.InstallActionRouter();
        router.InvokeElement(notes.Id);
        session.PumpFrame();
        Assert.True(host.IsTextInputActive, "a reader's invoke of the focused field");
    }

    /// <summary>A keyboard that went away with the field still focused and input left on: from
    /// Android 16, Back goes to the keyboard's own callback and SDL is never told, so its input stays
    /// on and a start for a field of the same kind showed nothing (Android 17 on a Galaxy S25, with
    /// TalkBack and without). The next start, from a press on the field or a move to another, stops
    /// input first. While the keyboard is up, a press restarts nothing.</summary>
    [Fact]
    public void AKeyboardThatWentAway_WithInputLeftOn_IsStoppedBeforeTheNextStart()
    {
        using var session = HeadlessSession.Create(Width, Height);
        var notes = TextArea.New("alpha bravo").Height(80);
        var other = TextArea.New("charlie").Height(80);
        session.Window.SetRoot(Flex.Column().Children(notes, other));
        session.PumpFrame();
        session.Window.Click(notes);
        session.Window.SetKeyboardInset(Keyboard);
        session.PumpFrame();
        var host = session.Window.TextInputHost;
        int stops = host.StopCount;
        session.Window.Click(notes);
        Assert.Equal(stops, host.StopCount);   // the keyboard is up

        session.Window.SetKeyboardInset(0);   // the keyboard's own Back: nothing else is told
        session.PumpFrame();
        Assert.True(host.IsTextInputActive);
        session.Window.Click(notes);
        Assert.Equal(stops + 1, host.StopCount);
        Assert.True(host.IsTextInputActive);

        // Again, and away to another field of the same kind.
        session.Window.SetKeyboardInset(Keyboard);
        session.Window.SetKeyboardInset(0);
        session.PumpFrame();
        session.Window.Click(other);
        Assert.Equal(stops + 2, host.StopCount);
        Assert.True(host.IsTextInputActive);
    }

    [Fact]
    public void Masked_WinsOverTheKind_AndUnmaskingGivesTheKindBack()
    {
        using var session = HeadlessSession.Create(Width, Height);
        var field = TextInput.New().Kind(TextInputKind.Number).Password();
        session.Window.SetRoot(Flex.Column().Children(field));
        session.PumpFrame();
        session.Window.Click(field);
        var host = session.Window.TextInputHost;
        Assert.Equal(TextInputKind.Password, host.Kind);

        field.Masked = false;   // a reveal toggle

        Assert.Equal(TextInputKind.Number, host.Kind);
    }

    [Fact]
    public void AKindChangedWhileTheFieldHasFocus_SwitchesTheKeyboard_AndTheSameKindDoesNot()
    {
        using var session = HeadlessSession.Create(Width, Height);
        var field = TextInput.New();
        session.Window.SetRoot(Flex.Column().Children(field));
        session.PumpFrame();
        session.Window.Click(field);
        var host = session.Window.TextInputHost;
        int starts = host.StartCount;

        field.InputKind = TextInputKind.Email;
        field.InputKind = TextInputKind.Email;

        Assert.Equal(TextInputKind.Email, host.Kind);
        Assert.Equal(starts + 1, host.StartCount);
    }

    private sealed class KindForm : Component
    {
        public TextInputKind Kind;
        public TextInputElement? Built;
        public void Rebuild() => MarkDirty();
        public override Element Build() => Flex.Column().Children(Built = TextInput.New().Kind(Kind));
    }

    [Fact]
    public void ARebuild_PatchesTheKind_ToTheLiveField()
    {
        using var session = HeadlessSession.Create(Width, Height);
        var form = new KindForm();
        session.Window.SetRoot(form.Mount());
        session.PumpFrame();
        var field = form.Built!;
        session.Window.Click(field);

        form.Kind = TextInputKind.Url;
        form.Rebuild();
        session.PumpFrame();

        Assert.NotSame(field, form.Built);    // a new description of the field, patched into the live one
        Assert.Equal(TextInputKind.Url, field.InputKind);
        Assert.Equal(TextInputKind.Url, session.Window.TextInputHost.Kind);
    }

    [Fact]
    public void ATextArea_AsksForSeveralLines()
    {
        using var session = HeadlessSession.Create(Width, Height);
        var area = TextArea.New("notes").Height(100);
        session.Window.SetRoot(Flex.Column().Children(area));
        session.PumpFrame();

        session.Window.Click(area);

        var host = session.Window.TextInputHost;
        Assert.Equal(TextInputKind.Text, host.Kind);
        Assert.True(host.Multiline, "Enter in a text area makes a new line, so the keyboard shows that key");
    }

    [Fact]
    public void TheRichTextEditor_AsksForSeveralLines()
    {
        using var session = HeadlessSession.Create(Width, Height);
        var editor = RichTextEditor.New().Height(100);
        session.Window.SetRoot(Flex.Column().Children(editor));
        session.PumpFrame();

        editor.Focus();

        Assert.True(session.Window.TextInputHost.Multiline);
    }

    [Theory]
    [InlineData(0, 1, TextInputKind.Number)]       // whole numbers, none below zero: digits
    [InlineData(0, 0.5, TextInputKind.Decimal)]    // a fraction: the point too
    [InlineData(-10, 1, TextInputKind.Decimal)]    // below zero: the minus too
    public void ANumberField_AsksForDigits_WithThePointOrMinusWhenItsRangeNeedsThem(double min, double step, TextInputKind expected)
    {
        using var session = HeadlessSession.Create(Width, Height);
        var number = NumericUpDown.New(value: 1, min: min, max: 100, step: step);
        session.Window.SetRoot(Flex.Column().Children(number));
        session.PumpFrame();

        number.Focus();

        Assert.Equal(expected, session.Window.TextInputHost.Kind);
        Assert.False(session.Window.TextInputHost.Multiline);
    }

    // ──── The keyboard's area (§3) ────

    private static (HeadlessSession Session, BoxElement Root) Started()
    {
        var session = HeadlessSession.Create(Width, Height);
        var root = Box.New();
        session.Window.SetRoot(root);
        session.PumpFrame();
        return (session, root);
    }

    [Fact]
    public void AKeyboard_IsToldOnce_AndTheNextFrameLaysTheRootOutAboveIt()
    {
        var (session, root) = Started();
        using var _ = session;
        var window = session.Window.Window;
        var told = new List<float>();
        int safeAreaTold = 0;
        window.KeyboardInsetChanged += () => told.Add(window.KeyboardInset);
        window.SafeAreaChanged += () => safeAreaTold++;

        session.Window.SetKeyboardInset(Keyboard);
        session.Window.SetKeyboardInset(Keyboard);   // the same: nothing more

        Assert.Equal([Keyboard], told);
        Assert.Equal(0, safeAreaTold);   // the keyboard is not the safe area
        Assert.Equal(Thickness.Zero, window.SafeArea);
        Assert.True(session.PumpFrame());
        Assert.Equal(new Rect(0, 0, Width, Height - Keyboard), root.Layout.AbsoluteBorderBox);

        session.Window.SetKeyboardInset(0);
        session.PumpFrame();

        Assert.Equal([Keyboard, 0f], told);
        Assert.Equal(new Rect(0, 0, Width, Height), root.Layout.AbsoluteBorderBox);
    }

    [Fact]
    public void LaysOutAboveKeyboardOff_LeavesTheRootUnderIt_AndKeyboardInsetStillSays()
    {
        var (session, root) = Started();
        using var _ = session;
        session.Window.Window.LaysOutAboveKeyboard = false;

        session.Window.SetKeyboardInset(Keyboard);
        session.PumpFrame();

        Assert.Equal(new Rect(0, 0, Width, Height), root.Layout.AbsoluteBorderBox);
        Assert.Equal(Keyboard, session.Window.Window.KeyboardInset);
    }

    [Fact]
    public void TheKeyboardAndTheGestureBar_TakeTheLargerOfTheTwo_FromTheBottom()
    {
        // Both are measured from the window's bottom edge: a keyboard over the gesture bar
        // includes it, so the root ends at the keyboard's top, not the sum's.
        var (session, root) = Started();
        using var _ = session;
        session.Window.SetSafeArea(new Thickness(Top: 24, Right: 0, Bottom: 48, Left: 0));

        session.Window.SetKeyboardInset(Keyboard);
        session.PumpFrame();

        Assert.Equal(new Rect(0, 24, Width, Height - 24 - Keyboard), root.Layout.AbsoluteBorderBox);
    }

    /// <summary>A form taller than the window: a scroll view of labelled fields, the last one at
    /// the bottom, where the keyboard comes up over it.</summary>
    private static (ScrollViewElement Scroll, TextInputElement Last, TextInputElement First) Form(HeadlessSession session)
    {
        var first = TextInput.New("first");
        var last = TextInput.New("last");
        var rows = new List<Element> { first };
        for (int i = 0; i < 12; i++) rows.Add(TextB.New($"Row {i}").Height(30f).FlexShrink(0));
        rows.Add(last);
        var scroll = ScrollView.Vertical().FlexGrow(1);
        scroll.Children(Flex.Column(gap: 4).Children(rows.ToArray()));
        session.Window.SetRoot(Flex.Column().Height(Dimension.Percent(100)).Children(scroll));
        session.PumpFrame();
        return (scroll, last, first);
    }

    [Fact]
    public void TheFocusedField_UnderAKeyboardThatComesUp_IsScrolledAboveIt()
    {
        using var session = HeadlessSession.Create(Width, Height);
        var (scroll, last, _) = Form(session);
        scroll.ScrollOffsetY = float.MaxValue;   // the last field at the bottom
        session.PumpFrame();
        last.Focus();
        session.PumpFrame();
        float offsetBefore = scroll.ScrollOffsetY;

        session.Window.SetKeyboardInset(Keyboard);
        session.PumpFrame();

        var shown = last.WindowBorderBox;
        Assert.True(shown.Bottom <= Height - Keyboard + 0.5f, $"the field ends at {shown.Bottom}, under the keyboard's top {Height - Keyboard}");
        Assert.True(shown.Y >= 0, $"the field starts at {shown.Y}, off the top");
        // The view is 120 shorter: the field, at the content's end, is 120 further down it.
        Assert.Equal(offsetBefore + Keyboard, scroll.ScrollOffsetY, 0.5f);
    }

    [Fact]
    public void AFieldUnderTheKeyboard_WhenTheRootIsLaidOutUnderIt_IsScrolledAboveTheKeyboardTop()
    {
        using var session = HeadlessSession.Create(Width, Height);
        session.Window.Window.LaysOutAboveKeyboard = false;
        var field = TextInput.New("low");
        var rows = new List<Element>();
        for (int i = 0; i < 7; i++) rows.Add(TextB.New($"Row {i}").Height(30f).FlexShrink(0));
        rows.Add(field);   // at about 7 × 34 = 238, under a keyboard whose top is at 180
        for (int i = 0; i < 10; i++) rows.Add(TextB.New($"After {i}").Height(30f).FlexShrink(0));
        var scroll = ScrollView.Vertical().FlexGrow(1);
        scroll.Children(Flex.Column(gap: 4).Children(rows.ToArray()));
        session.Window.SetRoot(Flex.Column().Height(Dimension.Percent(100)).Children(scroll));
        session.PumpFrame();
        field.Focus();
        session.PumpFrame();
        Assert.True(field.WindowBorderBox.Bottom > Height - Keyboard, "the test needs the field under the keyboard");

        session.Window.SetKeyboardInset(Keyboard);
        session.PumpFrame();

        Assert.True(field.WindowBorderBox.Bottom <= Height - Keyboard + 0.5f,
            $"the field ends at {field.WindowBorderBox.Bottom}, under the keyboard's top {Height - Keyboard}");
        Assert.Equal(new Rect(0, 0, Width, Height), session.Window.Window.Root!.Layout.AbsoluteBorderBox);
    }

    [Fact]
    public void FocusMovingToAFieldUnderTheKeyboard_ScrollsItAbove()
    {
        using var session = HeadlessSession.Create(Width, Height);
        var (scroll, last, first) = Form(session);
        first.Focus();
        session.PumpFrame();
        session.Window.SetKeyboardInset(Keyboard);
        session.PumpFrame();
        Assert.True(last.WindowBorderBox.Y > Height - Keyboard, "the last field starts out of view");

        last.Focus();   // the keyboard's Next, or a tap on a field
        session.PumpFrame();

        Assert.True(last.WindowBorderBox.Bottom <= Height - Keyboard + 0.5f, $"the field ends at {last.WindowBorderBox.Bottom}");
        Assert.True(scroll.ScrollOffsetY > 0);
    }

    [Fact]
    public void WithNoKeyboard_FocusMovingScrollsNothing()
    {
        // The reveal is the keyboard's: on the desktop, focusing a field by code moves no view.
        using var session = HeadlessSession.Create(Width, Height);
        var (scroll, last, _) = Form(session);

        last.Focus();
        session.PumpFrame();

        Assert.Equal(0, scroll.ScrollOffsetY);
    }

    [Fact]
    public void ADialog_IsCentredAboveTheKeyboard()
    {
        using var session = HeadlessSession.Create(Width, Height);
        session.Window.SetRoot(Box.New());
        session.PumpFrame();
        BoxElement? panel = null;
        _ = Dialog.Show<bool>(session.Window.Window, _ => panel = (BoxElement)Box.New().Width(200).Height(100));
        session.PumpFrame();
        float centredBefore = panel!.WindowBorderBox.Y + 50;
        Assert.Equal(Height / 2f, centredBefore, 1f);

        session.Window.SetKeyboardInset(Keyboard);
        session.PumpFrame();

        var box = panel.WindowBorderBox;
        Assert.True(box.Bottom <= Height - Keyboard + 0.5f, $"the dialog ends at {box.Bottom}, under the keyboard");
        Assert.Equal((Height - Keyboard) / 2f, box.Y + 50, 1f);
    }

    [Fact]
    public void AContextMenuOpenedAtTheBottom_StaysAboveTheKeyboard()
    {
        using var session = HeadlessSession.Create(Width, Height);
        session.Window.Window.LaysOutAboveKeyboard = false;   // so the click lands where the keyboard is
        var box = Box.New().FlexGrow(1);
        box.ContextMenu([new ContextMenuItem { Label = "Copy" }, new ContextMenuItem { Label = "Paste" }]);
        session.Window.SetRoot(Flex.Column().Height(Dimension.Percent(100)).Children(box));
        session.Window.SetKeyboardInset(Keyboard);
        session.PumpFrame();

        session.Window.Click(390, 170, MouseButton.Right);
        session.PumpFrame();

        var menu = Assert.Single(session.Window.OverlayContents.OfType<ContextMenuElement>());
        float bottom = menu.OverlayOrigin.Y + menu.Layout.Height;
        Assert.True(bottom <= Height - Keyboard + 0.01f, $"the menu reaches {bottom}, under the keyboard");
    }
}
