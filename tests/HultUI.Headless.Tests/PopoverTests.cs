using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Dialogs;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="Popover"/>: opens against its anchor and takes the keyboard, closes on Esc, on a
/// press outside or on its own anchor (without reopening), flips and follows the anchor, closes
/// when the anchor goes, and patches its content in place.
/// </summary>
public sealed class PopoverTests
{
    private sealed class Page : Component
    {
        public readonly Popover Filters;
        public bool ShowAnchor = true;
        public float Spacer;
        public string Query = "";
        public int Count = 3;
        public int Others;
        public int Applied;
        public int ClosedCount;

        public Page()
        {
            Filters = new Popover(() =>
            {
                var query = TextInput.New(Query).OnTextChanged(v => Query = v);
                query.AutomationId = "query";
                return Flex.Column(gap: 8).Children(
                    query,
                    TextElement.New($"{Count} matches"),
                    Button.New("Apply").OnClick(_ => { Applied++; Filters!.Close(); }));
            })
            { Closed = () => ClosedCount++ };
        }

        public void Refresh() => MarkDirty();

        public override Element Build()
        {
            var bar = Flex.Row(gap: 8).FlexShrink(0);
            // Keyed: by position alone, dropping the first button patches it into the second,
            // and the popover would stay open against what is now "Other".
            if (ShowAnchor)
            {
                var filters = Button.New("Filters").OnClick(e => Filters.Toggle(e.CurrentTarget));
                filters.Key = "filters";
                bar.AddChild(filters);
            }
            var other = Button.New("Other").OnClick(_ => Others++);
            other.Key = "other";
            bar.AddChild(other);
            return Flex.Column(gap: 0).Padding(20f).Children(
                Box.New().Height(Spacer).FlexShrink(0), bar, Box.New().FlexGrow(1));
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly HeadlessSession Session = HeadlessSession.Create(800, 600);
        public readonly Page Page = new();

        public Fixture(Action<Page>? setup = null)
        {
            setup?.Invoke(Page);
            Session.Window.SetRoot(Page.Mount());
            Session.PumpFrame();
        }

        public HeadlessElement Anchor => Session.Window.FindByRole(AccessibilityRole.Button, "Filters")!;
        public Element Card => Page.Filters.Card!;
        public void Pump() => Session.PumpFrames(2);
        public void Dispose() => Session.Dispose();
    }

    [Fact]
    public void ItOpensBelowItsAnchor_TakesTheKeyboard_AndEscGivesItBack()
    {
        using var f = new Fixture();
        f.Anchor.Click();
        f.Pump();
        Assert.True(f.Page.Filters.IsOpen);

        // Under the anchor, a theme gap away, lined up with its left edge — where it is drawn,
        // which is where a control in it says it is.
        var anchor = f.Anchor.Element.WindowBorderBox;
        var card = f.Card.WindowBorderBox;
        Assert.Equal(MathF.Round(anchor.Bottom + 6f), card.Y);
        Assert.Equal(MathF.Round(anchor.X), card.X);

        var dialog = f.Session.Window.FindByRole(AccessibilityRole.Dialog, "Filters");
        Assert.NotNull(dialog);
        Assert.Equal(true, f.Anchor.Node.Expanded);
        Assert.True(f.Session.Window.FindByAutomationId("query")!.IsFocused);

        f.Session.Window.PressKey(KeyCode.Escape);
        f.Pump();
        Assert.False(f.Page.Filters.IsOpen);
        Assert.True(f.Anchor.IsFocused);
        Assert.Equal(false, f.Anchor.Node.Expanded);
        Assert.Equal(1, f.Page.ClosedCount);
    }

    [Fact]
    public void APressOnItsAnchorOnlyClosesIt_AndAPressElsewhereClosesItAndGoesOn()
    {
        using var f = new Fixture();
        f.Anchor.Click();
        f.Pump();
        Assert.True(f.Page.Filters.IsOpen);

        f.Anchor.Click();
        f.Pump();
        Assert.False(f.Page.Filters.IsOpen);

        f.Anchor.Click();
        f.Pump();
        Assert.True(f.Page.Filters.IsOpen);

        f.Session.Window.FindByRole(AccessibilityRole.Button, "Other")!.Click();
        f.Pump();
        Assert.False(f.Page.Filters.IsOpen);
        Assert.Equal(1, f.Page.Others);
        Assert.Equal(2, f.Page.ClosedCount);
    }

    [Fact]
    public void AControlInItTakesClicks_AndCanCloseIt()
    {
        using var f = new Fixture();
        f.Anchor.Click();
        f.Pump();
        f.Session.Window.FindByRole(AccessibilityRole.Button, "Apply")!.Click();
        f.Pump();
        Assert.Equal(1, f.Page.Applied);
        Assert.False(f.Page.Filters.IsOpen);
    }

    [Fact]
    public void ItFlipsAboveWhenThereIsNoRoomBelow()
    {
        using var f = new Fixture(p => p.Spacer = 500);
        f.Anchor.Click();
        f.Pump();
        Assert.Equal(PopoverPlacement.Above, f.Page.Filters.ActualPlacement);
        Assert.Equal(MathF.Round(f.Anchor.Element.WindowBorderBox.Y - 6f - f.Card.WindowBorderBox.Height),
            f.Card.WindowBorderBox.Y);
    }

    [Fact]
    public void ItFollowsItsAnchorWhenThePageScrolls()
    {
        using var session = HeadlessSession.Create(800, 400);
        var popover = new Popover(() => TextElement.New("Details"));
        var anchor = Button.New("More");
        anchor.On<ClickEvent>(e => popover.Toggle(e.CurrentTarget));
        var scroll = ScrollView.Vertical().Children(Flex.Column(gap: 0).Children(
            Box.New().Height(100).FlexShrink(0), anchor, Box.New().Height(1500).FlexShrink(0)));
        session.Window.SetRoot(Flex.Column(gap: 0).Children(scroll.FlexGrow(1)));
        session.PumpFrame();

        session.Window.Click(anchor);
        session.PumpFrames(2);
        float before = popover.Card!.WindowBorderBox.Y;

        session.Window.Wheel(400, 300, 0, -1);
        session.PumpFrames(3);
        float moved = anchor.WindowBorderBox.Y - 100f;
        Assert.True(moved < 0, "the page did not scroll");
        Assert.Equal(MathF.Round(anchor.WindowBorderBox.Bottom + 6f), popover.Card!.WindowBorderBox.Y);
        Assert.NotEqual(before, popover.Card!.WindowBorderBox.Y);
    }

    [Fact]
    public void ItClosesWhenARebuildDropsItsAnchor()
    {
        using var f = new Fixture();
        f.Anchor.Click();
        f.Pump();
        f.Page.ShowAnchor = false;
        f.Page.Refresh();
        f.Pump();
        Assert.False(f.Page.Filters.IsOpen);
        Assert.Equal(1, f.Page.ClosedCount);
    }

    [Fact]
    public void RefreshPatchesTheOpenCard_KeepingItsControlsAndItsLook()
    {
        using var f = new Fixture();
        f.Anchor.Click();
        f.Pump();
        var query = f.Session.Window.FindByAutomationId("query")!.Element;
        f.Session.Window.TypeText("abc");

        f.Page.Count = 7;
        f.Page.Filters.Refresh();
        f.Pump();
        Assert.Same(query, f.Session.Window.FindByAutomationId("query")!.Element);
        Assert.True(query.IsFocused);
        Assert.Equal("abc", ((TextInputElement)query).Text);
        Assert.NotNull(f.Session.Window.GetByText("7 matches"));
        Assert.Equal(new Core.Primitives.Thickness(12), ((BoxElement)f.Card).Box.Padding);
    }

    [Fact]
    public void ArrowKeysWalkItsControls_WhenAskedTo()
    {
        using var session = HeadlessSession.Create(600, 400);
        var popover = new Popover(() => Flex.Column(gap: 0).Children(
            Button.New("One"), Button.New("Two"), Button.New("Three"))) { ArrowKeys = true };
        var anchor = Button.New("Open");
        anchor.On<ClickEvent>(e => popover.Toggle(e.CurrentTarget));
        session.Window.SetRoot(Flex.Column(gap: 0).Padding(20f).Children(anchor));
        session.PumpFrame();

        session.Window.Click(anchor);
        session.PumpFrames(2);
        Assert.True(session.Window.FindByRole(AccessibilityRole.Button, "One")!.IsFocused);
        session.Window.PressKey(KeyCode.Down);
        session.Window.PressKey(KeyCode.Down);
        Assert.True(session.Window.FindByRole(AccessibilityRole.Button, "Three")!.IsFocused);
        session.Window.PressKey(KeyCode.Up);
        Assert.True(session.Window.FindByRole(AccessibilityRole.Button, "Two")!.IsFocused);
    }

    /// <summary>
    /// A dropdown inside a popover opens its list under itself. It places the list from its
    /// box in the window, and the box it used to read left out where the popover is — so the
    /// list opened as far from the dropdown as the popover is from the window's corner.
    /// </summary>
    [Fact]
    public void ADropdownInsideOpensItsListUnderItself()
    {
        using var session = HeadlessSession.Create(800, 600);
        var dropdown = Dropdown.New(["Any", "Mine", "Others'"], selectedIndex: 0);
        var popover = new Popover(() => Flex.Column(gap: 0).Children(dropdown));
        var anchor = Button.New("Filters");
        anchor.On<ClickEvent>(e => popover.Toggle(e.CurrentTarget));
        session.Window.SetRoot(Flex.Column(gap: 0).Padding(new Core.Primitives.Thickness(200, 0, 0, 300)).Children(anchor));
        session.PumpFrame();

        session.Window.Click(anchor);
        session.PumpFrames(2);
        var live = (DropdownElement)LayoutProbe.Descendants<DropdownElement>(popover.Card!).Single();
        session.Window.Click(live);
        session.PumpFrames(2);

        var list = (Element)typeof(DropdownElement)
            .GetField("_popup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(live)!;
        var box = live.WindowBorderBox;
        Assert.Equal(box.X, list.WindowBorderBox.X);
        Assert.InRange(list.WindowBorderBox.Y, box.Bottom, box.Bottom + 8f);
    }

    /// <summary>
    /// Opened from a dialog, a press elsewhere in the dialog is outside the popover. The window
    /// used to dismiss overlays only on a press in the main tree, which a dialog's scrim covers,
    /// so there it stayed open until Esc.
    /// </summary>
    [Fact]
    public void OpenedFromADialog_APressElsewhereInTheDialogClosesIt_AndNotTheDialog()
    {
        using var session = HeadlessSession.Create(800, 600);
        var root = Flex.Column(gap: 0);
        session.Window.SetRoot(root);
        session.PumpFrame();

        var popover = new Popover(() => TextElement.New("Inside"));
        int pressed = 0;
        // Side by side, so the popover, opening below Info, does not cover Elsewhere.
        var shown = Dialog.Show<bool>(root.OverlayHost!, _ => Flex.Row(gap: 8).Padding(20f).Children(
            Button.New("Elsewhere").OnClick(_ => pressed++),
            Button.New("Info").OnClick(e => popover.Toggle(e.CurrentTarget))));
        session.PumpFrames(2);

        session.Window.FindByRole(AccessibilityRole.Button, "Info")!.Click();
        session.PumpFrames(2);
        Assert.True(popover.IsOpen);

        session.Window.FindByRole(AccessibilityRole.Button, "Elsewhere")!.Click();
        session.PumpFrames(2);
        Assert.False(popover.IsOpen);
        Assert.Equal(1, pressed);
        Assert.False(shown.IsCompleted);
    }
}
