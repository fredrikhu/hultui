using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
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
        // A partial override keeps the palette's colours.
        Assert.Equal(Theme.Light.Primary, s.OnTrackColor);
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

    // ── Disabled ─────────────────────────────────────────────────────────

    [Fact]
    public void Disabled_IgnoresItsClick_LeavesTheTabRing_AndDims()
    {
        int toggles = 0;
        var s = ToggleSwitch.New().OnToggled(_ => toggles++).Enabled(false);
        s.ApplyTheme(Theme.Dark);

        EventDispatcher.DispatchDirect(new ClickEvent(), s);
        Assert.False(s.IsOn);
        Assert.Equal(0, toggles);
        Assert.False(s.IsFocusable);
        Assert.False(s.IsEnabledForAccessibility);
        Assert.Equal(Theme.Dark.ToggleSwitch.DisabledOpacity, s.RenderOpacity);
        Assert.Equal(CursorType.Default, s.Inherited.Cursor);

        s.IsEnabled = true;
        EventDispatcher.DispatchDirect(new ClickEvent(), s);
        Assert.True(s.IsOn);
        Assert.Equal(1, toggles);
        Assert.True(s.IsFocusable);
        Assert.Equal(1f, s.RenderOpacity);
        Assert.Equal(CursorType.Pointer, s.Inherited.Cursor);
    }

    [Fact]
    public void Disabled_StopsItsClick_SoARowBehindItIsNotClicked()
    {
        int rowClicks = 0;
        var s = ToggleSwitch.New().Enabled(false);
        var row = Box.New().OnClick(_ => rowClicks++).Children(s);

        EventDispatcher.Dispatch(new ClickEvent(), s);
        Assert.Equal(0, rowClicks);

        s.IsEnabled = true;
        EventDispatcher.Dispatch(new ClickEvent(), s);
        Assert.Equal(1, rowClicks); // an enabled switch leaves its click to the app, as before
    }

    [Fact]
    public void Disabled_IsOnCanStillBeSetFromCode()
    {
        var s = ToggleSwitch.New().Enabled(false);
        s.IsOn = true;
        Assert.True(s.IsOn);
    }

    [Fact]
    public void Disabled_DimsTheAppsOwnOpacity_RatherThanReplacingIt()
    {
        var s = ToggleSwitch.New().Opacity(0.5f).Enabled(false);
        s.ApplyTheme(Theme.Light);
        Assert.Equal(0.5f, s.Opacity);
        Assert.Equal(0.5f * Theme.Light.ToggleSwitch.DisabledOpacity, s.RenderOpacity, 3);
    }

    [Fact]
    public void Enabled_Patch_CopiesIt()
    {
        var live = ToggleSwitch.New();
        live.PatchFrom(ToggleSwitch.New().Enabled(false));
        Assert.False(live.IsEnabled);
        Assert.False(live.IsFocusable);
    }
}
