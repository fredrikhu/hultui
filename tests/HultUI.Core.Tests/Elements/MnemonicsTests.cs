using HultUI.Core.Elements;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// The <c>&amp;</c> rule a menu item with <see cref="ContextMenuItem.UseMnemonic"/> and a menu
/// bar's title share: <c>&amp;</c> marks the next character, <c>&amp;&amp;</c> is an ampersand, and
/// only the first mark counts. An item without it shows its label as written.
/// </summary>
public class MnemonicsTests
{
    [Theory]
    [InlineData("&Copy", "Copy", 'c')]
    [InlineData("Save && quit", "Save & quit", '\0')]
    [InlineData("&&&Fish", "&Fish", 'f')]
    [InlineData("A&", "A&", '\0')]
    [InlineData("&a &b", "a b", 'a')]
    [InlineData("&Öppna", "Öppna", 'ö')]
    public void AnItemThatUsesMnemonics_ShowsItsLabelWithoutTheMarks_AndHasTheMarkedCharacter(
        string label, string shown, char mnemonic)
    {
        var item = new ContextMenuItem { Label = label, UseMnemonic = true };

        Assert.Equal(shown, item.DisplayLabel);
        Assert.Equal(mnemonic, item.Mnemonic);
    }

    [Fact]
    public void AnItemThatDoesNot_ShowsItsLabelAsWritten_AndHasNone()
    {
        var item = new ContextMenuItem { Label = "&Copy" };

        Assert.Equal("&Copy", item.DisplayLabel);
        Assert.Equal('\0', item.Mnemonic);
        Assert.Null(item.MnemonicSpans);
    }

    [Fact]
    public void TheUnderline_IsUnderTheMarkedCharacter()
    {
        var item = new ContextMenuItem { Label = "Save &as…", UseMnemonic = true };

        var span = Assert.Single(item.MnemonicSpans!);
        Assert.Equal(5, span.Start);
        Assert.Equal(1, span.Length);
        Assert.Equal(TextDecoration.Underline, span.Decoration);
    }
}
