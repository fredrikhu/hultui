using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using Xunit;

namespace HultUI.Headless.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// An element the app marks unavailable (<see cref="Element.AccessibleEnabled"/>) reads as not
/// enabled, and a reader cannot act on it. A button an app built from a box had no way to say it
/// was disabled, so a reader announced it as available; and invoked, it ran its click handler.
/// Like aria-disabled it changes nothing else: the pointer still reaches the element.
/// </summary>
public class AccessibleEnabledTests
{
    private static (HeadlessSession Session, AccessibilityActionRouter Router) Mount(Element root)
    {
        var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(root);
        session.PumpFrame();
        var router = new AccessibilityActionRouter(session.Window.Window);
        session.Window.Window.SetActionRouter(router);
        return (session, router);
    }

    private static BoxElement CustomButton(string label, Action clicked) =>
        Box.New().Width(120f).Height(32f).AccessibleRole(AccessibilityRole.Button)
            .Children(Text.New(label))
            .OnClick(_ => clicked());

    [Fact]
    public void ABoxMarkedUnavailable_ReadsAsNotEnabled_AndAReaderCannotInvokeIt()
    {
        int clicks = 0;
        var button = CustomButton("Send", () => clicks++).AccessibleEnabled(false);
        var (session, router) = Mount(Box.New().Children(button));
        using var _ = session;

        var node = session.Window.FindByRole(AccessibilityRole.Button, "Send");
        Assert.NotNull(node);
        Assert.False(node!.Node.IsEnabled);

        router.InvokeElement(button.Id);
        session.PumpFrame();
        Assert.Equal(0, clicks);

        // Marked available again: read as enabled, and a reader's invoke clicks it.
        button.AccessibleEnabled = true;
        session.PumpFrame();
        Assert.True(session.Window.FindByRole(AccessibilityRole.Button, "Send")!.Node.IsEnabled);
        router.InvokeElement(button.Id);
        session.PumpFrame();
        Assert.Equal(1, clicks);
    }

    /// <summary>It changes only what a reader reads and can do, as aria-disabled does: the pointer
    /// still reaches the element, and the app decides what its handler does then.</summary>
    [Fact]
    public void APointerClick_StillReachesABoxMarkedUnavailable()
    {
        int clicks = 0;
        var button = CustomButton("Send", () => clicks++).AccessibleEnabled(false);
        var (session, _) = Mount(Box.New().Children(button));
        using var __ = session;

        session.Window.Click(button);

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void AReaderCannotToggleSelectExpandOrSetTheValueOfAnElementMarkedUnavailable()
    {
        int toggles = 0, selects = 0, expands = 0;
        var toggle = Box.New().Width(40f).Height(20f).AccessibleName("Bold").AccessibleEnabled(false);
        toggle.AccessibleToggled = false;
        toggle.OnClick(_ => toggles++);
        var tab = Box.New().Width(40f).Height(20f).AccessibleName("Tab").AccessibleSelected(false)
            .AccessibleEnabled(false).OnClick(_ => selects++);
        var expander = Box.New().Width(40f).Height(20f).AccessibleName("More").AccessibleExpanded(false)
            .AccessibleEnabled(false).OnClick(_ => expands++);
        var input = TextInput.New("before").AccessibleName("Name").AccessibleEnabled(false);
        var (session, router) = Mount(Flex.Column().Children(toggle, tab, expander, input));
        using var _ = session;

        router.ToggleElement(toggle.Id);
        router.SelectElement(tab.Id);
        router.ExpandCollapseElement(expander.Id, expand: true);
        router.SetValue(input.Id, "after");
        session.PumpFrame();

        Assert.Equal((0, 0, 0, "before"), (toggles, selects, expands, input.Text));
    }

    /// <summary>A button the app disabled stays disabled to a reader, whatever the box mark says;
    /// marking a button unavailable makes it read so though its own state is enabled.</summary>
    [Fact]
    public void AButtonIsEnabledToAReader_OnlyWhenItAndItsMarkBothSaySo()
    {
        var button = Button.New("Save");
        var (session, _) = Mount(Box.New().Children(button));
        using var _s = session;
        bool Enabled() => session.Window.FindByRole(AccessibilityRole.Button, "Save")!.Node.IsEnabled;

        Assert.True(Enabled());
        button.AccessibleEnabled = false;
        session.PumpFrame();
        Assert.False(Enabled());
        button.AccessibleEnabled = true;
        button.IsEnabled = false;
        session.PumpFrame();
        Assert.False(Enabled());
    }

    /// <summary>The mark comes from the build, as every other accessible property: a rebuild that
    /// drops it makes the live element available again.</summary>
    private sealed class Form : Component
    {
        public Signal<bool> Ready { get; } = new(false);
        public override Element Build() =>
            Box.New().Children(Box.New().Width(80f).Height(24f).AccessibleRole(AccessibilityRole.Button)
                .AccessibleName("Submit").AccessibleEnabled(Ready.Value));
    }

    [Fact]
    public void ARebuildCarriesTheMarkToTheLiveElement()
    {
        var form = new Form();
        var (session, _) = Mount(form.Mount());
        using var _s = session;
        Assert.False(session.Window.FindByRole(AccessibilityRole.Button, "Submit")!.Node.IsEnabled);

        form.Ready.Value = true;
        session.PumpFrame();

        Assert.True(session.Window.FindByRole(AccessibilityRole.Button, "Submit")!.Node.IsEnabled);
    }
}
