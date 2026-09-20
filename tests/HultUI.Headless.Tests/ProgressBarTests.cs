using HultUI.Core.Accessibility;
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

public class ProgressBarTests
{
    [Fact]
    public void FillsTheWidthItIsGiven()
    {
        using var session = HeadlessSession.Create(width: 400, height: 200);
        var bar = ProgressBar.New(0.5);
        session.Window.SetRoot(Box.New().Padding(new Thickness(20)).Children(bar));
        session.PumpFrame();

        Assert.Equal(360f, bar.Layout.AbsoluteBorderBox.Width);
        Assert.Equal(4f, bar.Layout.AbsoluteBorderBox.Height);
    }

    [Fact]
    public void Indeterminate_SweepsWhileMounted_AndStopsWhenRemoved()
    {
        using var session = HeadlessSession.Create();
        var bar = ProgressBar.New().Indeterminate();
        session.Window.SetRoot(Box.New().Children(bar));
        session.PumpFrame();

        session.PumpFrame(TimeSpan.FromMilliseconds(700));
        Assert.InRange(bar.SweepPosition, 0.4f, 0.6f);   // half of a 1.4 s sweep
        Assert.True(AnimationManager.HasActiveAnimations);

        // Swapping the root fires no unmount; the sweep notices on its next tick that
        // its root lost its host, and the manager drops the cancelled tween a tick later.
        session.Window.SetRoot(Box.New());
        session.PumpFrames(2);
        Assert.Equal(0f, bar.SweepPosition);
        Assert.False(AnimationManager.HasActiveAnimations);
    }

    [Fact]
    public void ARebuildTwin_NeverStartsASweep()
    {
        using var session = HeadlessSession.Create();
        var twin = ProgressBar.New().Indeterminate();
        // Parented (so "mounted" in the lifecycle sense) but hosted by nothing.
        Box.New().Children(twin);
        session.PumpFrames(3);
        Assert.False(twin.IsHosted);
        Assert.False(AnimationManager.HasActiveAnimations);
    }

    [Fact]
    public void ClearingIndeterminate_StopsTheSweep()
    {
        using var session = HeadlessSession.Create();
        var bar = ProgressBar.New().Indeterminate();
        session.Window.SetRoot(Box.New().Children(bar));
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(300));
        Assert.True(bar.SweepPosition > 0f);

        bar.Indeterminate(false).Value(0.4);
        session.PumpFrame();
        Assert.Equal(0f, bar.SweepPosition);
        Assert.False(AnimationManager.HasActiveAnimations);
    }

    [Fact]
    public void ReadsAsAProgressBar_ByName()
    {
        using var session = HeadlessSession.Create();
        var bar = ProgressBar.New(0.68);
        bar.AccessibleName = "Downloading update";
        session.Window.SetRoot(Box.New().Children(bar));
        session.PumpFrame();

        Assert.NotNull(session.Window.FindByRole(AccessibilityRole.ProgressBar, "Downloading update"));
    }
}
