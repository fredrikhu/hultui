namespace HultUI.Media;

/// <summary>Where a <see cref="MediaSource"/> comes from.</summary>
public enum MediaSourceKind
{
    File,
    Uri,
    Stream,
}

/// <summary>
/// Something a backend can open: a file path, a URI, or a seekable stream. Which of
/// the three a backend accepts is in its <see cref="BackendCapabilities"/>.
/// </summary>
public sealed class MediaSource
{
    private MediaSource(MediaSourceKind kind, string? path, Uri? uri, string? url, Stream? stream, string displayName)
    {
        Kind = kind;
        Path = path;
        Uri = uri;
        Url = url;
        Stream = stream;
        DisplayName = displayName;
    }

    public MediaSourceKind Kind { get; }

    /// <summary>The file path, for <see cref="MediaSourceKind.File"/>.</summary>
    public string? Path { get; }

    /// <summary>
    /// The URI, for <see cref="MediaSourceKind.Uri"/>, when .NET could parse it. A
    /// backend should use <see cref="Url"/>, which is always set for that kind.
    /// </summary>
    public Uri? Uri { get; }

    /// <summary>
    /// The URL as text, for <see cref="MediaSourceKind.Uri"/>: what a backend opens.
    /// Set even for forms .NET cannot parse, such as mpv's <c>av://lavfi:testsrc</c>.
    /// </summary>
    public string? Url { get; }

    /// <summary>The stream, for <see cref="MediaSourceKind.Stream"/>. The caller owns it.</summary>
    public Stream? Stream { get; }

    /// <summary>
    /// What to call it: the file name, the URI's last segment or host, or the name
    /// given with a stream. The video element uses it as the accessible name.
    /// </summary>
    public string DisplayName { get; }

    public static MediaSource FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new MediaSource(MediaSourceKind.File, path, null, null, null, System.IO.Path.GetFileName(path));
    }

    public static MediaSource FromUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        string name = uri.IsAbsoluteUri && uri.Segments.Length > 0
            ? Uri.UnescapeDataString(uri.Segments[^1]).TrimEnd('/')
            : "";
        if (name.Length == 0) name = uri.IsAbsoluteUri ? uri.Host : uri.OriginalString;
        return new MediaSource(MediaSourceKind.Uri, null, uri, uri.OriginalString, null, name);
    }

    /// <summary>
    /// A URL as text. What .NET cannot parse — mpv's own forms such as
    /// <c>av://lavfi:testsrc</c>, where the scheme's payload looks like a port — is
    /// kept verbatim in <see cref="Url"/> with <see cref="Uri"/> null.
    /// </summary>
    public static MediaSource FromUri(string uri)
    {
        ArgumentException.ThrowIfNullOrEmpty(uri);
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return FromUri(parsed);
        int slash = uri.LastIndexOf('/');
        string name = slash >= 0 && slash < uri.Length - 1 ? uri[(slash + 1)..] : uri;
        return new MediaSource(MediaSourceKind.Uri, null, null, uri, null, name);
    }

    /// <param name="stream">A readable stream; seekable for anything but straight-through play.</param>
    /// <param name="displayName">What to call it — there is no name to derive.</param>
    public static MediaSource FromStream(Stream stream, string displayName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrEmpty(displayName);
        return new MediaSource(MediaSourceKind.Stream, null, null, null, stream, displayName);
    }

    public override string ToString() => Kind switch
    {
        MediaSourceKind.File => $"file:{Path}",
        MediaSourceKind.Uri => $"uri:{Url}",
        _ => $"stream:{DisplayName}",
    };
}
