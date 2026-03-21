using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Styling;

public class ThemeTests
{
    [Fact]
    public void Light_HasWhiteBackground()
    {
        var theme = Theme.Light;
        Assert.Equal(Color.White, theme.Background);
    }

    [Fact]
    public void Dark_HasDarkBackground()
    {
        var theme = Theme.Dark;
        Assert.NotEqual(Color.White, theme.Background);
        Assert.True(theme.Background.R < 50);
    }

    [Fact]
    public void ToResolvedStyle_MapsAllProperties()
    {
        var theme = new Theme
        {
            FontFamily = "Roboto",
            FontSize = 20f,
            FontWeight = FontWeight.Bold,
            TextColor = Color.Red,
            TextDirection = TextDirection.Rtl,
            Cursor = CursorType.Pointer
        };

        var resolved = theme.ToResolvedStyle();

        Assert.Equal("Roboto", resolved.FontFamily);
        Assert.Equal(20f, resolved.FontSize);
        Assert.Equal(FontWeight.Bold, resolved.FontWeight);
        Assert.Equal(Color.Red, resolved.TextColor);
        Assert.Equal(TextDirection.Rtl, resolved.TextDirection);
        Assert.Equal(CursorType.Pointer, resolved.Cursor);
    }

    [Fact]
    public void Light_TextColor_IsDark()
    {
        var theme = Theme.Light;
        var resolved = theme.ToResolvedStyle();
        // Light theme should have dark text
        Assert.True(resolved.TextColor.R < 50 && resolved.TextColor.G < 50 && resolved.TextColor.B < 50);
    }

    [Fact]
    public void Dark_TextColor_IsLight()
    {
        var theme = Theme.Dark;
        var resolved = theme.ToResolvedStyle();
        // Dark theme should have light text
        Assert.True(resolved.TextColor.R > 200);
    }
}
