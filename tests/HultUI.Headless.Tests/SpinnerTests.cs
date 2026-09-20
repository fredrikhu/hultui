using HultUI.Core.Accessibility;
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Headless.Tests;

public class SpinnerTests
{
    [Fact]
    public void Turns_WhilePainted_AndStopsWhenTheRootIsSwapped()
    {
        using var session = HeadlessSession.Create();
        var spinner = Spinner.New();
        session.Window.SetRoot(Box.New().Children(spinner));
        session.PumpFrame();                         // first paint starts the turn

        session.PumpFrame(TimeSpan.FromMilliseconds(450));
        Assert.InRange(spinner.Phase, 0.45f, 0.6f);  // half of a 0.9 s turn
        Assert.True(AnimationManager.HasActiveAnimations);

        session.Window.SetRoot(Box.New());
        session.PumpFrames(2);
        Assert.Equal(0f, spinner.Phase);
        Assert.False(AnimationManager.HasActiveAnimations);
    }

    [Fact]
    public void Removed_FromItsParent_Stops()
    {
        using var session = HeadlessSession.Create();
        var spinner = Spinner.New();
        var host = Box.New().Children(spinner);
        session.Window.SetRoot(host);
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(100));
        Assert.True(spinner.Phase > 0f);

        host.RemoveChild(spinner);
        session.PumpFrames(2);
        Assert.Equal(0f, spinner.Phase);
        Assert.False(AnimationManager.HasActiveAnimations);
    }

    [Fact]
    public void MeasuresToItsSize_AndReadsByName()
    {
        using var session = HeadlessSession.Create();
        var spinner = Spinner.New(24f);
        spinner.AccessibleName = "Fetching";
        // A row, so the cross axis does not stretch it: a column would, like any leaf.
        session.Window.SetRoot(Flex.Row().AlignItems(Core.Layout.AlignItems.FlexStart).Children(spinner));
        session.PumpFrame();

        Assert.Equal(24f, spinner.Layout.AbsoluteBorderBox.Width);
        Assert.Equal(24f, spinner.Layout.AbsoluteBorderBox.Height);
        Assert.NotNull(session.Window.FindByRole(AccessibilityRole.ProgressBar, "Fetching"));
    }
}
