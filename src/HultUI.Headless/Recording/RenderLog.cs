using System.Text;

namespace HultUI.Headless.Recording;

/// <summary>
/// The display list captured from one or more frames: every drawing call the
/// element tree issued, in order, with clip/transform nesting shown by
/// indentation. <see cref="ToString"/> renders it as the snapshot text to
/// compare against a checked-in baseline.
/// </summary>
public sealed class RenderLog
{
    private readonly List<RenderOp> _ops = [];

    public IReadOnlyList<RenderOp> Ops => _ops;

    public int Count => _ops.Count;

    internal void Add(RenderOp op) => _ops.Add(op);

    /// <summary>Drops everything recorded so far — call before the frame under test.</summary>
    public void Clear() => _ops.Clear();

    /// <summary>Ops with the given name, e.g. <c>"DrawText"</c>.</summary>
    public IEnumerable<RenderOp> Named(string name)
        => _ops.Where(o => o.Name == name);

    /// <summary>True when any recorded op's line contains <paramref name="fragment"/>.</summary>
    public bool Contains(string fragment)
        => _ops.Any(o => o.ToString().Contains(fragment, StringComparison.Ordinal));

    /// <summary>
    /// The snapshot text: one op per line, indented by nesting depth. Uses \n
    /// endings so baselines compare equal regardless of the platform that wrote
    /// them or git's autocrlf setting.
    /// </summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var op in _ops)
            sb.Append(op).Append('\n');
        return sb.ToString();
    }
}
