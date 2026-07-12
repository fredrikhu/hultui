using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class TabsTests
{
    private static TabsElement TwoTabs()
    {
        var tabs = Tabs.New()
            .Tab("A", () => Text.New("panelA"))
            .Tab("B", () => Text.New("panelB"));
        tabs.ApplyTheme(Theme.Light); // builds the subtree, as style resolution would
        return tabs;
    }

    private static string ContentText(TabsElement tabs)
    {
        // Top placement: children are [strip, contentHost]; the panel is the host's child.
        var host = tabs.Children[^1];
        return ((TextElement)host.Children[0]).Content;
    }

    [Fact]
    public void Build_ProducesStripAndContent_ShowingFirstTab()
    {
        var tabs = TwoTabs();
        Assert.Equal(2, tabs.Children.Count);
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.Equal("panelA", ContentText(tabs));
    }

    [Fact]
    public void SetSelected_SwapsContent()
    {
        var tabs = TwoTabs();
        tabs.SetSelected(1);
        Assert.Equal(1, tabs.SelectedIndex);
        Assert.Equal("panelB", ContentText(tabs));
    }

    [Fact]
    public void SetSelected_InvokesCallback()
    {
        int? changed = null;
        var tabs = Tabs.New()
            .Tab("A", () => Text.New("a"))
            .Tab("B", () => Text.New("b"))
            .OnTabChanged(i => changed = i);
        tabs.ApplyTheme(Theme.Light);

        tabs.SetSelected(1);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void PatchFrom_Uncontrolled_KeepsLiveSelection()
    {
        var live = TwoTabs();
        live.SetSelected(1);

        // A rebuild with no explicit index must not reset the user's selection.
        var incoming = Tabs.New().Tab("A", () => Text.New("panelA")).Tab("B", () => Text.New("panelB"));
        live.PatchFrom(incoming);
        live.OnReconciled();

        Assert.Equal(1, live.SelectedIndex);
        Assert.Equal("panelB", ContentText(live));
    }

    [Fact]
    public void PatchFrom_Controlled_AdoptsIncomingSelection()
    {
        var live = TwoTabs(); // index 0

        var incoming = Tabs.New()
            .Tab("A", () => Text.New("panelA"))
            .Tab("B", () => Text.New("panelB"))
            .Selected(1); // explicitly controlled
        live.PatchFrom(incoming);
        live.OnReconciled();

        Assert.Equal(1, live.SelectedIndex);
        Assert.Equal("panelB", ContentText(live));
    }
}
