using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class Checkbox
{
    public static CheckboxElement New(bool isChecked = false) => CheckboxElement.New(isChecked);
}
