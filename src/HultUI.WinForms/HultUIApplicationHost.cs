using System.Diagnostics;
using HultUI.Core.Elements;
using HultUI.Core.Styling;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace HultUI.WinForms;

/// <summary>
/// Owns the one HultUI <see cref="Application"/> a WinForms process has, and drives it
/// from the WinForms message loop.
///
/// <para>Most applications never touch this: adding a <see cref="HultUIControl"/> to a
/// form starts it, and removing the last one stops the pump. It is public for the
/// things a host legitimately decides — the theme, the frame interval, and reaching
/// the <see cref="Application"/> to open a stand-alone HultUI window alongside the
/// embedded ones.</para>
/// </summary>
/// <remarks>
/// One application per process is not a simplification, it is what the framework is:
/// <c>Application.Current</c>, <c>InvokeOnMainThread</c> and the UI-thread binding are
/// all static. Several controls therefore share one <see cref="Application"/> and one
/// element-tree thread, which is also what makes <c>InvokeOnMainThread</c> from a
/// background worker land correctly no matter which control's tree it updates.
/// </remarks>
public static class HultUIApplicationHost
{
    private static readonly WinFormsWindowingPlatform Platform = new();
    private static readonly List<HultUIControl> Controls = [];
    private static readonly Stopwatch Clock = new();

    private static Application? _application;
    private static WinFormsTimer? _pump;
    private static int _intervalMilliseconds = 8;

    /// <summary>
    /// The running application, or null until the first <see cref="HultUIControl"/>
    /// gets its window handle. Use it to set a theme, open an extra window, or reach
    /// DevTools.
    /// </summary>
    public static Application? Application => _application;

    /// <summary>
    /// How often the frame pump ticks, in milliseconds. Default 8.
    /// </summary>
    /// <remarks>
    /// A tick is not a frame: HultUI renders only what is dirty, so an idle control
    /// costs a tree walk and no drawing. Raising this trades input-to-pixel latency for
    /// fewer walks, and there is little to gain — an idle form was measured rendering
    /// two frames across a whole run.
    ///
    /// <para>A timer rather than <c>Application.Idle</c>, which fires when the message
    /// queue drains and is not a frame clock: measured at 19 idle events against 234
    /// timer ticks over the same four seconds.</para>
    /// </remarks>
    public static int PumpIntervalMilliseconds
    {
        get => _intervalMilliseconds;
        set
        {
            _intervalMilliseconds = Math.Max(1, value);
            if (_pump is not null) _pump.Interval = _intervalMilliseconds;
        }
    }

    /// <summary>The theme every hosted control renders with.</summary>
    /// <exception cref="InvalidOperationException">No control has been mounted yet.</exception>
    public static Theme Theme
    {
        get => Require().Theme;
        set => Require().Theme = value;
    }

    /// <summary>
    /// Creates the framework window for <paramref name="control"/>, starting the
    /// application if this is the first one.
    /// </summary>
    internal static (Window Window, WglPlatformWindow PlatformWindow) Mount(HultUIControl control, Element root)
    {
        Platform.PendingControl = control;

        Window window;
        if (_application is null)
        {
            // The Application constructor makes its primary window immediately, which is
            // this control's, and claims the calling thread — the WinForms UI thread —
            // as the one that owns every element tree from here on.
            _application = new Application(Platform, control.Name.Length > 0 ? control.Name : "HultUI",
                Math.Max(control.ClientSize.Width, 1), Math.Max(control.ClientSize.Height, 1));
            window = _application.PrimaryWindow;
        }
        else
        {
            window = _application.CreateWindow(control.Name.Length > 0 ? control.Name : "HultUI",
                Math.Max(control.ClientSize.Width, 1), Math.Max(control.ClientSize.Height, 1));
        }

        var platformWindow = Platform.TakeLastCreated();
        window.SetRoot(root);

        if (!Controls.Contains(control)) Controls.Add(control);
        StartPump();
        return (window, platformWindow);
    }

    /// <summary>Stops tracking a control whose handle has gone, stopping the pump with
    /// the last one.</summary>
    internal static void Unmount(HultUIControl control)
    {
        Controls.Remove(control);
        if (Controls.Count == 0) StopPump();
    }

    private static void StartPump()
    {
        if (_pump is not null) return;
        _pump = new WinFormsTimer { Interval = _intervalMilliseconds };
        _pump.Tick += (_, _) => Tick();
        _pump.Start();
        Clock.Restart();
    }

    private static void StopPump()
    {
        _pump?.Stop();
        _pump?.Dispose();
        _pump = null;
        Clock.Stop();
    }

    private static void Tick()
    {
        var application = _application;
        if (application is null || !application.IsRunning) return;

        var delta = Clock.Elapsed;
        Clock.Restart();
        application.RunFrame(delta);
    }

    /// <summary>
    /// Renders one frame immediately, ignoring dirty tracking. Used by a control's paint
    /// handler: after occlusion the GL back buffer's contents are undefined, so the
    /// window has to be redrawn even though nothing in the tree changed.
    /// </summary>
    /// <remarks>
    /// This repaints every hosted control, not just the one being painted, because the
    /// framework forces a frame per application rather than per window. With four
    /// surfaces that was measured at about 1.1 ms; narrowing it would mean a per-window
    /// force in the framework, which no caller has yet needed.
    /// </remarks>
    internal static void RenderNow()
    {
        var application = _application;
        if (application is null || !application.IsRunning) return;
        application.RunFrame(TimeSpan.Zero, forceRender: true);
    }

    private static Application Require()
        => _application ?? throw new InvalidOperationException(
            "No HultUI application yet. It starts when the first HultUIControl is added to a " +
            "form and its handle is created; set this afterwards, or from the control's Mounted event.");
}
