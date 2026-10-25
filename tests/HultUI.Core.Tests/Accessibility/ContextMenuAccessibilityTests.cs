using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Accessibility;

/// <summary>
/// A context menu draws its items rather than building an element for each, so without the
/// item-source contract a screen reader finds one empty box where the menu is. These pin
/// the items being published — and the separators not being.
/// </summary>
public class ContextMenuAccessibilityTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    private static ContextMenuElement Menu(params ContextMenuItem[] items) =>
        new(items, new ContextMenuTheme(), closeMenu: () => { });

    private AccessibilityNode BuildLaidOut(ContextMenuElement menu)
    {
        _layout.PerformLayout(menu, 300, 400);
        return AccessibilityTreeBuilder.Build(menu);
    }

    [Fact]
    public void EveryChoiceIsAnItemAndSeparatorsAreNot()
    {
        var tree = BuildLaidOut(Menu(
            new ContextMenuItem { Label = "Play" },
            ContextMenuItem.Separator(),
            new ContextMenuItem { Label = "Mark watched" }));

        Assert.Equal(AccessibilityRole.List, tree.Role);
        Assert.Equal(2, tree.Children.Count);
        Assert.Equal("Play", tree.Children[0].Name);
        Assert.Equal("Mark watched", tree.Children[1].Name);
    }

    [Fact]
    public void AnItemIsAnnouncedWithItsShortcut()
    {
        var tree = BuildLaidOut(Menu(new ContextMenuItem { Label = "Copy", ShortcutHint = "Ctrl C" }));

        Assert.Equal("Copy, Ctrl C", tree.Children[0].Name);
    }

    [Fact]
    public void InvokingAnItemRunsItsAction()
    {
        bool ran = false;
        var menu = Menu(
            new ContextMenuItem { Label = "First" },
            ContextMenuItem.Separator(),
            new ContextMenuItem { Label = "Second", Action = () => ran = true });

        // Index 1 in accessibility terms is the second *choice*, not the separator.
        ((IAccessibleItemSource)menu).ActivateItem(1);

        Assert.True(ran);
    }

    [Fact]
    public void TheChoiceInForceIsCheckedRatherThanMarkedInItsName()
    {
        var tree = BuildLaidOut(Menu(
            new ContextMenuItem { Label = "Grid", IsChecked = true },
            new ContextMenuItem { Label = "List", IsChecked = false }));

        // The mark is a state, not punctuation in front of the label: a reader says
        // "Grid, on" rather than "bullet Grid", and "List" is not three spaces long.
        Assert.Equal("Grid", tree.Children[0].Name);
        Assert.Equal("List", tree.Children[1].Name);
        Assert.True(tree.Children[0].IsToggled);
        Assert.False(tree.Children[1].IsToggled);
    }

    [Fact]
    public void AnOrdinaryItemHasNoToggleStateAtAll()
    {
        var tree = BuildLaidOut(Menu(
            new ContextMenuItem { Label = "Play" },
            new ContextMenuItem { Label = "Subtitles on", IsChecked = false }));

        // Null is "this is not a thing that can be on", which is different from "off" —
        // the bridge maps the two to different UIA answers.
        Assert.Null(tree.Children[0].IsToggled);
        Assert.False(tree.Children[1].IsToggled);
    }

    [Fact]
    public void ADisabledItemDoesNothingWhenInvoked()
    {
        bool ran = false;
        var menu = Menu(new ContextMenuItem { Label = "Nope", IsDisabled = true, Action = () => ran = true });

        ((IAccessibleItemSource)menu).ActivateItem(0);

        Assert.False(ran);
    }

    // ── Plan 17, round 5: what a reader is told while the menu is open ──

    /// <summary>What the menu told, property by property.</summary>
    private sealed class Told : IElementPropertyObserver
    {
        public readonly List<string> Properties = [];
        public void OnElementPropertyChanged(Element element, string propertyName) => Properties.Add(propertyName);
    }

    /// <summary>Draws nothing; measures a line <paramref name="lineHeight"/> high.</summary>
    private sealed class Drawing(float lineHeight) : HultUI.Core.Tests.Elements.NullRenderContext
    {
        public override HultUI.Core.Primitives.Size MeasureText(string text, ResolvedInheritedStyle style) =>
            new(text.Length * 7f, lineHeight);
    }

    [Fact]
    public void TheItemsHaveTheirPlaces_OnceTheMenuIsLaidOut_BeforeItIsDrawn()
    {
        var theme = new ContextMenuTheme();
        var tree = BuildLaidOut(Menu(
            new ContextMenuItem { Label = "Copy" },
            ContextMenuItem.Separator(),
            new ContextMenuItem { Label = "Paste" }));

        // The stub measures a line 14 high. The tree is built in the frame the menu opens, before it
        // is drawn, and its items had no height until then.
        float row = theme.ItemPadding.Top + 14f + theme.ItemPadding.Bottom;
        float separator = theme.SeparatorMargin * 2 + 1;
        Assert.Equal(theme.Padding.Top, tree.Children[0].BoundingRect.Y - tree.BoundingRect.Y, 0.01);
        Assert.Equal(row, tree.Children[0].BoundingRect.Height, 0.01);
        Assert.Equal(theme.Padding.Top + row + separator, tree.Children[1].BoundingRect.Y - tree.BoundingRect.Y, 0.01);
        Assert.Equal(row, tree.Children[1].BoundingRect.Height, 0.01);
    }

    [Theory]
    [InlineData(14f, 0)]
    [InlineData(20f, 1)]
    public void ADrawingThatPlacesTheItemsElsewhere_TellsTheirBounds_Once(float drawnLineHeight, int told)
    {
        var menu = Menu(new ContextMenuItem { Label = "Copy" }, new ContextMenuItem { Label = "Paste" });
        var observer = new Told();
        menu.AccessibilityObserver = observer;
        StyleResolver.Resolve(menu, Theme.Light);
        _layout.PerformLayout(menu, 300, 400);

        // The stub measured lines 14 high. A drawing context that measures them otherwise (only a
        // test's fixed metrics do) places the items elsewhere as it draws, once: it keeps its heights.
        ElementRenderer.RenderTree(menu, new Drawing(drawnLineHeight));
        ElementRenderer.RenderTree(menu, new Drawing(drawnLineHeight));
        Assert.Equal(Enumerable.Repeat(AccessibilityProperties.BoundingRect, told), observer.Properties);
    }

    [Fact]
    public void AMoveOfTheSelection_IsTold_AndOnlyAMove()
    {
        var menu = Menu(
            new ContextMenuItem { Label = "Copy" },
            ContextMenuItem.Separator(),
            new ContextMenuItem { Label = "Paste" });
        var told = new Told();
        menu.AccessibilityObserver = told;
        var items = (IAccessibleItemSource)menu;

        menu.MoveSelection(1);
        Assert.Equal(0, items.SelectedIndex);
        menu.MoveSelection(1);
        Assert.Equal(1, items.SelectedIndex);
        menu.SetHoveredIndex(2);   // where it is: no move
        menu.SetHoveredIndex(-1);
        Assert.Equal(-1, items.SelectedIndex);

        Assert.Equal(Enumerable.Repeat(AccessibilityProperties.IsSelected, 3), told.Properties);
    }
}
