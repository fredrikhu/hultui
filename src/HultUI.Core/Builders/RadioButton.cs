using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class RadioButton
{
    public static RadioButtonElement New(string groupName = "", bool isChecked = false) =>
        RadioButtonElement.New(groupName, isChecked);

    public static RadioButtonElement New(string groupName, bool isChecked, string label) =>
        RadioButtonElement.New(groupName, isChecked, label);
}
