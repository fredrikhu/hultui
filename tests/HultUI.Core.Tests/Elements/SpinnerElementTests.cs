using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class SpinnerElementTests
{
    [Fact]
    public void ReadsAsAnIndeterminateProgressBar()
    {
        var s = Spinner.New();
        Assert.Equal(AccessibilityRole.ProgressBar, AccessibilityTreeBuilder.ResolveRole(s));
        var range = (IRangeValueElement)s;
        Assert.True(range.IsReadOnly);
        Assert.Equal(0, range.Maximum);
    }

    [Fact]
    public void ApplyTheme_TakesTheThemeSize_UnlessOneWasAsked()
    {
        var themed = Theme.Light.With(new SpinnerTheme { Size = 24f, DotCount = 12 });

        var defaulted = Spinner.New();
        defaulted.ApplyTheme(themed);
        Assert.Equal(24f, defaulted.Size);
        Assert.Equal(12, defaulted.DotCount);

        var sized = Spinner.New(40f);
        sized.ApplyTheme(themed);
        Assert.Equal(40f, sized.Size);
        Assert.Equal(themed.Primary, sized.Color);
    }

    [Fact]
    public void Unpainted_DoesNotSpin()
    {
        var s = Spinner.New();
        Assert.Equal(0f, s.Phase);
    }

    [Fact]
    public void PatchFrom_KeepsTheAskedSize()
    {
        var live = Spinner.New();
        live.PatchFrom(Spinner.New(32f));
        Assert.Equal(32f, live.Size);
        live.ApplyTheme(Theme.Light);
        Assert.Equal(32f, live.Size);
    }
}
