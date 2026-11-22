using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="MenuBar"/>: the pointer opening and switching menus, and the keyboard the Windows
/// way — F10 and Alt to the titles, Alt and a letter to a menu, ← → across menus, Esc back.
/// </summary>
public sealed class MenuBarTests
{
    private sealed class Fixture : IDisposable
    {
        public readonly HeadlessSession Session = HeadlessSession.Create(900, 500);
        public readonly MenuBar Bar;
        public readonly List<string> Ran = [];
        public readonly TextInputElement Document;
        public bool Wrap;

        public Fixture()
        {
            Bar = new MenuBar(
            [
                new("&File", [Item("Open…"), Item("Save"), ContextMenuItem.Separator(), Item("Exit")]),
                new("&Edit", [Item("Undo"), Item("Redo")]),
                new("&View", () => [new ContextMenuItem { Label = "Word wrap", IsChecked = Wrap, Action = () => { Wrap = !Wrap; Ran.Add("wrap"); } }]),
            ]);
            Document = TextInput.New("");
            Document.AutomationId = "document";
            var root = Flex.Column(gap: 0).Children(Bar.Mount(), Document);
            root.On<KeyDownEvent>(e => { if (Bar.HandleKey(e)) e.StopPropagation = true; });
            root.On<KeyUpEvent>(e => Bar.HandleKeyUp(e));
            Session.Window.SetRoot(root);
            Session.PumpFrame();
            Document.Focus();
            Session.PumpFrame();
        }

        private ContextMenuItem Item(string label) => new() { Label = label, Action = () => Ran.Add(label) };

        public HeadlessElement Title(string name) => Session.Window.FindByRole(AccessibilityRole.MenuItem, name)!;

        public void Pump() => Session.PumpFrames(2);

        /// <summary>Whether the open menu shows an item with this label — a context menu's
        /// items are virtual rows in the accessibility tree.</summary>
        public bool MenuShows(string label) =>
            Walk(Session.Window.AccessibilityTree).Any(n => n.Name == label);

        public void Dispose() => Session.Dispose();
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node) =>
        node.Children.SelectMany(Walk).Prepend(node);

    [Fact]
    public void ItIsAMenuBarOfMenuItems_NamedWithoutTheirMnemonicMarkers()
    {
        using var f = new Fixture();
        Assert.NotNull(f.Session.Window.FindByRole(AccessibilityRole.MenuBar));
        foreach (var name in new[] { "File", "Edit", "View" })
        {
            var title = f.Title(name);
            Assert.NotNull(title);
            Assert.Equal(false, title.Element.AccessibleExpanded);
        }
        Assert.Equal('f', f.Bar.Menus[0].Mnemonic);
        Assert.Equal("Save & quit", new MenuBarItem("Save && quit", []).Label);
        Assert.Equal('\0', new MenuBarItem("Save && quit", []).Mnemonic);
    }

    [Fact]
    public void APressOpensAMenu_AnotherClosesIt_AndHoverSwitchesWhileOneIsOpen()
    {
        using var f = new Fixture();
        f.Session.Window.Click(f.Title("File").Element);
        f.Pump();
        Assert.Equal(0, f.Bar.OpenIndex);
        Assert.Equal(true, f.Title("File").Element.AccessibleExpanded);
        Assert.True(f.MenuShows("Open…"));

        // Moving onto Edit while File is open opens Edit instead.
        var edit = f.Title("Edit").Element.Layout.AbsoluteBorderBox;
        f.Session.Window.MoveMouse(edit.X + 5, edit.Y + 5);
        f.Pump();
        Assert.Equal(1, f.Bar.OpenIndex);
        Assert.True(f.MenuShows("Undo"));
        Assert.False(f.MenuShows("Open…"));

        f.Session.Window.Click(f.Title("Edit").Element);
        f.Pump();
        Assert.Equal(-1, f.Bar.OpenIndex);
        Assert.False(f.MenuShows("Undo"));
        Assert.False(f.Bar.IsActive);
    }

    [Fact]
    public void AltAndALetterOpensThatMenu_OnItsFirstItem()
    {
        using var f = new Fixture();
        f.Session.Window.PressKey(KeyCode.E, KeyModifiers.Alt);
        f.Pump();
        Assert.Equal(1, f.Bar.OpenIndex);
        f.Session.Window.PressKey(KeyCode.Enter);
        f.Pump();
        Assert.Equal(["Undo"], f.Ran);
        Assert.False(f.Bar.IsActive);
        Assert.True(f.Session.Window.FindByAutomationId("document")!.IsFocused, "the keyboard did not come back");
    }

    [Fact]
    public void F10WalksTheTitles_AndTheMenus_AndEscStepsBack()
    {
        using var f = new Fixture();
        f.Session.Window.PressKey(KeyCode.F10);
        f.Pump();
        Assert.True(f.Bar.IsActive);
        Assert.Equal(0, f.Bar.HighlightIndex);
        Assert.Equal(-1, f.Bar.OpenIndex);

        f.Session.Window.PressKey(KeyCode.Right);
        f.Session.Window.PressKey(KeyCode.Down);
        f.Pump();
        Assert.Equal(1, f.Bar.OpenIndex);   // Edit

        // In an open menu → and ← move to the neighbouring menu, wrapping.
        f.Session.Window.PressKey(KeyCode.Right);
        f.Pump();
        Assert.Equal(2, f.Bar.OpenIndex);
        f.Session.Window.PressKey(KeyCode.Right);
        f.Pump();
        Assert.Equal(0, f.Bar.OpenIndex);
        f.Session.Window.PressKey(KeyCode.Left);
        f.Pump();
        Assert.Equal(2, f.Bar.OpenIndex);

        // Esc closes the menu and leaves the bar on its title; Esc again lets go.
        f.Session.Window.PressKey(KeyCode.Escape);
        f.Pump();
        Assert.Equal(-1, f.Bar.OpenIndex);
        Assert.True(f.Bar.IsActive);
        Assert.Equal(2, f.Bar.HighlightIndex);
        f.Session.Window.PressKey(KeyCode.Escape);
        f.Pump();
        Assert.False(f.Bar.IsActive);
        Assert.True(f.Session.Window.FindByAutomationId("document")!.IsFocused);
    }

    [Fact]
    public void AltOnItsOwnTakesTheBar_ButNotAltWithAnotherKey_OrAltGr()
    {
        using var f = new Fixture();
        f.Session.Window.KeyDown(KeyCode.AltLeft, KeyModifiers.Alt);
        f.Session.Window.KeyUp(KeyCode.AltLeft);
        f.Pump();
        Assert.True(f.Bar.IsActive);
        f.Session.Window.KeyDown(KeyCode.AltLeft, KeyModifiers.Alt);
        f.Session.Window.KeyUp(KeyCode.AltLeft);
        f.Pump();
        Assert.False(f.Bar.IsActive);

        // Alt used for a chord is not Alt on its own.
        f.Session.Window.KeyDown(KeyCode.AltLeft, KeyModifiers.Alt);
        f.Session.Window.PressKey(KeyCode.Digit2, KeyModifiers.Alt);
        f.Session.Window.KeyUp(KeyCode.AltLeft);
        f.Pump();
        Assert.False(f.Bar.IsActive);

        // AltGr, which types @ on a Swedish keyboard, is left alone.
        f.Session.Window.KeyDown(KeyCode.AltRight, KeyModifiers.None, modifierKeys: ModifierKeys.AltGr);
        f.Session.Window.KeyUp(KeyCode.AltRight);
        f.Pump();
        Assert.False(f.Bar.IsActive);
    }

    [Fact]
    public void TheItemsAreAskedForEachTimeTheMenuOpens()
    {
        using var f = new Fixture();
        f.Session.Window.PressKey(KeyCode.V, KeyModifiers.Alt);
        f.Session.Window.PressKey(KeyCode.Enter);
        f.Pump();
        Assert.True(f.Wrap);

        f.Session.Window.PressKey(KeyCode.V, KeyModifiers.Alt);
        f.Pump();
        var item = Walk(f.Session.Window.AccessibilityTree).Single(n => n.Name == "Word wrap");
        Assert.Equal(true, item.IsToggled);
    }

    [Fact]
    public void MnemonicsAreUnderlinedWhileTheBarHasTheKeys()
    {
        using var f = new Fixture();
        TextElement Label() => LayoutProbe.Descendants<TextElement>(f.Title("File").Element).Single();
        Assert.Null(Label().Spans);

        f.Session.Window.PressKey(KeyCode.F10);
        f.Pump();
        var span = Assert.Single(Label().Spans!);
        Assert.Equal((0, 1), (span.Start, span.Length));
        Assert.Equal(HultUI.Core.Styling.TextDecoration.Underline, span.Decoration);
    }

    /// <summary>An item chosen with Alt and its mnemonic in the bar's open menu: letting go of that
    /// Alt is not Alt pressed alone, so the bar does not take the keyboard.</summary>
    [Fact]
    public void ChoosingWithAltAndAMnemonic_ThenLettingGoOfAlt_LeavesTheBar()
    {
        using var session = HeadlessSession.Create(600, 400);
        var ran = new List<string>();
        var bar = new MenuBar([new("&File", [new ContextMenuItem { Label = "&Save", UseMnemonic = true, Action = () => ran.Add("save") }])]);
        var document = TextInput.New("");
        var root = Flex.Column(gap: 0).Children(bar.Mount(), document);
        root.On<KeyDownEvent>(e => { if (bar.HandleKey(e)) e.StopPropagation = true; });
        root.On<KeyUpEvent>(e => bar.HandleKeyUp(e));
        session.Window.SetRoot(root);
        session.PumpFrame();
        document.Focus();
        bar.Open(0);
        session.PumpFrames(2);

        session.Window.KeyDown(KeyCode.AltLeft, KeyModifiers.Alt);
        session.Window.KeyDown(KeyCode.S, KeyModifiers.Alt, layoutKey: 's', text: "");
        session.PumpFrames(2);
        session.Window.KeyUp(KeyCode.AltLeft);
        session.PumpFrames(2);

        Assert.Equal(["save"], ran);
        Assert.False(bar.IsActive);
        Assert.Same(document, session.Window.Window.FocusedElement);
    }

    /// <summary>The same with two items sharing the letter, in a menu opened with Alt and its title's
    /// letter: the menu stays open on the next one, and letting go of Alt leaves the bar as it was,
    /// its title current and the keyboard its.</summary>
    [Fact]
    public void AnAltMnemonicTwoItemsShare_ThenLettingGoOfAlt_KeepsTheBarAsItWas()
    {
        using var session = HeadlessSession.Create(600, 400);
        var bar = new MenuBar([new("&File",
        [
            new ContextMenuItem { Label = "&Save", UseMnemonic = true },
            new ContextMenuItem { Label = "&Send", UseMnemonic = true },
        ])]);
        var root = Flex.Column(gap: 0).Children(bar.Mount(), TextInput.New(""));
        root.On<KeyDownEvent>(e => { if (bar.HandleKey(e)) e.StopPropagation = true; });
        root.On<KeyUpEvent>(e => bar.HandleKeyUp(e));
        session.Window.SetRoot(root);
        session.PumpFrame();
        session.Window.KeyDown(KeyCode.AltLeft, KeyModifiers.Alt);
        session.Window.PressKey(KeyCode.F, KeyModifiers.Alt, layoutKey: 'f', text: "");
        session.Window.KeyUp(KeyCode.AltLeft);
        session.PumpFrames(2);
        Assert.Equal(0, bar.HighlightIndex);

        session.Window.KeyDown(KeyCode.AltLeft, KeyModifiers.Alt);
        session.Window.PressKey(KeyCode.S, KeyModifiers.Alt, layoutKey: 's', text: "");
        session.Window.KeyUp(KeyCode.AltLeft);
        session.PumpFrames(2);

        Assert.Equal(0, bar.OpenIndex);
        Assert.Equal(0, bar.HighlightIndex);
        Assert.Single(session.Window.OverlayContents.OfType<ContextMenuElement>());
    }
}
