using HultUI.Core.Rendering;
using Xunit;

namespace HultUI.Core.Tests.Rendering;

/// <summary>
/// Which renderer a window gets: <c>HULTUI_RENDERER</c> over the app's request over the
/// default, in both directions, and a failed Vulkan start falling back to GL only when the app
/// asked for it rather than the environment.
/// </summary>
public class RendererSelectionTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("vulkan", RendererKind.Vulkan)]
    [InlineData("gl", RendererKind.OpenGL)]
    [InlineData("opengl", RendererKind.OpenGL)]
    public void TheEnvironmentValue_NamesARendererOrNothing(string? value, RendererKind? expected) =>
        Assert.Equal(expected, RendererSelection.ParseEnvironment(value));

    [Theory]
    // Nothing asked: the default, which is GL.
    [InlineData(null, null, RendererKind.OpenGL)]
    [InlineData(null, RendererKind.Default, RendererKind.OpenGL)]
    // The app asks and nothing overrides it.
    [InlineData(null, RendererKind.Vulkan, RendererKind.Vulkan)]
    [InlineData(null, RendererKind.OpenGL, RendererKind.OpenGL)]
    // The environment wins in both directions.
    [InlineData(RendererKind.OpenGL, RendererKind.Vulkan, RendererKind.OpenGL)]
    [InlineData(RendererKind.Vulkan, RendererKind.OpenGL, RendererKind.Vulkan)]
    [InlineData(RendererKind.Vulkan, null, RendererKind.Vulkan)]
    public void TheEnvironment_BeatsTheApp_WhichBeatsTheDefault(
        RendererKind? environment, RendererKind? requested, RendererKind expected) =>
        Assert.Equal(expected, RendererSelection.Resolve(environment, requested));

    [Fact]
    public void AFailedVulkanStart_FallsBack_OnlyWhenTheAppAsked()
    {
        Assert.True(RendererSelection.MayFallBack(environment: null));
        // HULTUI_RENDERER=vulkan is how the design docs test the renderer: a quiet GL window
        // there would make every such run a false negative.
        Assert.False(RendererSelection.MayFallBack(RendererKind.Vulkan));
    }
}
