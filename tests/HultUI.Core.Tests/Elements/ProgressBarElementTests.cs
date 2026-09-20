using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class ProgressBarElementTests
{
    [Fact]
    public void New_ClampsToTheRange()
    {
        Assert.Equal(1, ProgressBar.New(1.5).Value);
        Assert.Equal(0, ProgressBar.New(-1).Value);
        Assert.Equal(40, ProgressBar.New(40, max: 100).Value);
    }

    [Fact]
    public void Fraction_FollowsTheRange()
    {
        var bar = ProgressBar.New(25, max: 100);
        Assert.Equal(0.25, bar.Fraction);
        bar.Range(0, 50);
        Assert.Equal(0.5, bar.Fraction);
        bar.Range(5, 5);
        Assert.Equal(0, bar.Fraction);
    }

    [Fact]
    public void ReadsAsAProgressBar_WithAReadOnlyRangeValue()
    {
        var bar = ProgressBar.New(0.3);
        Assert.Equal(AccessibilityRole.ProgressBar, AccessibilityTreeBuilder.ResolveRole(bar));

        var range = (IRangeValueElement)bar;
        Assert.Equal(0.3, range.Value);
        Assert.Equal(0, range.Minimum);
        Assert.Equal(1, range.Maximum);
        Assert.True(range.IsReadOnly);
        range.SetAccessibleValue(0.9);
        Assert.Equal(0.3, bar.Value);
    }

    [Fact]
    public void Indeterminate_Unmounted_DoesNotStartAnAnimation()
    {
        var bar = ProgressBar.New().Indeterminate();
        Assert.True(bar.IsIndeterminate);
        Assert.Equal(0f, bar.SweepPosition);
    }

    [Fact]
    public void ApplyTheme_DerivesFromThePalette_AndHonoursOverrides()
    {
        var bar = ProgressBar.New();
        bar.ApplyTheme(Theme.Dark);
        Assert.Equal(Theme.Dark.Primary, bar.FillColor);
        Assert.Equal(Theme.Dark.Border, bar.TrackColor);

        bar.ApplyTheme(Theme.Light.With(new ProgressBarTheme { Height = 8f, CornerRadius = 0f }));
        Assert.Equal(8f, bar.Height);
        Assert.Equal(0f, bar.CornerRadius);
        Assert.Equal(Theme.Light.Primary, bar.FillColor); // a partial override keeps the palette's colours
    }

    [Fact]
    public void PatchFrom_CopiesValueRangeAndMode()
    {
        var live = ProgressBar.New(0.2);
        live.PatchFrom(ProgressBar.New(70, max: 100).Indeterminate());
        Assert.Equal(70, live.Value);
        Assert.Equal(100, live.Maximum);
        Assert.True(live.IsIndeterminate);
    }
}
