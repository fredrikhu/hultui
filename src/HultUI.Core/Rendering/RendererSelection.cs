namespace HultUI.Core.Rendering;

/// <summary>
/// Which renderer a window gets, and whether a failed Vulkan start may quietly become GL —
/// the decisions behind <see cref="RendererKind"/>, kept apart from SDL so they can be tested.
/// </summary>
internal static class RendererSelection
{
    /// <summary>
    /// What a <c>HULTUI_RENDERER</c> value asks for: null when it names nothing (unset or
    /// empty), <see cref="RendererKind.Vulkan"/> for <c>vulkan</c>, and
    /// <see cref="RendererKind.OpenGL"/> for anything else. An override in both directions —
    /// <c>vulkan</c> turns Vulkan on for an app that never asked, anything else turns it off
    /// for one that did.
    /// </summary>
    public static RendererKind? ParseEnvironment(string? value) => value switch
    {
        null or "" => null,
        "vulkan" => RendererKind.Vulkan,
        _ => RendererKind.OpenGL,
    };

    /// <summary>The renderer a window is created with: the environment wins over the app, and
    /// the app over the default. <see cref="RendererKind.Default"/> is OpenGL today.</summary>
    public static RendererKind Resolve(RendererKind? environment, RendererKind? requested) =>
        (environment ?? requested ?? RendererKind.Default) == RendererKind.Vulkan
            ? RendererKind.Vulkan
            : RendererKind.OpenGL;

    /// <summary>
    /// Whether a Vulkan start that failed may fall back to OpenGL. Only when the <b>app</b>
    /// asked: when <c>HULTUI_RENDERER</c> asked, the failure is the answer — that variable is
    /// how the design docs test a renderer, and silently handing back the other one would make
    /// every such run a false negative.
    /// </summary>
    public static bool MayFallBack(RendererKind? environment) => environment is null;

    /// <summary>
    /// Whether a Vulkan window on Windows subscribes to WinRT's display-colour event rather than
    /// polling the display configuration: <c>HULTUI_DISPLAY_INFORMATION</c> <c>1</c> or
    /// <c>0</c> decides, in either direction, and otherwise the app's request does. Off unless
    /// asked for — it is hand-rolled COM hooked into the window's message loop, which an app
    /// with no HDR content has no reason to carry.
    /// </summary>
    public static bool ResolveDisplayColorEvents(string? environment, bool requested) => environment switch
    {
        "1" => true,
        "0" => false,
        _ => requested,
    };
}
