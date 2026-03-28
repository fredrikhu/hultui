using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class CheckboxElementTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    [Fact]
    public void New_DefaultUnchecked()
    {
        var cb = CheckboxElement.New();
        Assert.False(cb.IsChecked);
    }

    [Fact]
    public void New_CanSetChecked()
    {
        var cb = CheckboxElement.New(true);
        Assert.True(cb.IsChecked);
    }

    [Fact]
    public void Click_TogglesState()
    {
        var cb = CheckboxElement.New();
        EventDispatcher.Dispatch(new ClickEvent { Position = new Point(5, 5) }, cb);
        Assert.True(cb.IsChecked);
        EventDispatcher.Dispatch(new ClickEvent { Position = new Point(5, 5) }, cb);
        Assert.False(cb.IsChecked);
    }

    [Fact]
    public void OnChanged_CalledOnToggle()
    {
        bool? changed = null;
        var cb = CheckboxElement.New();
        cb.OnChanged = v => changed = v;

        EventDispatcher.Dispatch(new ClickEvent { Position = new Point(5, 5) }, cb);
        Assert.True(changed);

        EventDispatcher.Dispatch(new ClickEvent { Position = new Point(5, 5) }, cb);
        Assert.False(changed);
    }

    [Fact]
    public void MeasureContent_ReturnsCheckboxSize()
    {
        var cb = CheckboxElement.New();
        cb.CheckboxSize = 24;
        var size = cb.MeasureContent(100, 100, new StubMeasureContext());
        Assert.Equal(24, size.Width);
        Assert.Equal(24, size.Height);
    }

    [Fact]
    public void PatchFrom_CopiesCheckedState()
    {
        var cb = CheckboxElement.New(true);
        var incoming = CheckboxElement.New(false);
        incoming.CheckboxSize = 24;

        cb.PatchFrom(incoming);

        Assert.False(cb.IsChecked); // copied from source
        Assert.Equal(24, cb.CheckboxSize); // patched
    }

    [Fact]
    public void InfersCheckboxRole()
    {
        var cb = CheckboxElement.New();
        Assert.Equal(AccessibilityRole.Checkbox, AccessibilityTreeBuilder.ResolveRole(cb));
    }
}
