using System.Buffers;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Core.Layout;

public sealed class LayoutEngine
{
    private readonly IMeasureContext _measureContext;

    public LayoutEngine(IMeasureContext measureContext)
    {
        _measureContext = measureContext;
    }

    public void PerformLayout(Element root, float availableWidth, float availableHeight, bool forceRootFill = true)
    {
        var constraint = new LayoutConstraint(availableWidth, availableHeight);
        // Only force size for auto-dimensioned roots when filling (e.g. window root).
        // Overlays should size to content instead.
        float? fw = forceRootFill && root.Box.Width.IsAuto ? availableWidth : null;
        float? fh = forceRootFill && root.Box.Height.IsAuto ? availableHeight : null;
        ComputeLayout(root, constraint, absoluteX: 0, absoluteY: 0, forcedWidth: fw, forcedHeight: fh);
        ClearLayoutDirty(root);
    }

    /// <param name="forcedWidth">If set, used instead of auto width (for root element or flex-resolved sizes)</param>
    /// <param name="forcedHeight">If set, used instead of auto height (for root element or flex-resolved sizes)</param>
    private Size ComputeLayout(Element element, LayoutConstraint constraint, float absoluteX, float absoluteY,
        float? forcedWidth = null, float? forcedHeight = null)
    {
        // Skip clean subtrees when constraints haven't changed
        if (!element.DirtyFlags.HasFlag(DirtyFlags.Layout)
            && element.CacheConstraintW == constraint.AvailableWidth
            && element.CacheConstraintH == constraint.AvailableHeight
            && element.CacheForcedW == forcedWidth
            && element.CacheForcedH == forcedHeight)
        {
            return element.CacheResultSize;
        }

        var box = element.Box;
        var padding = box.Padding;
        var border = box.BorderWidth;
        var margin = box.Margin;
        var outerSpacing = padding + border;

        // Resolve definite width/height. Forced size (from flex resolution) takes priority.
        var resolvedWidth = forcedWidth ?? box.Width.Resolve(constraint.AvailableWidth);
        var resolvedHeight = forcedHeight ?? box.Height.Resolve(constraint.AvailableHeight);

        // Content area available for children
        var contentAvailW = (resolvedWidth ?? constraint.AvailableWidth) - outerSpacing.Horizontal;
        var contentAvailH = (resolvedHeight ?? constraint.AvailableHeight) - outerSpacing.Vertical;
        contentAvailW = float.IsNegative(contentAvailW) ? 0 : contentAvailW;
        contentAvailH = float.IsNegative(contentAvailH) ? 0 : contentAvailH;

        // Count flow vs absolute children without allocating partition lists
        var children = element.Children;
        bool hasFlow = false;
        bool hasAbsolute = false;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i].Position.Type == PositionType.Absolute)
                hasAbsolute = true;
            else
                hasFlow = true;

            if (hasFlow && hasAbsolute)
                break;
        }

        // If leaf node, measure intrinsic content
        Size contentSize;
        if (!hasFlow)
        {
            contentSize = element.MeasureContent(contentAvailW, contentAvailH, _measureContext);
        }
        else
        {
            // Pass whether cross dimension is definite so single-line
            // containers with auto cross use natural content sizes
            bool hasDefiniteCross = (element.Flex.Direction is FlexDirection.Row or FlexDirection.RowReverse)
                ? resolvedHeight != null
                : resolvedWidth != null;

            contentSize = LayoutFlexChildren(element, children, contentAvailW, contentAvailH,
                contentAbsX: absoluteX + outerSpacing.Left + margin.Left,
                contentAbsY: absoluteY + outerSpacing.Top + margin.Top,
                hasDefiniteCross: hasDefiniteCross);
        }

        // Final element size
        var finalW = resolvedWidth ?? (contentSize.Width + outerSpacing.Horizontal);
        var finalH = resolvedHeight ?? (contentSize.Height + outerSpacing.Vertical);
        finalW = ApplyMinMax(finalW, box.MinWidth, box.MaxWidth, constraint.AvailableWidth);
        finalH = ApplyMinMax(finalH, box.MinHeight, box.MaxHeight, constraint.AvailableHeight);

        element.Layout = new LayoutResult(0, 0, finalW, finalH, padding, border, margin)
        {
            AbsoluteX = absoluteX + margin.Left,
            AbsoluteY = absoluteY + margin.Top
        };

        // Layout absolute children by scanning directly — no list needed
        if (hasAbsolute)
        {
            var contentBox = element.Layout.AbsoluteContentBox;
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i].Position.Type == PositionType.Absolute)
                    LayoutAbsoluteChild(children[i], contentBox);
            }
        }

        var resultSize = new Size(finalW + margin.Horizontal, finalH + margin.Vertical);

        // Update layout cache for subtree skipping on future passes
        element.CacheConstraintW = constraint.AvailableWidth;
        element.CacheConstraintH = constraint.AvailableHeight;
        element.CacheForcedW = forcedWidth;
        element.CacheForcedH = forcedHeight;
        element.CacheResultSize = resultSize;

        return resultSize;
    }

    private Size LayoutFlexChildren(Element container, IReadOnlyList<Element> children,
        float contentAvailW, float contentAvailH, float contentAbsX, float contentAbsY,
        bool hasDefiniteCross = true)
    {
        var flex = container.Flex;
        bool isRow = flex.Direction is FlexDirection.Row or FlexDirection.RowReverse;
        bool isReverse = flex.Direction is FlexDirection.RowReverse or FlexDirection.ColumnReverse;
        bool isWrap = flex.Wrap is not FlexWrap.NoWrap;
        bool isWrapReverse = flex.Wrap is FlexWrap.WrapReverse;

        float mainAvailable = isRow ? contentAvailW : contentAvailH;
        float crossAvailable = isRow ? contentAvailH : contentAvailW;
        float mainGap = isRow ? flex.ResolvedColumnGap : flex.ResolvedRowGap;
        float crossGap = isRow ? flex.ResolvedRowGap : flex.ResolvedColumnGap;

        // Scroll containers: unlimited main axis for flex distribution,
        // but keep original values as child constraints to avoid NaN overflow
        bool scrollOnMain = isRow
            ? container.Box.OverflowX == Overflow.Scroll
            : container.Box.OverflowY == Overflow.Scroll;
        float mainDistribute = scrollOnMain ? float.MaxValue / 2 : mainAvailable;

        // ── Phase 1: Measure all children to get hypothetical sizes ──
        // Use original mainAvailable (not unlimited) so children get proper constraints
        var (itemsArray, itemCount) = MeasureFlexItems(children, isRow, mainAvailable, crossAvailable);
        try
        {
        // ── Phase 2: Collect items into lines ──
        var lines = CollectLines(itemsArray, itemCount, mainDistribute, mainGap, isWrap);

        // ── Phase 3: Resolve flex grow/shrink per line ──
        foreach (var line in lines)
            ResolveFlexGrowShrink(line, mainDistribute, mainGap);

        // ── Phase 4: Compute per-line cross sizes ──
        ComputeLineCrossSizes(lines, crossAvailable, flex.AlignContent, isWrap, hasDefiniteCross);

        // ── Phase 5: Distribute lines along cross axis (AlignContent) ──
        float totalCrossSize = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            totalCrossSize += lines[i].CrossSize;
            if (i > 0) totalCrossSize += crossGap;
        }

        float crossOffset = ComputeAlignContentOffset(flex.AlignContent, crossAvailable, totalCrossSize, lines.Count);
        float alignContentGap = ComputeAlignContentGap(flex.AlignContent, crossAvailable, totalCrossSize, lines.Count, crossGap);

        // ── Phase 6: Position items within each line ──
        float actualTotalMain = 0;
        float actualTotalCross = 0;

        // Determine line iteration order for wrap-reverse
        for (int lineIdx = 0; lineIdx < lines.Count; lineIdx++)
        {
            int actualLineIdx = isWrapReverse ? lines.Count - 1 - lineIdx : lineIdx;
            var line = lines[actualLineIdx];

            // Compute line's total main size for justify
            float lineTotalMain = 0;
            for (int i = 0; i < line.Items.Count; i++)
            {
                lineTotalMain += line.Items[i].MainSize;
                if (i > 0) lineTotalMain += mainGap;
            }

            float mainOffset = ComputeJustifyOffset(flex.JustifyContent, mainDistribute, lineTotalMain, line.Items.Count);
            float justifyGap = ComputeJustifyGap(flex.JustifyContent, mainDistribute, lineTotalMain, line.Items.Count, mainGap);

            for (int i = 0; i < line.Items.Count; i++)
            {
                int idx = isReverse ? line.Items.Count - 1 - i : i;
                var item = line.Items[idx];
                var child = item.Element;

                var alignSelf = ResolveAlignSelf(child.Flex.AlignSelf, container.Flex.AlignItems);

                // Use container's content dimensions as constraint so percentage
                // dimensions resolve against the container, not the flex-resolved size
                var childConstraint = new LayoutConstraint(contentAvailW, contentAvailH);

                // Main axis: always force to flex-resolved size
                // Cross axis: only force stretch for auto-sized dimensions
                float? forcedW, forcedH;
                if (isRow)
                {
                    forcedW = item.MainSize;
                    forcedH = (alignSelf == AlignItems.Stretch && child.Box.Height.IsAuto)
                        ? (float?)line.CrossSize : null;
                }
                else
                {
                    forcedW = (alignSelf == AlignItems.Stretch && child.Box.Width.IsAuto)
                        ? (float?)line.CrossSize : null;
                    forcedH = item.MainSize;
                }

                // Pre-compute cross offset from hypothetical cross size so
                // ComputeLayout receives correct absolute positions on cache
                // misses, avoiding OffsetDescendants for the common case
                float prelimCrossOffset = ComputeAlignOffset(alignSelf, line.CrossSize, item.CrossSize);

                float childAbsX, childAbsY;
                if (isRow)
                {
                    childAbsX = contentAbsX + mainOffset;
                    childAbsY = contentAbsY + crossOffset + prelimCrossOffset;
                }
                else
                {
                    childAbsX = contentAbsX + crossOffset + prelimCrossOffset;
                    childAbsY = contentAbsY + mainOffset;
                }

                ComputeLayout(child, childConstraint, childAbsX, childAbsY, forcedWidth: forcedW, forcedHeight: forcedH);

                float childMainSize = isRow
                    ? child.Layout.Width + child.Layout.Margin.Horizontal
                    : child.Layout.Height + child.Layout.Margin.Vertical;
                float childCrossSize = isRow
                    ? child.Layout.Height + child.Layout.Margin.Vertical
                    : child.Layout.Width + child.Layout.Margin.Horizontal;

                float itemCrossOffset = ComputeAlignOffset(alignSelf, line.CrossSize, childCrossSize);

                float finalX, finalY;
                if (isRow)
                {
                    finalX = contentAbsX + mainOffset;
                    finalY = contentAbsY + crossOffset + itemCrossOffset;
                }
                else
                {
                    finalX = contentAbsX + crossOffset + itemCrossOffset;
                    finalY = contentAbsY + mainOffset;
                }

                float newAbsX = finalX + child.Layout.Margin.Left;
                float newAbsY = finalY + child.Layout.Margin.Top;
                float deltaX = newAbsX - child.Layout.AbsoluteX;
                float deltaY = newAbsY - child.Layout.AbsoluteY;

                child.Layout = child.Layout with
                {
                    X = isRow ? mainOffset : crossOffset + itemCrossOffset,
                    Y = isRow ? crossOffset + itemCrossOffset : mainOffset,
                    AbsoluteX = newAbsX,
                    AbsoluteY = newAbsY
                };

                if (deltaX != 0 || deltaY != 0)
                    OffsetDescendants(child, deltaX, deltaY);

                mainOffset += childMainSize + justifyGap;
            }

            float lineMainSize = mainOffset - justifyGap;
            actualTotalMain = Math.Max(actualTotalMain, lineMainSize);
            crossOffset += line.CrossSize + alignContentGap;
            actualTotalCross = crossOffset - alignContentGap;
        }

        return isRow
            ? new Size(actualTotalMain, actualTotalCross)
            : new Size(actualTotalCross, actualTotalMain);
        }
        finally
        {
            ArrayPool<FlexItem>.Shared.Return(itemsArray, clearArray: true);
        }
    }

    // ── Flex helpers ──

    private (FlexItem[] Array, int Count) MeasureFlexItems(IReadOnlyList<Element> children, bool isRow,
        float mainAvailable, float crossAvailable)
    {
        var array = ArrayPool<FlexItem>.Shared.Rent(Math.Max(1, children.Count));
        int count = 0;

        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child.Position.Type == PositionType.Absolute)
                continue;
            var childBox = child.Box;

            float? basisOrSize = child.Flex.FlexBasis.IsAuto
                ? (isRow ? childBox.Width.Resolve(mainAvailable) : childBox.Height.Resolve(mainAvailable))
                : child.Flex.FlexBasis.Resolve(mainAvailable);

            float hypotheticalMain;
            float hypotheticalCross;

            // Leaf elements: use lightweight measurement that skips LayoutResult
            // and cache updates, since Phase 6 will do the authoritative layout
            if (child.Children.Count == 0)
            {
                var constraintW = isRow ? mainAvailable : crossAvailable;
                var constraintH = isRow ? crossAvailable : mainAvailable;
                float? fw = basisOrSize.HasValue ? (isRow ? basisOrSize : null) : null;
                float? fh = basisOrSize.HasValue ? (isRow ? null : basisOrSize) : null;
                var measured = MeasureLeafSize(child, constraintW, constraintH, fw, fh);
                hypotheticalMain = basisOrSize ?? (isRow ? measured.Width : measured.Height);
                hypotheticalCross = isRow ? measured.Height : measured.Width;
            }
            else if (basisOrSize.HasValue)
            {
                hypotheticalMain = basisOrSize.Value;
                // Use container's available space as constraint so percentage
                // dimensions resolve against the container, not the basis
                var childConstraint = isRow
                    ? new LayoutConstraint(mainAvailable, crossAvailable)
                    : new LayoutConstraint(crossAvailable, mainAvailable);
                float? fw = isRow ? (float?)hypotheticalMain : null;
                float? fh = isRow ? null : (float?)hypotheticalMain;
                var measured = ComputeLayout(child, childConstraint, 0, 0, forcedWidth: fw, forcedHeight: fh);
                hypotheticalCross = isRow ? measured.Height : measured.Width;
            }
            else
            {
                var childConstraint = isRow
                    ? new LayoutConstraint(mainAvailable, crossAvailable)
                    : new LayoutConstraint(crossAvailable, mainAvailable);
                var measured = ComputeLayout(child, childConstraint, 0, 0);
                hypotheticalMain = isRow ? measured.Width : measured.Height;
                hypotheticalCross = isRow ? measured.Height : measured.Width;
            }

            array[count++] = new FlexItem(child, hypotheticalMain, hypotheticalCross);
        }

        return (array, count);
    }

    /// <summary>
    /// Computes the outer (margin-box) size of a leaf element without setting
    /// element.Layout or updating the layout cache. Used by Phase 1 measurement
    /// so that Phase 6 can be the single authoritative ComputeLayout call.
    /// </summary>
    private Size MeasureLeafSize(Element element, float constraintW, float constraintH,
        float? forcedWidth = null, float? forcedHeight = null)
    {
        var box = element.Box;
        var outerSpacing = box.Padding + box.BorderWidth;
        var margin = box.Margin;

        var resolvedW = forcedWidth ?? box.Width.Resolve(constraintW);
        var resolvedH = forcedHeight ?? box.Height.Resolve(constraintH);

        var contentAvailW = Math.Max(0, (resolvedW ?? constraintW) - outerSpacing.Horizontal);
        var contentAvailH = Math.Max(0, (resolvedH ?? constraintH) - outerSpacing.Vertical);

        var contentSize = element.MeasureContent(contentAvailW, contentAvailH, _measureContext);

        var finalW = resolvedW ?? (contentSize.Width + outerSpacing.Horizontal);
        var finalH = resolvedH ?? (contentSize.Height + outerSpacing.Vertical);
        finalW = ApplyMinMax(finalW, box.MinWidth, box.MaxWidth, constraintW);
        finalH = ApplyMinMax(finalH, box.MinHeight, box.MaxHeight, constraintH);

        return new Size(finalW + margin.Horizontal, finalH + margin.Vertical);
    }

    private static List<FlexLine> CollectLines(FlexItem[] itemsArray, int itemCount,
        float mainAvailable, float gap, bool isWrap)
    {
        var lines = new List<FlexLine>();

        if (!isWrap || itemCount == 0)
        {
            // Single line: slice directly into the rented array
            lines.Add(new FlexLine(new ArraySegment<FlexItem>(itemsArray, 0, itemCount)));
            return lines;
        }

        // Multi-line: break into lines when items overflow.
        // Each line is a contiguous segment of the same array — no per-line allocation.
        int lineStart = 0;
        int lineItemCount = 0;
        float currentLineMain = 0;

        for (int i = 0; i < itemCount; i++)
        {
            var item = itemsArray[i];
            float itemMainWithGap = item.MainSize + (lineItemCount > 0 ? gap : 0);

            if (lineItemCount > 0 && currentLineMain + itemMainWithGap > mainAvailable)
            {
                lines.Add(new FlexLine(new ArraySegment<FlexItem>(itemsArray, lineStart, lineItemCount)));
                lineStart = i;
                lineItemCount = 0;
                currentLineMain = 0;
                itemMainWithGap = item.MainSize;
            }

            lineItemCount++;
            currentLineMain += itemMainWithGap;
        }

        if (lineItemCount > 0)
            lines.Add(new FlexLine(new ArraySegment<FlexItem>(itemsArray, lineStart, lineItemCount)));

        return lines;
    }

    private static void ResolveFlexGrowShrink(FlexLine line, float mainAvailable, float gap)
    {
        var items = line.Items;
        float totalMainSize = 0;
        for (int i = 0; i < items.Count; i++)
        {
            totalMainSize += items[i].MainSize;
            if (i > 0) totalMainSize += gap;
        }

        float remainingSpace = mainAvailable - totalMainSize;

        if (remainingSpace > 0)
        {
            float totalGrow = 0;
            foreach (var item in items)
                totalGrow += item.Element.Flex.FlexGrow;

            if (totalGrow > 0)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    float grow = item.Element.Flex.FlexGrow;
                    if (grow > 0)
                    {
                        float extra = remainingSpace * (grow / totalGrow);
                        items[i] = item with { MainSize = item.MainSize + extra };
                    }
                }
            }
        }
        else if (remainingSpace < 0)
        {
            float totalShrink = 0;
            foreach (var item in items)
                totalShrink += item.Element.Flex.FlexShrink * item.MainSize;

            if (totalShrink > 0)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    float shrinkFactor = item.Element.Flex.FlexShrink * item.MainSize;
                    if (shrinkFactor > 0)
                    {
                        float reduction = -remainingSpace * (shrinkFactor / totalShrink);
                        items[i] = item with { MainSize = Math.Max(0, item.MainSize - reduction) };
                    }
                }
            }
        }
    }

    private static void ComputeLineCrossSizes(List<FlexLine> lines, float crossAvailable,
        AlignContent alignContent, bool isWrap, bool hasDefiniteCross)
    {
        // Each line's cross size is the max cross size of its items
        for (int i = 0; i < lines.Count; i++)
        {
            float maxCross = 0;
            foreach (var item in lines[i].Items)
                maxCross = Math.Max(maxCross, item.CrossSize);
            lines[i] = lines[i] with { CrossSize = maxCross };
        }

        // If single line (nowrap), the line takes the full cross available
        // when the container has a definite cross size. Per CSS spec, if the
        // cross size is indefinite, use the max of items' natural cross sizes.
        if (!isWrap && lines.Count == 1)
        {
            if (hasDefiniteCross)
                lines[0] = lines[0] with { CrossSize = crossAvailable };
            return;
        }

        // For AlignContent.Stretch with wrap, distribute remaining cross space
        if (alignContent == AlignContent.Stretch && lines.Count > 0)
        {
            float totalCross = 0;
            foreach (var line in lines)
                totalCross += line.CrossSize;

            float remaining = crossAvailable - totalCross;
            if (remaining > 0)
            {
                float extra = remaining / lines.Count;
                for (int i = 0; i < lines.Count; i++)
                    lines[i] = lines[i] with { CrossSize = lines[i].CrossSize + extra };
            }
        }
    }

    private void LayoutAbsoluteChild(Element child, Rect containerContentBox)
    {
        var pos = child.Position;
        var constraint = new LayoutConstraint(containerContentBox.Width, containerContentBox.Height);
        ComputeLayout(child, constraint, 0, 0);

        float x = containerContentBox.X;
        float y = containerContentBox.Y;

        if (pos.Left.Resolve(containerContentBox.Width) is float left)
            x = containerContentBox.X + left;
        else if (pos.Right.Resolve(containerContentBox.Width) is float right)
            x = containerContentBox.Right - child.Layout.Width - right;

        if (pos.Top.Resolve(containerContentBox.Height) is float top)
            y = containerContentBox.Y + top;
        else if (pos.Bottom.Resolve(containerContentBox.Height) is float bottom)
            y = containerContentBox.Bottom - child.Layout.Height - bottom;

        float deltaX = x - child.Layout.AbsoluteX;
        float deltaY = y - child.Layout.AbsoluteY;
        child.Layout = child.Layout with { AbsoluteX = x, AbsoluteY = y };

        if (deltaX != 0 || deltaY != 0)
            OffsetDescendants(child, deltaX, deltaY);
    }

    private static void OffsetDescendants(Element element, float dx, float dy)
    {
        foreach (var child in element.Children)
        {
            child.Layout = child.Layout with
            {
                AbsoluteX = child.Layout.AbsoluteX + dx,
                AbsoluteY = child.Layout.AbsoluteY + dy
            };
            OffsetDescendants(child, dx, dy);
        }
    }

    // ── Static helpers ──

    private static float ApplyMinMax(float value, Dimension min, Dimension max, float reference)
    {
        value = float.MaxNative(value, min.ResolveOr(reference, -1f));
        value = float.MinNative(value, max.ResolveOr(reference, float.MaxValue));
        return value;
    }

    private static float ComputeJustifyOffset(JustifyContent justify, float available, float used, int itemCount)
    {
        if (itemCount == 0) return 0;
        float free = Math.Max(0, available - used);

        return justify switch
        {
            JustifyContent.FlexEnd => free,
            JustifyContent.Center => free / 2,
            JustifyContent.SpaceAround when itemCount > 0 => free / itemCount / 2,
            JustifyContent.SpaceEvenly when itemCount > 0 => free / (itemCount + 1),
            _ => 0
        };
    }

    private static AlignItems ResolveAlignSelf(AlignSelf alignSelf, AlignItems containerAlignItems)
    {
        if (alignSelf == AlignSelf.Auto)
            return containerAlignItems;
        // AlignSelf values are offset by 1 from AlignItems due to the Auto member
        return (AlignItems)((byte)alignSelf - 1);
    }

    private static float ComputeJustifyGap(JustifyContent justify, float available, float used, int itemCount, float gap)
    {
        if (itemCount <= 1) return 0;
        float free = Math.Max(0, available - used);

        return justify switch
        {
            JustifyContent.SpaceBetween => free / (itemCount - 1) + gap,
            JustifyContent.SpaceAround => free / itemCount + gap,
            JustifyContent.SpaceEvenly => free / (itemCount + 1) + gap,
            _ => gap
        };
    }

    private static float ComputeAlignOffset(AlignItems align, float crossAvailable, float childCrossSize)
    {
        float free = Math.Max(0, crossAvailable - childCrossSize);
        return align switch
        {
            AlignItems.FlexEnd => free,
            AlignItems.Center => free / 2,
            _ => 0
        };
    }

    private static float ComputeAlignContentOffset(AlignContent align, float crossAvailable, float totalCross, int lineCount)
    {
        if (lineCount == 0) return 0;
        float free = Math.Max(0, crossAvailable - totalCross);

        return align switch
        {
            AlignContent.FlexEnd => free,
            AlignContent.Center => free / 2,
            AlignContent.SpaceAround when lineCount > 0 => free / lineCount / 2,
            _ => 0
        };
    }

    private static float ComputeAlignContentGap(AlignContent align, float crossAvailable, float totalCross, int lineCount, float gap)
    {
        if (lineCount <= 1) return 0;
        float free = Math.Max(0, crossAvailable - totalCross);

        return align switch
        {
            AlignContent.SpaceBetween => free / (lineCount - 1) + gap,
            AlignContent.SpaceAround => free / lineCount + gap,
            _ => gap
        };
    }

    private static void ClearLayoutDirty(Element element)
    {
        element.DirtyFlags &= ~DirtyFlags.Layout;
        foreach (var child in element.Children)
            ClearLayoutDirty(child);
    }
}

internal record struct FlexItem(Element Element, float MainSize, float CrossSize);

internal record struct FlexLine(ArraySegment<FlexItem> Items, float CrossSize = 0);
