using HultUI.Core.Platform;
using HultUI.Platform.SDL3;
using SDL;
using Xunit;

namespace HultUI.Platform.SDL3.Tests;

/// <summary>SDL's reading of the desktop's theme, as <c>Application.DesktopColorScheme</c>
/// reports it.</summary>
public sealed class ColorSchemeTests
{
    [Theory]
    [InlineData(SDL_SystemTheme.SDL_SYSTEM_THEME_UNKNOWN, ColorScheme.NoPreference)]
    [InlineData(SDL_SystemTheme.SDL_SYSTEM_THEME_LIGHT, ColorScheme.Light)]
    [InlineData(SDL_SystemTheme.SDL_SYSTEM_THEME_DARK, ColorScheme.Dark)]
    public void SdlThemes_MapToColorSchemes(SDL_SystemTheme theme, ColorScheme expected)
    {
        Assert.Equal(expected, SDL3Window.ToColorScheme(theme));
    }
}
