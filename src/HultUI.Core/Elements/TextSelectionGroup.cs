namespace HultUI.Core.Elements;

/// <summary>
/// A selection that runs across many labels at once — the code lines of a diff, say —
/// held in <b>model</b> coordinates rather than in the elements that happen to be showing
/// it. The group stores "from line 4 offset 7 to line 900 offset 2"; each label works out
/// its own highlight from that, via <see cref="RangeFor"/>.
/// <para>
/// That indirection is what makes the selection survive virtualization. A list that
/// recycles its rows destroys the element the drag started in, and an element-anchored
/// selection dies with it: the anchor can no longer be found, so the drag stops extending,
/// and a copy can only gather the lines still on screen. Here the anchor is a line number,
/// which no amount of recycling can invalidate, and <see cref="SelectedText"/> reads the
/// model instead of the tree — so copying 900 lines does not require 900 live labels.
/// </para>
/// <para>
/// Give every label in the selection the same group and its own
/// <see cref="TextElement.SelectionIndex"/>. Order comes from that index, not from tree
/// order, so a label may be absent, rebuilt, or recycled without disturbing the rest.
/// </para>
/// </summary>
public sealed class TextSelectionGroup
{
    private Func<int, string?>? _textAt;
    private object? _modelKey;

    /// <summary>Number of addressable lines in the model. Indices run 0..Count-1.</summary>
    public int Count { get; private set; }

    private int _anchorIndex = -1, _anchorOffset;
    private int _focusIndex = -1, _focusOffset;

    /// <summary>
    /// Points the group at a model. <paramref name="modelKey"/> identifies *which* model:
    /// when it changes, the selection is dropped, because offsets into the old content mean
    /// nothing in the new one. Call this on every build — rebinding the same key is cheap and
    /// keeps a live selection intact across a rebuild.
    /// </summary>
    /// <param name="modelKey">Identifies the content being bound. A different key means a
    /// different document, and any live selection is dropped rather than carried onto text it
    /// no longer refers to; the same key across a rebuild keeps it.</param>
    /// <param name="count">How many lines the model has.</param>
    /// <param name="textAt">
    /// The text of line <c>i</c>, or <c>null</c> where the model has no line at that index —
    /// a collapsed region, or the blank half of a split diff opposite an added line. A null
    /// line takes no highlight and contributes nothing to a copy. An <i>empty</i> string is a
    /// real, empty line: it copies as a blank line.
    /// </param>
    public void Bind(object modelKey, int count, Func<int, string?> textAt)
    {
        if (!Equals(_modelKey, modelKey))
        {
            _modelKey = modelKey;
            Clear();
        }
        Count = count;
        _textAt = textAt;
    }

    /// <summary>True when the group holds a range covering at least one character.</summary>
    public bool HasSelection =>
        _anchorIndex >= 0 && _focusIndex >= 0 &&
        !(_anchorIndex == _focusIndex && _anchorOffset == _focusOffset);

    /// <summary>Bumped whenever the selection changes, so a caller can tell it moved.</summary>
    public int Version { get; private set; }

    /// <summary>Starts a selection at a line and offset, discarding any previous one.</summary>
    public void Anchor(int index, int offset)
    {
        _anchorIndex = _focusIndex = index;
        _anchorOffset = _focusOffset = offset;
        Version++;
    }

    /// <summary>Moves the loose end of the selection. The anchor stays put.</summary>
    public void ExtendTo(int index, int offset)
    {
        if (_anchorIndex < 0) { Anchor(index, offset); return; }
        if (_focusIndex == index && _focusOffset == offset) return;
        _focusIndex = index;
        _focusOffset = offset;
        Version++;
    }

    /// <summary>Selects every line in the model.</summary>
    public void SelectAll()
    {
        if (Count <= 0) return;
        _anchorIndex = 0; _anchorOffset = 0;
        _focusIndex = Count - 1;
        _focusOffset = (_textAt?.Invoke(Count - 1))?.Length ?? 0;
        Version++;
    }

    /// <summary>Drops the selection.</summary>
    public void Clear()
    {
        if (_anchorIndex < 0 && _focusIndex < 0) return;
        _anchorIndex = _focusIndex = -1;
        _anchorOffset = _focusOffset = 0;
        Version++;
    }

    // Anchor and focus in document order — the drag may have run upwards.
    private (int Index, int Offset) Low =>
        _anchorIndex < _focusIndex || (_anchorIndex == _focusIndex && _anchorOffset <= _focusOffset)
            ? (_anchorIndex, _anchorOffset)
            : (_focusIndex, _focusOffset);

    private (int Index, int Offset) High =>
        _anchorIndex < _focusIndex || (_anchorIndex == _focusIndex && _anchorOffset <= _focusOffset)
            ? (_focusIndex, _focusOffset)
            : (_anchorIndex, _anchorOffset);

    /// <summary>
    /// The range a label at <paramref name="index"/> should highlight, given how long its
    /// own text is. <c>(0, 0)</c> means "nothing selected here" — which is the answer for
    /// every line outside the span, so a label can call this unconditionally.
    /// </summary>
    public (int Anchor, int Caret) RangeFor(int index, int contentLength)
    {
        if (!HasSelection || index < 0) return (0, 0);
        var (loIdx, loOff) = Low;
        var (hiIdx, hiOff) = High;
        if (index < loIdx || index > hiIdx) return (0, 0);
        if (_textAt?.Invoke(index) is null && _textAt is not null) return (0, 0);

        int Clamp(int o) => Math.Clamp(o, 0, contentLength);
        if (loIdx == hiIdx) return (Clamp(loOff), Clamp(hiOff));
        if (index == loIdx) return (Clamp(loOff), contentLength);
        if (index == hiIdx) return (0, Clamp(hiOff));
        return (0, contentLength);
    }

    /// <summary>
    /// The selected text, read from the model — so it covers the whole span whether or not
    /// the lines are currently realized. Lines are joined by newline; a null line is skipped.
    /// </summary>
    public string SelectedText
    {
        get
        {
            if (!HasSelection || _textAt is null) return "";
            var (loIdx, loOff) = Low;
            var (hiIdx, hiOff) = High;

            var parts = new List<string>();
            for (int i = Math.Max(0, loIdx); i <= Math.Min(hiIdx, Count - 1); i++)
            {
                if (_textAt(i) is not { } line) continue;   // no line here — not a blank one
                int from = i == loIdx ? Math.Clamp(loOff, 0, line.Length) : 0;
                int to = i == hiIdx ? Math.Clamp(hiOff, 0, line.Length) : line.Length;
                if (to < from) (from, to) = (to, from);
                parts.Add(line[from..to]);
            }
            return string.Join("\n", parts);
        }
    }
}
