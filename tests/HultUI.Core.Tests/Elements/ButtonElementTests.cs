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

    /// <summary>
    /// What the label draws, not what the button computed. A button writes its text colour
    /// and size into its inherited style from <see cref="Element.ApplyTheme"/>, and the style
    /// pass used to resolve the button's inherited style before asking it to — so on the
    /// first pass the label drew in its parent's colour and size, and stayed that way until
    /// something else happened to dirty the button (enabling it, say). Every test above
    /// calls ApplyTheme directly and reads the button, which is why none of them saw it.
    /// </summary>
    [Fact]
    public void TheLabel_DrawsInTheButtonsColourAndSize_FromTheFirstStylePass()
    {
        var theme = Theme.Dark;
        Assert.NotEqual(theme.TextColor, theme.PrimaryText);   // or the first assertion proves nothing
        var linkStyle = new ButtonStyle { Text = new Color(244, 200, 126, 255), FontSize = 12f };
        var primary = Button.New("Play");
        var link = Button.New("See all", ButtonVariant.Ghost);
        link.Style = linkStyle;
        var root = Box.New();
        root.AddChild(primary);
        root.AddChild(link);

        StyleResolver.Resolve(root, theme);   // once — no second pass to paper over it

        var playLabel = (TextElement)primary.Children[0];
        Assert.Equal(theme.PrimaryText, playLabel.ResolvedInherited.TextColor);
        Assert.Equal(theme.Button.FontWeight, playLabel.ResolvedInherited.FontWeight);
        var linkLabel = (TextElement)link.Children[0];
        Assert.Equal(linkStyle.Text, linkLabel.ResolvedInherited.TextColor);
        Assert.Equal(12f, linkLabel.ResolvedInherited.FontSize);
        // And the button's own resolved style, which its cursor and focus ring read.
        Assert.Equal(theme.PrimaryText, primary.ResolvedInherited.TextColor);
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
            Secondary = new ButtonStyle { Border = brand },
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
            Primary = new ButtonStyle { HoverBorder = hoverBorder },
        });
        var b = Button.New("Go");
        b.ApplyTheme(themed);
        Assert.Equal(b.ResolvedBackground, b.BackgroundColor);

        b.SetInteractionHovered(true);
        Assert.Equal(b.ResolvedHoverBackground, b.BackgroundColor);
        Assert.Equal(hoverBorder, b.BorderColor);

        b.SetInteractionPressed(true);
        Assert.Equal(b.ResolvedPressedBackground, b.BackgroundColor);

        b.SetInteractionPressed(false); b.SetInteractionHovered(false);
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

    // ── Styles ───────────────────────────────────────────────────────────

    private static readonly ButtonStyle RoundIcon = new()
    {
        Size = 28f, Padding = new Thickness(0), Circular = true,
    };

    [Fact]
    public void AStyle_LayersOverTheVariant_AndKeepsTheColoursItDoesNotName()
    {
        var b = Button.New(ButtonVariant.Secondary).Style(RoundIcon);
        b.ApplyTheme(Theme.Dark);

        Assert.Equal(Dimension.Px(28f), b.Box.Width);
        Assert.Equal(Dimension.Px(28f), b.Box.Height);
        Assert.Equal(new Thickness(0), b.Box.Padding);
        Assert.Equal(new CornerRadius(14f), b.CornerRadius);       // half the size: round
        Assert.Equal(Theme.Dark.Border, b.ResolvedBorder);          // still the secondary outline
        Assert.Equal(Theme.Dark.TextColor, b.ResolvedText);
        Assert.Equal(new Thickness(1f), b.Box.BorderWidth);         // still the shared width
    }

    [Fact]
    public void AStyleOnTheThemeVariant_ReshapesEveryButtonOfIt()
    {
        var themed = Theme.Light.With(new ButtonTheme
        {
            Ghost = new ButtonStyle { Padding = new Thickness(3, 4, 3, 4), FontWeight = FontWeight.Medium },
        });
        var ghost = Button.New("More", ButtonVariant.Ghost);
        var primary = Button.New("Go");
        ghost.ApplyTheme(themed);
        primary.ApplyTheme(themed);

        Assert.Equal(new Thickness(3, 4, 3, 4), ghost.Box.Padding);
        Assert.Equal(FontWeight.Medium, ghost.Inherited.FontWeight);
        Assert.Equal(new Thickness(8, 16, 8, 16), primary.Box.Padding);   // untouched
    }

    [Fact]
    public void TheElementsStyle_WinsOverTheThemeVariants()
    {
        var themed = Theme.Light.With(new ButtonTheme
        {
            Secondary = new ButtonStyle { Padding = new Thickness(9), CornerRadius = new CornerRadius(2f) },
        });
        var b = Button.New(ButtonVariant.Secondary).Style(RoundIcon);
        b.ApplyTheme(themed);

        Assert.Equal(new Thickness(0), b.Box.Padding);
        Assert.Equal(new CornerRadius(14f), b.CornerRadius);
    }

    [Fact]
    public void ASizeTheStyleDoesNotName_IsLeftToTheApp()
    {
        var b = Button.New("Commit").Width(Dimension.Percent(100));
        b.ApplyTheme(Theme.Dark);
        Assert.Equal(Dimension.Percent(100), b.Box.Width);
    }

    [Fact]
    public void Style_FluentAndPatch_BothChangeIt()
    {
        var b = Button.New("x").Style(RoundIcon);
        Assert.Same(RoundIcon, b.Style);

        var live = Button.New("x");
        live.PatchFrom(Button.New("x").Style(RoundIcon));
        Assert.Same(RoundIcon, live.Style);
        live.PatchFrom(Button.New("x"));
        Assert.Null(live.Style);
    }

    [Fact]
    public void AButton_IsAButtonToAssistiveTechnology_BeforeAnythingSubscribesToIt()
    {
        Assert.Equal(Core.Accessibility.AccessibilityRole.Button,
            Core.Accessibility.AccessibilityTreeBuilder.InferRole(Button.New("x")));
    }

    // ── Disabled ─────────────────────────────────────────────────────────

    [Fact]
    public void Disabled_SwallowsItsClick_LeavesTheTabRing_AndDims()
    {
        int clicks = 0;
        var b = Button.New("Start").OnClick(_ => clicks++).Enabled(false);
        b.ApplyTheme(Theme.Dark);

        EventDispatcher.DispatchDirect(new ClickEvent(), b);
        Assert.Equal(0, clicks);
        Assert.False(b.IsFocusable);
        Assert.False(b.IsEnabledForAccessibility);
        Assert.Equal(Theme.Dark.Button.DisabledOpacity, b.RenderOpacity);
        Assert.Equal(CursorType.Default, b.Inherited.Cursor);

        b.IsEnabled = true;
        b.ApplyTheme(Theme.Dark);
        EventDispatcher.DispatchDirect(new ClickEvent(), b);
        Assert.Equal(1, clicks);
        Assert.True(b.IsFocusable);
        Assert.Equal(1f, b.RenderOpacity);
    }

    [Fact]
    public void Disabled_DoesNotLightUnderThePointer()
    {
        var b = Button.New("Go").Enabled(false);
        b.ApplyTheme(Theme.Light);

        b.SetInteractionHovered(true);
        b.SetInteractionPressed(true);
        Assert.Equal(b.ResolvedBackground, b.BackgroundColor);
        Assert.Equal(b.ResolvedBorder, b.BorderColor);
    }

    [Fact]
    public void Disabled_DimsTheAppsOwnOpacity_RatherThanReplacingIt()
    {
        var b = Button.New("Go").Opacity(0.5f).Enabled(false);
        b.ApplyTheme(Theme.Light);
        Assert.Equal(0.5f, b.Opacity);                                     // the app's, untouched
        Assert.Equal(0.5f * Theme.Light.Button.DisabledOpacity, b.RenderOpacity, 3);
    }

    [Fact]
    public void Enabled_Patch_CopiesIt()
    {
        var live = Button.New("x");
        live.PatchFrom(Button.New("x").Enabled(false));
        Assert.False(live.IsEnabled);
        Assert.False(live.IsFocusable);
    }

    // ── Interaction ──────────────────────────────────────────────────────

    [Fact]
    public void AThemeChangeMidHover_KeepsTheButtonLit_InTheNewColours()
    {
        var b = Button.New("Go");
        b.ApplyTheme(Theme.Light);
        b.SetInteractionHovered(true);

        b.ApplyTheme(Theme.Dark);
        Assert.Equal(b.ResolvedHoverBackground, b.BackgroundColor);
        Assert.NotEqual(Theme.Light.Primary, b.ResolvedBackground);
    }

    [Fact]
    public void AnAppsOwnHoverStyle_LayersOverTheButtons()
    {
        var ring = new Color(9, 9, 9);
        var b = Button.New("Go").OnHover(e => e.BorderColor = ring);
        b.ApplyTheme(Theme.Light);

        b.SetInteractionHovered(true);
        Assert.Equal(b.ResolvedHoverBackground, b.BackgroundColor);   // the button's own hover fill
        Assert.Equal(ring, b.BorderColor);                             // the app's border on top

        b.SetInteractionHovered(false);
        Assert.Equal(b.ResolvedBorder, b.BorderColor);
    }

    [Fact]
    public void AReconciledButton_LightsInItsOwnColours_NotTheDiscardedBuildS()
    {
        // The hover style is copied from the incoming build; it must read the retained
        // element it is handed, not the discarded one it was constructed on.
        var live = Button.New("Go");
        live.ApplyTheme(Theme.Light);
        var incoming = Button.New("Go", ButtonVariant.Danger);
        incoming.ApplyTheme(Theme.Light);
        live.PatchFrom(incoming);
        live.ApplyTheme(Theme.Light);

        live.SetInteractionHovered(true);
        Assert.Equal(live.ResolvedHoverBackground, live.BackgroundColor);
    }
}
