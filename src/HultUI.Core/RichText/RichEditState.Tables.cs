using EditKind = HultUI.Core.RichText.RichUndoHistory.EditKind;

namespace HultUI.Core.RichText;

public sealed partial class RichEditState
{
    // ── Tables (docs/plans/16-rich-text-editor.md, stage 9) ──
    //
    // A table is a run of cell blocks, so typing, marks, links and undo work in a cell as in any
    // block. What is here is what a table adds: moving between cells, and changing its shape —
    // each a rewrite of the whole run, which keeps every cell numbered where it is.

    /// <summary>The table the caret is in; null outside one.</summary>
    public RichTableRange? CurrentTable => Document.TableAt(Selection.Focus.Block);

    // The table the cell at `block` is in, reusing `cached` while it still covers it.
    private RichTableRange TableOf(int block, ref RichTableRange? cached)
    {
        if (cached is not { } t || !t.Contains(block)) cached = Document.TableAt(block);
        return cached!.Value;
    }

    /// <summary>A table of <paramref name="rows"/> rows — the header among them — by
    /// <paramref name="columns"/> at the caret, on lines of its own, with the caret in its first
    /// cell. Not inside another table.</summary>
    public bool InsertTable(int rows = 3, int columns = 3)
    {
        if (!Features.Allows(RichTextFeatures.Tables)) return false;
        var cut = CutSelection();
        if (cut.Block.IsTableCell) return false;
        return InsertApart(cut, RichTable.Create(rows, columns), caretInside: true);
    }

    /// <summary>Enter in a table: the end of the cell below. From the last row, out of the table
    /// into the paragraph after it — the empty one there, or a new one — which is the way out of a
    /// table that ends the document.</summary>
    private bool CellBelow()
    {
        int at = Selection.Focus.Block;
        if (Document.TableAt(at) is not { } t) return false;
        int row = t.RowOf(at);
        if (row + 1 < t.Rows)
        {
            int below = t.IndexOf(row + 1, t.ColumnOf(at));
            Select(RichSelection.Caret(below, Document[below].Text.Length));
            return true;
        }
        if (t.End < Document.BlockCount && Document[t.End] is { Kind: BlockKind.Paragraph, Text.Length: 0 })
        {
            Select(RichSelection.Caret(t.End, 0));
            return true;
        }
        Commit(t.End, 0, [RichBlock.Paragraph()], RichSelection.Caret(t.End, 0), EditKind.Other);
        return true;
    }

    /// <summary>
    /// Tab in a table: the end of the next cell, or with <paramref name="backward"/> the one
    /// before. Tab in the last cell adds a row and goes to its first cell. False outside a table,
    /// and backwards from the first cell — where Shift+Tab leaves the editor.
    /// </summary>
    public bool NextCell(bool backward = false)
    {
        int at = Selection.Focus.Block;
        if (Document.TableAt(at) is not { } t) return false;
        if (backward)
        {
            if (at == t.Start) return false;
            Select(RichSelection.Caret(at - 1, Document[at - 1].Text.Length));
            return true;
        }
        if (at + 1 < t.End)
        {
            Select(RichSelection.Caret(at + 1, Document[at + 1].Text.Length));
            return true;
        }
        return Features.Allows(RichTextFeatures.Tables) && AddRow(t, t.Rows, caretColumn: 0);
    }

    /// <summary>A row under the caret's — over it when <paramref name="below"/> is false — and
    /// the caret into it, in the same column. A row over the header becomes the header.</summary>
    public bool InsertRow(bool below = true)
    {
        if (!Features.Allows(RichTextFeatures.Tables) || CurrentTable is not { } t) return false;
        int at = Selection.Focus.Block;
        return AddRow(t, below ? t.RowOf(at) + 1 : t.RowOf(at), t.ColumnOf(at));
    }

    /// <summary>A column after the caret's — before it when <paramref name="after"/> is false —
    /// and the caret into it, in the same row.</summary>
    public bool InsertColumn(bool after = true)
    {
        if (!Features.Allows(RichTextFeatures.Tables) || CurrentTable is not { } t) return false;
        int focus = Selection.Focus.Block;
        int at = after ? t.ColumnOf(focus) + 1 : t.ColumnOf(focus);
        return Rebuild(t, t.Rows, t.Columns + 1,
            (r, c) => c < at ? CellAt(t, r, c) : c == at ? null : CellAt(t, r, c - 1),
            t.RowOf(focus), at);
    }

    /// <summary>The caret's row; the table, when it is the only one. Deleting the header makes
    /// the row under it the header.</summary>
    public bool DeleteRow()
    {
        if (CurrentTable is not { } t) return false;
        if (t.Rows == 1) return DeleteTable();
        int focus = Selection.Focus.Block;
        int row = t.RowOf(focus);
        return Rebuild(t, t.Rows - 1, t.Columns, (r, c) => CellAt(t, r < row ? r : r + 1, c),
            Math.Min(row, t.Rows - 2), t.ColumnOf(focus));
    }

    /// <summary>The caret's column; the table, when it is the only one.</summary>
    public bool DeleteColumn()
    {
        if (CurrentTable is not { } t) return false;
        if (t.Columns == 1) return DeleteTable();
        int focus = Selection.Focus.Block;
        int column = t.ColumnOf(focus);
        return Rebuild(t, t.Rows, t.Columns - 1, (r, c) => CellAt(t, r, c < column ? c : c + 1),
            t.RowOf(focus), Math.Min(column, t.Columns - 2));
    }

    /// <summary>The table the caret is in, whole. The caret goes to the start of what followed
    /// it, or the end of what came before.</summary>
    public bool DeleteTable()
    {
        if (CurrentTable is not { } t) return false;
        var caret = t.End < Document.BlockCount
            ? RichSelection.Caret(t.Start, 0)
            : t.Start > 0 ? RichSelection.Caret(t.Start - 1, Document[t.Start - 1].Text.Length) : RichSelection.Caret(0, 0);
        Commit(t.Start, t.Count, [], caret, EditKind.Other);
        return true;
    }

    private RichBlock CellAt(RichTableRange t, int row, int column) => Document[t.IndexOf(row, column)];

    // A new row at index `at`, its cells aligned as their columns.
    private bool AddRow(RichTableRange t, int at, int caretColumn) =>
        Rebuild(t, t.Rows + 1, t.Columns,
            (r, c) => r < at ? CellAt(t, r, c)
                : r == at ? RichBlock.Cell(r, c) with { Align = CellAt(t, 0, c).Align }
                : CellAt(t, r - 1, c),
            at, caretColumn);

    // Table `t` replaced by `rows` × `columns` cells from `cellAt` (null: a new empty one), each
    // numbered where it lands, with the caret at the end of the cell at (caretRow, caretColumn).
    private bool Rebuild(RichTableRange t, int rows, int columns, Func<int, int, RichBlock?> cellAt,
        int caretRow, int caretColumn)
    {
        var cells = new List<RichBlock>(rows * columns);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                var cell = cellAt(r, c) ?? RichBlock.Cell(r, c);
                cells.Add(cell.Row == r && cell.Column == c ? cell : cell with { Row = r, Column = c });
            }
        int caret = Math.Clamp(caretRow, 0, rows - 1) * columns + Math.Clamp(caretColumn, 0, columns - 1);
        Commit(t.Start, t.Count, cells, RichSelection.Caret(t.Start + caret, cells[caret].Text.Length), EditKind.Other);
        return true;
    }
}
