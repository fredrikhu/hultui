using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class RadioButton
{
    public static RadioButtonElement New(string groupName = "", bool isChecked = false, string? label = null,
        [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        RadioButtonElement.New(groupName, isChecked, label, file, line);
}
