using System.Collections.Immutable;

namespace HultUI.Core.RichText;

/// <summary>
/// Where a table sits in a document: <see cref="Rows"/> × <see cref="Columns"/> cell blocks from
/// <see cref="Start"/>, row by row.
/// </summary>
public readonly record struct RichTableRange(int Start, int Rows, int Columns)
{
    public int Count => Rows * Columns;

    /// <summary>The block after the table's last cell.</summary>
    public int End => Start + Count;

    public bool Contains(int block) => block >= Start && block < End;

    public int IndexOf(int row, int column) => Start + row * Columns + column;

    public int RowOf(int block) => (block - Start) / Columns;

    public int ColumnOf(int block) => (block - Start) % Columns;
}

/// <summary>
/// Tables in the flat block model (docs/plans/16-rich-text-editor.md, stage 9). A table is a run of
/// <see cref="BlockKind.TableCell"/> blocks in reading order; a cell at (0, 0) starts a new one, so
/// two tables can meet. Row 0 is the header. Every row has the same number of cells, and each
/// cell's <see cref="RichBlock.Row"/> and <see cref="RichBlock.Column"/> say where it is — what
/// <see cref="Normalize(RichDocument)"/> makes true of a run that arrives otherwise.
/// </summary>
public static class RichTable
{
    /// <summary>An empty table: a header row and <paramref name="rows"/> − 1 rows under it.</summary>
    public static ImmutableArray<RichBlock> Create(int rows, int columns)
    {
        rows = Math.Max(1, rows);
        columns = Math.Max(1, columns);
        var cells = ImmutableArray.CreateBuilder<RichBlock>(rows * columns);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
                cells.Add(RichBlock.Cell(r, c));
        return cells.MoveToImmutable();
    }

    /// <summary>Whether the block at <paramref name="index"/> is the first cell of a table.</summary>
    public static bool StartsTable(IReadOnlyList<RichBlock> blocks, int index) =>
        blocks[index].IsTableCell
        && (index == 0 || !blocks[index - 1].IsTableCell || (blocks[index].Row == 0 && blocks[index].Column == 0));

    /// <summary>The table the block at <paramref name="index"/> is a cell of; null outside one.
    /// A run that is not rectangular reads as the rows its first row's width makes of it —
    /// <see cref="Normalize(RichDocument)"/> it first.</summary>
    public static RichTableRange? At(IReadOnlyList<RichBlock> blocks, int index)
    {
        if (index < 0 || index >= blocks.Count || !blocks[index].IsTableCell) return null;
        int start = index;
        while (!StartsTable(blocks, start)) start--;
        int end = start + 1;
        while (end < blocks.Count && blocks[end].IsTableCell && !StartsTable(blocks, end)) end++;

        int columns = 1;
        while (start + columns < end && blocks[start + columns].Row == blocks[start].Row
               && blocks[start + columns].Column > blocks[start + columns - 1].Column)
            columns++;
        int rows = (end - start + columns - 1) / columns;
        return new RichTableRange(start, rows, columns);
    }

    /// <summary><paramref name="document"/> with every table rectangular and numbered; the same
    /// instance when they all are.</summary>
    public static RichDocument Normalize(RichDocument document)
    {
        var blocks = Normalize(document.Blocks);
        return blocks is null ? document : new RichDocument(blocks);
    }

    /// <summary>The blocks with every run of cells made a rectangular, numbered table: a new row
    /// where the numbering starts one, short rows filled with empty cells. Null when nothing
    /// needed it. A cell that is already right stays the same instance.</summary>
    public static List<RichBlock>? Normalize(IReadOnlyList<RichBlock> blocks)
    {
        List<RichBlock>? result = null;
        int i = 0;
        while (i < blocks.Count)
        {
            if (!blocks[i].IsTableCell)
            {
                result?.Add(blocks[i]);
                i++;
                continue;
            }
            int start = i, end = i + 1;
            while (end < blocks.Count && blocks[end].IsTableCell && !StartsTable(blocks, end)) end++;

            var rows = SplitRows(blocks, start, end);
            int columns = rows.Max(r => r.Count);
            bool normal = rows.All(r => r.Count == columns);
            for (int r = 0; normal && r < rows.Count; r++)
                for (int c = 0; normal && c < columns; c++)
                    normal = rows[r][c].Row == r && rows[r][c].Column == c;

            if (!normal && result is null)
            {
                result = new List<RichBlock>(blocks.Count);
                for (int k = 0; k < start; k++) result.Add(blocks[k]);
            }
            if (result is not null)
            {
                for (int r = 0; r < rows.Count; r++)
                    for (int c = 0; c < columns; c++)
                    {
                        var cell = c < rows[r].Count ? rows[r][c] : RichBlock.Cell(r, c) with { Align = rows[0].Count > c ? rows[0][c].Align : BlockAlign.Start };
                        result.Add(cell.Row == r && cell.Column == c ? cell : cell with { Row = r, Column = c });
                    }
            }
            i = end;
        }
        return result;
    }

    // A run of cells as rows: a new row wherever the cell's row number changes or its column
    // number does not go up.
    private static List<List<RichBlock>> SplitRows(IReadOnlyList<RichBlock> blocks, int start, int end)
    {
        var rows = new List<List<RichBlock>> { new() { blocks[start] } };
        for (int k = start + 1; k < end; k++)
        {
            var prev = blocks[k - 1];
            var cell = blocks[k];
            if (cell.Row != prev.Row || cell.Column <= prev.Column) rows.Add([]);
            rows[^1].Add(cell);
        }
        return rows;
    }
}
