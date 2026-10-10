using System.Buffers;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Core.Layout;

public sealed partial class LayoutEngine
{
    /// <summary>
    /// Grid container counterpart to <see cref="LayoutFlexChildren"/>. Resolves
    /// column tracks first, then row tracks (since row heights depend on the
    /// widths cells receive), then positions each cell. Phase 6 calls
    /// <see cref="ComputeLayout"/> exactly once per cell — same single-authoritative-pass
    /// contract as the flex path.
    /// </summary>
    private Size LayoutGridChildren(Element container, IReadOnlyList<Element> children,
        float contentAvailW, float contentAvailH, float contentAbsX, float contentAbsY)
    {
        var grid = container.GridRead;

        // ── Determine track counts (declared + implicit from cell positions) ──
        int maxRow = -1, maxCol = -1;
        int flowCount = 0;
        for (int i = 0; i < children.Count; i++)
        {
            var c = children[i];
            if (c.PositionRead.Type == PositionType.Absolute) continue;
            flowCount++;
            if (c.GridItemRead.Row > maxRow) maxRow = c.GridItemRead.Row;
            if (c.GridItemRead.Column > maxCol) maxCol = c.GridItemRead.Column;
        }

        int colCount = Math.Max(grid.ColumnTemplate.Length, maxCol + 1);
        int rowCount = Math.Max(grid.RowTemplate.Length, maxRow + 1);
        if (colCount == 0 || rowCount == 0 || flowCount == 0)
            return Size.Zero;

        var colTracks = ArrayPool<GridTrack>.Shared.Rent(colCount);
        var rowTracks = ArrayPool<GridTrack>.Shared.Rent(rowCount);
        var colSizes = ArrayPool<float>.Shared.Rent(colCount);
        var rowSizes = ArrayPool<float>.Shared.Rent(rowCount);
        var colOrigins = ArrayPool<float>.Shared.Rent(colCount);
        var rowOrigins = ArrayPool<float>.Shared.Rent(rowCount);
        try
        {
            BuildEffectiveTracks(grid.ColumnTemplate, grid.ImplicitColumn, colTracks, colCount);
            BuildEffectiveTracks(grid.RowTemplate, grid.ImplicitRow, rowTracks, rowCount);

            // ── Resolve columns first; row resolution needs known column widths ──
            ResolveColumnTracks(colTracks, colCount, colSizes, contentAvailW, grid.ColumnGap, children, flowCount);
            ResolveRowTracks(rowTracks, rowCount, rowSizes, contentAvailH, grid.RowGap, children, flowCount, colSizes, colCount);

            // ── Compute cumulative track origins ──
            float colTotal = SumWithGaps(colSizes, colCount, grid.ColumnGap);
            float rowTotal = SumWithGaps(rowSizes, rowCount, grid.RowGap);
            TrackOrigins(colSizes, colCount, grid.ColumnGap, colOrigins);
            TrackOrigins(rowSizes, rowCount, grid.RowGap, rowOrigins);

            // ── Phase 6: position each cell ──
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child.PositionRead.Type == PositionType.Absolute) continue;

                int col = Math.Clamp(child.GridItemRead.Column, 0, colCount - 1);
                int row = Math.Clamp(child.GridItemRead.Row, 0, rowCount - 1);

                float cellX = contentAbsX + colOrigins[col];
                float cellY = contentAbsY + rowOrigins[row];
                float cellW = colSizes[col];
                float cellH = rowSizes[row];

                var justifySelf = ResolveJustifySelf(child.GridItemRead.JustifySelf, grid.JustifyItems);
                var alignSelf = ResolveAlignSelfFromGrid(child.GridItemRead.AlignSelf, grid.AlignItems);

                var childMargin = child.BoxRead.Margin;

                // Stretch = force border-box to (cell - margins). Otherwise pass null
                // so ComputeLayout uses the child's own size, then we offset to align.
                // A ratio box with its height given takes its width from it, not from the cell.
                float? forcedW = (justifySelf == JustifyItems.Stretch && child.BoxRead.Width.IsAuto
                        && !(RatioOf(child) > 0f && !child.BoxRead.Height.IsAuto))
                    ? cellW - childMargin.Horizontal : null;
                // A ratio box whose width is settled takes its height from it, at the top of
                // the cell, rather than being stretched down it as well (CSS: normal is start).
                bool widthSettled = forcedW.HasValue || !child.BoxRead.Width.IsAuto;
                float? forcedH = (alignSelf == AlignItems.Stretch && child.BoxRead.Height.IsAuto
                        && !(RatioOf(child) > 0f && widthSettled))
                    ? cellH - childMargin.Vertical : null;

                // Constraint = container content area, so percentages on the child
                // resolve against the container (matches flex behavior).
                var childConstraint = new LayoutConstraint(contentAvailW, contentAvailH);

                // Pre-compute offsets so ComputeLayout receives correct absolute positions
                // on cache misses (avoids OffsetDescendants for the common path).
                float prelimChildOuterW = forcedW.HasValue
                    ? forcedW.Value + childMargin.Horizontal
                    : MeasureSize(child, childConstraint).Width;
                float prelimChildOuterH = forcedH.HasValue
                    ? forcedH.Value + childMargin.Vertical
                    : MeasureSize(child, childConstraint, forcedW).Height;

                float xOffset = JustifyOffsetInCell(justifySelf, cellW, prelimChildOuterW);
                float yOffset = AlignOffsetInCell(alignSelf, cellH, prelimChildOuterH);

                float childAbsX = cellX + xOffset;
                float childAbsY = cellY + yOffset;

                ComputeLayout(child, childConstraint, childAbsX, childAbsY,
                    forcedWidth: forcedW, forcedHeight: forcedH);

                // Re-read the actual child outer size and finalize position
                float actualOuterW = child.Layout.Width + child.Layout.Margin.Horizontal;
                float actualOuterH = child.Layout.Height + child.Layout.Margin.Vertical;
                float finalXOffset = JustifyOffsetInCell(justifySelf, cellW, actualOuterW);
                float finalYOffset = AlignOffsetInCell(alignSelf, cellH, actualOuterH);

                float newAbsX = cellX + finalXOffset + child.Layout.Margin.Left;
                float newAbsY = cellY + finalYOffset + child.Layout.Margin.Top;
                float deltaX = newAbsX - child.Layout.AbsoluteX;
                float deltaY = newAbsY - child.Layout.AbsoluteY;

                child.Layout = child.Layout with
                {
                    X = colOrigins[col] + finalXOffset,
                    Y = rowOrigins[row] + finalYOffset,
                    AbsoluteX = newAbsX,
                    AbsoluteY = newAbsY,
                };

                if (deltaX != 0 || deltaY != 0)
                    OffsetDescendants(child, deltaX, deltaY);
            }

            return new Size(colTotal, rowTotal);
        }
        finally
        {
            ArrayPool<GridTrack>.Shared.Return(colTracks, clearArray: true);
            ArrayPool<GridTrack>.Shared.Return(rowTracks, clearArray: true);
            ArrayPool<float>.Shared.Return(colSizes);
            ArrayPool<float>.Shared.Return(rowSizes);
            ArrayPool<float>.Shared.Return(colOrigins);
            ArrayPool<float>.Shared.Return(rowOrigins);
        }
    }

    /// <summary>
    /// Measure-only mirror of <see cref="LayoutGridChildren"/>. Resolves track
    /// sizes (Phases 1–5) and returns the resulting content (border-box) size
    /// without writing <see cref="Element.Layout"/> on any descendant.
    /// </summary>
    private Size MeasureGridChildrenSize(Element container, IReadOnlyList<Element> children,
        float contentAvailW, float contentAvailH)
    {
        var grid = container.GridRead;

        int maxRow = -1, maxCol = -1;
        int flowCount = 0;
        for (int i = 0; i < children.Count; i++)
        {
            var c = children[i];
            if (c.PositionRead.Type == PositionType.Absolute) continue;
            flowCount++;
            if (c.GridItemRead.Row > maxRow) maxRow = c.GridItemRead.Row;
            if (c.GridItemRead.Column > maxCol) maxCol = c.GridItemRead.Column;
        }

        int colCount = Math.Max(grid.ColumnTemplate.Length, maxCol + 1);
        int rowCount = Math.Max(grid.RowTemplate.Length, maxRow + 1);
        if (colCount == 0 || rowCount == 0 || flowCount == 0)
            return Size.Zero;

        var colTracks = ArrayPool<GridTrack>.Shared.Rent(colCount);
        var rowTracks = ArrayPool<GridTrack>.Shared.Rent(rowCount);
        var colSizes = ArrayPool<float>.Shared.Rent(colCount);
        var rowSizes = ArrayPool<float>.Shared.Rent(rowCount);
        try
        {
            BuildEffectiveTracks(grid.ColumnTemplate, grid.ImplicitColumn, colTracks, colCount);
            BuildEffectiveTracks(grid.RowTemplate, grid.ImplicitRow, rowTracks, rowCount);

            ResolveColumnTracks(colTracks, colCount, colSizes, contentAvailW, grid.ColumnGap, children, flowCount);
            ResolveRowTracks(rowTracks, rowCount, rowSizes, contentAvailH, grid.RowGap, children, flowCount, colSizes, colCount);

            return new Size(
                SumWithGaps(colSizes, colCount, grid.ColumnGap),
                SumWithGaps(rowSizes, rowCount, grid.RowGap));
        }
        finally
        {
            ArrayPool<GridTrack>.Shared.Return(colTracks, clearArray: true);
            ArrayPool<GridTrack>.Shared.Return(rowTracks, clearArray: true);
            ArrayPool<float>.Shared.Return(colSizes);
            ArrayPool<float>.Shared.Return(rowSizes);
        }
    }

    // ── Track resolution helpers ──

    private static void BuildEffectiveTracks(GridTrack[] declared, GridTrack implicitTrack,
        GridTrack[] dest, int count)
    {
        for (int i = 0; i < count; i++)
            dest[i] = i < declared.Length ? declared[i] : implicitTrack;
    }

    /// <summary>
    /// Resolve column widths in three passes: Fixed/Percent → Auto (intrinsic
    /// measurement) → Fr (proportional distribution of remaining space).
    /// </summary>
    private void ResolveColumnTracks(GridTrack[] tracks, int count, float[] sizes,
        float available, float gap, IReadOnlyList<Element> children, int flowCount)
    {
        // Pass 1: Fixed and Percent
        for (int i = 0; i < count; i++)
        {
            sizes[i] = tracks[i].Kind switch
            {
                GridTrackKind.Fixed => tracks[i].Value,
                GridTrackKind.Percent => tracks[i].Value / 100f * available,
                _ => 0f,
            };
        }

        // Pass 2: Auto — measure cells in each Auto column
        if (AnyAuto(tracks, count))
        {
            var starts = ArrayPool<int>.Shared.Rent(count + 1);
            var order = ArrayPool<int>.Shared.Rent(flowCount);
            try
            {
                BucketByTrack(children, rows: false, count, starts, order);
                for (int col = 0; col < count; col++)
                {
                    if (!tracks[col].IsAuto) continue;
                    float maxW = 0f;
                    for (int k = starts[col]; k < starts[col + 1]; k++)
                    {
                        var child = children[order[k]];

                        // Measure unconstrained on the column axis so we get the cell's
                        // intrinsic width. Cross axis uses the container's available.
                        var measured = MeasureSize(child, new LayoutConstraint(available, available));
                        if (measured.Width > maxW) maxW = measured.Width;
                    }
                    sizes[col] = maxW;
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(starts);
                ArrayPool<int>.Shared.Return(order);
            }
        }

        // Pass 3: Fr — distribute remaining space
        DistributeFr(tracks, count, sizes, available, gap);
    }

    /// <summary>
    /// Resolve row heights given already-known column widths so each cell's
    /// height is measured against the width it will actually occupy.
    /// </summary>
    private void ResolveRowTracks(GridTrack[] tracks, int count, float[] sizes,
        float available, float gap, IReadOnlyList<Element> children, int flowCount,
        float[] colSizes, int colCount)
    {
        for (int i = 0; i < count; i++)
        {
            sizes[i] = tracks[i].Kind switch
            {
                GridTrackKind.Fixed => tracks[i].Value,
                GridTrackKind.Percent => tracks[i].Value / 100f * available,
                _ => 0f,
            };
        }

        if (AnyAuto(tracks, count))
        {
            var starts = ArrayPool<int>.Shared.Rent(count + 1);
            var order = ArrayPool<int>.Shared.Rent(flowCount);
            try
            {
                BucketByTrack(children, rows: true, count, starts, order);
                for (int row = 0; row < count; row++)
                {
                    if (!tracks[row].IsAuto) continue;
                    float maxH = 0f;
                    for (int k = starts[row]; k < starts[row + 1]; k++)
                    {
                        var child = children[order[k]];

                        int col = Math.Clamp(child.GridItemRead.Column, 0, colCount - 1);
                        float colW = colSizes[col];

                        // Force the child's border-box width to the column width minus
                        // its margins so text wrapping happens against the actual cell
                        // size — that's what determines the row height.
                        float childMargin = child.BoxRead.Margin.Horizontal;
                        float? forcedW = colW - childMargin;
                        if (forcedW < 0f) forcedW = 0f;

                        var measured = MeasureSize(child,
                            new LayoutConstraint(colW, available),
                            forcedWidth: forcedW);

                        if (measured.Height > maxH) maxH = measured.Height;
                    }
                    sizes[row] = maxH;
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(starts);
                ArrayPool<int>.Shared.Return(order);
            }
        }

        DistributeFr(tracks, count, sizes, available, gap);
    }

    private static bool AnyAuto(GridTrack[] tracks, int count)
    {
        for (int i = 0; i < count; i++)
            if (tracks[i].IsAuto) return true;
        return false;
    }

    /// <summary>
    /// The in-flow children grouped by the track they name on one axis, in one pass over them
    /// (a counting sort, stable): track t's cells are <c>children[order[k]]</c> for k from
    /// <c>starts[t]</c> to <c>starts[t + 1] - 1</c>, in child order. A cell naming a track
    /// outside 0..count-1 is in none, as the auto passes never matched it there (placement
    /// clamps it into the last track; measurement leaves it out). Each auto track used to look
    /// at every child, which made the passes O(tracks × cells): 1,080,000 visits a layout for
    /// a 300-row grid. <paramref name="starts"/> needs count + 1 slots, <paramref name="order"/>
    /// one per in-flow child.
    /// </summary>
    internal static void BucketByTrack(IReadOnlyList<Element> children, bool rows, int count,
        int[] starts, int[] order)
    {
        Array.Clear(starts, 0, count + 1);
        for (int i = 0; i < children.Count; i++)
        {
            var c = children[i];
            if (c.PositionRead.Type == PositionType.Absolute) continue;
            int t = rows ? c.GridItemRead.Row : c.GridItemRead.Column;
            if ((uint)t < (uint)count) starts[t + 1]++;
        }
        for (int t = 0; t < count; t++) starts[t + 1] += starts[t];

        // Fill with starts[t] as track t's cursor; it ends at track t's end, which is where
        // track t + 1 starts, so moving every slot up by one puts the starts back.
        for (int i = 0; i < children.Count; i++)
        {
            var c = children[i];
            if (c.PositionRead.Type == PositionType.Absolute) continue;
            int t = rows ? c.GridItemRead.Row : c.GridItemRead.Column;
            if ((uint)t < (uint)count) order[starts[t]++] = i;
        }
        for (int t = count; t > 0; t--) starts[t] = starts[t - 1];
        starts[0] = 0;
    }

    private static void DistributeFr(GridTrack[] tracks, int count, float[] sizes,
        float available, float gap)
    {
        float totalFr = 0f;
        float consumed = 0f;
        for (int i = 0; i < count; i++)
        {
            if (tracks[i].IsFr) totalFr += tracks[i].Value;
            else consumed += sizes[i];
        }
        if (count > 1) consumed += (count - 1) * gap;
        float remaining = available - consumed;
        if (remaining < 0f) remaining = 0f;

        if (totalFr > 0f)
        {
            for (int i = 0; i < count; i++)
            {
                if (tracks[i].IsFr)
                    sizes[i] = remaining * (tracks[i].Value / totalFr);
            }
        }
    }

    private static float SumWithGaps(float[] sizes, int count, float gap)
    {
        if (count == 0) return 0f;
        float total = 0f;
        for (int i = 0; i < count; i++) total += sizes[i];
        if (count > 1) total += (count - 1) * gap;
        return total;
    }

    /// <summary>
    /// Where each track starts: the sizes and gaps before it, added up from the first track in
    /// the order a per-track loop adds them (<c>origin += size + gap</c>), so every origin is the
    /// same float to the bit — in one pass for all tracks rather than one per cell, which summed
    /// ~900 tracks a cell in a 300-row grid.
    /// </summary>
    internal static void TrackOrigins(float[] sizes, int count, float gap, float[] origins)
    {
        if (count == 0) return;
        float origin = 0f;
        origins[0] = origin;
        for (int i = 1; i < count; i++)
        {
            origin += sizes[i - 1] + gap;
            origins[i] = origin;
        }
    }

    // ── Cell alignment helpers ──

    private static JustifyItems ResolveJustifySelf(JustifySelf self, JustifyItems containerDefault) =>
        self switch
        {
            JustifySelf.Auto => containerDefault,
            JustifySelf.Start => JustifyItems.Start,
            JustifySelf.End => JustifyItems.End,
            JustifySelf.Center => JustifyItems.Center,
            JustifySelf.Stretch => JustifyItems.Stretch,
            _ => containerDefault,
        };

    private static AlignItems ResolveAlignSelfFromGrid(AlignSelf self, AlignItems containerDefault)
    {
        if (self == AlignSelf.Auto) return containerDefault;
        // AlignSelf values are offset by 1 from AlignItems due to the Auto member
        return (AlignItems)((byte)self - 1);
    }

    private static float JustifyOffsetInCell(JustifyItems justify, float cellSize, float childOuter)
    {
        float free = cellSize - childOuter;
        if (free < 0f) free = 0f;
        return justify switch
        {
            JustifyItems.End => free,
            JustifyItems.Center => free / 2f,
            _ => 0f,
        };
    }

    private static float AlignOffsetInCell(AlignItems align, float cellSize, float childOuter)
    {
        float free = cellSize - childOuter;
        if (free < 0f) free = 0f;
        return align switch
        {
            AlignItems.FlexEnd => free,
            AlignItems.Center => free / 2f,
            _ => 0f,
        };
    }
}
