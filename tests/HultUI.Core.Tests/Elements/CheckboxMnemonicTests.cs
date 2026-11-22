using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>A check box whose label marks its mnemonic with <c>&amp;</c> (<see cref="CheckboxElement.UseMnemonic"/>):
/// shown and read out without the mark, the marked letter its <see cref="CheckboxElement.Mnemonic"/>.</summary>
public class CheckboxMnemonicTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    private static CheckboxElement Mnemonic(string label)
    {
        var checkbox = Checkbox.New(false, label);
        checkbox.UseMnemonic = true;
        return checkbox;
    }

    [Fact]
    public void TheMarkedLetterIsTheMnemonic()
    {
        var checkbox = Mnemonic("&Wrap lines");

        Assert.Equal('w', checkbox.Mnemonic);
        Assert.Equal("Wrap lines", checkbox.DisplayLabel);
        Assert.Equal("&Wrap lines", checkbox.Label);

        checkbox.Label = "Show &hidden files";
        Assert.Equal('h', checkbox.Mnemonic);
        Assert.Equal("Show hidden files", checkbox.DisplayLabel);
    }

    [Fact]
    public void ADoubledAmpersandIsALiteral()
    {
        var checkbox = Mnemonic("Tabs && &spaces");

        Assert.Equal("Tabs & spaces", checkbox.DisplayLabel);
        Assert.Equal('s', checkbox.Mnemonic);
        Assert.Equal('\0', Mnemonic("Tabs && spaces").Mnemonic);
    }

    [Fact]
    public void TheAccessibleNameHasNoMarker()
    {
        var checkbox = Mnemonic("&Wrap lines");
        _layout.PerformLayout(checkbox, 300, 100);

        Assert.Equal("Wrap lines", AccessibilityTreeBuilder.Build(checkbox).Name);
    }

    [Fact]
    public void WithoutUseMnemonicTheLabelIsUnchanged()
    {
        var checkbox = Checkbox.New(false, "&Wrap lines");
        _layout.PerformLayout(checkbox, 300, 100);

        Assert.Equal("&Wrap lines", checkbox.DisplayLabel);
        Assert.Equal('\0', checkbox.Mnemonic);
        Assert.Equal("&Wrap lines", AccessibilityTreeBuilder.Build(checkbox).Name);
        Assert.Equal("", Checkbox.New(false).DisplayLabel);
    }

    [Fact]
    public void ARebuildCarriesTheMnemonic()
    {
        var kept = Checkbox.New(false, "Wrap lines");
        kept.PatchFrom(Mnemonic("&Wrap lines"));

        Assert.True(kept.UseMnemonic);
        Assert.Equal('w', kept.Mnemonic);
        Assert.Equal("Wrap lines", kept.DisplayLabel);
    }
}
