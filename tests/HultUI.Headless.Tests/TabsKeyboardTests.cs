using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="TabsElement"/> from the keyboard, as the ARIA tab pattern has it. Its headers could
/// not take focus, so a keyboard user could not change tabs at all. Now Tab stops at the open tab
/// and only there, the arrow keys along the strip move to the next tab and open it, Home and End
/// go to the ends, Enter and Space open a tab that has focus without being open, and Delete
/// closes a closable one.
/// </summary>
public class TabsKeyboardTests
{
    private static TabsElement ThreeTabs() => Tabs.New()
        .Tab("General", () => TextElement.New("general page"))
        .Tab("Inbox", () => TextElement.New("inbox page"))
        .Tab("Logs", () => TextElement.New("logs page"));

    private static IReadOnlyList<Element> Headers(TabsElement tabs) =>
        tabs.Children.Single(c => c.Role == AccessibilityRole.TabList).Children;

    private static string PanelText(TabsElement tabs) =>
        ((TextElement)tabs.Children.Single(c => c.Role == AccessibilityRole.TabPanel).Children[0]).Content;

    private static Element? Focused(HeadlessSession session) => session.Window.Window.FocusedElement;

    private static (HeadlessSession Session, TabsElement Tabs) Show(TabsElement tabs, params Element[] around)
    {
        var session = HeadlessSession.Create(480, 240);
        var root = Box.New();
        root.AddChild(tabs);
        foreach (var e in around) root.AddChild(e);
        session.Application.SetRoot(root);
        session.PumpFrames(2);
        return (session, tabs);
    }

    [Fact]
    public void TabStopsAtTheOpenTab_AndOnlyThere()
    {
        var after = ButtonElement.New("After");
        var (session, tabs) = Show(ThreeTabs(), after);
        using var _ = session;

        session.Window.PressKey(KeyCode.Tab);
        Assert.Same(Headers(tabs)[0], Focused(session));
        Assert.True(Headers(tabs)[0].ShowsFocusVisual);

        // Past the tabs that are not open, to what follows the strip.
        session.Window.PressKey(KeyCode.Tab);
        Assert.Same(after, Focused(session));

        // With another tab open, coming back lands on that one.
        session.Window.Click(Headers(tabs)[2]);
        session.PumpFrames(2);
        after.Focus();
        session.Window.PressKey(KeyCode.Tab, KeyModifiers.Shift);
        Assert.Same(Headers(tabs)[2], Focused(session));
    }

    [Fact]
    public void AClickFocusesTheTab_AndTheArrowsMoveOnAndOpen()
    {
        var (session, tabs) = Show(ThreeTabs());
        using var _ = session;

        session.Window.Click(Headers(tabs)[0]);
        Assert.Same(Headers(tabs)[0], Focused(session));

        session.Window.PressKey(KeyCode.Right);
        session.PumpFrames(2);
        Assert.Equal(1, tabs.SelectedIndex);
        Assert.Same(Headers(tabs)[1], Focused(session));
        Assert.Equal("inbox page", PanelText(tabs));

        session.Window.PressKey(KeyCode.Right);
        Assert.Equal(2, tabs.SelectedIndex);
        // Round the end to the start, and back.
        session.Window.PressKey(KeyCode.Right);
        Assert.Equal(0, tabs.SelectedIndex);
        session.Window.PressKey(KeyCode.Left);
        Assert.Equal(2, tabs.SelectedIndex);

        session.Window.PressKey(KeyCode.Home);
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.Same(Headers(tabs)[0], Focused(session));
        session.Window.PressKey(KeyCode.End);
        Assert.Equal(2, tabs.SelectedIndex);
        Assert.Same(Headers(tabs)[2], Focused(session));
        session.PumpFrames(2);
        Assert.Equal("logs page", PanelText(tabs));
    }

    /// <summary>A strip down the side runs on Up and Down; Left and Right are not its keys.</summary>
    [Fact]
    public void AStripDownTheSideUsesUpAndDown()
    {
        var (session, tabs) = Show(ThreeTabs().Placement(TabPlacement.Left));
        using var _ = session;
        session.Window.Click(Headers(tabs)[0]);

        session.Window.PressKey(KeyCode.Down);
        Assert.Equal(1, tabs.SelectedIndex);
        Assert.Same(Headers(tabs)[1], Focused(session));
        session.Window.PressKey(KeyCode.Up);
        Assert.Equal(0, tabs.SelectedIndex);

        session.Window.PressKey(KeyCode.Right);
        Assert.Equal(0, tabs.SelectedIndex);
    }

    [Fact]
    public void EnterOrSpaceOpensTheTabWithFocus()
    {
        var (session, tabs) = Show(ThreeTabs());
        using var _ = session;

        Headers(tabs)[2].Focus();
        Assert.Equal(0, tabs.SelectedIndex);
        session.Window.PressKey(KeyCode.Enter);
        Assert.Equal(2, tabs.SelectedIndex);

        Headers(tabs)[1].Focus();
        session.Window.PressKey(KeyCode.Space);
        Assert.Equal(1, tabs.SelectedIndex);
    }

    /// <summary>Ctrl, Alt or the Windows key with an arrow is someone else's shortcut.</summary>
    [Fact]
    public void AnArrowWithAModifierIsNotTheStrips()
    {
        var (session, tabs) = Show(ThreeTabs());
        using var _ = session;
        session.Window.Click(Headers(tabs)[0]);

        session.Window.PressKey(KeyCode.Right, KeyModifiers.Ctrl);
        session.Window.PressKey(KeyCode.Right, KeyModifiers.Alt);
        session.Window.PressKey(KeyCode.End, KeyModifiers.Super);

        Assert.Equal(0, tabs.SelectedIndex);
    }

    [Fact]
    public void DeleteClosesAClosableTab_AndNothingElse()
    {
        int closed = 0;
        var (session, tabs) = Show(Tabs.New()
            .Tab("General", () => TextElement.New("general page"))
            .Tab("Logs", () => TextElement.New("logs page"), onClose: () => closed++));
        using var _ = session;

        Headers(tabs)[0].Focus();
        session.Window.PressKey(KeyCode.Delete);
        Assert.Equal(0, closed);

        Headers(tabs)[1].Focus();
        session.Window.PressKey(KeyCode.Delete);
        Assert.Equal(1, closed);
    }

    /// <summary>A key pressed in the open panel is the panel's, whatever it is.</summary>
    [Fact]
    public void KeysInThePanelAreNotTheStrips()
    {
        var field = TextInput.New("abc");
        var (session, tabs) = Show(Tabs.New()
            .Tab("General", () => field)
            .Tab("Inbox", () => TextElement.New("inbox page")));
        using var _ = session;

        session.Window.Click(field);
        session.Window.PressKey(KeyCode.Right);
        session.Window.PressKey(KeyCode.End);

        Assert.Equal(0, tabs.SelectedIndex);
        Assert.Same(field, Focused(session));
    }
}
