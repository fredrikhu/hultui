namespace HultUI.Core.Accessibility;

/// <summary>
/// Content that HultUI draws but cannot describe: something else — a browser engine — built
/// it, and keeps its own accessibility tree for it in windows of its own. An element names
/// those windows here, and the platform bridge joins that tree to HultUI's at the element.
/// </summary>
/// <remarks>
/// <para>On Windows both are window handles. <see cref="Window"/> is a child of the app's
/// window; UI Automation would list it under the window's root, and the bridge moves it under
/// the element instead, so the element's children are the foreign tree. It has no size of its
/// own to find by position — the content is composited, not shown in that window — so a point
/// inside the element is sent to <see cref="HitTestWindow"/>, a window whose providers can
/// say what is there.</para>
/// <para>The default value means none, which is every element but a web view.</para>
/// </remarks>
/// <param name="Window">The window whose accessibility tree is the element's content; zero for none.</param>
/// <param name="HitTestWindow">The window that resolves a point inside the element; zero when
/// there is none, and the element itself is the answer.</param>
public readonly record struct NativeAccessibleContent(nint Window, nint HitTestWindow = 0)
{
    /// <summary>True when there is no native content.</summary>
    public bool IsEmpty => Window == 0;
}
