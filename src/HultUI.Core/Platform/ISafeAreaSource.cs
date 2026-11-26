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

    /// <summary>How far up from the window's bottom edge the soft keyboard reaches, in layout
    /// units; 0 while it is hidden, and always on the desktop (docs/design/SOFT-KEYBOARD.md §3).</summary>
    float KeyboardInset { get; }

    /// <summary>Raised on the UI thread when <see cref="SafeArea"/> or <see cref="KeyboardInset"/>
    /// changed: a rotation, the bars shown or hidden, the keyboard shown or hidden.</summary>
    event Action? SafeAreaChanged;
}
