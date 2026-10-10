using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Layout;

/// <summary>
/// A grid finds each auto track's cells in one pass over its children and sums its track origins
/// once a layout (plan 17, round 2). Before, each auto track scanned every child and each cell
/// summed the tracks before it: O(rows² × columns). The answers must be the old ones to the bit —
/// the same cells in each track, in child order, so the same maxima; the same additions in the
/// same order, so the same origins. These compare the new code with the old loops, kept here.
/// </summary>
public class GridTrackResolutionTests
{
    // ── The old loops ──

    /// <summary>The old auto passes' membership test: track t's cells were the in-flow children whose
    /// own index was t, visited in child order.</summary>
    private static List<int> OldCellsOf(IReadOnlyList<Element> children, bool rows, int t)
    {
        var cells = new List<int>();
        for (int i = 0; i < children.Count; i++)
        {
            var c = children[i];
            if (c.PositionRead.Type == PositionType.Absolute) continue;
            if ((rows ? c.GridItemRead.Row : c.GridItemRead.Column) != t) continue;
            cells.Add(i);
        }
        return cells;
    }

    /// <summary>The old per-cell origin: every track before it, size and gap, added from the first.</summary>
    private static float OldTrackOrigin(float[] sizes, int trackIndex, float gap)
    {
        float origin = 0f;
        for (int i = 0; i < trackIndex; i++)
            origin += sizes[i] + gap;
        return origin;
    }

    private static List<Element> RandomCells(Random rng, int count, int tracks)
    {
        var children = new List<Element>();
        for (int i = 0; i < count; i++)
        {
            // Mostly inside the tracks; some past the end, some negative (placed in the first
            // track, measured in none), some absolutely placed (in no track at all).
            int Index() => rng.Next(10) switch
            {
                0 => -1 - rng.Next(3),
                1 => tracks + rng.Next(3),
                _ => rng.Next(Math.Max(1, tracks)),
            };
            var cell = Box.New().GridCell(Index(), Index());
            if (rng.Next(8) == 0) cell.Position.Type = PositionType.Absolute;
            children.Add(cell);
        }
        return children;
    }

    [Fact]
    public void EachTrackHasTheCellsTheOldScanFoundInChildOrder()
    {
        for (int seed = 1; seed <= 2000; seed++)
        {
            var rng = new Random(seed);
            int count = rng.Next(1, 40);
            var children = RandomCells(rng, rng.Next(0, 120), count);
            int flow = children.Count(c => c.PositionRead.Type != PositionType.Absolute);

            foreach (bool rows in new[] { false, true })
            {
                // Rented arrays come back dirty: the bucketing must not rely on clean ones.
                var starts = Enumerable.Repeat(-7, count + 1 + rng.Next(5)).ToArray();
                var order = Enumerable.Repeat(-7, flow + rng.Next(5)).ToArray();
                LayoutEngine.BucketByTrack(children, rows, count, starts, order);

                for (int t = 0; t < count; t++)
                {
                    var expected = OldCellsOf(children, rows, t);
                    var actual = order[starts[t]..starts[t + 1]];
                    Assert.True(expected.SequenceEqual(actual),
                        $"seed {seed}, {(rows ? "row" : "column")} {t} of {count}: " +
                        $"[{string.Join(", ", expected)}] by the old scan, [{string.Join(", ", actual)}] bucketed");
                }
            }
        }
    }

    [Fact]
    public void EveryTrackOriginIsTheOldSumToTheBit()
    {
        for (int seed = 1; seed <= 2000; seed++)
        {
            var rng = new Random(seed);
            int count = rng.Next(0, 400);
            // Fractions that do not add up exactly in a float, wide and narrow tracks together,
            // empty tracks, and gaps of every kind: any reordering of the additions shows.
            var sizes = new float[count + rng.Next(3)];
            for (int i = 0; i < sizes.Length; i++)
                sizes[i] = rng.Next(6) switch
                {
                    0 => 0f,
                    1 => (float)rng.NextDouble() * 0.01f,
                    2 => (float)rng.NextDouble() * 10_000f,
                    _ => (float)Math.Round(rng.NextDouble() * 200, 2),
                };
            float gap = rng.Next(4) switch
            {
                0 => 0f,
                1 => rng.Next(12),
                _ => (float)rng.NextDouble() * 9f,
            };

            var origins = new float[Math.Max(1, count) + 2];
            Array.Fill(origins, float.NaN);
            LayoutEngine.TrackOrigins(sizes, count, gap, origins);

            for (int t = 0; t < count; t++)
            {
                float expected = OldTrackOrigin(sizes, t, gap);
                Assert.True(BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(origins[t]),
                    $"seed {seed}, track {t} of {count}, gap {gap:R}: {expected:R} the old way, {origins[t]:R} now");
            }
        }
    }

    /// <summary>A whole grid, laid out, against the old rules worked out here: cells of a set size
    /// (whose measurement is that size, exactly), in fixed, percentage, auto and fr tracks with
    /// fractional sizes and gaps, some past the template, before it or absolutely placed. A cell's
    /// box sits at its track origins, so a different maximum or a different sum shows in its X or Y.</summary>
    [Fact]
    public void ARandomGridPlacesItsCellsWhereTheOldTrackLoopsPutThem()
    {
        for (int seed = 1; seed <= 500; seed++)
        {
            var rng = new Random(seed);
            GridTrack Track() => rng.Next(5) switch
            {
                0 => GridTrack.Fixed((float)Math.Round(rng.NextDouble() * 120, 2)),
                1 => GridTrack.Percent((float)Math.Round(rng.NextDouble() * 30, 1)),
                2 => GridTrack.Fr(1 + rng.Next(3)),
                _ => GridTrack.Auto,
            };
            var columns = Enumerable.Range(0, rng.Next(1, 8)).Select(_ => Track()).ToArray();
            var rowTemplate = Enumerable.Range(0, rng.Next(0, 6)).Select(_ => Track()).ToArray();
            float gap = rng.Next(3) == 0 ? 0f : (float)Math.Round(rng.NextDouble() * 9, 3);

            var grid = Grid.Of(columns, rowTemplate, gap);
            grid.Grid.ColumnGap = (float)Math.Round(rng.NextDouble() * 9, 3);
            var children = RandomCells(rng, rng.Next(1, 80), Math.Max(columns.Length, 3));
            foreach (var c in children)
                c.Width(Dimension.Px((float)Math.Round(1 + rng.NextDouble() * 90, 2)))
                 .Height(Dimension.Px((float)Math.Round(1 + rng.NextDouble() * 40, 2)));
            grid.Children(children.ToArray());

            const float W = 900, H = 700;
            new LayoutEngine(new StubMeasureContext()).PerformLayout(grid, W, H);

            // The old rules.
            int maxCol = -1, maxRow = -1;
            foreach (var c in children.Where(c => c.PositionRead.Type != PositionType.Absolute))
            {
                maxCol = Math.Max(maxCol, c.GridItemRead.Column);
                maxRow = Math.Max(maxRow, c.GridItemRead.Row);
            }
            int colCount = Math.Max(columns.Length, maxCol + 1), rowCount = Math.Max(rowTemplate.Length, maxRow + 1);
            if (colCount == 0 || rowCount == 0) continue;
            var colTracks = Enumerable.Range(0, colCount).Select(i => i < columns.Length ? columns[i] : GridTrack.Auto).ToArray();
            var rowTracks = Enumerable.Range(0, rowCount).Select(i => i < rowTemplate.Length ? rowTemplate[i] : GridTrack.Auto).ToArray();
            var colSizes = OldSizes(colTracks, W, grid.Grid.ColumnGap, t =>
                OldCellsOf(children, rows: false, t).Select(i => children[i].BoxRead.Width.Value));
            var rowSizes = OldSizes(rowTracks, H, grid.Grid.RowGap, t =>
                OldCellsOf(children, rows: true, t).Select(i => children[i].BoxRead.Height.Value));

            foreach (var c in children.Where(c => c.PositionRead.Type != PositionType.Absolute))
            {
                int col = Math.Clamp(c.GridItemRead.Column, 0, colCount - 1);
                int row = Math.Clamp(c.GridItemRead.Row, 0, rowCount - 1);
                float x = OldTrackOrigin(colSizes, col, grid.Grid.ColumnGap);
                float y = OldTrackOrigin(rowSizes, row, grid.Grid.RowGap);
                Assert.True(x.Equals(c.Layout.X) && y.Equals(c.Layout.Y),
                    $"seed {seed}, cell ({c.GridItemRead.Row}, {c.GridItemRead.Column}): at " +
                    $"({c.Layout.X:R}, {c.Layout.Y:R}), the old loops put it at ({x:R}, {y:R})");
            }
        }
    }

    /// <summary>The old track sizing: fixed, percentage of the room, auto the largest of its cells
    /// (from 0, the first of equals), then fr shares of what is left (unchanged code, copied).</summary>
    private static float[] OldSizes(GridTrack[] tracks, float available, float gap, Func<int, IEnumerable<float>> cells)
    {
        var sizes = new float[tracks.Length];
        for (int i = 0; i < tracks.Length; i++)
        {
            sizes[i] = tracks[i].Kind switch
            {
                GridTrackKind.Fixed => tracks[i].Value,
                GridTrackKind.Percent => tracks[i].Value / 100f * available,
                _ => 0f,
            };
            if (tracks[i].IsAuto)
            {
                float max = 0f;
                foreach (float s in cells(i))
                    if (s > max) max = s;
                sizes[i] = max;
            }
        }

        float totalFr = 0f, consumed = 0f;
        for (int i = 0; i < tracks.Length; i++)
        {
            if (tracks[i].IsFr) totalFr += tracks[i].Value;
            else consumed += sizes[i];
        }
        if (tracks.Length > 1) consumed += (tracks.Length - 1) * gap;
        float remaining = available - consumed;
        if (remaining < 0f) remaining = 0f;
        if (totalFr > 0f)
            for (int i = 0; i < tracks.Length; i++)
                if (tracks[i].IsFr) sizes[i] = remaining * (tracks[i].Value / totalFr);
        return sizes;
    }
}
