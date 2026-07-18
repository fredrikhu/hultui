using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.Platform;

/// <summary>
/// Optional capability of an <see cref="IPlatformWindow"/>: custom window
/// chrome for borderless windows — resize borders, drag regions, and the
/// native-frame integration Windows needs for Snap Layouts. The framework
/// Window discovers this with <c>platformWindow as IWindowChrome</c> and
/// skips all chrome wiring when it is absent (headless).
/// </summary>
public interface IWindowChrome
{
    /// <summary>
    /// True when the platform keeps the OS-native frame for a borderless window
    /// (Windows: WM_NCHITTEST-based move/resize + Snap Layouts) rather than
    /// synthesizing chrome behavior from a hit-test callback.
    /// </summary>
    bool UsesWindowsNativeFrame { get; }

    /// <summary>Width of the invisible resize border, in layout units.</summary>
    int ResizeBorderDip { get; set; }

    /// <summary>Which window edges allow interactive resizing.</summary>
    ResizeEdges ResizableEdges { get; set; }

    /// <summary>
    /// Synthesized-chrome path: full hit-test callback in layout units,
    /// returning the platform's hit-test result code.
    /// </summary>
    Func<int, int, int>? HitTestHandler { get; set; }

    /// <summary>Native-frame path: is this layout-unit point a caption-drag region?</summary>
    Func<int, int, bool>? IsWindowDragPoint { get; set; }

    /// <summary>Native-frame path: is this layout-unit point the maximize button (Snap Layouts)?</summary>
    Func<int, int, bool>? IsWindowMaximizePoint { get; set; }

    /// <summary>Native-frame path: laid-out bounds of the maximize button, if any.</summary>
    Func<Rect?>? GetWindowMaximizeButtonBounds { get; set; }

    /// <summary>Native-frame path: non-client hover state of the maximize button changed.</summary>
    Action<bool>? OnMaximizeButtonHover { get; set; }

    /// <summary>Keeps the native caption band tall enough to contain the caption UI, in pixels.</summary>
    void SetCaptionHeightPx(int px);
}
