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

    /// <summary>Scale factor from layout units to physical pixels.</summary>
    float DisplayScale { get; }

    WindowStyle Style { get; }
    bool IsOpen { get; }
    bool IsMaximized { get; }

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
