using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class ToggleSwitch
{
    /// <summary>
    /// An on/off switch: a pill with a knob that slides across. Toggles on click and on
    /// Space while focused, and reads as a switch to assistive technology.
    /// </summary>
    public static ToggleSwitchElement New(bool isOn = false, string? label = null, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        ToggleSwitchElement.New(isOn, label, file, line);
}
