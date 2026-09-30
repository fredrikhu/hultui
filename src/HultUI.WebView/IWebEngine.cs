using HultUI.Core.Events;
using HultUI.Core.Rendering;

namespace HultUI.WebView;

/// <summary>
/// A browser engine: WebView2 on Windows, WPE WebKit on Linux, or
/// <see cref="Testing.FakeWebEngine"/> in tests. It owns whatever its views share — on
/// WebView2, one browser process per user data folder — so an application normally
/// creates one and disposes it on exit, after the views.
/// </summary>
public interface IWebEngine : IDisposable
{
    /// <summary>A short name for logs and error messages: "WebView2", "WPE".</summary>
    string Name { get; }

    /// <summary>
    /// Whether this engine can create views on this machine. Cheap and never throws, so
    /// an application can ask before building a page and show an install link instead.
    /// </summary>
    WebEngineAvailability Availability { get; }

    /// <summary>
    /// Creates one page. Returns at once; the view reports
    /// <see cref="WebEngineEvent.Ready"/> or <see cref="WebEngineEvent.Failed"/> when its
    /// engine has decided, which for some engines waits for the first placement in a
    /// window. UI thread.
    /// </summary>
    IWebEngineView CreateView(WebViewOptions options);
}

/// <summary>
/// One page in an engine. <see cref="WebViewController"/> is the only caller; an
/// application talks to the controller.
/// </summary>
/// <remarks>
/// <para><b>Threading.</b> Every member is called on the UI thread. <see cref="Event"/>
/// may be raised on any thread, and pixels may be published into the host's sink from any
/// thread; the controller marshals both. The view never touches elements or reactive
/// state.</para>
/// <para><b>Before ready.</b> The controller holds navigation, script and messages until
/// <see cref="WebEngineEvent.Ready"/>, so a view need not queue them. Placement,
/// visibility, input and focus arrive whenever they happen.</para>
/// </remarks>
public interface IWebEngineView : IDisposable
{
    WebEngineCapabilities Capabilities { get; }

    /// <summary>Called once, first, on the UI thread: where frames go.</summary>
    void Attach(IWebViewHost host);

    /// <summary>
    /// Which window the page is in and where, in that window's device pixels. Sent when
    /// any of it changes: the element moved, resized, scrolled, changed windows, or the
    /// window's scale changed. The page renders at <see cref="WebViewPlacement.Bounds"/>'s
    /// size; the position is for the engine's own popups and input method, which are
    /// placed on screen against it.
    /// </summary>
    void SetPlacement(in WebViewPlacement placement);

    /// <summary>False while no element shows the page: the engine may throttle it and stop
    /// producing frames. The page keeps its state.</summary>
    void SetVisible(bool visible);

    void Navigate(Uri uri);

    /// <summary>Loads a document from a string. Its origin is opaque: messages from it
    /// arrive with a null <see cref="WebEngineEvent.MessageReceived.Origin"/>.</summary>
    void NavigateToString(string html);

    void GoBack();
    void GoForward();
    void Reload();
    void Stop();

    /// <summary>Runs script in the top document; the result is its value as JSON.</summary>
    Task<string> ExecuteScriptAsync(string script);

    /// <summary>Delivers a JSON message to the page's message listener.</summary>
    void PostMessage(string json);

    /// <summary>A pointer event over the page, in device pixels relative to its top-left.</summary>
    void SendPointer(in WebPointerInput input);

    /// <summary>A key, for an engine without <see cref="WebEngineCapabilities.NativeKeyboard"/>.</summary>
    void SendKey(in WebKeyInput input);

    /// <summary>Committed or composing text, for an engine without
    /// <see cref="WebEngineCapabilities.NativeKeyboard"/>.</summary>
    void SendText(in WebTextInput input);

    /// <summary>
    /// The element gained focus. <see cref="FocusReason.Next"/> starts at the page's first
    /// tab stop and <see cref="FocusReason.Previous"/> at its last; anything else restores
    /// the page's own focus.
    /// </summary>
    void Focus(FocusReason reason);

    /// <summary>The element lost focus to something else in the window.</summary>
    void Blur();

    event Action<WebEngineEvent>? Event;
}

/// <summary>
/// What the controller gives a view to deliver its output through. Two ways, as for video:
/// pixels into <see cref="Pixels"/> from any thread, which every engine can do; and, for an
/// engine that registers an <see cref="IGpuFrameRenderer"/>, rendering into a GPU framebuffer
/// inside the frame on a surface that runs external GL. Which applies is known only once the
/// element is first drawn — the renderer hears through <see cref="IGpuFrameRenderer.OnModeDecided"/>.
/// </summary>
public interface IWebViewHost
{
    /// <summary>The page's pixels. Write whole frames, from any thread.</summary>
    PixelFrameSink Pixels { get; }

    /// <summary>Registers a GPU renderer; call from <see cref="IWebEngineView.Attach"/>. The
    /// page is then drawn through a <see cref="GpuFrameSink"/>.</summary>
    void UseGpuRenderer(IGpuFrameRenderer renderer);

    /// <summary>
    /// Any thread: the GPU renderer has a new frame and wants to be asked for it. Never blocks
    /// and never touches the platform on the caller's thread. (A pixel producer needs nothing
    /// here: publishing to <see cref="Pixels"/> asks.)
    /// </summary>
    void RequestRepaint();
}

/// <summary>What a view does itself rather than through the element.</summary>
[Flags]
public enum WebEngineCapabilities
{
    None = 0,

    /// <summary>
    /// Keyboard and input-method text reach the page without the element: WebView2 holds
    /// Win32 focus in a child window of its own while the page has focus. The element then
    /// forwards no keys and asks the window for no text input.
    /// </summary>
    NativeKeyboard = 1 << 0,

    /// <summary>The engine can open developer tools for the page.</summary>
    DevTools = 1 << 1,
}

/// <summary>Whether an engine can run here, and if not, why.</summary>
/// <param name="IsAvailable">True when views can be created.</param>
/// <param name="Reason">Why not, for a person to read: "The WebView2 Runtime is not installed."</param>
/// <param name="InstallLink">Where the missing piece comes from, when there is such a place.</param>
public readonly record struct WebEngineAvailability(bool IsAvailable, string? Reason = null, Uri? InstallLink = null)
{
    public static WebEngineAvailability Available => new(true);
    public static WebEngineAvailability Missing(string reason, Uri? installLink = null) => new(false, reason, installLink);
}

/// <summary>A rectangle in whole device pixels.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>Where a page is.</summary>
/// <param name="Window">The native parent window (<c>Window.NativeHandle</c>); zero when
/// there is none — headless, or a platform whose engine needs none.</param>
/// <param name="Bounds">The page in that window's client area, in device pixels.</param>
/// <param name="Scale">Device pixels per layout unit, which the engine rasterizes at.</param>
public readonly record struct WebViewPlacement(nint Window, PixelRect Bounds, float Scale);
