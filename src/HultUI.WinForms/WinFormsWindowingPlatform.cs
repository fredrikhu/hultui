using HultUI.Core.Platform;
using HultUI.Core.Styling;
using HultUI.Rendering.Skia;
using HultUI.WinForms.Interop;

namespace HultUI.WinForms;

/// <summary>
/// The windowing platform for HultUI hosted in WinForms. There is no native event
/// queue of its own to drain — WinForms owns the message loop and delivers input as
/// control events — so pumping is a no-op and every window it makes is bound to the
/// control that asked for it.
/// </summary>
internal sealed class WinFormsWindowingPlatform : IWindowingPlatform
{
    /// <summary>
    /// The control the next <see cref="CreateWindow"/> call mounts into.
    /// </summary>
    /// <remarks>
    /// <c>Application.CreateWindow</c> takes a title and a size and no way to say
    /// <em>where</em>, so the host sets this immediately before calling it. The same
    /// technique <c>HeadlessWindowingPlatform.NextWindowScale</c> already uses in-repo,
    /// and it keeps the framework's API unchanged for a host-specific need.
    /// </remarks>
    internal HultUIControl? PendingControl { get; set; }

    /// <summary>
    /// The platform window from the most recent <see cref="CreateWindow"/>, so the host
    /// can hand it to the control that asked for it. <c>Window.PlatformWindow</c> is
    /// internal to the framework, and the platform is the one place that already knows
    /// which window it built.
    /// </summary>
    private WglPlatformWindow? _lastCreated;

    /// <summary>
    /// The text services every control's surface draws and measures with: fonts resolved
    /// once, caches shared, and nothing of them left behind when a control's handle is
    /// destroyed or recreated. The preloaded set when <see cref="GraphicsPreload"/> made one.
    /// Shared on one thread only — every control belongs to the one application and its UI
    /// thread.
    /// </summary>
    private SkiaTextServices? _text;

    private SkiaTextServices TextServices() => _text ??= GraphicsPreload.TakeText() ?? new SkiaTextServices();

    internal WglPlatformWindow TakeLastCreated()
    {
        var created = _lastCreated
            ?? throw new InvalidOperationException("No window was created.");
        _lastCreated = null;
        return created;
    }

    public TimeProvider TimeProvider => TimeProvider.System;

    public IPlatformWindow CreateWindow(string title, int width, int height, WindowStyle style)
    {
        var control = PendingControl
            ?? throw new InvalidOperationException(
                "A HultUI window was created without a host control. Windows on this platform " +
                "belong to a HultUIControl; create them through the control rather than by " +
                "calling Application.CreateWindow directly.");
        PendingControl = null;
        _lastCreated = new WglPlatformWindow(control, title, style, TextServices);
        return _lastCreated;
    }

    /// <summary>
    /// Nothing to pump: the host's message loop already dispatched to the controls,
    /// which raised their input as it happened. Returns true because there is no such
    /// thing as the platform asking this application to quit — that is the host form's
    /// decision, not ours.
    /// </summary>
    public bool PumpEvents(Action<IPlatformWindow>? onWindowClose) => true;

    /// <summary>No-op. Parking the thread belongs to the host's own idle handling; a
    /// wait here would stall the form.</summary>
    public void WaitForEvents(int timeoutMs) { }

    /// <summary>No-op: there is no wait here to cut short. The host's timer or idle
    /// handler decides when the next <c>RunFrame</c> runs.</summary>
    public void Wake() { }

    public void Dispose()
    {
        _text?.Dispose();
        _text = null;
    }
}
