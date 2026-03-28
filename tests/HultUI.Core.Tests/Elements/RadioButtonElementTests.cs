using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class RadioButtonElementTests
{
    [Fact]
    public void New_DefaultUnchecked()
    {
        var rb = RadioButtonElement.New("group1");
        Assert.False(rb.IsChecked);
        Assert.Equal("group1", rb.GroupName);
    }

    [Fact]
    public void Click_Selects()
    {
        var rb = RadioButtonElement.New("group1");
        EventDispatcher.Dispatch(new ClickEvent { Position = new Point(5, 5) }, rb);
        Assert.True(rb.IsChecked);
    }

    [Fact]
    public void Click_AlreadySelected_NoOp()
    {
        var rb = RadioButtonElement.New("group1", true);
        int callCount = 0;
        rb.OnChanged = _ => callCount++;

        EventDispatcher.Dispatch(new ClickEvent { Position = new Point(5, 5) }, rb);
        Assert.True(rb.IsChecked);
        Assert.Equal(0, callCount); // Already checked, no change
    }

    [Fact]
    public void MutualExclusion_WithinGroup()
    {
        var container = BoxElement.New();
        var rb1 = RadioButtonElement.New("group1", true);
        var rb2 = RadioButtonElement.New("group1");
        var rb3 = RadioButtonElement.New("group1");
        container.AddChild(rb1);
        container.AddChild(rb2);
        container.AddChild(rb3);

        Assert.True(rb1.IsChecked);
        Assert.False(rb2.IsChecked);

        // Click rb2 — should deselect rb1
        EventDispatcher.Dispatch(new ClickEvent { Position = new Point(5, 5) }, rb2);
        Assert.False(rb1.IsChecked);
        Assert.True(rb2.IsChecked);
        Assert.False(rb3.IsChecked);

        // Click rb3 — should deselect rb2
        EventDispatcher.Dispatch(new ClickEvent { Position = new Point(5, 5) }, rb3);
        Assert.False(rb1.IsChecked);
        Assert.False(rb2.IsChecked);
        Assert.True(rb3.IsChecked);
    }

    [Fact]
    public void MutualExclusion_NestedInWrappers()
    {
        // Radio buttons inside separate wrapper elements (like Flex.Row in the sample)
        var root = BoxElement.New();
        var wrapper1 = BoxElement.New();
        var wrapper2 = BoxElement.New();
        var wrapper3 = BoxElement.New();
        var rb1 = RadioButtonElement.New("group1", true);
        var rb2 = RadioButtonElement.New("group1");
        var rb3 = RadioButtonElement.New("group1");
        wrapper1.AddChild(rb1);
        wrapper2.AddChild(rb2);
        wrapper3.AddChild(rb3);
        root.AddChild(wrapper1);
        root.AddChild(wrapper2);
        root.AddChild(wrapper3);

        Assert.True(rb1.IsChecked);

        // Click rb2 — should deselect rb1 even though they're not direct siblings
        EventDispatcher.Dispatch(new ClickEvent { Position = new Point(5, 5) }, rb2);
        Assert.False(rb1.IsChecked);
        Assert.True(rb2.IsChecked);
        Assert.False(rb3.IsChecked);
    }

    [Fact]
    public void DifferentGroups_Independent()
    {
        var container = BoxElement.New();
        var rbA = RadioButtonElement.New("groupA", true);
        var rbB = RadioButtonElement.New("groupB", true);
        container.AddChild(rbA);
        container.AddChild(rbB);

        // Click rbB — should not affect rbA (different group)
        EventDispatcher.Dispatch(new ClickEvent { Position = new Point(5, 5) }, rbB);
        Assert.True(rbA.IsChecked);
        Assert.True(rbB.IsChecked);
    }

    [Fact]
    public void OnChanged_CalledOnSelection()
    {
        var container = BoxElement.New();
        var rb1 = RadioButtonElement.New("g", true);
        bool? rb1Changed = null;
        rb1.OnChanged = v => rb1Changed = v;

        var rb2 = RadioButtonElement.New("g");
        bool? rb2Changed = null;
        rb2.OnChanged = v => rb2Changed = v;

        container.AddChild(rb1);
        container.AddChild(rb2);

        EventDispatcher.Dispatch(new ClickEvent { Position = new Point(5, 5) }, rb2);
        Assert.False(rb1Changed); // Deselected
        Assert.True(rb2Changed);  // Selected
    }

    [Fact]
    public void PatchFrom_CopiesCheckedState()
    {
        var rb = RadioButtonElement.New("g", true);
        var incoming = RadioButtonElement.New("g");
        incoming.RadioSize = 24;

        rb.PatchFrom(incoming);

        Assert.False(rb.IsChecked); // copied from source
        Assert.Equal(24, rb.RadioSize); // patched
    }

    [Fact]
    public void InfersRadioButtonRole()
    {
        var rb = RadioButtonElement.New("g");
        Assert.Equal(AccessibilityRole.RadioButton, AccessibilityTreeBuilder.ResolveRole(rb));
    }

    [Fact]
    public void MeasureContent_ReturnsRadioSize()
    {
        var rb = RadioButtonElement.New("g");
        rb.RadioSize = 20;
        var size = rb.MeasureContent(100, 100, new StubMeasureContext());
        Assert.Equal(20, size.Width);
        Assert.Equal(20, size.Height);
    }
}
