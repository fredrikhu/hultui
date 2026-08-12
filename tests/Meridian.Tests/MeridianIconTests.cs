using System.Reflection;
using HultUI.Icons;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Every Phosphor name the app references must exist in the bundled set —
/// <see cref="Phosphor.Draw"/> renders an unknown name as an empty box rather
/// than throwing, so a typo would otherwise cost an icon silently.
/// </summary>
public class MeridianIconTests
{
    public static TheoryData<string, string> AllIconConstants()
    {
        var data = new TheoryData<string, string>();
        foreach (var field in typeof(Icons).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (field.IsLiteral && field.FieldType == typeof(string))
                data.Add(field.Name, (string)field.GetRawConstantValue()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllIconConstants))]
    public void EveryIconConstantNamesABundledIcon(string constant, string phosphorName)
    {
        Assert.True(Phosphor.Has(phosphorName),
            $"Icons.{constant} = \"{phosphorName}\" is not in the bundled Phosphor set — " +
            "add it to scripts/phosphor-icons.txt and regenerate.");
    }
}
