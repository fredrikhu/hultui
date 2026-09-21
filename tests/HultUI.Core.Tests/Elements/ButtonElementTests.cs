using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class ButtonElementTests
{
    [Fact]
    public void Primary_IsFilledWithThePrimaryColour_AndCarriesAnInvisibleBorder()
    {
        var b = Button.New("Save");
        b.ApplyTheme(Theme.Dark);

        Assert.Equal(Theme.Dark.Primary, b.ResolvedBackground);
        Assert.Equal(Theme.Dark.PrimaryText, b.ResolvedText);
        Assert.Equal(0, b.ResolvedBorder.A);
        Assert.Equal(new Thickness(1f), b.Box.BorderWidth);
    }

    [Fact]
    public void Secondary_IsOutlinedOnNoFill()
    {
        var b = Button.New("Cancel", ButtonVariant.Secondary);
        b.ApplyTheme(Theme.Dark);

        Assert.Equal(0, b.ResolvedBackground.A);
        Assert.Equal(Theme.Dark.Border, b.ResolvedBorder);
        Assert.Equal(Theme.Dark.TextColor, b.ResolvedText);
        // Lit by a wash of its own text colour, not by darkening nothing.
        Assert.Equal(Theme.Dark.TextColor.R, b.ResolvedHoverBackground.R);
        Assert.True(b.ResolvedHoverBackground.A > 0 && b.ResolvedHoverBackground.A < 255);
    }

    [Fact]
    public void Ghost_HasNeitherFillNorBorder_InThePrimaryColour()
    {
        var b = Button.New("More", ButtonVariant.Ghost);
        b.ApplyTheme(Theme.Light);

        Assert.Equal(0, b.ResolvedBackground.A);
        Assert.Equal(0, b.ResolvedBorder.A);
        Assert.Equal(Theme.Light.Primary, b.ResolvedText);
    }

    [Fact]
    public void EveryVariant_SharesOneBorderWidth_SoSwitchingNeverMovesNeighbours()
    {
        var themed = Theme.Light.With(new ButtonTheme { BorderWidth = new Thickness(2f) });
        foreach (var v in Enum.GetValues<ButtonVariant>())
        {
            var b = Button.New("x", v);
            b.ApplyTheme(themed);
            Assert.Equal(new Thickness(2f), b.Box.BorderWidth);
        }
    }

    [Fact]
    public void AVariantOverride_KeepsThePaletteForWhatItDoesNotSay()
    {
        var brand = new Color(200, 30, 30);
        var themed = Theme.Dark.With(new ButtonTheme
        {
            Secondary = new ButtonVariantTheme { Border = brand },
        });
        var b = Button.New("Cancel", ButtonVariant.Secondary);
        b.ApplyTheme(themed);

        Assert.Equal(brand, b.ResolvedBorder);
        Assert.Equal(Theme.Dark.TextColor, b.ResolvedText);   // untouched
        Assert.Equal(0, b.ResolvedBackground.A);              // still outlined
    }

    [Fact]
    public void HoverAndPress_SwapTheDrawnFillAndBorder()
    {
        var hoverBorder = new Color(1, 2, 3);
        var themed = Theme.Light.With(new ButtonTheme
        {
            Primary = new ButtonVariantTheme { HoverBorder = hoverBorder },
        });
        var b = Button.New("Go");
        b.ApplyTheme(themed);
        Assert.Equal(b.ResolvedBackground, b.BackgroundColor);

        EventDispatcher.DispatchDirect(new PointerEnterEvent(), b);
        Assert.Equal(b.ResolvedHoverBackground, b.BackgroundColor);
        Assert.Equal(hoverBorder, b.BorderColor);

        EventDispatcher.DispatchDirect(new PointerDownEvent(), b);
        Assert.Equal(b.ResolvedPressedBackground, b.BackgroundColor);

        EventDispatcher.DispatchDirect(new PointerLeaveEvent(), b);
        Assert.Equal(b.ResolvedBackground, b.BackgroundColor);
        Assert.Equal(b.ResolvedBorder, b.BorderColor);
    }

    [Fact]
    public void Variant_FluentAndPatch_BothChangeIt()
    {
        var b = Button.New("x").Variant(ButtonVariant.Danger);
        Assert.Equal(ButtonVariant.Danger, b.Variant);

        var live = Button.New("x");
        live.PatchFrom(Button.New("x", ButtonVariant.Ghost));
        Assert.Equal(ButtonVariant.Ghost, live.Variant);
    }
}
