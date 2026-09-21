using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// The layered field style: the element's own <see cref="TextInputStyle"/>, then the
/// theme's <see cref="TextInputTheme"/>, then the palette — what a bordered wrapper box
/// around a flush input used to be for.
/// </summary>
public class TextInputStyleTests
{
    private static readonly Color Brand = new(200, 30, 30);
    private static readonly Color Fill = new(10, 20, 30);

    private static readonly TextInputStyle Bordered = new()
    {
        Border = Brand, BorderWidth = new Thickness(1f),
        Padding = new Thickness(0, 10, 0, 10), CornerRadius = new CornerRadius(8f),
    };

    /// <summary>A theme whose field is flush, the way the apps' are.</summary>
    private static readonly Theme Flush = Theme.Dark.With(new TextInputTheme
    {
        BorderWidth = new Thickness(0), BorderColor = Color.Transparent,
        Background = Fill, Padding = new Thickness(6, 0),
    });

    [Fact]
    public void AStyle_LayersOverTheTheme_AndKeepsWhatItDoesNotName()
    {
        var input = TextInput.New("x").Style(Bordered);
        input.ApplyTheme(Flush);

        Assert.Equal(Brand, input.BorderColor);
        Assert.Equal(new Thickness(1f), input.Box.BorderWidth);
        Assert.Equal(new Thickness(0, 10, 0, 10), input.Box.Padding);
        Assert.Equal(new CornerRadius(8f), input.CornerRadius);
        Assert.Equal(Fill, input.BackgroundColor);                      // the theme's, untouched
        Assert.Equal(Theme.Dark.Secondary, input.PlaceholderColor);      // the palette's
    }

    [Fact]
    public void AStyle_ReachesTheTextColours_Too()
    {
        var placeholder = new Color(1, 2, 3);
        var focus = new Color(4, 5, 6);
        var input = TextInput.New("x").Style(new TextInputStyle { Placeholder = placeholder, FocusBorder = focus });
        input.ApplyTheme(Theme.Light);

        Assert.Equal(placeholder, input.PlaceholderColor);
        Assert.Equal(focus, input.FocusBorderColor);
        Assert.Equal(Theme.Light.TextColor, input.CursorColor);           // still the palette's
    }

    [Fact]
    public void ABuilderFontSize_WinsOverBothTheStyleAndTheTheme()
    {
        var input = TextInput.New("x").Style(new TextInputStyle { FontSize = 20f }).FontSize(11f);
        input.ApplyTheme(Theme.Dark);
        Assert.Equal(11f, input.Inherited.FontSize);

        var styled = TextInput.New("x").Style(new TextInputStyle { FontSize = 20f });
        styled.ApplyTheme(Theme.Dark);
        Assert.Equal(20f, styled.Inherited.FontSize);
    }

    [Fact]
    public void Chromeless_KeepsTheBoxForTheCaller_ButTakesTheStylesTextColours()
    {
        var placeholder = new Color(1, 2, 3);
        var input = TextInput.New("x").Chromeless()
            .Style(new TextInputStyle { Border = Brand, Placeholder = placeholder });
        input.ApplyTheme(Theme.Dark);

        Assert.Equal(Color.Transparent, input.BorderColor);
        Assert.Equal(placeholder, input.PlaceholderColor);
    }

    [Fact]
    public void Style_FluentAndPatch_BothChangeIt()
    {
        var live = TextInput.New("x");
        live.PatchFrom(TextInput.New("x").Style(Bordered));
        Assert.Same(Bordered, live.Style);
        live.PatchFrom(TextInput.New("x"));
        Assert.Null(live.Style);
    }

    [Fact]
    public void ATextArea_TakesTheSameStyle()
    {
        var area = TextArea.New("x").Style(Bordered);
        area.ApplyTheme(Flush);

        Assert.Equal(Brand, area.BorderColor);
        Assert.Equal(new Thickness(0, 10, 0, 10), area.Box.Padding);
        Assert.Equal(Fill, area.BackgroundColor);

        var live = TextArea.New("x");
        live.PatchFrom(area);
        Assert.Same(Bordered, live.Style);
    }

    [Fact]
    public void AHeightTheStyleDoesNotName_IsLeftToTheElement()
    {
        var input = TextInput.New("x").Style(Bordered).Height(34f);
        input.ApplyTheme(Flush);
        Assert.Equal(Dimension.Px(34f), input.Box.Height);
    }
}
