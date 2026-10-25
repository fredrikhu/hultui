using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The dropdown holds focus while its list is open, so everything here runs through the
/// control itself: its keys, its popup, and the outside-click dismissal handshake with
/// the overlay host — the part where a press on the control must read as a toggle
/// rather than close-then-reopen, because the host dismisses overlays before the press
/// reaches the element.
/// </summary>
public class DropdownTests
{
    private static readonly string[] Options = ["Alpha", "Beta", "Gamma"];

    private static DropdownElement Mount(HeadlessSession session, int selected = -1)
    {
        var dropdown = Dropdown.New(Options, selected);
        session.Window.SetRoot(Box.New().Padding(new Thickness(40)).Children(dropdown));
        session.PumpFrame();
        return dropdown;
    }

    /// <summary>Window-space centre of a popup row, from the anchor the control used
    /// plus the row offsets the menu computed when it rendered.</summary>
    private static (float x, float y) RowPoint(DropdownElement dropdown, ContextMenuElement popup, int index)
    {
        var box = dropdown.Layout.AbsoluteBorderBox;
        return (box.X + 15, box.Bottom + 2 + popup.ItemTop(index) + 4);
    }

    [Fact]
    public void Click_Opens_AndClickingARow_Commits()
    {
        using var session = HeadlessSession.Create();
        var dropdown = Mount(session);
        int fired = 0, firedWith = -1;
        dropdown.Changed(i => { fired++; firedWith = i; });

        session.Window.Click(dropdown);
        session.PumpFrame();

        Assert.True(dropdown.IsOpen);
        var popup = Assert.IsType<ContextMenuElement>(Assert.Single(session.Window.OverlayContents));

        var (x, y) = RowPoint(dropdown, popup, 1);
        session.Window.Click(x, y);
        session.PumpFrame();

        Assert.False(dropdown.IsOpen);
        Assert.Empty(session.Window.OverlayContents);
        Assert.Equal(1, dropdown.SelectedIndex);
        Assert.Equal("Beta", dropdown.SelectedItem);
        Assert.Equal(1, fired);
        Assert.Equal(1, firedWith);
    }

    [Fact]
    public void Keyboard_OpensNavigatesAndCommits()
    {
        using var session = HeadlessSession.Create();
        var dropdown = Mount(session);
        dropdown.Focus();

        session.Window.PressKey(KeyCode.Enter);
        Assert.True(dropdown.IsOpen);

        session.Window.PressKey(KeyCode.Down);   // nothing selected: first Down lands on Alpha
        session.Window.PressKey(KeyCode.Down);   // Beta
        session.Window.PressKey(KeyCode.Enter);
        session.PumpFrame();

        Assert.False(dropdown.IsOpen);
        Assert.Equal(1, dropdown.SelectedIndex);
    }

    [Fact]
    public void TheList_OpensWithTheCurrentValueHighlighted()
    {
        using var session = HeadlessSession.Create();
        var dropdown = Mount(session, selected: 2);
        dropdown.Focus();

        session.Window.PressKey(KeyCode.Down);   // opens
        session.Window.PressKey(KeyCode.Up);     // from Gamma up to Beta
        session.Window.PressKey(KeyCode.Enter);

        Assert.Equal(1, dropdown.SelectedIndex);
    }

    [Fact]
    public void Escape_ClosesWithoutCommitting()
    {
        using var session = HeadlessSession.Create();
        var dropdown = Mount(session, selected: 0);
        int fired = 0;
        dropdown.Changed(_ => fired++);
        dropdown.Focus();

        session.Window.PressKey(KeyCode.Enter);
        session.Window.PressKey(KeyCode.Down);
        session.Window.PressKey(KeyCode.Escape);
        session.PumpFrame();

        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void ClickingTheControlWhileOpen_TogglesItClosed()
    {
        using var session = HeadlessSession.Create();
        var dropdown = Mount(session);

        session.Window.Click(dropdown);
        Assert.True(dropdown.IsOpen);

        // The host dismisses the overlay before this press reaches the control; the
        // control must treat that as its toggle, not open a fresh list.
        session.Window.Click(dropdown);
        session.PumpFrame();

        Assert.False(dropdown.IsOpen);
        Assert.Empty(session.Window.OverlayContents);
    }

    [Fact]
    public void AnOutsideClick_Dismisses_AndTheNextPressStillOpens()
    {
        using var session = HeadlessSession.Create();
        var dropdown = Mount(session);

        session.Window.Click(dropdown);
        Assert.True(dropdown.IsOpen);

        session.Window.Click(700, 500);
        session.PumpFrame();
        Assert.False(dropdown.IsOpen);

        // The dismissal suppression must not leak into this unrelated, later press.
        session.Window.Click(dropdown);
        Assert.True(dropdown.IsOpen);
    }

    [Fact]
    public void TabbingAway_ClosesTheList()
    {
        using var session = HeadlessSession.Create();
        var first = Dropdown.New(Options);
        var second = Dropdown.New(Options);
        session.Window.SetRoot(Box.New().Padding(new Thickness(40)).Children(
            Flex.Column(gap: 12).Children(first, second)));
        session.PumpFrame();

        first.Focus();
        session.Window.PressKey(KeyCode.Enter);
        Assert.True(first.IsOpen);

        session.Window.PressKey(KeyCode.Tab);
        session.PumpFrame();

        Assert.False(first.IsOpen);
        Assert.Empty(session.Window.OverlayContents);
    }

    [Fact]
    public void PickingTheCurrentValueAgain_DoesNotRaiseTheCallback()
    {
        using var session = HeadlessSession.Create();
        var dropdown = Mount(session, selected: 1);
        int fired = 0;
        dropdown.Changed(_ => fired++);
        dropdown.Focus();

        session.Window.PressKey(KeyCode.Enter);  // opens with Beta highlighted
        session.Window.PressKey(KeyCode.Enter);  // commits Beta again
        session.PumpFrame();

        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void SettingSelectedIndexFromCode_DoesNotRaiseTheCallback()
    {
        using var session = HeadlessSession.Create();
        var dropdown = Mount(session);
        int fired = 0;
        dropdown.Changed(_ => fired++);

        dropdown.SelectedIndex = 2;

        Assert.Equal("Gamma", dropdown.SelectedItem);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void ReadsAsAComboBox_NamedByItsSelection()
    {
        using var session = HeadlessSession.Create();
        var dropdown = Mount(session, selected: 0);
        session.PumpFrame();

        Assert.NotNull(session.Window.FindByRole(AccessibilityRole.ComboBox, "Alpha"));
    }

    [Fact]
    public void HomeAndEnd_JumpToTheEdges()
    {
        using var session = HeadlessSession.Create();
        var dropdown = Mount(session, selected: 1);
        dropdown.Focus();

        session.Window.PressKey(KeyCode.Enter);
        session.Window.PressKey(KeyCode.End);
        session.Window.PressKey(KeyCode.Enter);
        Assert.Equal(2, dropdown.SelectedIndex);

        session.Window.PressKey(KeyCode.Enter);
        session.Window.PressKey(KeyCode.Home);
        session.Window.PressKey(KeyCode.Enter);
        Assert.Equal(0, dropdown.SelectedIndex);
    }

    /// <summary>
    /// The list opens under the control on a page that has been scrolled.
    ///
    /// <para>The popup is placed in window coordinates, and a scroll view does not move its
    /// children's layout boxes — it offsets them when painting. Anchoring on the layout box
    /// therefore put the list wherever the control <i>would</i> be if nothing had scrolled: on
    /// a long page, most of a screen away from the control it belongs to. Asserted by clicking
    /// the row where it appears, because "the list is somewhere" is not the property that
    /// matters — being able to pick from it is.</para>
    /// </summary>
    [Fact]
    public void OnAScrolledPageTheListOpensUnderTheControl()
    {
        using var session = HeadlessSession.Create(400, 300);
        var dropdown = Dropdown.New(Options, 0);
        var scroll = ScrollView.New().Height(300f).Children(
            Box.New().Height(600f).FlexShrink(0),
            Box.New().Padding(new Thickness(10)).FlexShrink(0).Children(dropdown),
            Box.New().Height(600f).FlexShrink(0));
        session.Window.SetRoot(scroll);
        session.PumpFrames(2);

        scroll.ScrollOffsetY = 560f;      // brings the control near the top of the viewport
        session.PumpFrames(2);
        Assert.NotEqual(dropdown.Layout.AbsoluteBorderBox.Y, dropdown.ScreenBorderBox.Y);

        session.Window.Click(dropdown);
        session.PumpFrame();
        var popup = Assert.IsType<ContextMenuElement>(Assert.Single(session.Window.OverlayContents));

        // Same arithmetic as RowPoint, off the screen box rather than the layout box.
        var box = dropdown.ScreenBorderBox;
        session.Window.Click(box.X + 15, box.Bottom + 2 + popup.ItemTop(2) + 4);
        session.PumpFrame();

        Assert.Equal(2, dropdown.SelectedIndex);
        Assert.Equal("Gamma", dropdown.SelectedItem);
    }

    /// <summary>
    /// A list longer than the window runs past its bottom edge. The rows out there are laid out
    /// but not drawn: a 200-item list drew every row's text on every frame it was open, hover
    /// included, most of them below the window (plan 17, round 4). They are still the list's:
    /// the keyboard reaches the last, each is still an accessible item with its place, and the
    /// row cut by the window's edge is drawn.
    /// </summary>
    [Fact]
    public void ALongList_DrawsOnlyTheRowsInTheWindow_AndKeepsTheRest()
    {
        using var session = HeadlessSession.Create(400, 300, options: new HeadlessOptions { RecordDrawCalls = true });
        var items = Enumerable.Range(0, 200).Select(i => $"Option {i:000}").ToArray();
        var dropdown = Dropdown.New(items, -1);
        session.Window.SetRoot(Box.New().Padding(new Thickness(40)).Children(dropdown));
        session.PumpFrame();
        dropdown.Focus();

        session.Window.PressKey(KeyCode.Enter);
        var popup = Assert.IsType<ContextMenuElement>(Assert.Single(session.Window.OverlayContents));
        session.Window.RenderLog.Clear();
        popup.MarkPaintDirty();
        session.PumpFrame();

        var drawn = session.Window.RenderLog.Named("DrawText")
            .Select(o => o.Args).Where(a => a.StartsWith("\"Option ", StringComparison.Ordinal)).ToList();
        float top = popup.WindowBorderBox.Y;
        int inWindow = Enumerable.Range(0, items.Length).Count(i => top + popup.ItemTop(i) < 300);
        Assert.InRange(inWindow, 5, 20);
        Assert.InRange(drawn.Count, inWindow, inWindow + 1);
        Assert.Contains(drawn, a => a.StartsWith($"\"Option {inWindow - 1:000}\"", StringComparison.Ordinal)); // cut by the edge

        // Every row has its place, past the window too, and is an item a reader can find.
        var source = (IAccessibleItemSource)popup;
        Assert.Equal(items.Length, source.ItemCount);
        Assert.True(popup.ItemTop(199) > popup.ItemTop(198) && popup.ItemTop(198) > 300);
        Assert.Equal(popup.ItemTop(199), source.ItemBounds(199)!.Value.Top);

        session.Window.PressKey(KeyCode.End);
        Assert.Equal(199, popup.HoveredIndex);
        session.Window.PressKey(KeyCode.Enter);
        session.PumpFrame();
        Assert.Equal(199, dropdown.SelectedIndex);
    }
}
