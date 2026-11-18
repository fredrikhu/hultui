using HultUI.Platform.SDL3;
using SDL;
using Xunit;
using static SDL.SDL3;

namespace HultUI.Platform.SDL3.Tests;

/// <summary>
/// <c>Application.SetAppMetadata</c>: the app id the desktop finds an app's <c>.desktop</c> file by
/// (the Wayland <c>app_id</c>, the class of the X11 <c>WM_CLASS</c>). The checks are the form an id
/// a desktop file can carry has, and that it comes before the first window; the last test sees it
/// reach SDL, whose metadata is process-wide, so the class runs with the other SDL tests, one at a
/// time.
/// </summary>
[Collection(SdlDummyDriver.Collection)]
public sealed unsafe class AppMetadataTests
{
    [Theory]
    [InlineData("com.example.App")]
    [InlineData("com.example.App.devel")]
    [InlineData("org.example.My_App-2")]
    [InlineData("example")]
    public void AReverseDomainId_IsAnAppId(string identifier)
    {
        Assert.True(AppMetadata.IsValidIdentifier(identifier));
    }

    public static TheoryData<string> NotIds => new()
    {
        "", " ", "com example", "com/example", ".com.example", "com.example.", "com..example", "å.b",
        new string('a', 256),
    };

    [Theory]
    [MemberData(nameof(NotIds))]
    public void AnIdThatNoDesktopFileCouldCarry_IsNot(string identifier)
    {
        Assert.False(AppMetadata.IsValidIdentifier(identifier));
    }

    [Fact]
    public void Validate_RejectsAnEmptyName_AndABadId()
    {
        Assert.ThrowsAny<ArgumentException>(() => AppMetadata.Validate(" ", "a.b", false));
        var bad = Assert.Throws<ArgumentException>(() => AppMetadata.Validate("App", "a..b", false));
        Assert.Equal("identifier", bad.ParamName);
    }

    [Fact]
    public void Validate_RefusesOnceAWindowExists()
    {
        Assert.Throws<InvalidOperationException>(() => AppMetadata.Validate("App", "a.b", windowExists: true));
        AppMetadata.Validate("App", "a.b", windowExists: false);
    }

    [Fact]
    public void SetAppMetadata_ReachesSdl()
    {
        // SDL reads SDL_APP_ID over the identifier it was given: a packager's override.
        Assert.True(Environment.GetEnvironmentVariable("SDL_APP_ID") is null,
            "SDL_APP_ID is set in this environment, and SDL reports it instead of the identifier");
        try
        {
            SDL3Window.SetAppMetadata("HultUI test", "1.2", "org.hultui.Test");

            Assert.Equal("org.hultui.Test", SDL_GetAppMetadataProperty(SDL_PROP_APP_METADATA_IDENTIFIER_STRING));
            Assert.Equal("HultUI test", SDL_GetAppMetadataProperty(SDL_PROP_APP_METADATA_NAME_STRING));
            Assert.Equal("1.2", SDL_GetAppMetadataProperty(SDL_PROP_APP_METADATA_VERSION_STRING));
        }
        finally
        {
            SDL_SetAppMetadata((byte*)null, null, null);
        }
    }
}
