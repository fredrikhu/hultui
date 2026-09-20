using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

public class ToggleSwitchTests
{
    private static ToggleSwitchElement Mount(HeadlessSession session, bool isOn = false)
    {
        var s = ToggleSwitch.New(isOn, "Airplane mode");
        session.Window.SetRoot(Box.New().Padding(new Thickness(40)).Children(s));
        session.PumpFrame();
        return s;
    }

    [Fact]
    public void Click_Toggles_AndFiresChanged()
    {
        using var session = HeadlessSession.Create();
        var s = Mount(session);
        var seen = new List<bool>();
        s.OnToggled(seen.Add);

        session.Window.Click(s);
        session.Window.Click(s);

        Assert.Equal([true, false], seen);
        Assert.False(s.IsOn);
    }

    [Fact]
    public void Space_Toggles_WhileFocused()
    {
        using var session = HeadlessSession.Create();
        var s = Mount(session);

        session.Window.Click(s);          // focus + toggle on
        Assert.True(s.IsOn);
        session.Window.PressKey(KeyCode.Space);
        Assert.False(s.IsOn);
    }

    [Fact]
    public void Toggling_SlidesTheKnob_OverTheTransition()
    {
        using var session = HeadlessSession.Create();
        var s = Mount(session);

        session.Window.Click(s);
        Assert.True(s.IsOn);
        // Mid-slide after one 60 Hz frame of a 150 ms transition.
        session.PumpFrame();
        Assert.InRange(s.KnobPosition, 0.01f, 0.99f);
        session.PumpFrame(TimeSpan.FromMilliseconds(300));
        Assert.Equal(1f, s.KnobPosition);
    }

    [Fact]
    public void ReadsAsASwitch_WithItsLabelAsName()
    {
        using var session = HeadlessSession.Create();
        Mount(session, isOn: true);

        var node = session.Window.FindByRole(AccessibilityRole.Switch, "Airplane mode");
        Assert.NotNull(node);
    }
}
