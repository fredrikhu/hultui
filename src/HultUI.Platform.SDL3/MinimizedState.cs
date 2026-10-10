namespace HultUI.Platform.SDL3;

/// <summary>
/// Whether a window is minimized as far as drawing is concerned: a minimized window is not drawn
/// (see <c>Application.RenderDirtyWindows</c>) and is drawn again when it is restored.
///
/// <para><b>On Wayland SDL's flag is a guess.</b> xdg-shell has no minimized state: SDL sets
/// <c>SDL_WINDOW_MINIMIZED</c> itself when the app calls <c>SDL_MinimizeWindow</c>, cannot take
/// it back (there is no request to unminimize, so <c>SDL_RestoreWindow</c> leaves it), and
/// clears it only at the next configure event that says the window is activated. A compositor
/// that restores the window without one leaves the flag set for good. WSLg does: restored from
/// the taskbar, the window got the keyboard back but never that configure, and since minimized
/// windows stopped being drawn (82e382b) it showed its last frame for as long as it stayed open:
/// 0 frames in the 15 s watched after the restore, with a change every 33 ms and a resize, on GL
/// and Vulkan alike.</para>
///
/// <para>A window that has gained the keyboard focus since it was minimized is on screen, so on
/// Wayland that ends the minimized state whatever the flag says. Elsewhere the flag is the
/// window system's answer and is taken as it is.</para>
/// </summary>
internal static class MinimizedState
{
    /// <param name="sdlSaysMinimized">SDL's <c>SDL_WINDOW_MINIMIZED</c>, read live.</param>
    /// <param name="wayland">SDL's video driver is Wayland.</param>
    /// <param name="focusedSinceMinimize">The window gained the keyboard focus after the app
    /// last minimized it.</param>
    internal static bool IsMinimized(bool sdlSaysMinimized, bool wayland, bool focusedSinceMinimize) =>
        sdlSaysMinimized && !(wayland && focusedSinceMinimize);
}
