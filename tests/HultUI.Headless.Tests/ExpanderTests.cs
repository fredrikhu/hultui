using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="ExpanderElement"/>: the header opens and closes the body by pointer and keyboard,
/// the body slides, state survives rebuilds or is the app's, a function body is built only
/// while open, and actions in the header do not toggle it.
/// </summary>
public sealed class ExpanderTests
{
    private sealed class Page : Component
    {
        /// <summary>Null leaves the expander its own state; a value hands it to the app.</summary>
        public bool? Open;
        public bool Lazy;
        public int Built;
        public int Added;
        public readonly List<bool> Reported = [];
        public string Typed = "";

        public void Refresh() => MarkDirty();

        public override Element Build()
        {
            Element Body()
            {
                Built++;
                var field = TextInput.New(Typed).OnTextChanged(v => Typed = v);
                field.AutomationId = "field";
                return Flex.Column(gap: 4).Children(field, Box.New().Height(100).FlexShrink(0));
            }

            var expander = Lazy ? Expander.New("Advanced", Body) : Expander.New("Advanced", Body());
            expander.Actions(Button.New("Add").OnClick(_ => Added++));
            expander.OnExpandedChanged(v =>
            {
                Reported.Add(v);
                if (Open is not null) { Open = v; MarkDirty(); }
            });
            if (Open is { } open) expander.Expanded(open);
            return Flex.Column(gap: 0).Children(expander, Button.New("After"));
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly HeadlessSession Session = HeadlessSession.Create(600, 500);
        public readonly Page Page;
        public readonly ComponentHost Host;

        public Fixture(Action<Page>? setup = null)
        {
            Page = new Page();
            setup?.Invoke(Page);
            Host = Page.Mount();
            Session.Window.SetRoot(Host);
            Session.PumpFrame();
        }

        public ExpanderElement Expander => LayoutProbe.Descendants<ExpanderElement>(Host).Single();
        public HeadlessElement Header => Session.Window.FindByRole(AccessibilityRole.Button, "Advanced")!;
        public HeadlessElement? Field => Session.Window.FindByAutomationId("field");

        /// <summary>Long enough for any opening or closing to finish.</summary>
        public void Settle() => Session.PumpFrames(30);

        public void Rebuild()
        {
            Page.Refresh();
            Session.PumpFrame();
        }

        public void Dispose() => Session.Dispose();
    }

    [Fact]
    public void TheHeaderIsAButtonThatOpensAndClosesTheBody()
    {
        using var f = new Fixture();
        Assert.Equal(false, f.Header.Node.Expanded);
        Assert.Null(f.Field);

        f.Header.Click();
        f.Settle();
        Assert.Equal(true, f.Header.Node.Expanded);
        Assert.NotNull(f.Field);
        Assert.Equal(1f, f.Expander.Progress);
        Assert.Equal([true], f.Page.Reported);

        f.Header.Click();
        f.Settle();
        Assert.Equal(false, f.Header.Node.Expanded);
        Assert.Null(f.Field);
        Assert.Single(f.Expander.Children);
        Assert.Equal([true, false], f.Page.Reported);
    }

    [Fact]
    public void TheBodySlidesOpenAndShut_AndTheChevronTurnsWithIt()
    {
        using var f = new Fixture();
        f.Header.Click();
        var heights = new List<float>();
        for (int i = 0; i < 30; i++)
        {
            f.Session.PumpFrame();
            heights.Add(f.Expander.Children[1].Layout.Height);
        }
        float full = heights[^1];
        // 100 of box, 4 of gap, the text box, and the body's padding.
        Assert.True(full > 120, $"open body is {full}");
        Assert.Contains(heights, h => h > 0 && h < full * 0.9f);
        for (int i = 1; i < heights.Count; i++)
            Assert.True(heights[i] >= heights[i - 1], $"the body shrank while opening: {string.Join(", ", heights)}");

        // Half-way the body clips what has not come into view yet; open, it does not.
        var body = (BoxElement)f.Expander.Children[1];
        Assert.Equal(Overflow.Visible, body.Box.OverflowY);

        var chevron = LayoutProbe.Descendants<ExpanderElement.ChevronElement>(f.Expander).Single();
        Assert.Equal(0f, chevron.Angle);   // pointing down

        f.Header.Click();
        f.Session.PumpFrames(3);
        Assert.Equal(Overflow.Hidden, body.Box.OverflowY);
        Assert.InRange(body.Layout.Height, 1f, full - 1f);
        Assert.InRange(chevron.Angle, -89f, -1f);
        f.Settle();
        Assert.Equal(-90f, chevron.Angle); // pointing right
        Assert.Single(f.Expander.Children);
    }

    [Fact]
    public void TabReachesTheHeader_AndEnterAndSpaceToggleIt_WithNumLockOn()
    {
        using var f = new Fixture();
        f.Session.Window.PressKey(KeyCode.Tab);
        Assert.True(f.Header.IsFocused);

        f.Session.Window.PressKey(KeyCode.Enter, KeyModifiers.NumLock);
        f.Settle();
        Assert.True(f.Expander.IsExpanded);

        // Ctrl+Space is somebody else's chord.
        f.Session.Window.PressKey(KeyCode.Space, KeyModifiers.Ctrl);
        f.Settle();
        Assert.True(f.Expander.IsExpanded);

        f.Session.Window.PressKey(KeyCode.Space);
        f.Settle();
        Assert.False(f.Expander.IsExpanded);
    }

    [Fact]
    public void ItKeepsItsOwnStateAcrossRebuilds_AndTheBodyElementWhileClosed()
    {
        using var f = new Fixture();
        f.Header.Click();
        f.Settle();
        f.Session.Window.Click(f.Field!.Element);
        f.Session.Window.TypeText("hello");
        var field = (TextInputElement)f.Field!.Element;

        f.Rebuild();
        Assert.True(f.Expander.IsExpanded);
        Assert.Same(field, f.Field!.Element);

        // Closed, the body is out of the tree but kept, and still patched by a rebuild — so
        // what lives in the element, like its undo history, is there when it opens again.
        f.Header.Click();
        f.Settle();
        f.Rebuild();
        Assert.Null(f.Field);
        f.Header.Click();
        f.Settle();
        Assert.Same(field, f.Field!.Element);
        Assert.Equal("hello", field.Text);
        f.Session.Window.Click(field);
        f.Session.Window.PressKey(KeyCode.Z, KeyModifiers.Ctrl);
        Assert.NotEqual("hello", field.Text);
    }

    [Fact]
    public void ExpandedHandsTheStateToTheApp_AndClosingTakesTheKeyboardToTheHeader()
    {
        using var f = new Fixture(p => p.Open = false);
        f.Header.Click();
        f.Settle();
        Assert.True(f.Page.Open);
        Assert.True(f.Expander.IsExpanded);

        f.Field!.Focus();
        Assert.True(f.Field!.IsFocused);

        // The app closes it: no report back, and the keyboard is not left in a body that went.
        f.Page.Open = false;
        f.Rebuild();
        f.Settle();
        Assert.False(f.Expander.IsExpanded);
        Assert.Null(f.Field);
        Assert.Equal([true], f.Page.Reported);
        Assert.True(f.Header.IsFocused);

        f.Page.Open = true;
        f.Rebuild();
        f.Settle();
        Assert.NotNull(f.Field);
    }

    [Fact]
    public void AFunctionBodyIsBuiltOnlyWhileOpen()
    {
        using var f = new Fixture(p => p.Lazy = true);
        f.Rebuild();
        Assert.Equal(0, f.Page.Built);
        Assert.Null(f.Expander.Content);

        f.Header.Click();
        f.Settle();
        Assert.Equal(1, f.Page.Built);
        Assert.NotNull(f.Field);
        f.Rebuild();
        Assert.Equal(2, f.Page.Built);

        f.Header.Click();
        f.Settle();
        Assert.Null(f.Expander.Content);
        f.Rebuild();
        Assert.Equal(2, f.Page.Built);
    }

    [Fact]
    public void ItReadsAsAButtonNamedByItsTitle_Once_UnderTheExpandersAutomationId()
    {
        using var session = HeadlessSession.Create(400, 300);
        var plain = Expander.New("Network", TextElement.New("Proxy: none"));
        plain.AutomationId = "network";
        var busy = Expander.New("Recent", TextElement.New("Nothing yet.")).Actions(Button.New("Clear"));
        session.Window.SetRoot(Flex.Column(gap: 0).Children(plain, busy));
        session.PumpFrame();

        // Only the title: the button is named by it and does not also hold it as a label.
        var network = session.Window.FindByAutomationId("network")!;
        Assert.Equal(AccessibilityRole.Button, network.Role);
        Assert.Equal("Network", network.Name);
        Assert.Equal(false, network.Node.Expanded);
        Assert.Empty(network.Node.Children);

        // With actions, named by the title outright, and the actions are inside it.
        var recent = session.Window.FindByRole(AccessibilityRole.Button, "Recent")!;
        Assert.Contains(recent.Node.Children, n => n.Role == AccessibilityRole.Button && n.Name == "Clear");
    }

    /// <summary>
    /// A card's header gives up its bottom corners while open and takes them back when closed,
    /// and the pointer leaving does not undo either: hover puts back the look it captured when
    /// it began. Closing is where that showed — hovered while open, so the captured look was
    /// the open one, and a closed card kept square bottom corners.
    /// </summary>
    [Fact]
    public void ACardsHeaderKeepsItsShape_AfterThePointerLeaves()
    {
        using var session = HeadlessSession.Create(400, 300);
        session.Application.Theme = new Theme
        {
            Expander = new ExpanderTheme { Border = new Color(200, 200, 200), CornerRadius = 6f },
        };
        var card = Expander.New("Card", TextElement.New("Inside"));
        session.Window.SetRoot(Flex.Column(gap: 8).Padding(20f).Children(card));
        session.PumpFrame();
        var header = (BoxElement)card.HeaderRow;

        session.Window.Click(header);
        session.PumpFrames(30);
        Assert.Equal(new CornerRadius(6f, 6f, 0f, 0f), header.CornerRadius);

        session.Window.MoveMouse(390, 290);
        session.PumpFrames(3);
        Assert.False(header.IsHovered);
        Assert.Equal(new CornerRadius(6f, 6f, 0f, 0f), header.CornerRadius);

        // And the other way: hovered while open, closed, left.
        session.Window.Click(header);
        session.PumpFrames(30);
        Assert.Equal(new CornerRadius(6f), header.CornerRadius);
        session.Window.MoveMouse(390, 290);
        session.PumpFrames(3);
        Assert.Equal(new CornerRadius(6f), header.CornerRadius);
    }

    [Fact]
    public void ABareDisclosuresHeaderKeepsAllFourCornersOpen()
    {
        using var session = HeadlessSession.Create(400, 300);
        var bare = Expander.New("Bare", TextElement.New("Inside")).DefaultExpanded();
        session.Window.SetRoot(Flex.Column(gap: 8).Padding(20f).Children(bare));
        session.PumpFrame();
        Assert.Equal(new CornerRadius(4f), ((BoxElement)bare.HeaderRow).CornerRadius);
    }

    [Fact]
    public void ActionsSitInTheHeaderButDoNotToggleIt()
    {
        using var f = new Fixture();
        var add = f.Session.Window.FindByRole(AccessibilityRole.Button, "Add")!;
        var header = f.Header.Element.Layout.AbsoluteBorderBox;
        var box = add.Element.Layout.AbsoluteBorderBox;
        Assert.True(box.Y >= header.Y && box.Bottom <= header.Bottom, "the action is not in the header row");

        add.Click();
        f.Settle();
        Assert.Equal(1, f.Page.Added);
        Assert.False(f.Expander.IsExpanded);

        // Still there, and still not a toggle, after a rebuild patched it.
        f.Rebuild();
        f.Session.Window.FindByRole(AccessibilityRole.Button, "Add")!.Click();
        f.Settle();
        Assert.Equal(2, f.Page.Added);
        Assert.False(f.Expander.IsExpanded);
    }
}
