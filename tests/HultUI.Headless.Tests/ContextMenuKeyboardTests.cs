using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// Context menus from the keyboard. The Menu key and Shift+F10 open the focused element's menu
/// under it when nothing took the key, with its first item highlighted and its mnemonics shown; an
/// open menu takes its keys — Home, End, Space, Backspace, Tab, mnemonics and type-ahead — without
/// taking the focus, and the text of a key it took reaches no element. Letters, Backspace and Tab went
/// to the focused element under the menu, and no key opened one.
/// </summary>
public class ContextMenuKeyboardTests
{
    private sealed class Scene
    {
        public required HeadlessSession Session { get; init; }
        public required BoxElement Box { get; init; }
        public required TextInputElement Field { get; init; }
        public required List<string> Chosen { get; init; }
        public HeadlessWindow Window => Session.Window;
        public ContextMenuElement? Menu => Window.OverlayContents.OfType<ContextMenuElement>().FirstOrDefault();
        public int Menus => Window.OverlayContents.OfType<ContextMenuElement>().Count();
    }

    /// <summary>A focused box with a menu, and a text field, in a 400×300 window.</summary>
    private static Scene Mount(Func<List<string>, IReadOnlyList<ContextMenuItem>>? items = null, bool atBottom = false)
    {
        var session = HeadlessSession.Create(400, 300);
        var chosen = new List<string>();
        var box = Box.New().Width(120f).Height(30f);
        box.IsFocusable = true;
        box.ContextMenu(() => items?.Invoke(chosen) ??
        [
            new() { Label = "&Copy", UseMnemonic = true, Action = () => chosen.Add("Copy") },
            new() { Label = "&Paste", UseMnemonic = true, Action = () => chosen.Add("Paste") },
        ]);
        var field = TextInput.New("abc");
        var root = Flex.Column(gap: 8).Padding(20f).FlexGrow(1);
        if (atBottom) root.Children(field, Box.New().FlexGrow(1), box);
        else root.Children(box, field);
        session.Window.SetRoot(root);
        session.PumpFrame();
        box.Focus();
        session.PumpFrame();
        return new Scene { Session = session, Box = box, Field = field, Chosen = chosen };
    }

    private static readonly ContextMenuItem[] Words =
    [
        new() { Label = "Copy" },
        new() { Label = "Close" },
        new() { Label = "Close others" },
        new() { Label = "Paste" },
    ];

    private static void Type(HeadlessWindow window, string letters)
    {
        foreach (char c in letters)
            window.PressKey((KeyCode)((int)KeyCode.A + (char.ToLowerInvariant(c) - 'a')), layoutKey: c, text: c.ToString());
    }

    [Theory]
    [InlineData(KeyCode.F10, KeyModifiers.Shift)]
    [InlineData(KeyCode.Menu, KeyModifiers.None)]
    public void TheMenuKeys_OpenTheFocusedElementsMenu_UnderIt_OnItsFirstItem(KeyCode key, KeyModifiers modifiers)
    {
        var s = Mount();
        using var _ = s.Session;

        s.Window.PressKey(key, modifiers);
        s.Session.PumpFrame();

        Assert.Equal(1, s.Menus);
        var box = s.Box.WindowBorderBox;
        var menu = s.Menu!.WindowBorderBox;
        Assert.Equal(box.X, menu.X, 0.5f);
        Assert.Equal(box.Bottom, menu.Y, 0.5f);
        Assert.Equal(0, s.Menu.HoveredIndex);
        Assert.Same(s.Box, s.Window.Window.FocusedElement);
    }

    [Fact]
    public void ABoxAtTheWindowsBottom_GetsItsMenuAboveIt()
    {
        var s = Mount(atBottom: true);
        using var _ = s.Session;

        s.Window.PressKey(KeyCode.F10, KeyModifiers.Shift);
        s.Session.PumpFrame();

        var box = s.Box.WindowBorderBox;
        var menu = s.Menu!.WindowBorderBox;
        Assert.Equal(box.Y, menu.Bottom, 0.5f);
        Assert.Equal(box.X, menu.X, 0.5f);
    }

    [Fact]
    public void AHandlerThatTakesTheKey_KeepsIt()
    {
        var s = Mount();
        using var _ = s.Session;
        s.Box.On<KeyDownEvent>(e => { if (e.Key == KeyCode.Menu) e.Handled = true; });

        s.Window.PressKey(KeyCode.Menu);
        s.Session.PumpFrame();

        Assert.Equal(0, s.Menus);
    }

    [Fact]
    public void AHandlerThatOpensItsOwnMenu_WithoutStoppingTheKey_GetsOnlyItsOwn()
    {
        var s = Mount();
        using var _ = s.Session;
        s.Box.On<KeyDownEvent>(e =>
        {
            if (e.Key == KeyCode.Menu)
                s.Window.Window.OpenContextMenu([new ContextMenuItem { Label = "Own" }], new Point(200, 200));
        });

        s.Window.PressKey(KeyCode.Menu);
        s.Session.PumpFrame();

        Assert.Equal(1, s.Menus);
        Assert.Equal("Own", s.Menu!.ItemAt(0)!.Label);
    }

    [Fact]
    public void TheKeys_AreTheWindowsToSet()
    {
        var s = Mount();
        using var _ = s.Session;

        s.Window.Window.ContextMenuKeys = [];
        s.Window.PressKey(KeyCode.Menu);
        s.Session.PumpFrame();
        Assert.Equal(0, s.Menus);

        s.Window.Window.ContextMenuKeys = [new KeyChord(KeyCode.M, KeyModifiers.Ctrl)];
        s.Window.PressKey(KeyCode.M, KeyModifiers.Ctrl, layoutKey: 'm');
        s.Session.PumpFrame();
        Assert.Equal(1, s.Menus);

        Assert.Throws<ArgumentNullException>(() => s.Window.Window.ContextMenuKeys = null!);
    }

    [Fact]
    public void WithNoMenu_TheKeyOpensNothing_AndTheFocusedElementHadIt()
    {
        using var session = HeadlessSession.Create(400, 300);
        var box = Box.New().Width(120f).Height(30f);
        box.IsFocusable = true;
        int keys = 0;
        box.On<KeyDownEvent>(e => { if (e.Key == KeyCode.F10) keys++; });
        session.Window.SetRoot(Flex.Column().Padding(20f).Children(box));
        session.PumpFrame();
        box.Focus();

        session.Window.PressKey(KeyCode.F10, KeyModifiers.Shift);
        session.PumpFrame();

        Assert.Equal(1, keys);
        Assert.Empty(session.Window.OverlayContents.OfType<ContextMenuElement>());
        Assert.False(session.Window.Window.OpenContextMenu(box));
    }

    [Fact]
    public void OpenContextMenu_OfAnElementWithAMenu_OpensItAsTheKeyDoes()
    {
        var s = Mount();
        using var _ = s.Session;

        Assert.True(s.Window.Window.OpenContextMenu(s.Box));
        s.Session.PumpFrame();

        Assert.Equal(1, s.Menus);
        Assert.Equal(0, s.Menu!.HoveredIndex);
        Assert.True(s.Menu.ShowMnemonics);
    }

    [Fact]
    public void InAFocusedTextField_ShiftF10_OpensItsEditMenu()
    {
        var s = Mount();
        using var _ = s.Session;
        s.Field.Focus();
        s.Session.PumpFrame();

        s.Window.PressKey(KeyCode.F10, KeyModifiers.Shift);
        s.Session.PumpFrame();

        Assert.Equal("Undo", s.Menu!.ItemAt(0)!.Label);
        var field = s.Field.WindowBorderBox;
        Assert.Equal(field.Bottom, s.Menu.WindowBorderBox.Y, 0.5f);
    }

    [Fact]
    public void AMnemonicThatFocusesAField_TypesNothing_ThereOrUnderTheMenu()
    {
        using var session = HeadlessSession.Create(400, 300);
        var first = TextInput.New("one");
        var second = TextInput.New("two");
        session.Window.SetRoot(Flex.Column(gap: 8).Padding(20f).Children(first, second));
        session.PumpFrame();
        first.Focus();
        session.Window.Window.OpenContextMenu(
            [new ContextMenuItem { Label = "&Rename…", UseMnemonic = true, Action = second.Focus }], new Point(40, 40));
        session.PumpFrame();

        session.Window.PressKey(KeyCode.R, layoutKey: 'r', text: "r");
        session.PumpFrame();

        Assert.Empty(session.Window.OverlayContents.OfType<ContextMenuElement>());
        Assert.Same(second, session.Window.Window.FocusedElement);
        Assert.Equal("one", first.Text);
        Assert.Equal("two", second.Text);
    }

    [Fact]
    public void ALetterNoItemHas_ReachesNothingUnderTheMenu()
    {
        var s = Mount();
        using var _ = s.Session;
        s.Field.Focus();
        int keys = 0;
        s.Field.On<KeyDownEvent>(e => { if (e.Key == KeyCode.Z) keys++; });
        s.Window.Window.OpenContextMenu(Words, new Point(40, 40));
        s.Session.PumpFrame();

        s.Window.PressKey(KeyCode.Z, layoutKey: 'z', text: "z");
        s.Session.PumpFrame();

        Assert.Equal(1, s.Menus);
        Assert.Equal(0, keys);
        Assert.Equal("abc", s.Field.Text);
    }

    [Fact]
    public void OnARussianLayout_TheLetterTypedIsTheOneMatched()
    {
        var s = Mount();
        using var _ = s.Session;
        s.Window.Window.OpenContextMenu([new ContextMenuItem { Label = "Сохранить" }, new ContextMenuItem { Label = "Copy" }],
            new Point(40, 40));
        s.Session.PumpFrame();

        s.Window.PressKey(KeyCode.C, layoutKey: 'c', text: "с");

        Assert.Equal(0, s.Menu!.HoveredIndex);
    }

    /// <summary>The WinForms host's AltGr: Ctrl+Alt with no text, then the character on its own.</summary>
    [Fact]
    public void TextWithoutAKey_IsTheMenus_NotTheFields()
    {
        var s = Mount();
        using var _ = s.Session;
        s.Field.Focus();
        s.Window.Window.OpenContextMenu([new ContextMenuItem { Label = "Copy" }, new ContextMenuItem { Label = "@home" }],
            new Point(40, 40));
        s.Session.PumpFrame();

        s.Window.KeyDown(KeyCode.Q, KeyModifiers.Ctrl | KeyModifiers.Alt);
        s.Window.TypeText("@");
        s.Session.PumpFrame();

        Assert.Equal("abc", s.Field.Text);
        Assert.Equal(1, s.Menu!.HoveredIndex);
    }

    /// <summary>The WinForms host: a key brings no text (null), and its character comes after it on
    /// its own. Each character is typed in the menu once, and none reaches the field.</summary>
    [Fact]
    public void AKeyWhoseTextComesAfterIt_TypesInTheMenuOnce()
    {
        var s = Mount();
        using var _ = s.Session;
        s.Field.Focus();
        s.Window.Window.OpenContextMenu([new ContextMenuItem { Label = "Cut" }, new ContextMenuItem { Label = "Copy" }],
            new Point(40, 40));
        s.Session.PumpFrame();

        s.Window.KeyDown(KeyCode.C);
        s.Window.TypeText("c");

        Assert.Equal(0, s.Menu!.HoveredIndex);   // Cut: "c" once, not "cc"
        Assert.Equal("abc", s.Field.Text);
    }

    [Fact]
    public void AMnemonicWhoseTextComesAfterIt_RunsOnce_AndTypesNothing()
    {
        var s = Mount();
        using var _ = s.Session;
        s.Field.Focus();
        int ran = 0;
        s.Window.Window.OpenContextMenu([new ContextMenuItem { Label = "&Rename…", UseMnemonic = true, Action = () => ran++ }],
            new Point(40, 40));
        s.Session.PumpFrame();

        s.Window.KeyDown(KeyCode.R);
        s.Window.TypeText("r");
        s.Session.PumpFrame();

        Assert.Equal(1, ran);
        Assert.Equal("abc", s.Field.Text);
    }

    [Fact]
    public void ASpaceWhoseTextComesAfterIt_Chooses_AndTypesNothing()
    {
        var s = Mount();
        using var _ = s.Session;
        s.Field.Focus();
        int ran = 0;
        s.Window.Window.OpenContextMenu([new ContextMenuItem { Label = "Copy", Action = () => ran++ }], new Point(40, 40));
        s.Session.PumpFrame();
        s.Window.PressKey(KeyCode.Down);

        s.Window.KeyDown(KeyCode.Space);
        s.Window.TypeText(" ");
        s.Session.PumpFrame();

        Assert.Equal(1, ran);
        Assert.Equal("abc", s.Field.Text);
    }

    [Fact]
    public void ASpaceWhoseTextComesAfterIt_WhileTypingAhead_IsPartOfTheText()
    {
        var s = Mount();
        using var _ = s.Session;
        s.Window.Window.OpenContextMenu(Words, new Point(40, 40));
        s.Session.PumpFrame();

        Type(s.Window, "close");
        s.Window.KeyDown(KeyCode.Space);
        s.Window.TypeText(" ");
        Type(s.Window, "o");

        Assert.Equal(2, s.Menu!.HoveredIndex);   // "close o": Close others
    }

    [Fact]
    public void ACompositionUnderAMenu_DoesNotReachTheField()
    {
        var s = Mount();
        using var _ = s.Session;
        s.Field.Focus();
        s.Window.Window.OpenContextMenu(Words, new Point(40, 40));
        s.Session.PumpFrame();

        ((HeadlessPlatformWindow)s.Window.Window.PlatformWindow).RaiseInput(new InputEvent.TextEditingInput("か", 0, 1));
        s.Session.PumpFrame();

        Assert.Equal("", s.Field.CompositionText);
        Assert.Equal("abc", s.Field.Text);
    }

    [Fact]
    public void BackspaceAndTab_UnderAMenu_LeaveTheFieldAndTheFocus()
    {
        var s = Mount();
        using var _ = s.Session;
        s.Field.Focus();
        s.Window.Window.OpenContextMenu(Words, new Point(40, 40));
        s.Session.PumpFrame();

        s.Window.PressKey(KeyCode.Backspace);
        s.Window.PressKey(KeyCode.Tab);
        s.Window.PressKey(KeyCode.Tab, KeyModifiers.Shift);
        s.Session.PumpFrame();

        Assert.Equal("abc", s.Field.Text);
        Assert.Same(s.Field, s.Window.Window.FocusedElement);
        Assert.Equal(1, s.Menus);
    }

    [Fact]
    public void HomeEndPageUpAndPageDown_GoToTheEnds()
    {
        var s = Mount();
        using var _ = s.Session;
        s.Window.Window.OpenContextMenu(Words, new Point(40, 40));
        s.Session.PumpFrame();

        s.Window.PressKey(KeyCode.End);
        Assert.Equal(3, s.Menu!.HoveredIndex);
        s.Window.PressKey(KeyCode.Home);
        Assert.Equal(0, s.Menu.HoveredIndex);
        s.Window.PressKey(KeyCode.PageDown);
        Assert.Equal(3, s.Menu.HoveredIndex);
        s.Window.PressKey(KeyCode.PageUp);
        Assert.Equal(0, s.Menu.HoveredIndex);
    }

    [Fact]
    public void TypeAhead_RunsOnTheSessionsClock()
    {
        var s = Mount();
        using var _ = s.Session;
        s.Window.Window.OpenContextMenu(Words, new Point(40, 40));
        s.Session.PumpFrame();

        Type(s.Window, "clo");
        Assert.Equal(1, s.Menu!.HoveredIndex);   // Close

        s.Session.AdvanceTime(TimeSpan.FromMilliseconds(1100));
        Type(s.Window, "c");
        Assert.Equal(2, s.Menu.HoveredIndex);    // a new run: Close others
        Type(s.Window, "c");
        Assert.Equal(0, s.Menu.HoveredIndex);    // Copy
        Type(s.Window, "c");
        Assert.Equal(1, s.Menu.HoveredIndex);    // Close

        s.Session.AdvanceTime(TimeSpan.FromMilliseconds(1100));
        Type(s.Window, "x");
        Assert.Equal(1, s.Menu.HoveredIndex);    // no match: the highlight stays
        Assert.Equal(1, s.Menus);
    }

    [Fact]
    public void SpaceWhileTypingAhead_IsPartOfTheText_AndOnItsOwnChooses()
    {
        var s = Mount(chosen =>
        [
            new() { Label = "Copy", Action = () => chosen.Add("Copy") },
            new() { Label = "Close" },
            new() { Label = "Close others" },
        ]);
        using var _ = s.Session;
        s.Window.Window.OpenContextMenu(s.Box);
        s.Session.PumpFrame();

        Type(s.Window, "close");
        s.Window.PressKey(KeyCode.Space, text: " ");
        Type(s.Window, "o");
        Assert.Equal(2, s.Menu!.HoveredIndex);   // "close o": Close others

        s.Session.AdvanceTime(TimeSpan.FromMilliseconds(1100));
        s.Window.PressKey(KeyCode.Home);
        s.Window.PressKey(KeyCode.Space, text: " ");
        s.Session.PumpFrame();
        Assert.Equal(0, s.Menus);
    }

    [Fact]
    public void Mnemonics_AreUnderlined_InAMenuOpenedFromTheKeyboard_OrOnceAKeyIsUsed()
    {
        var s = Mount();
        using var _ = s.Session;

        s.Window.PressKey(KeyCode.Menu);
        s.Session.PumpFrame();
        Assert.True(s.Menu!.ShowMnemonics);
        s.Window.PressKey(KeyCode.Escape);
        s.Session.PumpFrame();

        s.Window.RightClick(s.Box);
        s.Session.PumpFrame();
        Assert.False(s.Menu!.ShowMnemonics);
        s.Window.PressKey(KeyCode.Down);
        Assert.True(s.Menu.ShowMnemonics);
    }

    [Fact]
    public void ShiftF10_InAListFlowingInColumns_OpensUnderTheCurrentCell()
    {
        using var session = HeadlessSession.Create(600, 300);
        var list = VirtualListElement<string>.New()
            .ItemHeight(20f)
            .Items(Enumerable.Range(0, 200).Select(i => $"item {i}").ToList())
            .RenderWith(t => Flex.Row().Children(TextB.New(t).FontSize(12f)))
            .Flow(ListFlow.Columns, 150f);
        list.IsFocusable = true;
        list.ContextMenu(() => [new ContextMenuItem { Label = "Open" }]);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(list.FlexGrow(1)));
        session.PumpFrames(2);
        list.ScrollOffsetX = 150f;
        int rows = list.RowsPerColumn;
        list.CurrentIndex = 2 * rows + 3;   // the second column in view
        list.Focus();
        session.PumpFrame();

        session.Window.PressKey(KeyCode.F10, KeyModifiers.Shift);
        session.PumpFrame();

        var content = list.WindowContentBox;
        var menu = session.Window.OverlayContents.OfType<ContextMenuElement>().Single().WindowBorderBox;
        Assert.Equal(content.X + 150f, menu.X, 0.5f);
        Assert.Equal(content.Y + 4 * 20f, menu.Y, 0.5f);
    }

    /// <summary>A list scrolled sideways past the start of its current row: the menu stays in the window.</summary>
    [Fact]
    public void AnAnchorPastTheWindowsLeftEdge_KeepsTheMenuInTheWindow()
    {
        using var session = HeadlessSession.Create(600, 300);
        var list = VirtualListElement<string>.New()
            .ItemHeight(20f)
            .Items(Enumerable.Range(0, 50).Select(i => $"item {i}").ToList())
            .RenderWith(t => TextB.New(t).FontSize(12f))
            .RowWidth(1200);
        list.IsFocusable = true;
        list.ContextMenu(() => [new ContextMenuItem { Label = "Open" }]);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(list.FlexGrow(1)));
        session.PumpFrames(2);
        list.ScrollOffsetX = 300f;
        list.CurrentIndex = 2;
        list.Focus();
        session.PumpFrame();

        session.Window.PressKey(KeyCode.F10, KeyModifiers.Shift);
        session.PumpFrame();

        var menu = session.Window.OverlayContents.OfType<ContextMenuElement>().Single().WindowBorderBox;
        Assert.Equal(0f, menu.X, 0.5f);
        Assert.Equal(list.WindowContentBox.Y + 3 * 20f, menu.Y, 0.5f);
    }

    [Fact]
    public void TypeAhead_InAFlyout_MovesTheReadersFocus()
    {
        using var session = HeadlessSession.Create(600, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        session.Window.SetRoot(Box.New().Width(50f).Height(20f));
        session.PumpFrame();
        session.Window.Window.OpenContextMenu(
        [
            new ContextMenuItem
            {
                Label = "Sort by",
                Children = [new ContextMenuItem { Label = "Name" }, new ContextMenuItem { Label = "Size" }],
            },
        ], new Point(40, 40));
        session.PumpFrame();
        session.Window.PressKey(KeyCode.Down);
        session.Window.PressKey(KeyCode.Right);
        session.PumpFrame();

        Type(session.Window, "s");
        session.PumpFrame();

        var flyout = session.Window.OverlayContents.OfType<ContextMenuElement>().Last();
        Assert.Equal(1, flyout.HoveredIndex);
        var focused = bridge.FocusedNode!;
        Assert.Equal(AccessibilityRole.Menu, focused.Role);
        Assert.Equal(flyout.Id, focused.ElementId);
        Assert.StartsWith("Size", focused.Children[focused.CurrentItem].Name);
    }
}
