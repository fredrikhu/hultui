namespace HultUI.Core.Elements;

/// <summary>
/// The undo and redo stacks behind <see cref="TextInputElement"/> and <see cref="TextAreaElement"/>.
///
/// <para>Every edit either field makes is one shape — replace the range starting at
/// <c>Start</c> that held <c>Removed</c> with <c>Inserted</c> — so a step stores exactly that
/// and nothing else: typing, a paste over a selection, a Backspace and a cut are all the same
/// record with one side or the other empty. Storing the change rather than a snapshot of the
/// value keeps a long document from costing a copy of itself per step.</para>
///
/// <para>Steps group the way word processors group them, so one Ctrl+Z takes back what a person
/// thinks of as one action: a run of typing up to the start of the next word, a run of
/// Backspaces, a run of forward Deletes. A paste, a cut or deleting a selection is always a
/// step of its own, and so is an input-method commit — a CJK phrase arrives as one insert, and
/// merging phrases would make a paragraph of Chinese a single step. Anything that moves the
/// caret without editing ends the run (<see cref="Seal"/>), so typing, clicking elsewhere and
/// typing again is two steps even when the second insert happens to land where the first
/// ended.</para>
/// </summary>
internal sealed class TextUndoHistory
{
    /// <summary>How many steps are kept; the oldest is dropped past this.</summary>
    public const int MaxSteps = 200;

    public enum EditKind : byte
    {
        /// <summary>Characters typed at the caret, replacing the selection if there was one.</summary>
        Typing,
        /// <summary>Backspace with no selection: the range ends where the caret was.</summary>
        DeleteBackward,
        /// <summary>Delete with no selection: the range starts where the caret was.</summary>
        DeleteForward,
        /// <summary>Paste, cut, deleting a selection: never merged with a neighbour.</summary>
        Other,
    }

    private sealed class Step
    {
        public required EditKind Kind;
        public required int Start;
        public required string Removed;
        public required string Inserted;
        public required int CaretBefore;
        public required int AnchorBefore;
    }

    private readonly List<Step> _undo = [];
    private readonly List<Step> _redo = [];

    // Whether the newest undo step may still absorb the next edit.
    private bool _open;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Whether a run of typing ends where the next word starts. Off for a password,
    /// whose words an undo taking them back one at a time would count out.</summary>
    public bool BreaksAtWords { get; set; } = true;

    /// <summary>
    /// Records an edit about to be applied: the range at <paramref name="start"/> holding
    /// <paramref name="removed"/> becomes <paramref name="inserted"/>. The caret and anchor are
    /// the ones <i>before</i> the edit, which is the selection an undo puts back. A new edit
    /// discards whatever could have been redone.
    /// </summary>
    public void Record(EditKind kind, int start, string removed, string inserted, int caretBefore, int anchorBefore)
    {
        if (removed.Length == 0 && inserted.Length == 0) return;

        _redo.Clear();
        if (_open && _undo.Count > 0 && TryMerge(_undo[^1], kind, start, removed, inserted))
            return;

        if (_undo.Count == MaxSteps) _undo.RemoveAt(0);
        _undo.Add(new Step
        {
            Kind = kind, Start = start, Removed = removed, Inserted = inserted,
            CaretBefore = caretBefore, AnchorBefore = anchorBefore,
        });
        _open = kind switch
        {
            EditKind.Other => false,
            EditKind.Typing => !IsMoreThanOneCodePoint(inserted),
            _ => true,
        };
    }

    private bool TryMerge(Step top, EditKind kind, int start, string removed, string inserted)
    {
        if (kind != top.Kind) return false;

        switch (kind)
        {
            case EditKind.Typing:
                // Over a fresh selection, or anywhere but the end of the run: a new step.
                if (removed.Length != 0 || start != top.Start + top.Inserted.Length) return false;
                if (IsMoreThanOneCodePoint(inserted)) return false;
                // A word starts a new step, so "hello world" undoes as "world", then "hello ".
                if (BreaksAtWords && top.Inserted.Length > 0 && char.IsWhiteSpace(top.Inserted[^1]) && !char.IsWhiteSpace(inserted[0]))
                    return false;
                top.Inserted += inserted;
                return true;

            case EditKind.DeleteBackward:
                if (inserted.Length != 0 || start + removed.Length != top.Start) return false;
                top.Start = start;
                top.Removed = removed + top.Removed;
                return true;

            case EditKind.DeleteForward:
                if (inserted.Length != 0 || start != top.Start) return false;
                top.Removed += removed;
                return true;

            default:
                return false;
        }
    }

    /// <summary>One keystroke can type a surrogate pair; more than one code point is an input
    /// method committing a phrase, or a test typing a string at once.</summary>
    private static bool IsMoreThanOneCodePoint(string s) =>
        s.Length > 2 || (s.Length == 2 && !char.IsSurrogatePair(s[0], s[1]));

    /// <summary>Ends the current run: the next edit starts a step of its own. Called whenever the
    /// caret moves for any reason other than an edit.</summary>
    public void Seal() => _open = false;

    /// <summary>Forgets everything — for a value replaced from outside, which the recorded
    /// offsets no longer describe.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _open = false;
    }

    /// <summary>
    /// Reverts the newest step in <paramref name="text"/> and hands back the selection it was
    /// made from. False when there is nothing to undo — or when the text no longer holds what
    /// the step inserted, which means something changed the value without recording it: the
    /// history is dropped rather than applied to text it does not describe.
    /// </summary>
    public bool Undo(ref string text, out int caret, out int anchor)
    {
        caret = anchor = 0;
        if (_undo.Count == 0) return false;

        var step = _undo[^1];
        if (!Holds(text, step.Start, step.Inserted)) { Clear(); return false; }

        _undo.RemoveAt(_undo.Count - 1);
        text = string.Concat(text.AsSpan(0, step.Start), step.Removed, text.AsSpan(step.Start + step.Inserted.Length));
        _redo.Add(step);
        _open = false;
        caret = step.CaretBefore;
        anchor = step.AnchorBefore;
        return true;
    }

    /// <summary>Re-applies the newest undone step; the caret lands collapsed after what it
    /// inserted, where it was when the edit was first made.</summary>
    public bool Redo(ref string text, out int caret, out int anchor)
    {
        caret = anchor = 0;
        if (_redo.Count == 0) return false;

        var step = _redo[^1];
        if (!Holds(text, step.Start, step.Removed)) { Clear(); return false; }

        _redo.RemoveAt(_redo.Count - 1);
        text = string.Concat(text.AsSpan(0, step.Start), step.Inserted, text.AsSpan(step.Start + step.Removed.Length));
        _undo.Add(step);
        _open = false;
        caret = anchor = step.Start + step.Inserted.Length;
        return true;
    }

    private static bool Holds(string text, int start, string expected) =>
        start >= 0 && start + expected.Length <= text.Length
        && text.AsSpan(start, expected.Length).SequenceEqual(expected);
}
