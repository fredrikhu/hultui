using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Platform.Accessibility.Linux;
using Xunit;

namespace HultUI.Platform.Accessibility.Linux.Tests;

/// <summary>
/// A masked field on AT-SPI: the password-text role, which is how GTK's password entries are
/// read, and the bullets for its value. 40 is ATSPI_ROLE_PASSWORD_TEXT in atspi-constants.h,
/// between PANEL (39) and POPUP_MENU (41).
/// </summary>
public class AtSpiPasswordTests
{
    private static AtSpiAccessible Accessible(TextInputElement input)
    {
        var root = BoxElement.New();
        root.AddChild(input);
        var tree = AccessibilityTreeBuilder.Build(root);
        return AtSpiAccessible.From(tree.Children[0], isRoot: false, AtSpiConstants.RootPath, 0, 1f);
    }

    [Fact]
    public void APasswordFieldIsPasswordText_WithTheBulletsForItsValue()
    {
        var acc = Accessible(TextInput.New("hunter2").Password());

        Assert.Equal(40u, acc.Role);
        Assert.Equal("password text", AtSpiConstants.RoleName(acc.Role));
        Assert.Equal("●●●●●●●", acc.Value);
    }

    [Fact]
    public void AnOrdinaryFieldIsText_WithItsText()
    {
        var acc = Accessible(TextInput.New("fredrik"));

        Assert.Equal(AtSpiConstants.RoleText, acc.Role);
        Assert.Equal("fredrik", acc.Value);
    }
}
