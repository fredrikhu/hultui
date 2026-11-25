using HultUI.Core.Primitives;

namespace HultUI.Core.Platform;

/// <summary>
/// A platform window that knows which of its edges the system covers: the status bar, the
/// gesture bar, a display cutout (docs/design/ANDROID-PLATFORM.md §3). <c>Window.SafeArea</c>
/// reads it. SDL's window on Android, and the headless window, whose test sets it.
/// </summary>
internal interface ISafeAreaSource
{
    /// <summary>How far in from each edge the system's furniture reaches, in layout units.</summary>
    Thickness SafeArea { get; }

    /// <summary>Raised on the UI thread when <see cref="SafeArea"/> changed: a rotation, the bars
    /// shown or hidden.</summary>
    event Action? SafeAreaChanged;
}
