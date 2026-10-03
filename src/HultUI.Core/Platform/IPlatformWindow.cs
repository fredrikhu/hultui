using HultUI.Core.Events;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Platform;

/// <summary>
/// Platform abstraction for a single native (or headless) window: the rendering
/// surface, logical size/scale, input delivery, and lifecycle. The framework
/// <c>Window</c> talks only to this interface; SDL3 implements it for real OS
/// windows and HultUI.Headless implements it for display-free testing.
/// </summary>
/// <remarks>
/// Native-frame/chrome integration (hit-test regions, caption height, Snap
/// Layouts) is deliberately not part of this interface — platforms that support
/// it additionally implement <see cref="IWindowChrome"/>, which the framework
/// discovers with a type test and skips otherwise.
/// </remarks>
public interface IPlatformWindow : IDisposable
{
    IRenderSurface Surface { get; }

    /// <summary>Client width in layout units (pixels / DisplayScale).</summary>
    float LayoutWidth { get; }

    /// <summary>Client height in layout units (pixels / DisplayScale).</summary>
    float LayoutHeight { get; }

    /// <summary>Scale factor from layout units to physical pixels: the scale the OS asks
    /// for, times <see cref="UserScale"/>.</summary>
    float DisplayScale { get; }

    /// <summary>
    /// The user's own zoom on top of the OS scaling — 1.0 for "as the system says". Raising
    /// it draws the whole UI larger and fits less in the window, which is what someone asking
    /// for bigger text on a 4K panel means: growing only the font would leave every fixed
    /// row height, gutter and icon at its old size. Implementations clamp to a range the
    /// window is still usable in.
    /// </summary>
    float UserScale { get; set; }

    /// <summary>
    /// The refresh rate of the display the window is on, in Hz, or 0 when the platform
    /// does not know. The frame loop paces rendered frames to it, since the swap
    /// interval is 0 and nothing else would stop an animation from rendering as fast
    /// as the GPU returns.
    /// </summary>
    float RefreshRate { get; }

    /// <summary>
    /// The window system the window is on, as the platform names it: "wayland", "x11",
    /// "windows", "cocoa"; null when there is none (headless) or the platform does not say. For
    /// something that must match it before it starts, as a browser engine picks its own
    /// display backend.
    /// </summary>
    string? WindowSystem => null;

    WindowStyle Style { get; }
    bool IsOpen { get; }
    bool IsMaximized { get; }

    /// <summary>
    /// The native window title — what the OS shows in the task bar, the Alt+Tab
    /// switcher and (for a decorated window) the caption. Settable after creation
    /// so an app can reflect its current document; a borderless window still has
    /// one, because the task bar and switcher read it even when no caption is drawn.
    /// </summary>
    string Title { get; set; }

    IClipboard Clipboard { get; }
    IFileDialogs FileDialogs { get; }
    ITextInputHost TextInputHost { get; }

    /// <summary>
    /// Raised once per translated input event, with positions already converted
    /// to layout units. The framework Window subscribes its input pipeline here.
    /// </summary>
    event Action<InputEvent>? InputReceived;

    // ──── Resize coalescing ────
    // Native resize events can arrive faster than we want to relayout (and on
    // Windows arrive inside a modal loop). The platform sets ResizePending; the
    // application loop throttles relayout via LastResizeRenderTick.

    bool ResizePending { get; set; }
    long LastResizeRenderTick { get; set; }

    // ──── Window activation ────
    // Set by the platform when the OS makes this window the foreground one, and
    // cleared by the application loop as it raises Window.Activated. A flag rather
    // than a callback for the same reason resizes use one: the native event can
    // arrive on a modal loop or between frames, and the framework wants it handed
    // to app code on the UI thread at a known point in the frame.

    bool ActivatedPending { get; set; }

    /// <summary>
    /// Invoked by platforms whose native resize runs a modal loop (Win32) to
    /// render a throttled frame mid-resize. Headless platforms never call it.
    /// </summary>
    Action? LiveResizeFrame { get; set; }

    /// <summary>Makes the rendering context current on this thread. No-op when the surface has no context affinity.</summary>
    void MakeCurrent();

    /// <summary>Reveals the window after its first frame is presented. No-op headless.</summary>
    void EnsureShown();

    /// <summary>Presents the back buffer. No-op for surfaces without swap chains.</summary>
    void SwapBuffers();

    /// <summary>
    /// True while the window covers a whole display with no chrome. Distinct from
    /// <see cref="IsMaximized"/>, which fills the work area and leaves the task bar and
    /// the window's own frame in place.
    /// </summary>
    bool IsFullscreen => false;

    /// <summary>
    /// Takes the window fullscreen, or brings it back to where it was.
    ///
    /// <para>This is the <i>windowed</i> kind — what games call borderless fullscreen: the
    /// window is resized to the display it is on and stripped of chrome, and the display
    /// mode is left alone. No mode switch means no black flash, no rearranged desktop when
    /// the app crashes, and an alt-tab that is instant rather than a resolution change.
    /// Exclusive fullscreen buys a video player nothing.</para>
    ///
    /// <para>A platform that cannot do it (a hosted control, a headless surface) leaves
    /// this a no-op and keeps answering false — callers treat that as "this window does
    /// not do fullscreen", not as an error.</para>
    /// </summary>
    void SetFullscreen(bool fullscreen) { }

    void Close();
    void Minimize();
    void Maximize();
    void Restore();
    /// <summary>Bring the window to the front / request the OS's attention.</summary>
    void Raise();

    void SetMinimumSize(int layoutWidth, int layoutHeight);
    void SetIcon(int width, int height, ReadOnlySpan<byte> rgbaPixels);
    void SetCursor(CursorType cursor);

    /// <summary>
    /// One-line description of the *achieved* graphics configuration — renderer,
    /// video driver, swap interval, MSAA samples, drawable size — or null when
    /// the backend has nothing meaningful to report (headless). Logged once at
    /// window creation and stamped into frame traces, so cross-OS performance
    /// comparisons carry the config they ran under (a llvmpipe fallback, an
    /// unrequested MSAA framebuffer, or a vsync'd swap all masquerade as "slow
    /// rendering" otherwise). Diagnostic only — not a stable format, do not parse.
    /// </summary>
    string? DescribeGraphics() => null;
}
