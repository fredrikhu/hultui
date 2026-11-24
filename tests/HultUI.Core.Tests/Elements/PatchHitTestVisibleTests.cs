using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// <see cref="Element.IsHitTestVisible"/> follows the build, as <see cref="Element.IsFocusable"/>
/// does. A rebuild that patched an element left it as it was made, so a field built out of the
/// pointer's reach — shown greyed until a checkbox beside it is ticked — never took a click
/// again, whatever the later builds said.
/// </summary>
public class PatchHitTestVisibleTests
{
    [Fact]
    public void APatch_GivesBackWhatTheBuildAsksFor()
    {
        var live = DatePickerElement.New();
        live.IsHitTestVisible = false;
        live.IsFocusable = false;

        live.PatchFrom(DatePickerElement.New());

        Assert.True(live.IsHitTestVisible);
        Assert.True(live.IsFocusable);
    }

    [Fact]
    public void APatch_TakesItAway_WhenTheBuildDoes()
    {
        var live = new BoxElement();
        var off = new BoxElement { IsHitTestVisible = false };

        live.PatchFrom(off);

        Assert.False(live.IsHitTestVisible);
    }
}
