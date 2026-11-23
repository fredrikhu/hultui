namespace HultUI.Core.Platform;

/// <summary>The desktop's light or dark preference.</summary>
public enum ColorScheme : byte
{
    /// <summary>The desktop states none, or it cannot be read (no portal, no session bus, a host
    /// that does not ask). An app picks its own default.</summary>
    NoPreference,
    Light,
    Dark,
}

/// <summary>
/// Implemented by a windowing platform that can read the desktop's appearance settings. The
/// framework discovers it with a type test, as it does <see cref="IMonitorSource"/>; a platform
/// without it reports <see cref="ColorScheme.NoPreference"/>.
/// </summary>
public interface IDesktopSettingsSource
{
    /// <summary>The desktop's light or dark preference now. Cheap to read.</summary>
    ColorScheme ColorScheme { get; }

    /// <summary>Raised on the UI thread, from the event pump, after a desktop setting changed;
    /// the properties already hold the new values.</summary>
    event Action? Changed;
}
