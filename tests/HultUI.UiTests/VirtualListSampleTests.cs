using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// Drives the 100k-item VirtualList sample through FlaUI to prove the on-demand
/// accessibility path: items are exposed lazily (no freeze walking in), and a
/// list item reports and updates its selection state.
/// </summary>
[Collection("ui")]
public sealed class VirtualListSampleTests : IDisposable
{
    private readonly Application _app;
    private readonly UIA3Automation _automation;
    private readonly Window _window;

    public VirtualListSampleTests()
    {
        _app = Application.Launch(SamplePath.VirtualList());
        _automation = new UIA3Automation();
        _window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(15))
            ?? throw new InvalidOperationException("sample window did not appear");
    }

    private AutomationElement List() =>
        _window.FindFirstDescendant(cf => cf.ByControlType(ControlType.List))
        ?? throw new InvalidOperationException("no List found");

    [UiFact]
    public void List_ExposesFirstItem_Lazily()
    {
        var first = List().FindFirstChild();
        Assert.NotNull(first);
        Assert.Equal(ControlType.ListItem, first!.ControlType);
        Assert.Equal("Item #0", first.Name);
    }

    [UiFact]
    public void ListItem_ReportsAndUpdatesSelection()
    {
        var first = List().FindFirstChild()!;
        var selectionItem = first.Patterns.SelectionItem.Pattern;
        Assert.False(selectionItem.IsSelected.Value, "nothing selected initially");

        selectionItem.Select();

        Retry.WhileFalse(
            () => List().FindFirstChild()!.Patterns.SelectionItem.Pattern.IsSelected.Value,
            timeout: TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "item did not become selected after Select()");
    }

    [UiFact]
    public void ListItems_ReportOffscreenState()
    {
        var list = List();
        var first = list.FindFirstChild();
        Assert.False(first!.Properties.IsOffscreen.Value); // top item is visible

        // The last of 100k items is far below the viewport — reached instantly.
        var last = _automation.TreeWalkerFactory.GetRawViewWalker().GetLastChild(list);
        Assert.NotNull(last);
        Assert.True(last!.Properties.IsOffscreen.Value);
    }

    [UiFact]
    public void List_SupportsScrollPattern()
    {
        var list = List();
        var scroll = list.Patterns.Scroll.Pattern;
        Assert.True(scroll.VerticallyScrollable.Value);
        Assert.InRange(scroll.VerticalScrollPercent.Value, -0.01, 1.0); // starts at the top

        scroll.SetScrollPercent(-1 /* keep horizontal */, 50.0);

        Retry.WhileFalse(
            () => list.Patterns.Scroll.Pattern.VerticalScrollPercent.Value > 40,
            timeout: TimeSpan.FromSeconds(5), throwOnTimeout: true,
            timeoutMessage: "vertical scroll percent did not update");

        // After scrolling halfway, the very first item is far above the viewport.
        Assert.True(list.FindFirstChild()!.Properties.IsOffscreen.Value);
    }

    [UiFact]
    public void List_ExposesContainerSelection()
    {
        var selection = List().Patterns.Selection.Pattern;
        Assert.False(selection.CanSelectMultiple.Value);
        Assert.Empty(selection.Selection.Value); // nothing selected initially

        List().FindFirstChild()!.Patterns.SelectionItem.Pattern.Select();

        Retry.WhileFalse(
            () =>
            {
                var sel = List().Patterns.Selection.Pattern.Selection.Value;
                return sel.Length == 1 && sel[0].Name == "Item #0";
            },
            timeout: TimeSpan.FromSeconds(5), throwOnTimeout: true,
            timeoutMessage: "container selection did not reflect the selected item");
    }

    [UiFact]
    public void List_FindsItemByName_WithoutWalking()
    {
        // The container locates a deep item straight from its data — no walking
        // 42 siblings — and returns exactly that element.
        var found = List().Patterns.ItemContainer.Pattern.FindItemByProperty(
            null, _automation.PropertyLibrary.Element.Name, "Item #42");

        Assert.NotNull(found);
        Assert.Equal("Item #42", found!.Name);
        Assert.Equal(ControlType.ListItem, found.ControlType);
    }

    public void Dispose()
    {
        _automation.Dispose();
        try { _app.Close(); } catch { /* best effort */ }
        _app.Dispose();
    }
}
