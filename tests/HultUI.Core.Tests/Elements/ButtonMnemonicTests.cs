using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>A button whose label marks its mnemonic with <c>&amp;</c> (<see cref="ButtonElement.UseMnemonic"/>):
/// shown and read out without the mark, the marked letter its <see cref="ButtonElement.Mnemonic"/>.</summary>
public class ButtonMnemonicTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    private static ButtonElement Mnemonic(string label)
    {
        var button = Button.New(label);
        button.UseMnemonic = true;
        return button;
    }

    [Fact]
    public void TheMarkedLetterIsTheMnemonic()
    {
        var button = Mnemonic("&Apply");

        Assert.Equal('a', button.Mnemonic);
        Assert.Equal("Apply", button.DisplayLabel);
        Assert.Equal("&Apply", button.Label);
        var label = Assert.IsType<TextElement>(button.Children[0]);
        Assert.Equal("Apply", label.Content);
        Assert.Equal(0, label.MnemonicIndex);

        button.Label = "Re&try";
        Assert.Equal('t', button.Mnemonic);
        Assert.Equal("Retry", label.Content);
        Assert.Equal(2, label.MnemonicIndex);
    }

    [Fact]
    public void ADoubledAmpersandIsALiteral()
    {
        var button = Mnemonic("Save && &quit");

        Assert.Equal("Save & quit", button.DisplayLabel);
        Assert.Equal('q', button.Mnemonic);
        Assert.Equal('\0', Mnemonic("Save && quit").Mnemonic);
    }

    [Fact]
    public void TheAccessibleNameHasNoMarker()
    {
        var button = Mnemonic("&Apply");
        _layout.PerformLayout(button, 300, 100);

        Assert.Equal("Apply", AccessibilityTreeBuilder.Build(button).Name);
    }

    [Fact]
    public void WithoutUseMnemonicTheLabelIsUnchanged()
    {
        var button = Button.New("&Apply");
        _layout.PerformLayout(button, 300, 100);

        Assert.Equal("&Apply", button.DisplayLabel);
        Assert.Equal('\0', button.Mnemonic);
        Assert.Equal(-1, ((TextElement)button.Children[0]).MnemonicIndex);
        Assert.Equal("&Apply", AccessibilityTreeBuilder.Build(button).Name);

        // Off again: the label as written comes back.
        var on = Mnemonic("&Apply");
        on.UseMnemonic = false;
        Assert.Equal("&Apply", ((TextElement)on.Children[0]).Content);
    }

    /// <summary>A rebuild hands the kept button the new one's mnemonic, then patches its label child
    /// from the new one's, which already shows the text without its mark (the reconciler's order).</summary>
    [Fact]
    public void ARebuildCarriesTheMnemonic()
    {
        var kept = Button.New("Apply");
        var built = Mnemonic("&Retry");
        kept.PatchFrom(built);
        kept.Children[0].PatchFrom(built.Children[0]);

        Assert.True(kept.UseMnemonic);
        Assert.Equal('r', kept.Mnemonic);
        Assert.Equal("Retry", kept.DisplayLabel);
        Assert.Equal("&Retry", kept.Label);
        Assert.Equal("Retry", ((TextElement)kept.Children[0]).Content);
        Assert.Equal(0, ((TextElement)kept.Children[0]).MnemonicIndex);
    }

    /// <summary>A button whose first child is a glyph keeps its children, and has no mnemonic.</summary>
    [Fact]
    public void AGlyphButton_KeepsItsGlyph_AndHasNoMnemonic()
    {
        var button = Button.New("&Apply");
        var glyph = Box.New().Width(12f).Height(12f);
        button.ReplaceChildren(glyph);

        button.UseMnemonic = true;

        Assert.Same(glyph, Assert.Single(button.Children));
        Assert.Equal('\0', button.Mnemonic);
        Assert.Equal("", button.DisplayLabel);
    }

    /// <summary>Children replaced after the flag was set: the button reports what it shows now,
    /// and no mnemonic for a label it no longer has.</summary>
    [Fact]
    public void ChildrenReplacedLater_LeaveNoStaleMnemonic()
    {
        var button = Mnemonic("&Ok");
        button.ReplaceChildren(TextElement.New("Save"));

        Assert.Equal('\0', button.Mnemonic);
        Assert.Equal("Save", button.Label);
        Assert.Equal("Save", button.DisplayLabel);
    }

    /// <summary>A button with no children yet gets none from the flag; its label, set later, is marked.</summary>
    [Fact]
    public void AnEmptyButton_GetsNoChild_UntilItsLabelIsSet()
    {
        var button = Button.New(ButtonVariant.Ghost);
        button.UseMnemonic = true;
        Assert.Empty(button.Children);

        button.Label = "&Ok";
        Assert.Equal('o', button.Mnemonic);
        Assert.Equal("Ok", ((TextElement)Assert.Single(button.Children)).Content);
    }
}
