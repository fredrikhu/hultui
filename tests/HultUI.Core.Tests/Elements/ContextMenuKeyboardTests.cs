using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// A context menu used from the keyboard: Home and End, mnemonics, type-ahead, and the character a
/// key typed. The window hands the menu its keys (Window.HandleMenuKey); these drive the menu
/// itself, passing the clock's milliseconds in.
/// </summary>
public class ContextMenuKeyboardTests
{
    private static ContextMenuElement Menu(params ContextMenuItem[] items) =>
        new(items, new ContextMenuTheme(), closeMenu: () => { });

    private static ContextMenuItem Item(string label, Action? action = null, bool disabled = false) =>
        new() { Label = label, UseMnemonic = true, Action = action, IsDisabled = disabled };

    [Fact]
    public void MoveToEdge_SkipsSeparatorsAndDisabledItems_AndAnAllDisabledMenuHasNone()
    {
        var menu = Menu(ContextMenuItem.Separator(), Item("Off", disabled: true), Item("Copy"), Item("Paste"),
            Item("Off", disabled: true), ContextMenuItem.Separator());

        menu.MoveToEdge(first: true);
        Assert.Equal(2, menu.HoveredIndex);
        menu.MoveToEdge(first: false);
        Assert.Equal(3, menu.HoveredIndex);

        var none = Menu(Item("A", disabled: true), ContextMenuItem.Separator(), Item("B", disabled: true));
        none.MoveToEdge(first: true);
        Assert.Equal(-1, none.HoveredIndex);
        none.MoveToEdge(first: false);
        Assert.Equal(-1, none.HoveredIndex);
    }

    [Fact]
    public void AUniqueMnemonic_RunsItsItem_AndClosesTheMenuOnce()
    {
        int ran = 0, closed = 0;
        var menu = new ContextMenuElement([Item("&Copy"), Item("&Rename…", () => ran++)], new ContextMenuTheme(),
            closeMenu: () => closed++);

        Assert.Equal(TypedKeyResult.Activated, menu.HandleTypedKey('r', alt: false, nowMs: 0));
        Assert.Equal(1, ran);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void AMnemonicOnADisabledItem_IsSkipped()
    {
        int ran = 0;
        var menu = Menu(Item("&Rename…", () => ran++, disabled: true), Item("Copy"));

        Assert.Equal(TypedKeyResult.Taken, menu.HandleTypedKey('r', alt: false, nowMs: 0));
        Assert.Equal(0, ran);
        Assert.Equal(-1, menu.HoveredIndex);
    }

    [Fact]
    public void AMnemonicTwoItemsHave_SelectsThemInTurn_AndRunsNothing()
    {
        int ran = 0;
        var menu = Menu(Item("&Copy", () => ran++), Item("Paste"), Item("&Close", () => ran++));

        Assert.Equal(TypedKeyResult.Taken, menu.HandleTypedKey('c', alt: false, nowMs: 0));
        Assert.Equal(0, menu.HoveredIndex);
        menu.HandleTypedKey('c', alt: false, nowMs: 2000);
        Assert.Equal(2, menu.HoveredIndex);
        menu.HandleTypedKey('c', alt: false, nowMs: 4000);
        Assert.Equal(0, menu.HoveredIndex);
        Assert.Equal(0, ran);
    }

    [Fact]
    public void TypeAhead_SelectsTheFirstItemStartingWithWhatWasTyped_AndARunOfOneCharacterWalks()
    {
        var menu = Menu(new() { Label = "Copy" }, new() { Label = "Close" }, new() { Label = "Close others" },
            new() { Label = "Paste" });

        menu.HandleTypedKey('c', alt: false, nowMs: 0);
        menu.HandleTypedKey('l', alt: false, nowMs: 100);
        menu.HandleTypedKey('o', alt: false, nowMs: 200);
        Assert.Equal(1, menu.HoveredIndex);   // Close

        // A new run (more than a second on), from the item after Close.
        menu.HandleTypedKey('c', alt: false, nowMs: 1300);
        Assert.Equal(2, menu.HoveredIndex);   // Close others
        menu.HandleTypedKey('c', alt: false, nowMs: 1400);
        Assert.Equal(0, menu.HoveredIndex);   // Copy, round the end
        menu.HandleTypedKey('c', alt: false, nowMs: 1500);
        Assert.Equal(1, menu.HoveredIndex);   // Close

        Assert.Equal(TypedKeyResult.Taken, menu.HandleTypedKey('x', alt: false, nowMs: 2600));
        Assert.Equal(1, menu.HoveredIndex);
    }

    [Fact]
    public void WhileTypingAhead_AMnemonicDoesNotFire()
    {
        int opened = 0;
        var menu = Menu(new() { Label = "Copy" }, Item("&Open", () => opened++), new() { Label = "Close" });

        menu.HandleTypedKey('c', alt: false, nowMs: 0);
        Assert.Equal(TypedKeyResult.Taken, menu.HandleTypedKey('o', alt: false, nowMs: 100));

        Assert.Equal(0, opened);
        Assert.Equal(0, menu.HoveredIndex);   // "co": Copy
    }

    [Fact]
    public void AltWithNoItemsMnemonic_IsNotTheMenus_AndAltWithOne_FiresEvenWhileTypingAhead()
    {
        int opened = 0;
        var menu = Menu(new() { Label = "Copy" }, Item("&Open", () => opened++));

        Assert.Equal(TypedKeyResult.NotHandled, menu.HandleTypedKey('z', alt: true, nowMs: 0));

        menu.HandleTypedKey('c', alt: false, nowMs: 100);
        Assert.Equal(TypedKeyResult.Activated, menu.HandleTypedKey('o', alt: true, nowMs: 200));
        Assert.Equal(1, opened);
    }

    [Fact]
    public void ASubmenusMnemonic_OpensIt()
    {
        var menu = Menu(Item("Copy"), new() { Label = "&More", UseMnemonic = true, Children = [Item("Alpha")] });
        int opened = -1;
        menu.HoverChanged = i => opened = i;

        Assert.Equal(TypedKeyResult.Activated, menu.HandleTypedKey('m', alt: false, nowMs: 0));
        Assert.Equal(1, opened);
    }

    [Fact]
    public void Backspace_ShortensTheTypeAheadText()
    {
        var menu = Menu(new() { Label = "Copy" }, new() { Label = "Close" }, new() { Label = "Clsid" });

        menu.HandleTypedKey('c', alt: false, nowMs: 0);
        menu.HandleTypedKey('l', alt: false, nowMs: 100);
        menu.HandleTypedKey('o', alt: false, nowMs: 200);
        Assert.Equal(1, menu.HoveredIndex);
        menu.BackspaceTypeAhead();
        menu.HandleTypedKey('s', alt: false, nowMs: 300);

        Assert.Equal(2, menu.HoveredIndex);   // "cls", not "clos"
    }

    public static TheoryData<KeyDownEvent, char> Keys => new()
    {
        { new KeyDownEvent { Key = KeyCode.O, Text = "Ö", LayoutKey = 'Ö' }, 'ö' },
        // Russian: the key's text, not the Latin letter SDL names it by.
        { new KeyDownEvent { Key = KeyCode.C, Text = "с", LayoutKey = 'c' }, 'с' },
        // French number row: what it types, not its digit.
        { new KeyDownEvent { Key = KeyCode.Digit1, Text = "&", LayoutKey = '1' }, '&' },
        // An Alt chord types nothing: the layout's letter.
        { new KeyDownEvent { Key = KeyCode.F, Modifiers = KeyModifiers.Alt, Text = "", LayoutKey = 'f' }, 'f' },
        // The keypad with NumLock: its digit.
        { new KeyDownEvent { Key = KeyCode.Numpad5, Modifiers = KeyModifiers.NumLock }, '5' },
        // A host that says nothing: the letter at the key's position.
        { new KeyDownEvent { Key = KeyCode.Q }, 'q' },
        { new KeyDownEvent { Key = KeyCode.Left }, '\0' },
    };

    [Theory]
    [MemberData(nameof(Keys))]
    public void TheCharacterAKeyTyped_IsItsTextFirst(KeyDownEvent key, char typed)
    {
        Assert.Equal(typed, ContextMenuElement.TypedChar(key));
    }

    [Theory]
    [InlineData("@", '@')]
    [InlineData("", '\0')]
    [InlineData("\r", '\0')]
    public void TheCharacterOfTextWithoutAKey_IsItsFirst(string text, char typed)
    {
        Assert.Equal(typed, ContextMenuElement.TypedChar(text));
    }
}
