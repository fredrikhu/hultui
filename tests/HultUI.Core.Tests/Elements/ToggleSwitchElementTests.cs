using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class ToggleSwitchElementTests
{
    [Fact]
    public void New_StartsWithTheGivenState_AndTheKnobOnThatSide()
    {
        var off = ToggleSwitch.New();
        var on = ToggleSwitch.New(true, "Wi-Fi");

        Assert.False(off.IsOn);
        Assert.Equal(0f, off.KnobPosition);
        Assert.True(on.IsOn);
        Assert.Equal(1f, on.KnobPosition);
        Assert.Equal("Wi-Fi", on.Label);
    }

    [Fact]
    public void IsOn_Unmounted_SnapsTheKnob()
    {
        var s = ToggleSwitch.New();
        s.IsOn = true;
        Assert.Equal(1f, s.KnobPosition);
        s.IsOn = false;
        Assert.Equal(0f, s.KnobPosition);
    }

    [Fact]
    public void IsOn_SettingTheSameValue_DoesNotFireChanged()
    {
        var s = ToggleSwitch.New(true);
        int fired = 0;
        s.OnToggled(_ => fired++);
        s.IsOn = true;
        Assert.Equal(0, fired);
    }

    [Fact]
    public void InfersTheSwitchRole_AndReportsItsState()
    {
        var s = ToggleSwitch.New(true);
        Assert.Equal(AccessibilityRole.Switch, AccessibilityTreeBuilder.ResolveRole(s));
        Assert.True(AccessibilityTreeBuilder.ResolveIsToggled(s));
        s.IsOn = false;
        Assert.False(AccessibilityTreeBuilder.ResolveIsToggled(s));
    }

    [Fact]
    public void ApplyTheme_DerivesColoursFromThePalette()
    {
        var s = ToggleSwitch.New();
        s.ApplyTheme(Theme.Dark);
        Assert.Equal(Theme.Dark.Primary, s.OnTrackColor);
        Assert.Equal(Theme.Dark.PrimaryText, s.OnKnobColor);
        Assert.Equal(Theme.Dark.SurfaceVariant, s.OffTrackColor);
    }

    [Fact]
    public void ApplyTheme_HonoursAnAppOverride()
    {
        var themed = Theme.Light.With(new ToggleSwitchTheme { TrackWidth = 60f, TransitionSeconds = 0f });
        var s = ToggleSwitch.New();
        s.ApplyTheme(themed);
        Assert.Equal(60f, s.TrackWidth);
        Assert.Equal(0f, s.TransitionSeconds);
    }

    [Fact]
    public void PatchFrom_CopiesTheIncomingState_AndHandler()
    {
        var live = ToggleSwitch.New();
        bool? seen = null;
        var incoming = ToggleSwitch.New(true, "Bluetooth").OnToggled(v => seen = v);

        live.PatchFrom(incoming);

        Assert.True(live.IsOn);
        Assert.Equal("Bluetooth", live.Label);
        Assert.Same(incoming.OnChanged, live.OnChanged);
        Assert.Null(seen); // a patch is not a user toggle
    }
}
