using Xunit;
using static HultUI.Platform.SDL3.MinimizedState;

namespace HultUI.Platform.SDL3.Tests;

/// <summary>
/// Whether a window SDL calls minimized is left undrawn. On Wayland SDL's flag outlives a restore
/// the compositor does not announce with an activated configure (WSLg's), so a focus gain since
/// the minimize ends it there; elsewhere the flag is the window system's own answer.
/// </summary>
public class MinimizedStateTests
{
    [Fact]
    public void OnWayland_AFocusGainSinceTheMinimize_EndsIt_WhateverTheFlagSays()
    {
        Assert.False(IsMinimized(sdlSaysMinimized: true, wayland: true, focusedSinceMinimize: true));
    }

    [Fact]
    public void OnWayland_WithoutOne_TheFlagStands()
    {
        Assert.True(IsMinimized(sdlSaysMinimized: true, wayland: true, focusedSinceMinimize: false));
        Assert.False(IsMinimized(sdlSaysMinimized: false, wayland: true, focusedSinceMinimize: false));
    }

    [Fact]
    public void Elsewhere_TheFlagIsTheAnswer_FocusOrNot()
    {
        // Windows reports a minimized window's state itself, and a minimized window there has
        // no area to draw on (ba79958): a stray focus event must not bring the drawing back.
        Assert.True(IsMinimized(sdlSaysMinimized: true, wayland: false, focusedSinceMinimize: true));
        Assert.True(IsMinimized(sdlSaysMinimized: true, wayland: false, focusedSinceMinimize: false));
        Assert.False(IsMinimized(sdlSaysMinimized: false, wayland: false, focusedSinceMinimize: true));
    }
}
