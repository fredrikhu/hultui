namespace HultUI.Core.Events;

/// <summary>
/// Holds the payload for a drag &amp; drop operation.
/// For OS-level drops, contains files and/or text.
/// For internal drags, contains user-defined keyed data.
/// </summary>
public sealed class DragData
{
    private Dictionary<string, object>? _custom;

    // ──── OS-level drop data ────

    /// <summary>
    /// The platform's own data object while an OS drag is over the window — on Windows an
    /// <c>IDataObject*</c>, with every format the source offers, not only the files and text
    /// read into <see cref="Files"/> and <see cref="Text"/>. Valid until the drag leaves or is
    /// dropped; zero when the platform has none to give. For content that hands a drag on to
    /// something that reads it itself, as a web page's engine does.
    /// </summary>
    public nint NativeData { get; internal set; }
    public List<string> Files { get; } = [];
    public string? Text { get; set; }
    public bool HasFiles => Files.Count > 0;
    public bool HasText => Text is not null;

    // ──── Internal custom data ────
    public void Set<T>(string key, T value) where T : notnull
    {
        _custom ??= [];
        _custom[key] = value;
    }

    public bool TryGet<T>(string key, out T? value)
    {
        if (_custom is not null && _custom.TryGetValue(key, out var obj) && obj is T typed)
        {
            value = typed;
            return true;
        }
        value = default;
        return false;
    }

    public bool Has(string key) => _custom is not null && _custom.ContainsKey(key);
}
