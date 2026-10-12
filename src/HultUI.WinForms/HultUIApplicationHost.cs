using System.Diagnostics;
using HultUI.Core.Elements;
using HultUI.Core.Styling;
using HultUI.WinForms.Interop;
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
    // WinForms' own context, for a wake from any thread to reach the UI thread's message loop.
    private static SynchronizationContext? _context;
    private static int _kickPending;
    private static Action? _kick;

    /// <summary>
    /// The running application, or null until the first <see cref="HultUIControl"/>
    /// gets its window handle. Use it to set a theme, open an extra window, or reach
    /// DevTools.
    /// </summary>
    public static Application? Application => _application;

    /// <summary>
    /// Starts loading the graphics driver on a worker thread, so the first
    /// <see cref="HultUIControl"/> shown does not pay for it. Call it as the first
    /// statement in <c>Main</c>; everything the application does before that control
    /// appears — reading settings, building the form, opening a file — then overlaps
    /// with a load it would otherwise have waited for.
    /// </summary>
    /// <example>
    /// <code>
    /// [STAThread]
    /// static void Main()
    /// {
    ///     HultUIApplicationHost.BeginGraphicsPreload();
    ///     ApplicationConfiguration.Initialize();
    ///     Application.Run(new MainForm());
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>Safe to call more than once, safe to call and then never show a control,
    /// and safe on a machine where it cannot work — a failure leaves the ordinary path
    /// to pay the cost as before. The worker is a background thread and never delays
    /// process exit.</para>
    ///
    /// <para>Unlike the SDL backend's equivalent this needs no opt-in flag. That one is
    /// gated because loading off-thread steps outside SDL's documented threading
    /// contract; here there is no SDL, and warming the driver from another thread is
    /// ordinary WGL.</para>
    /// </remarks>
    public static void BeginGraphicsPreload() => GraphicsPreload.Begin();

    /// <summary>
    /// The shortest time between two frames of the pump, in milliseconds. Default 8.
    /// </summary>
    /// <remarks>
    /// The pump ticks only when something is due, as <c>Application.Run</c> does: every
    /// interval while a control needs a frame or a tween moves, once when a delay or a
    /// coalesced accessibility refresh comes due, and not at all while nothing does — then
    /// a post from another thread, or a change made on the UI thread (input, a WinForms
    /// handler writing a signal), brings the next tick forward. It ticked every 8 ms whatever
    /// happened, about 60 times a second in an idle form (WinForms timers round up to the
    /// system's 15.6 ms tick).
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
        // WinForms installs its context on the UI thread with the first control, which this is.
        _context = SynchronizationContext.Current;
        _pump = new WinFormsTimer { Interval = _intervalMilliseconds };
        _pump.Tick += (_, _) => Tick();
        Platform.Woken = _kick ??= Kick;
        Clock.Restart();
        _pump.Start();
    }

    private static void StopPump()
    {
        _pump?.Stop();
        _pump?.Dispose();
        _pump = null;
        Platform.Woken = null;
        _application?.HostUnpark();
        Clock.Stop();
    }

    private static void Tick()
    {
        var application = _application;
        _pump?.Stop();
        if (application is null || !application.IsRunning) return;

        application.HostUnpark();
        var delta = Clock.Elapsed;
        Clock.Restart();
        application.HostRunFrame(delta);
        Schedule(application);
    }

    /// <summary>After a frame: the next tick when the loop has one due, none until a wake
    /// when it has nothing. Parks the loop meanwhile, so work arriving on the UI thread
    /// (input, a handler posting or writing a signal) wakes it as it ends Run's wait.</summary>
    private static void Schedule(Application application)
    {
        if (_pump is null) return;
        if (application.HostNextFrameIn(TimeSpan.FromMilliseconds(_intervalMilliseconds)) is { } due)
        {
            _pump.Interval = Math.Max(1, (int)Math.Ceiling(due.TotalMilliseconds));
            _pump.Start();
        }
        application.HostPark(_kick!);
    }

    /// <summary>The loop's wake, from any thread: a tick, posted to the message loop, at most
    /// one outstanding.</summary>
    private static void Kick()
    {
        if (Interlocked.Exchange(ref _kickPending, 1) == 1) return;
        if (_context is not { } context)
        {
            Volatile.Write(ref _kickPending, 0);
            return;
        }
        context.Post(static _ =>
        {
            Volatile.Write(ref _kickPending, 0);
            KickNow();
        }, null);
    }

    private static void KickNow()
    {
        if (_pump is null) return;
        // Frames no closer than the interval: a wake soon after a tick waits out the rest of it.
        double since = Clock.Elapsed.TotalMilliseconds;
        if (since >= _intervalMilliseconds)
        {
            Tick();
            return;
        }
        _pump.Stop();
        _pump.Interval = Math.Max(1, (int)Math.Ceiling(_intervalMilliseconds - since));
        _pump.Start();
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
        application.HostUnpark();
        application.HostRunFrame(TimeSpan.Zero, forceRender: true);
        Schedule(application);
    }

    private static Application Require()
        => _application ?? throw new InvalidOperationException(
            "No HultUI application yet. It starts when the first HultUIControl is added to a " +
            "form and its handle is created; set this afterwards, or from the control's Mounted event.");
}
