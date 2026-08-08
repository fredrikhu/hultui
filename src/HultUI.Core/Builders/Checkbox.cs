using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class Checkbox
{
    public static CheckboxElement New(bool isChecked = false, string? label = null, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        CheckboxElement.New(isChecked, label, file, line);
}
