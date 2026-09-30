namespace HultUI.WebView;

/// <summary>Where a download is.</summary>
public enum WebDownloadState
{
    /// <summary>Asked for, and waiting for the application's answer.</summary>
    Requested,
    InProgress,
    Completed,
    /// <summary>Cancelled, by the application or the person, before it finished.</summary>
    Cancelled,
    /// <summary>Stopped by an error: the network, the server, the disk.</summary>
    Failed,
}

/// <summary>
/// A page started a download. Nothing is saved unless the application says where: the
/// controller shows it to the <see cref="WebViewController.DownloadRequested"/> handlers, and
/// one of them calls <see cref="SaveTo"/> — at once, or later after asking the person where,
/// having called <see cref="Defer"/>. If none does, the download is cancelled: an application
/// never finds files it did not ask for, and the engine's own download interface never shows.
/// </summary>
public sealed class WebDownloadRequest
{
    private Action<string?>? _complete;

    /// <param name="uri">What is being downloaded.</param>
    /// <param name="suggestedFileName">The name the server or the page suggests, without a folder.</param>
    /// <param name="mimeType">The content type, if the server said.</param>
    /// <param name="totalBytes">The size, if known.</param>
    /// <param name="download">The object the engine reports progress through once saved.</param>
    /// <param name="complete">Called once with the full path to save to, or null to cancel.</param>
    public WebDownloadRequest(Uri uri, string suggestedFileName, string? mimeType, long? totalBytes,
        WebDownload download, Action<string?> complete)
    {
        Uri = uri;
        SuggestedFileName = suggestedFileName;
        MimeType = mimeType;
        TotalBytes = totalBytes;
        Download = download ?? throw new ArgumentNullException(nameof(download));
        _complete = complete ?? throw new ArgumentNullException(nameof(complete));
    }

    public Uri Uri { get; }
    public string SuggestedFileName { get; }
    public string? MimeType { get; }
    public long? TotalBytes { get; }

    /// <summary>The download's progress, once it is saved somewhere.</summary>
    public WebDownload Download { get; }

    /// <summary>A handler will answer later, with <see cref="SaveTo"/> or <see cref="Cancel"/>.</summary>
    public bool IsDeferred { get; private set; }

    /// <summary>The download has been answered.</summary>
    public bool IsCompleted => Volatile.Read(ref _complete) is null;

    /// <summary>Say that the answer comes later — after a save dialog, say. The download waits.</summary>
    public void Defer() => IsDeferred = true;

    /// <summary>Save to <paramref name="path"/>, a full file path. Returns the download's
    /// progress. Later calls do nothing.</summary>
    public WebDownload SaveTo(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (Interlocked.Exchange(ref _complete, null) is { } complete)
        {
            Download.Start(path);
            complete(path);
        }
        return Download;
    }

    /// <summary>Do not download. Later calls do nothing.</summary>
    public void Cancel()
    {
        if (Interlocked.Exchange(ref _complete, null) is { } complete)
        {
            Download.Finish(WebDownloadState.Cancelled);
            complete(null);
        }
    }
}

/// <summary>
/// A download in progress. The engine updates it on the UI thread; <see cref="Changed"/> is
/// raised there after each update.
/// </summary>
public sealed class WebDownload
{
    private Action? _cancel;

    /// <param name="cancel">Stops the download in the engine.</param>
    public WebDownload(Action cancel) => _cancel = cancel;

    /// <summary>Where it is being saved; null until the application has said.</summary>
    public string? Path { get; private set; }

    public WebDownloadState State { get; private set; } = WebDownloadState.Requested;
    public long BytesReceived { get; private set; }

    /// <summary>The size, when the server said.</summary>
    public long? TotalBytes { get; private set; }

    /// <summary>Raised on the UI thread after the state or the progress changed.</summary>
    public event Action<WebDownload>? Changed;

    /// <summary>Stops the download; what was saved so far is removed by the engine.</summary>
    public void Cancel()
    {
        if (State is WebDownloadState.Completed or WebDownloadState.Cancelled or WebDownloadState.Failed) return;
        Interlocked.Exchange(ref _cancel, null)?.Invoke();
    }

    /// <summary>For engines: the download began, into <paramref name="path"/>.</summary>
    public void Start(string path)
    {
        Path = path;
        State = WebDownloadState.InProgress;
        Changed?.Invoke(this);
    }

    /// <summary>For engines: bytes arrived. UI thread.</summary>
    public void Report(long bytesReceived, long? totalBytes)
    {
        if (State != WebDownloadState.InProgress) return;
        if (BytesReceived == bytesReceived && TotalBytes == totalBytes) return;
        BytesReceived = bytesReceived;
        TotalBytes = totalBytes;
        Changed?.Invoke(this);
    }

    /// <summary>For engines: the download ended. UI thread.</summary>
    public void Finish(WebDownloadState state)
    {
        if (State is WebDownloadState.Completed or WebDownloadState.Cancelled or WebDownloadState.Failed) return;
        State = state;
        _cancel = null;
        Changed?.Invoke(this);
    }
}
