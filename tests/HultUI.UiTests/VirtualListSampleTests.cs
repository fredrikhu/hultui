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

    [Fact]
    public void List_ExposesFirstItem_Lazily()
    {
        var first = List().FindFirstChild();
        Assert.NotNull(first);
        Assert.Equal(ControlType.ListItem, first!.ControlType);
        Assert.Equal("Item #0", first.Name);
    }

    [Fact]
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

    [Fact]
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

    public void Dispose()
    {
        _automation.Dispose();
        try { _app.Close(); } catch { /* best effort */ }
        _app.Dispose();
    }
}
