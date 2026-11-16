namespace HultUI;

/// <summary>
/// A request to close a window, handed to <see cref="Window.CloseRequested"/>: call
/// <see cref="Cancel"/> to keep the window open.
/// </summary>
public sealed class WindowCloseRequest
{
    /// <summary>Keeps the window open. Any handler may: a cancel holds, whatever the handlers
    /// after it do, so two documents with unsaved work each keep the window.</summary>
    public void Cancel() => IsCancelled = true;

    /// <summary>Whether a handler kept the window open.</summary>
    public bool IsCancelled { get; private set; }
}
