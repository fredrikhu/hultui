using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class NumericUpDownElementTests
{
    [Fact]
    public void New_ClampsAndSnapsToTheStep()
    {
        Assert.Equal(100, NumericUpDown.New(150).Value);
        Assert.Equal(0, NumericUpDown.New(-3).Value);
        Assert.Equal(20, NumericUpDown.New(23, 0, 100, step: 10).Value);
        Assert.Equal(2.5, NumericUpDown.New(2.4, 0, 10, step: 0.5).Value);
    }

    [Fact]
    public void DisplayText_ShowsAsManyDecimalsAsTheStep()
    {
        Assert.Equal("7", NumericUpDown.New(7).DisplayText);
        Assert.Equal("7.5", NumericUpDown.New(7.5, 0, 10, step: 0.5).DisplayText);
        Assert.Equal("0.25", NumericUpDown.New(0.25, 0, 1, step: 0.25).DisplayText);
        Assert.Equal("12 pt", NumericUpDown.New(12).Format(v => $"{v:0.#} pt").DisplayText);
    }

    [Fact]
    public void SettingValue_DoesNotFireChanged_ButAssistiveTechDoes()
    {
        var n = NumericUpDown.New(5);
        double? seen = null;
        n.OnValueChanged(v => seen = v);

        n.Value = 6;
        Assert.Null(seen);

        ((IRangeValueElement)n).SetAccessibleValue(250);
        Assert.Equal(100, n.Value);
        Assert.Equal(100, seen);
    }

    [Fact]
    public void ReadsAsASpinButton_WithAWritableRange()
    {
        var n = NumericUpDown.New(5, 0, 50, step: 5).LargeStep(25);
        Assert.Equal(AccessibilityRole.SpinButton, AccessibilityTreeBuilder.ResolveRole(n));
        Assert.Equal("5", AccessibilityTreeBuilder.ResolveValue(n));
        var r = (IRangeValueElement)n;
        Assert.False(r.IsReadOnly);
        Assert.Equal(5, r.SmallChange);
        Assert.Equal(25, r.LargeChange);
        Assert.Equal(50, r.Maximum);
    }

    [Fact]
    public void ChangingTheRange_ReclampsTheValue()
    {
        var n = NumericUpDown.New(80);
        n.Range(0, 50);
        Assert.Equal(50, n.Value);
    }
}
