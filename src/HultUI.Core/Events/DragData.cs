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
