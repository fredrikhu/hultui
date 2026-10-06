using System.Collections.Immutable;

namespace HultUI.Core.RichText;

/// <summary>
/// The undo and redo stacks behind <see cref="RichEditState"/>, grouped the way
/// <c>TextUndoHistory</c> groups a plain field's: a run of typing up to the start of the next
/// word, a run of Backspaces, a run of Deletes; everything else — a paste, a format, Enter — a
/// step of its own; any caret move ends the run (<see cref="Seal"/>).
/// </summary>
/// <remarks>
/// Every edit replaces a run of blocks, so a step is that run before and after. Blocks are
/// immutable and shared, so a step costs the blocks it touched, not a copy of the document. A
/// step applies only to a document that still holds the very block instances it left behind;
/// anything else means the document changed without being recorded, and the history is dropped
/// rather than applied to blocks it does not describe.
/// </remarks>
internal sealed class RichUndoHistory
{
    public const int MaxSteps = 200;

    public enum EditKind : byte
    {
        /// <summary>Characters typed at a collapsed caret.</summary>
        Typing,
        /// <summary>Backspace with no selection.</summary>
        DeleteBackward,
        /// <summary>Delete with no selection.</summary>
        DeleteForward,
        /// <summary>Anything else: never merged with a neighbour.</summary>
        Other,
    }

    private sealed class Step
    {
        public required EditKind Kind;
        public required int Index;
        public required ImmutableArray<RichBlock> Before;
        public required ImmutableArray<RichBlock> After;
        public required RichSelection SelectionBefore;
        public required RichSelection SelectionAfter;
        public string Typed = "";
    }

    private readonly List<Step> _undo = [];
    private readonly List<Step> _redo = [];
    private bool _open;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Records that blocks <c>[index, index + before.Length)</c>, which held
    /// <paramref name="before"/>, now hold <paramref name="after"/>. <paramref name="typed"/> is
    /// the text a <see cref="EditKind.Typing"/> step inserted, which decides where a word ends.
    /// </summary>
    public void Record(EditKind kind, int index, ImmutableArray<RichBlock> before, ImmutableArray<RichBlock> after,
        RichSelection selectionBefore, RichSelection selectionAfter, string typed = "")
    {
        _redo.Clear();
        if (_open && _undo.Count > 0 && TryMerge(_undo[^1], kind, index, before, after, selectionBefore, selectionAfter, typed))
            return;

        if (_undo.Count == MaxSteps) _undo.RemoveAt(0);
        _undo.Add(new Step
        {
            Kind = kind, Index = index, Before = before, After = after,
            SelectionBefore = selectionBefore, SelectionAfter = selectionAfter, Typed = typed,
        });
        _open = kind switch
        {
            EditKind.Other => false,
            EditKind.Typing => !IsMoreThanOneCodePoint(typed),
            _ => true,
        };
    }

    private static bool TryMerge(Step top, EditKind kind, int index, ImmutableArray<RichBlock> before,
        ImmutableArray<RichBlock> after, RichSelection selectionBefore, RichSelection selectionAfter, string typed)
    {
        if (kind != top.Kind || kind == EditKind.Other) return false;
        // The edit must start from exactly what the step left: the same blocks, the same caret.
        if (index != top.Index || before.Length != top.After.Length || selectionBefore != top.SelectionAfter)
            return false;
        for (int i = 0; i < before.Length; i++)
            if (!ReferenceEquals(before[i], top.After[i])) return false;

        if (kind == EditKind.Typing)
        {
            if (IsMoreThanOneCodePoint(typed)) return false;
            // A word starts a new step, so "hello world" undoes as "world", then "hello ".
            if (top.Typed.Length > 0 && char.IsWhiteSpace(top.Typed[^1]) && typed.Length > 0 && !char.IsWhiteSpace(typed[0]))
                return false;
            top.Typed += typed;
        }

        top.After = after;
        top.SelectionAfter = selectionAfter;
        return true;
    }

    private static bool IsMoreThanOneCodePoint(string s) =>
        s.Length > 2 || (s.Length == 2 && !char.IsSurrogatePair(s[0], s[1]));

    /// <summary>Ends the current run: the next edit is a step of its own.</summary>
    public void Seal() => _open = false;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _open = false;
    }

    public bool Undo(ref RichDocument document, out RichSelection selection)
    {
        selection = default;
        if (_undo.Count == 0) return false;
        var step = _undo[^1];
        if (!Holds(document, step.Index, step.After)) { Clear(); return false; }

        _undo.RemoveAt(_undo.Count - 1);
        document = document.ReplaceBlocks(step.Index, step.After.Length, step.Before);
        _redo.Add(step);
        _open = false;
        selection = step.SelectionBefore;
        return true;
    }

    public bool Redo(ref RichDocument document, out RichSelection selection)
    {
        selection = default;
        if (_redo.Count == 0) return false;
        var step = _redo[^1];
        if (!Holds(document, step.Index, step.Before)) { Clear(); return false; }

        _redo.RemoveAt(_redo.Count - 1);
        document = document.ReplaceBlocks(step.Index, step.Before.Length, step.After);
        _undo.Add(step);
        _open = false;
        selection = step.SelectionAfter;
        return true;
    }

    private static bool Holds(RichDocument document, int index, ImmutableArray<RichBlock> blocks)
    {
        if (index < 0 || index + blocks.Length > document.BlockCount) return false;
        for (int i = 0; i < blocks.Length; i++)
            if (!ReferenceEquals(document.Blocks[index + i], blocks[i])) return false;
        return true;
    }
}
