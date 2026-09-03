namespace HultUI.Core.Platform;

/// <summary>
/// Implemented by platform windows that are backed by a real Win32 window, exposing
/// the handle so window-handle services — today the UI Automation bridge — work
/// regardless of which backend created it.
/// </summary>
/// <remarks>
/// Discovered with a type test and skipped when absent, the same way
/// <see cref="IWindowChrome"/> is: a backend with no HWND (the headless platform, and
/// every non-Windows one) simply does not implement it.
///
/// <para>This exists because accessibility must not be the privilege of one backend.
/// The bridge needs an HWND and nothing else — it subclasses that window's procedure
/// to answer <c>WM_GETOBJECT</c> — so a host embedding HultUI in a WinForms or WPF
/// control gets the same screen-reader support as a stand-alone window by handing
/// over its control's handle.</para>
/// </remarks>
public interface IWin32WindowHandle
{
    /// <summary>
    /// The native <c>HWND</c>, or <see cref="IntPtr.Zero"/> when the window is not yet
    /// created or has been destroyed. Callers must treat zero as "no handle" rather
    /// than assuming a live window.
    /// </summary>
    nint Hwnd { get; }
}
