namespace HultUI.Core.Platform;

/// <summary>
/// Whether the app is on screen (<c>Application.LifecycleState</c>). Always
/// <see cref="Foreground"/> on Windows and Linux, where minimizing a window is not leaving the
/// app. More states may come (picture-in-picture is the likely one), so a <c>switch</c> over it
/// needs a default.
/// </summary>
public enum AppLifecycleState
{
    /// <summary>On screen: the app draws and takes input.</summary>
    Foreground,

    /// <summary>Off screen: Home was pressed, another app came to the front, or the screen was
    /// locked. HultUI draws nothing; the UI thread still runs posted work, timers and
    /// <c>await</c> continuations. The system may end the process without another word.</summary>
    Background,
}

/// <summary>
/// A platform that tells the application when it leaves the screen and comes back, and when the
/// system wants memory back: SDL on Android (and iOS, later), and the headless platform, whose
/// session drives it. SDL on Windows and Linux implements it and never raises anything.
/// <c>Application</c> follows it and raises its own events of the same names; app code uses
/// those. Every event is raised on the UI thread, synchronously, while the platform reports it.
/// </summary>
public interface IAppLifecycleSource
{
    /// <summary>Where the app is now. Already the new value when an event is raised.</summary>
    AppLifecycleState LifecycleState { get; }

    /// <summary>The app is leaving the screen. The last moment the app can count on.</summary>
    event Action? EnteringBackground;

    /// <summary>The app is back on screen, before its first frame.</summary>
    event Action? EnteredForeground;

    /// <summary>The system asks for memory back. The platform has emptied what it keeps (the
    /// GPU's caches, the text caches) before it raises this.</summary>
    event Action? LowMemory;

    /// <summary>The system is ending the app, where it says so. Best effort.</summary>
    event Action? Terminating;
}
