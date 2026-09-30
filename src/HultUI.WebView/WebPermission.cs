namespace HultUI.WebView;

/// <summary>What a page asks to be allowed to do.</summary>
public enum WebPermission
{
    /// <summary>Something this version does not name.</summary>
    Unknown,
    Microphone,
    Camera,
    Geolocation,
    Notifications,
    /// <summary>Motion and light sensors and the like.</summary>
    OtherSensors,
    ClipboardRead,
    /// <summary>Start another download without a click in between.</summary>
    MultipleDownloads,
    /// <summary>Read and write files the person picked, through the File System Access API.</summary>
    FileReadWrite,
    Autoplay,
    LocalFonts,
    MidiSystemExclusive,
    WindowManagement,
    PersistentStorage,
}

/// <summary>
/// A page asked for a permission — the camera, the clipboard, notifications. Nothing is
/// allowed unless the application says so: the controller shows it to the
/// <see cref="WebViewController.PermissionRequested"/> handlers, and it is denied unless one
/// calls <see cref="Allow"/> — at once, or later, having called <see cref="Defer"/> to ask the
/// person first. The engine's own permission prompt never shows. Answered exactly once, so an
/// engine may wait for the answer across threads.
/// </summary>
public sealed class WebPermissionRequest
{
    private Action<bool>? _complete;

    /// <param name="permission">What is asked for.</param>
    /// <param name="origin">Whose page asks.</param>
    /// <param name="isUserInitiated">A click or a key led to it, rather than script alone.</param>
    /// <param name="complete">Called once: allowed or not.</param>
    public WebPermissionRequest(WebPermission permission, Uri? origin, bool isUserInitiated, Action<bool> complete)
    {
        Permission = permission;
        Origin = origin;
        IsUserInitiated = isUserInitiated;
        _complete = complete ?? throw new ArgumentNullException(nameof(complete));
    }

    public WebPermission Permission { get; }
    public Uri? Origin { get; }
    public bool IsUserInitiated { get; }

    /// <summary>A handler will answer later, with <see cref="Allow"/> or <see cref="Deny"/>.</summary>
    public bool IsDeferred { get; private set; }

    /// <summary>The request has been answered.</summary>
    public bool IsCompleted => Volatile.Read(ref _complete) is null;

    /// <summary>Say that the answer comes later — after asking the person. The page waits.</summary>
    public void Defer() => IsDeferred = true;

    /// <summary>Allow it. Later calls do nothing.</summary>
    public void Allow() => Interlocked.Exchange(ref _complete, null)?.Invoke(true);

    /// <summary>Deny it. Later calls do nothing.</summary>
    public void Deny() => Interlocked.Exchange(ref _complete, null)?.Invoke(false);
}
