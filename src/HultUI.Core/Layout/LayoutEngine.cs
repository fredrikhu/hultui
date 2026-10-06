using System.Buffers;
using HultUI.Core.DevTools;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Core.Layout;

public sealed partial class LayoutEngine
{
    private readonly IMeasureContext _measureContext;
    private readonly FrameTracer? _tracer;

    public LayoutEngine(IMeasureContext measureContext, FrameTracer? tracer = null)
    {
        _measureContext = measureContext;
        _tracer = tracer;
    }

    public void PerformLayout(Element root, float availableWidth, float availableHeight, bool forceRootFill = true)
    {
        var constraint = new LayoutConstraint(availableWidth, availableHeight);
        // Only force size for auto-dimensioned roots when filling (e.g. window root).
        // Overlays should size to content instead.
        float? fw = forceRootFill && root.BoxRead.Width.IsAuto ? availableWidth : null;
        float? fh = forceRootFill && root.BoxRead.Height.IsAuto ? availableHeight : null;
        _passDepth++;
        try
        {
            ComputeLayout(root, constraint, absoluteX: 0, absoluteY: 0, forcedWidth: fw, forcedHeight: fh);
        }
        finally
        {
            // Only the outermost pass drops the plans: one laid out from inside another (nothing
            // does today) would otherwise clear items the outer one is still reading.
            if (--_passDepth == 0) ClearPlans();
        }
        ClearLayoutDirty(root);
    }

    private int _passDepth;

    /// <param name="element">The subtree root to lay out.</param>
    /// <param name="constraint">The space available to it.</param>
    /// <param name="absoluteX">Window-space X of the content box this subtree sits in, threaded
    /// down so every element can record an absolute box without a second pass.</param>
    /// <param name="absoluteY">Window-space Y, as <paramref name="absoluteX"/>.</param>
    /// <param name="forcedWidth">If set, used instead of auto width (for root element or flex-resolved sizes)</param>
    /// <param name="forcedHeight">If set, used instead of auto height (for root element or flex-resolved sizes)</param>
    private Size ComputeLayout(Element element, LayoutConstraint constraint, float absoluteX, float absoluteY,
        float? forcedWidth = null, float? forcedHeight = null)
    {
        using var _trace = _tracer is null
            ? default
            : _tracer.Begin("Layout", element.GetType().Name, element.Key ?? element.Name);

        // Skip clean subtrees when constraints and forced sizes haven't changed.
        // On an axis whose size is set — forced, or given in pixels, with no percentage min or max —
        // the space offered goes nowhere: not into the box, its content room, or anything below.
        // A resize offers every row's fixed-width cell a new width, and it was laid out again.
        if ((element.DirtyFlags & DirtyFlags.Layout) == 0
            && (element.CacheConstraintW == constraint.AvailableWidth
                || SizeIsSet(forcedWidth, element.BoxRead.Width, element.BoxRead.MinWidth, element.BoxRead.MaxWidth))
            && (element.CacheConstraintH == constraint.AvailableHeight
                || SizeIsSet(forcedHeight, element.BoxRead.Height, element.BoxRead.MinHeight, element.BoxRead.MaxHeight))
            && ForcedDimMatches(element.CacheForcedW, forcedWidth)
            && ForcedDimMatches(element.CacheForcedH, forcedHeight)
            && WithinReach(element.Layout.AbsoluteX, element.Layout.AbsoluteY)
            && WithinReach(absoluteX, absoluteY))
        {
            return element.CacheResultSize;
        }

        var box = element.BoxRead;
        var padding = box.Padding;
        var border = box.BorderWidth;
        var margin = box.Margin;
        var outerSpacing = padding + border;

        // Count flow vs absolute children without allocating partition lists
        var children = element.Children;
        bool hasFlow = false;
        bool hasAbsolute = false;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i].PositionRead.Type == PositionType.Absolute)
                hasAbsolute = true;
            else
                hasFlow = true;

            if (hasFlow && hasAbsolute)
                break;
        }

        float finalW, finalH;
        if (!hasFlow && margin.Horizontal == 0f && margin.Vertical == 0f)
        {
            // A leaf's box is exactly what MeasureLeafSize works out, so it is taken from there —
            // from the cache the measure pass filled, at the same constraint, rather than measured
            // a second time. Only without a margin: the cache holds the margin box, and taking a
            // margin back off is not always exact in floating point.
            var outer = MeasureLeafSize(element, constraint.AvailableWidth, constraint.AvailableHeight,
                forcedWidth, forcedHeight);
            finalW = outer.Width;
            finalH = outer.Height;
        }
        else
        {
            // Resolve definite width/height (a forced size, from flex resolution, takes priority)
            // and the content area available for children — with min and max already applied,
            // and an aspect ratio's missing side filled in.
            var (resolvedWidth, contentAvailW, resolvedHeight, contentAvailH, ratio) = ResolveBox(element, forcedWidth, forcedHeight,
                constraint.AvailableWidth, constraint.AvailableHeight, outerSpacing);

            // If leaf node, measure intrinsic content
            Size contentSize;
            if (!hasFlow)
            {
                contentSize = element.MeasureContent(contentAvailW, contentAvailH, _measureContext);
            }
            else if (element.GridRead.HasTemplate)
            {
                contentSize = LayoutGridChildren(element, children, contentAvailW, contentAvailH,
                    contentAbsX: absoluteX + outerSpacing.Left + margin.Left,
                    contentAbsY: absoluteY + outerSpacing.Top + margin.Top);
            }
            else
            {
                var own = OwnSize(box, outerSpacing, constraint, resolvedWidth, contentAvailW, resolvedHeight, contentAvailH);

                contentSize = LayoutFlexChildren(element, children, contentAvailW, contentAvailH,
                    contentAbsX: absoluteX + outerSpacing.Left + margin.Left,
                    contentAbsY: absoluteY + outerSpacing.Top + margin.Top,
                    own.Width, own.Height);
            }

            // Final element size
            finalW = resolvedWidth ?? (contentSize.Width + outerSpacing.Horizontal);
            finalH = resolvedHeight ?? (contentSize.Height + outerSpacing.Vertical);
            finalW = ApplyMinMax(finalW, box.MinWidth, box.MaxWidth, constraint.AvailableWidth);
            if (ratio > 0f && resolvedWidth is null && resolvedHeight is null)
                finalH = finalW / ratio;
            finalH = ApplyMinMax(finalH, box.MinHeight, box.MaxHeight, constraint.AvailableHeight);
        }

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
                if (children[i].PositionRead.Type == PositionType.Absolute)
                    LayoutAbsoluteChild(children[i], contentBox);
            }
        }

        // How far a scroll view can scroll is a function of where its children ended up, so
        // this is the pass that knows it. It used to be the renderer's job, and the renderer
        // culls — a view clipped away by an ancestor was never painted, so it reported a
        // content size of zero, and the offset setters clamp against that: scrolling it did
        // nothing and said nothing. See ScrollViewElement.UpdateScrollExtent.
        if (element is ScrollViewElement scrollView)
            scrollView.UpdateScrollExtent();

        var resultSize = new Size(finalW + margin.Horizontal, finalH + margin.Vertical);

        // Update layout cache for subtree skipping on future passes
        element.CacheConstraintW = constraint.AvailableWidth;
        element.CacheConstraintH = constraint.AvailableHeight;
        element.CacheForcedW = forcedWidth;
        element.CacheForcedH = forcedHeight;
        element.CacheResultSize = resultSize;

        // Clear this element's layout dirty flag now (not at end of PerformLayout)
        // so the cache check can succeed on the next call within the same pass.
        // Critical: LayoutFlexChildren calls ComputeLayout twice for each child —
        // once in Phase 1 (measurement) and once in Phase 6 (positioning). Without
        // this, the dirty flag stays set during the first pass and the cache always
        // misses, causing every container to be fully laid out twice (and the work
        // to compound exponentially with tree depth).
        element.DirtyFlags &= ~DirtyFlags.Layout;

        return resultSize;
    }

    /// <summary>
    /// Whether a forced size matches the one the kept layout was made with: exactly. It used to
    /// accept a box laid out unforced and now forced to the size it came out at, as the same
    /// layout — it is not: unforced, the content is offered the room available; forced, the
    /// forced size. A child with a percentage height in a row that started stretching its items
    /// kept the share of the old room. (A patch used to mark every element it touched for layout,
    /// which hid it from rebuilt components.)
    /// </summary>
    private static bool ForcedDimMatches(float? cached, float? current) => cached == current;

    /// <summary>
    /// Whether a box kept from an earlier pass can be moved to a new place by the difference
    /// (OffsetDescendants), or has to be laid out where it now is. Past 2^24 a float has no
    /// fraction of a pixel left, and a box pushed past an unbounded edge sits near 1.7E+38: moved
    /// out there and back, its descendants came back where 1.7E+38 − 1.7E+38 put them, at 0, not
    /// where they were. Only a layout gone past the edge of unbounded space puts anything there.
    /// </summary>
    private static bool WithinReach(float x, float y) =>
        float.Abs(x) < FarthestKeptPosition && float.Abs(y) < FarthestKeptPosition;

    private const float FarthestKeptPosition = 1 << 24;

    /// <summary>Whether a box's size on one axis is the same whatever space it is offered there:
    /// forced, or given in pixels, and held by no percentage min or max (ResolveAxis).</summary>
    private static bool SizeIsSet(float? forced, Dimension size, Dimension min, Dimension max) =>
        (forced is not null || size.Unit == DimensionUnit.Pixels)
        && min.Unit != DimensionUnit.Percent && max.Unit != DimensionUnit.Percent;

    /// <summary>Lays out a flex container's children. <paramref name="ownWidth"/> and
    /// <paramref name="ownHeight"/> are its own content-box size, each as the range it settles
    /// in (<see cref="OwnSize"/>): lines are justified in the main one and, wrapped, aligned in
    /// the cross one.</summary>
    private Size LayoutFlexChildren(Element container, IReadOnlyList<Element> children,
        float contentAvailW, float contentAvailH, float contentAbsX, float contentAbsY,
        SizeRange ownWidth, SizeRange ownHeight)
    {
        var flex = container.FlexRead;
        bool isRow = flex.Direction is FlexDirection.Row or FlexDirection.RowReverse;
        var ownMain = isRow ? ownWidth : ownHeight;
        var ownCross = isRow ? ownHeight : ownWidth;
        bool isReverse = flex.Direction is FlexDirection.RowReverse or FlexDirection.ColumnReverse;
        bool isWrap = flex.Wrap is not FlexWrap.NoWrap;
        bool isWrapReverse = flex.Wrap is FlexWrap.WrapReverse;

        float mainAvailable = isRow ? contentAvailW : contentAvailH;
        float crossAvailable = isRow ? contentAvailH : contentAvailW;
        float mainGap = isRow ? flex.ResolvedColumnGap : flex.ResolvedRowGap;
        float crossGap = isRow ? flex.ResolvedRowGap : flex.ResolvedColumnGap;

        // Scroll containers, and anything already inside one's scrolling axis,
        // distribute against an indefinite main size: content overflows and
        // scrolls rather than being compressed to the viewport.
        bool scrollOnMain = (isRow
                ? container.BoxRead.OverflowX == Overflow.Scroll
                : container.BoxRead.OverflowY == Overflow.Scroll)
            || LayoutSpace.IsUnbounded(mainAvailable);
        float mainDistribute = scrollOnMain ? LayoutSpace.Unbounded : mainAvailable;

        // ── Phase 1: Measure all children to get hypothetical sizes ──
        // Children are measured against the distribution size, so the indefinite
        // main axis reaches nested containers too — otherwise a plain Flex.Column
        // inside a ScrollView would resolve itself against the viewport and shrink
        // its own children to fit. Percentages against it resolve to auto (see
        // Dimension.Resolve), so no absurd sizes leak out.
        //
        // Phases 1 to 3.5 and the baselines are usually done already: the measure pass ran them
        // for this container, at these inputs, and filed what they came to (TakePlan).
        FlexItem[]? itemsArray = null;
        List<FlexLine>? lines = null;
        try
        {
            if (!isWrap && TakePlan(container, contentAvailW, contentAvailH) is { } planned)
            {
                lines = _linePool.TryPop(out var pooled) ? pooled : new List<FlexLine>(1);
                lines.Add(new FlexLine(planned));
            }
            else
            {
                (itemsArray, int itemCount, bool anyBaseline, _, _) = MeasureFlexItems(children, isRow, mainDistribute, crossAvailable, flex.AlignItems);

                // ── Phase 2: Collect items into lines ──
                lines = CollectLines(itemsArray, itemCount, mainDistribute, mainGap, isWrap);

                // ── Phase 3: Resolve flex grow/shrink per line ──
                foreach (var line in lines)
                    ResolveFlexGrowShrink(line, mainDistribute, mainGap, isRow, crossAvailable);

                // ── Phase 3.5: Re-measure items whose main size flexing changed ──
                foreach (var line in lines)
                    RemeasureFlexedCrossSizes(line, isRow, mainDistribute, crossAvailable);

                if (anyBaseline) ResolveBaselines(container, lines, isRow, mainDistribute, crossAvailable);
            }

            // ── Phase 4: Compute per-line cross sizes ──
            ComputeLineCrossSizes(lines, ownCross, crossGap, flex.AlignContent, isWrap);

            // ── Phase 5: Distribute lines along cross axis (AlignContent) ──
            float totalCrossSize = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                totalCrossSize += lines[i].CrossSize;
                if (i > 0) totalCrossSize += crossGap;
            }

            float alignSpace = AlignContentSpace(totalCrossSize, ownCross, isWrap);
            float crossOffset = ComputeAlignContentOffset(flex.AlignContent, alignSpace, totalCrossSize, lines.Count);
            float alignContentGap = ComputeAlignContentGap(flex.AlignContent, alignSpace, totalCrossSize, lines.Count, crossGap);

            // ── Phase 6: Position items within each line ──
            float actualTotalMain = 0;
            float actualTotalCross = 0;
            float justifySpace = JustifySpace(lines, mainGap, ownMain);

            // Determine line iteration order for wrap-reverse
            for (int lineIdx = 0; lineIdx < lines.Count; lineIdx++)
            {
                int actualLineIdx = isWrapReverse ? lines.Count - 1 - lineIdx : lineIdx;
                var line = lines[actualLineIdx];

                float lineTotalMain = LineMain(line, mainGap);
                float mainOffset = ComputeJustifyOffset(flex.JustifyContent, justifySpace, lineTotalMain, line.Items.Count);
                float justifyGap = ComputeJustifyGap(flex.JustifyContent, justifySpace, lineTotalMain, line.Items.Count, mainGap);

                for (int i = 0; i < line.Items.Count; i++)
                {
                    int idx = isReverse ? line.Items.Count - 1 - i : i;
                    var item = line.Items[idx];
                    var child = item.Element;

                    var alignSelf = ResolveAlignSelf(child.FlexRead.AlignSelf, container.FlexRead.AlignItems);

                    // Use container's content dimensions as constraint so percentage
                    // dimensions resolve against the container, not the flex-resolved size.
                    // The main axis must use the same value Phase 1 measured against
                    // (mainDistribute, which is indefinite inside a scroll container):
                    // both ComputeLayout's and MeasureSize's caches are keyed on the
                    // constraint, so a mismatch here misses every cache entry Phase 1
                    // populated and lays the whole subtree out twice — the cascade the
                    // comments in Phase 1 and ComputeLayout warn about.
                    var childConstraint = isRow
                        ? new LayoutConstraint(mainDistribute, contentAvailH)
                        : new LayoutConstraint(contentAvailW, mainDistribute);

                    // Main axis: always force to flex-resolved size.
                    // Cross axis: only force stretch for auto-sized dimensions.
                    // item.MainSize and line.CrossSize are outer (margin-box) sizes,
                    // but ComputeLayout's forcedWidth/forcedHeight are border-box —
                    // subtract the child's margins to convert.
                    var childMargin = child.BoxRead.Margin;
                    float? forcedW, forcedH;
                    // A ratio box is stretched across, never down — its height follows its width —
                    // and across only a bounded width, the one Phase 1 measured it at, and only when
                    // its height is not given: then the height decides the width.
                    bool hasRatio = RatioOf(child) > 0f;
                    if (isRow)
                    {
                        forcedW = item.MainSize - childMargin.Horizontal;
                        forcedH = (alignSelf == AlignItems.Stretch && child.BoxRead.Height.IsAuto && !hasRatio)
                            ? (float?)(line.CrossSize - childMargin.Vertical) : null;
                    }
                    else
                    {
                        forcedW = (alignSelf == AlignItems.Stretch && child.BoxRead.Width.IsAuto
                                && (!hasRatio || (child.BoxRead.Height.IsAuto && !LayoutSpace.IsUnbounded(crossAvailable))))
                            ? (float?)(line.CrossSize - childMargin.Horizontal) : null;
                        forcedH = item.MainSize - childMargin.Vertical;
                    }

                    // Pre-compute cross offset from hypothetical cross size so
                    // ComputeLayout receives correct absolute positions on cache
                    // misses, avoiding OffsetDescendants for the common case
                    float prelimCrossOffset = ItemCrossOffset(alignSelf, line, item, item.CrossSize);

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

                    float itemCrossOffset = ItemCrossOffset(alignSelf, line, item, childCrossSize);

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
                actualTotalMain = float.MaxNative(actualTotalMain, lineMainSize);
                crossOffset += line.CrossSize + alignContentGap;
                actualTotalCross = crossOffset - alignContentGap;
            }

            return isRow
                ? new Size(actualTotalMain, actualTotalCross)
                : new Size(actualTotalCross, actualTotalMain);
        }
        finally
        {
            if (itemsArray is not null) ArrayPool<FlexItem>.Shared.Return(itemsArray, clearArray: true);
            if (lines is not null) ReturnLines(lines);
        }
    }

    // ── Flex helpers ──

    /// <returns>The items; whether any of them aligns by baseline in a row — noted here, in
    /// the loop that already visits every item, so a row that never asks for baseline alignment
    /// pays one comparison per item for it rather than a pass of its own; and the least height
    /// the children are offered (the cross size in a row, the main size in a column) at which
    /// all their measurements hold (see <see cref="MeasureCache"/>); and the widths they are
    /// offered (the main size in a row, the cross size in a column) at which all of them hold.</returns>
    private (FlexItem[] Array, int Count, bool AnyBaseline, float HeightFrom, WidthRange WidthFrom) MeasureFlexItems(
        IReadOnlyList<Element> children, bool isRow, float mainAvailable, float crossAvailable, AlignItems containerAlign)
    {
        var array = ArrayPool<FlexItem>.Shared.Rent(Math.Max(1, children.Count));
        int count = 0;
        bool anyBaseline = false;
        bool rowAlignsByBaseline = isRow && containerAlign == AlignItems.Baseline;
        float heightFrom = AnyHeight;
        var widthFrom = WidthRange.Any;

        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child.PositionRead.Type == PositionType.Absolute)
                continue;
            var childBox = child.BoxRead;
            var childFlex = child.FlexRead;
            if (isRow && !anyBaseline)
                anyBaseline = childFlex.AlignSelf == AlignSelf.Baseline
                    || (childFlex.AlignSelf == AlignSelf.Auto && rowAlignsByBaseline);

            float basisOrSize = childFlex.FlexBasis.IsAuto
                ? (isRow ? childBox.Width.ResolveOr(mainAvailable, -1f) : childBox.Height.ResolveOr(mainAvailable, -1f))
                : childFlex.FlexBasis.ResolveOr(mainAvailable, -1f);
            bool hasBasisOrSize = basisOrSize >= 0f;
            // In a column a percentage basis is a share of the height offered, resolved right here.
            if (!isRow && (childFlex.FlexBasis.IsAuto ? childBox.Height : childFlex.FlexBasis).Unit == DimensionUnit.Percent)
                heightFrom = ExactHeight;
            // And in a row, of the width.
            else if (isRow && (childFlex.FlexBasis.IsAuto ? childBox.Width : childFlex.FlexBasis).Unit == DimensionUnit.Percent)
                widthFrom = WidthRange.Both(widthFrom, WidthRange.At(mainAvailable));

            float hypotheticalMain;
            float hypotheticalCross;

            // A ratio box in a column is as wide as the column's room for it, as a block is, and
            // so as tall as that width makes it: measured at that width, or it would be measured
            // from its content — a box with none at no height — and Phase 6 would force that.
            // The line it is on is then that wide, so Phase 6 stretches it to the same width.
            float? stretchedWidth = !isRow && StretchesAcross(child, containerAlign, crossAvailable)
                ? float.MaxNative(0f, crossAvailable - childBox.Margin.Horizontal)
                : null;
            if (stretchedWidth is not null)
                widthFrom = WidthRange.Both(widthFrom, WidthRange.At(crossAvailable));

            // Both MainSize and CrossSize on FlexItem are the outer (margin-box)
            // size. MeasureLeafSize and ComputeLayout already return outer sizes,
            // so the code below just reads measured.Width/Height directly. The
            // explicit-basis case used to assign basisOrSize (border-box), which
            // caused flex distribution to under-count sibling margins and Phase 6
            // to double-count them when forcing the dimension.
            // Phase 1 must measure WITHOUT writing element.Layout or polluting
            // the layout cache. Phase 6 will then call ComputeLayout with the
            // flex-resolved size, which is the single authoritative layout pass.
            // Calling ComputeLayout here would write a cache entry keyed on
            // forcedWidth=null (or basis), which Phase 6's call with the resolved
            // size would always miss — causing the entire subtree to be laid out
            // twice at every container level (exponential cascade with depth).
            Size measured;
            if (child.Children.Count == 0)
            {
                var constraintW = isRow ? mainAvailable : crossAvailable;
                var constraintH = isRow ? crossAvailable : mainAvailable;
                float? fw = hasBasisOrSize && isRow ? basisOrSize : stretchedWidth;
                float? fh = hasBasisOrSize ? (isRow ? null : basisOrSize) : null;
                measured = MeasureLeafSize(child, constraintW, constraintH, fw, fh);
            }
            else if (hasBasisOrSize)
            {
                // Use container's available space as constraint so percentage
                // dimensions resolve against the container, not the basis
                var childConstraint = isRow
                    ? new LayoutConstraint(mainAvailable, crossAvailable)
                    : new LayoutConstraint(crossAvailable, mainAvailable);
                float? fw = isRow ? (float?)basisOrSize : stretchedWidth;
                float? fh = isRow ? null : (float?)basisOrSize;
                measured = MeasureSize(child, childConstraint, fw, fh);
            }
            else
            {
                var childConstraint = isRow
                    ? new LayoutConstraint(mainAvailable, crossAvailable)
                    : new LayoutConstraint(crossAvailable, mainAvailable);
                measured = MeasureSize(child, childConstraint, stretchedWidth);
            }
            heightFrom = HeightFromBoth(heightFrom, _measuredFromH);
            widthFrom = WidthRange.Both(widthFrom, _measuredW);

            hypotheticalMain = isRow ? measured.Width : measured.Height;
            hypotheticalCross = isRow ? measured.Height : measured.Width;

            // NOTE: CSS's automatic minimum size for flex items (min-height:auto,
            // which stops an item being compressed below its content) is not
            // implemented — see FlexShrinkTests for the case it would fix.
            // Approximating it with this hypothetical measurement is unsound: for
            // a container the measurement is its full content extent, not its
            // min-content size, so an ancestor row wrapping a scroll view would be
            // pinned to the entire scrollable height and defeat the viewport.
            // Implementing it properly needs a real min-content measurement pass.
            array[count++] = new FlexItem(child, hypotheticalMain, hypotheticalCross, hypotheticalMain);
        }

        return (array, count, anyBaseline, heightFrom, widthFrom);
    }

    /// <summary>
    /// Container counterpart to <see cref="MeasureLeafSize"/>. Returns the outer
    /// (margin-box) size of an element subtree without writing element.Layout or
    /// updating the layout cache. Recurses through children via
    /// <see cref="MeasureFlexChildrenSize"/>, which routes back into this method
    /// (and <see cref="MeasureLeafSize"/> for leaves), keeping the entire Phase 1
    /// measurement in a write-free subgraph.
    /// </summary>
    private Size MeasureSize(Element element, LayoutConstraint constraint,
        float? forcedWidth = null, float? forcedHeight = null)
    {
        // A leaf (nothing in flow; absolutely placed children don't count) is measured one way
        // wherever the measuring starts — a grid cell, a flex item, a baseline probe — so its
        // cache entries are shared, and keyed the way MeasureLeafSize keys them.
        var children = element.Children;
        bool hasFlow = false;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i].PositionRead.Type != PositionType.Absolute)
            {
                hasFlow = true;
                break;
            }
        }
        if (!hasFlow)
            return MeasureLeafSize(element, constraint.AvailableWidth, constraint.AvailableHeight, forcedWidth, forcedHeight);

        // Cache hit: each ancestor's Phase 1 re-measures this element with a
        // small set of alternating (constraint, forced) inputs, so results are
        // reusable both within a pass and across frames. The cache is cleared
        // by MarkLayoutDirty (which propagates up from any changed descendant),
        // so a hit here means the cached size is still authoritative — no need
        // to consult DirtyFlags.Layout (that flag belongs to ComputeLayout's
        // cache, which gates whether descendant positions need re-writing).
        if (element.MeasureCache.TryGet(constraint.AvailableWidth, constraint.AvailableHeight,
                forcedWidth, forcedHeight, out var cachedSize, out var cachedFrom, out var cachedW))
        {
            _measuredFromH = cachedFrom;
            _measuredW = cachedW;
            return cachedSize;
        }

        using var _trace = _tracer is null
            ? default
            : _tracer.Begin("Measure", element.GetType().Name, element.Key ?? element.Name);

        var box = element.BoxRead;
        var outerSpacing = box.Padding + box.BorderWidth;
        var margin = box.Margin;

        // The same resolution ComputeLayout makes, or the measured size is not the laid-out one.
        var (resolvedW, contentAvailW, resolvedH, contentAvailH, ratio) = ResolveBox(element, forcedWidth, forcedHeight,
            constraint.AvailableWidth, constraint.AvailableHeight, outerSpacing);

        Size contentSize;
        float contentHeightFrom;
        WidthRange contentWidthFrom;
        if (element.GridRead.HasTemplate)
        {
            contentSize = MeasureGridChildrenSize(element, children, contentAvailW, contentAvailH);
            contentHeightFrom = ExactHeight;   // not worked out for grids
            contentWidthFrom = WidthRange.At(contentAvailW);
        }
        else
        {
            bool isRow = element.FlexRead.Direction is FlexDirection.Row or FlexDirection.RowReverse;
            var own = OwnSize(box, outerSpacing, constraint, resolvedW, contentAvailW, resolvedH, contentAvailH);
            // Only a box sized from its content reads the widths its content holds for (WidthsOffered).
            bool widthFromContent = resolvedW is null
                && box.MinWidth.Unit != DimensionUnit.Percent && box.MaxWidth.Unit != DimensionUnit.Percent;
            contentSize = MeasureFlexChildrenSize(element, children, contentAvailW, contentAvailH,
                isRow ? own.Height : own.Width, widthFromContent, out contentHeightFrom, out contentWidthFrom);
        }

        var finalW = resolvedW ?? (contentSize.Width + outerSpacing.Horizontal);
        var finalH = resolvedH ?? (contentSize.Height + outerSpacing.Vertical);
        finalW = ApplyMinMax(finalW, box.MinWidth, box.MaxWidth, constraint.AvailableWidth);
        if (ratio > 0f && resolvedW is null && resolvedH is null)
            finalH = finalW / ratio;
        finalH = ApplyMinMax(finalH, box.MinHeight, box.MaxHeight, constraint.AvailableHeight);

        var resultSize = new Size(finalW + margin.Horizontal, finalH + margin.Vertical);

        // The heights it holds for. A percentage height, min or max reads the height offered: that
        // height only. A definite height — given, forced, or made from the width by a ratio —
        // offers the content the same room whatever this box is offered: any height. Otherwise
        // the content is offered this height less padding and border, so the content's floor plus
        // those is this box's (a ratio then makes the height from the content's width, which the
        // offered height does not touch).
        float heightFrom = HasPercentHeight(box) ? ExactHeight
            : resolvedH is not null ? AnyHeight
            : contentHeightFrom + outerSpacing.Vertical;
        var widthFrom = WidthsOffered(box, forcedWidth, resolvedW, contentWidthFrom, outerSpacing.Horizontal,
            constraint.AvailableWidth);
        element.MeasureCache.Store(constraint.AvailableWidth, constraint.AvailableHeight,
            forcedWidth, forcedHeight, resultSize, heightFrom, widthFrom);
        _measuredFromH = heightFrom;
        _measuredW = widthFrom;

        // Do NOT clear DirtyFlags.Layout here — it belongs to ComputeLayout,
        // which uses it to decide whether to re-lay out descendant positions.
        // Clearing it here would let Phase 6's cache hit on stale child
        // positions when the subtree was reconciled with new elements.

        return resultSize;
    }

    /// <summary>
    /// Measure-only mirror of <see cref="LayoutFlexChildren"/>. Runs Phases 1-4
    /// (measure items, collect lines, resolve grow/shrink, compute line cross
    /// sizes) and returns the resulting content (border-box) size. Skips Phase 6
    /// positioning entirely — descendants are not written. <paramref name="ownCross"/> is the
    /// container's own cross size (<see cref="OwnSize"/>), which the lines are held in or,
    /// with AlignContent.Stretch, grown to.
    /// </summary>
    /// <param name="widthsWanted">Whether the widths the answer holds for are read: only for a
    /// container sized from its content. One whose width is set holds at any width whatever its
    /// content does, and is laid out at the one content width it always has.</param>
    private Size MeasureFlexChildrenSize(Element container, IReadOnlyList<Element> children,
        float contentAvailW, float contentAvailH, SizeRange ownCross, bool widthsWanted,
        out float heightFrom, out WidthRange widthFrom)
    {
        var flex = container.FlexRead;
        bool isRow = flex.Direction is FlexDirection.Row or FlexDirection.RowReverse;
        bool isWrap = flex.Wrap is not FlexWrap.NoWrap;

        float mainAvailable = isRow ? contentAvailW : contentAvailH;
        float crossAvailable = isRow ? contentAvailH : contentAvailW;
        float mainGap = isRow ? flex.ResolvedColumnGap : flex.ResolvedRowGap;
        float crossGap = isRow ? flex.ResolvedRowGap : flex.ResolvedColumnGap;

        // Mirrors LayoutFlexChildren — see the commentary there.
        bool scrollOnMain = (isRow
                ? container.BoxRead.OverflowX == Overflow.Scroll
                : container.BoxRead.OverflowY == Overflow.Scroll)
            || LayoutSpace.IsUnbounded(mainAvailable);
        float mainDistribute = scrollOnMain ? LayoutSpace.Unbounded : mainAvailable;

        var (itemsArray, itemCount, anyBaseline, childrenFrom, childrenW) = MeasureFlexItems(children, isRow, mainDistribute, crossAvailable, flex.AlignItems);
        List<FlexLine>? lines = null;
        try
        {
            lines = CollectLines(itemsArray, itemCount, mainDistribute, mainGap, isWrap);

            foreach (var line in lines)
                ResolveFlexGrowShrink(line, mainDistribute, mainGap, isRow, crossAvailable);

            // The measured height of a row is the height its parent will force on it, so
            // this pass needs the same post-flex cross sizes the layout pass takes.
            foreach (var line in lines)
            {
                var (remeasuredFrom, remeasuredW) = RemeasureFlexedCrossSizes(line, isRow, mainDistribute, crossAvailable);
                childrenFrom = HeightFromBoth(childrenFrom, remeasuredFrom);
                childrenW = WidthRange.Both(childrenW, remeasuredW);
            }

            // Both ranges read the items' total and grow factors and the baselines: worked out once.
            bool baselinesExact = anyBaseline && !BaselinesFromTextOnly(container, lines);
            var totals = !isWrap && (widthsWanted || !isRow) ? LineTotals(lines[0], mainGap) : default;
            heightFrom = FlexHeightFrom(container, isRow, isWrap, baselinesExact, mainDistribute, childrenFrom, totals);
            widthFrom = widthsWanted
                ? FlexWidthFrom(container, isRow, isWrap, baselinesExact, mainDistribute, childrenW, totals)
                    .With(contentAvailW)
                : WidthRange.At(contentAvailW);

            // As the layout pass does, or a row baseline alignment makes taller would be
            // measured shorter than it lays out.
            if (anyBaseline) ResolveBaselines(container, lines, isRow, mainDistribute, crossAvailable);

            // For the layout pass, which would otherwise work all of the above out again.
            if (!isWrap) FilePlan(container, widthFrom, contentAvailH, heightFrom, itemsArray, itemCount);

            ComputeLineCrossSizes(lines, ownCross, crossGap, flex.AlignContent, isWrap);

            float totalMain = 0;
            float totalCross = 0;
            for (int li = 0; li < lines.Count; li++)
            {
                var line = lines[li];
                totalCross += line.CrossSize;
                if (li > 0) totalCross += crossGap;

                float lineMain = LineMain(line, mainGap);
                if (lineMain > totalMain) totalMain = lineMain;
            }

            return isRow
                ? new Size(totalMain, totalCross)
                : new Size(totalCross, totalMain);
        }
        finally
        {
            ArrayPool<FlexItem>.Shared.Return(itemsArray, clearArray: true);
            if (lines is not null) ReturnLines(lines);
        }
    }

    /// <summary>
    /// Computes the outer (margin-box) size of a leaf element without setting
    /// element.Layout or updating the layout cache. Used by Phase 1 measurement
    /// so that Phase 6 can be the single authoritative ComputeLayout call.
    /// </summary>
    private Size MeasureLeafSize(Element element, float constraintW, float constraintH,
        float? forcedWidth = null, float? forcedHeight = null)
    {
        // Memoize like MeasureSize: a leaf is measured many times per layout — once
        // per ancestor's Phase 1 measure and again in Phase 6 arrange, across every
        // nesting level — and for text those MeasureContent calls dominate the pass.
        // The cache is keyed on (constraint, forced) and invalidated by
        // MarkLayoutDirty (which resets MeasureCache up the changed subtree).
        // The offered height is in the key only when it can change the answer. Text measures at a
        // width, and a box with no percentage height resolves nothing against it; keyed on it, the
        // measure pass (which offers a label unbounded height) and the layout pass (which offers
        // it its line's) each measured every label again.
        var box = element.BoxRead;
        bool anyHeight = element.MeasuresAtWidthOnly && !HasPercentHeight(box);

        // For such a leaf a forced height changes nothing but the height: the width, and the
        // wrapping it decides, come from the width alone. So the stretched label of the layout
        // pass is the unforced one of the measure pass, with the height it was given held in its
        // min and max — unless a ratio ties its width to that height.
        if (anyHeight && forcedHeight is { } forcedH && box.AspectRatio <= 0f)
        {
            var natural = MeasureLeafSize(element, constraintW, constraintH, forcedWidth, null);
            return new Size(natural.Width,
                ApplyMinMax(forcedH, box.MinHeight, box.MaxHeight, constraintH) + box.Margin.Vertical);
        }

        if (element.MeasureCache.TryGet(constraintW, constraintH, forcedWidth, forcedHeight,
                out var cachedSize, out var cachedFrom, out var cachedW))
        {
            _measuredFromH = cachedFrom;
            _measuredW = cachedW;
            return cachedSize;
        }

        var outerSpacing = box.Padding + box.BorderWidth;
        var margin = box.Margin;

        // As in ComputeLayout: a max width is what text wraps at, so it has to be in the
        // width the text is measured in — not applied to a box measured on one long line.
        var (resolvedW, contentAvailW, resolvedH, contentAvailH, ratio) = ResolveBox(element, forcedWidth, forcedHeight,
            constraintW, constraintH, outerSpacing);

        var contentSize = element.MeasureContent(contentAvailW, contentAvailH, _measureContext, out float sameFromW);

        var finalW = resolvedW ?? (contentSize.Width + outerSpacing.Horizontal);
        var finalH = resolvedH ?? (contentSize.Height + outerSpacing.Vertical);
        finalW = ApplyMinMax(finalW, box.MinWidth, box.MaxWidth, constraintW);
        if (ratio > 0f && resolvedW is null && resolvedH is null)
            finalH = finalW / ratio;
        finalH = ApplyMinMax(finalH, box.MinHeight, box.MaxHeight, constraintH);

        var resultSize = new Size(finalW + margin.Horizontal, finalH + margin.Vertical);

        // Any height, as for a container (MeasureSize), when the box's own height is set — given,
        // forced, or made from its width by a ratio — or its content ignores the height
        // (IgnoresAvailableHeight), and no percentage reads the height offered: the answer is
        // then the same whatever it is. A picture of a set size in a
        // column held its column's measurement to the one height it was taken at, and the layout
        // pass, offering another, laid the column out again.
        float heightFrom = anyHeight
            || ((resolvedH is not null || element.IgnoresAvailableHeight) && !HasPercentHeight(box))
            ? AnyHeight : ExactHeight;
        var contentWidthFrom = float.IsNaN(sameFromW)
            ? WidthRange.At(contentAvailW)
            : new WidthRange(sameFromW, float.PositiveInfinity);
        var widthFrom = WidthsOffered(box, forcedWidth, resolvedW, contentWidthFrom, outerSpacing.Horizontal, constraintW);
        element.MeasureCache.Store(constraintW, constraintH, forcedWidth, forcedHeight, resultSize, heightFrom, widthFrom);
        _measuredFromH = heightFrom;
        _measuredW = widthFrom;

        return resultSize;
    }

    // ── The heights a measurement holds for (MeasureCache) ──
    //
    // Every measurement says, besides the size, the least height it could have been offered and
    // still come out the same — so the measure pass, which offers most boxes an unbounded height,
    // and the layout pass, which offers them their line's, share it. MeasureSize and
    // MeasureLeafSize leave it in _measuredFromH for the caller that just asked; the flex
    // measurement folds its children's into its own.

    /// <summary>A measurement that holds whatever height is offered.</summary>
    private const float AnyHeight = float.NegativeInfinity;

    /// <summary>A measurement that holds at the height it was offered and no other (NaN: no
    /// height is <c>&gt;=</c> it).</summary>
    private const float ExactHeight = float.NaN;

    /// <summary>What the last MeasureSize or MeasureLeafSize returned holds for.</summary>
    private float _measuredFromH;

    /// <summary>The widths offered that the last MeasureSize or MeasureLeafSize answer holds for.</summary>
    private WidthRange _measuredW;

    // ── The widths a measurement holds for (MeasureCache) ──
    //
    // A box whose own width is set — given in pixels, forced by its parent's flex resolution, or
    // made from a set height by a ratio — gives its content the same room whatever it is offered:
    // any width. One sized from its content gives the content the width offered, held in its min
    // and max, less padding and border; so it holds for the widths that come to a content width
    // the content holds at. Text holds from the width of its one line when it fits on one; a row
    // where its children hold and its items fit without growing; a column where its children
    // hold. A percentage width, min or max reads the width offered: that width only.

    /// <summary>The widths offered at which a box measured at <paramref name="offered"/> comes out
    /// the same, its content holding for <paramref name="content"/> (content widths).</summary>
    private static WidthRange WidthsOffered(BoxStyle box, float? forcedWidth, float? resolvedW,
        WidthRange content, float spacing, float offered)
    {
        if (box.MinWidth.Unit == DimensionUnit.Percent || box.MaxWidth.Unit == DimensionUnit.Percent)
            return WidthRange.At(offered);
        if (resolvedW is not null)
            return forcedWidth is not null || box.Width.Unit != DimensionUnit.Percent
                ? WidthRange.Any
                : WidthRange.At(offered);

        // The content is offered max(0, min(max(w, min), max) - spacing) of an offered w, which
        // never falls as w grows (ResolveAxis): the widths coming to a content width in the range
        // are a range too. The width measured at is put back in by the caller.
        float min = box.MinWidth.ResolveOr(0f, -1f);
        float max = box.MaxWidth.ResolveOr(0f, float.MaxValue);
        float narrowest = float.MaxNative(0f, float.MinNative(min, max) - spacing);
        float widest = float.MaxNative(0f, max - spacing);
        float lo = content.Lo <= narrowest ? float.NegativeInfinity : LeastOffered(content.Lo, spacing);
        float hi = content.Hi >= widest ? float.PositiveInfinity : MostOffered(content.Hi, spacing);
        return new WidthRange(lo, hi).With(offered);
    }

    // The ends of WidthsOffered: x + spacing, stepped in only where rounding makes it come back to
    // the wrong side of x — subtracting never runs backwards, so one end checked is every width
    // past it checked. Stepping in always cost the commonest case: a row item laid out at the
    // width it measured, with no padding, missed by one step. Rounding is half a step, so two
    // steps settle it; past four, the end is given up (an empty range: the width measured alone).
    private static float LeastOffered(float x, float spacing)
    {
        float w = x + spacing;
        for (int i = 0; w - spacing < x; i++)
        {
            if (i == 4) return float.PositiveInfinity;
            w = float.BitIncrement(w);
        }
        return w;
    }

    private static float MostOffered(float x, float spacing)
    {
        float w = x + spacing;
        for (int i = 0; w - spacing > x; i++)
        {
            if (i == 4) return float.NegativeInfinity;
            w = float.BitDecrement(w);
        }
        return w;
    }

    /// <summary>The content widths at which a flex container's measurement holds, its children
    /// holding for <paramref name="children"/>. A row offers its width to its children as their
    /// main size and distributes it: the answer holds where nothing grows and the items fit — at
    /// or above their total — and the children hold; one that scrolls sideways offers them
    /// unbounded room whatever it is offered. A column offers its width to its children across,
    /// and holds where they do, unless it shrinks them: their floors (MinContentHeight) are read
    /// at its width. Wrapping, and rows aligning anything but text on its baseline, hold at their
    /// own width only.</summary>
    /// <remarks>An empty range stands for "its own width only": the caller widens it to take in
    /// the width it was measured at.</remarks>
    private static WidthRange FlexWidthFrom(Element container, bool isRow, bool isWrap, bool baselinesExact,
        float mainDistribute, WidthRange children, FlexTotals totals)
    {
        var only = new WidthRange(float.PositiveInfinity, float.NegativeInfinity);
        if (isWrap || baselinesExact) return only;
        if (isRow && container.BoxRead.OverflowX == Overflow.Scroll) return WidthRange.Any;
        if (mainDistribute - totals.Total < 0f) return only;   // shrinks
        if (!isRow) return children;
        if (totals.Grow > 0f) return only;
        return WidthRange.Both(children, new WidthRange(totals.Total, float.PositiveInfinity));
    }

    /// <summary>A line's items' total main size, gaps in, and their grow factors.</summary>
    private readonly record struct FlexTotals(float Total, float Grow);

    /// <summary>The sum ResolveFlexGrowShrink compares with the space, in its order, so "it fits"
    /// is decided the same way for the ranges as there.</summary>
    private static FlexTotals LineTotals(FlexLine line, float mainGap)
    {
        var items = line.Items;
        float total = 0, grow = 0;
        for (int i = 0; i < items.Count; i++)
        {
            total += items[i].BaseMain;
            if (i > 0) total += mainGap;
            grow += items[i].Element.FlexRead.FlexGrow;
        }
        return new FlexTotals(total, grow);
    }

    /// <summary>Both measurements hold from the higher floor; one that holds at its own height
    /// only makes both so.</summary>
    private static float HeightFromBoth(float a, float b) =>
        float.IsNaN(a) || float.IsNaN(b) ? ExactHeight : float.MaxNative(a, b);

    /// <summary>
    /// The least content height at which a flex container's measurement holds. A row offers its
    /// height to its children as their cross size and does nothing else with it, so it holds
    /// where they all do. A column distributes it: the answer holds wherever nothing grows (no
    /// grow factor) and nothing shrinks (the items fit) — at or above its items' total — and the
    /// children, offered it as their main size, hold. A scrolling column distributes unbounded
    /// space whatever it is offered. Wrapping columns, and rows aligning anything but text on its
    /// baseline, are not worked out.
    /// </summary>
    /// <summary>Whether every item aligned by its baseline is a label: its baseline is its first
    /// line's, at its width, whatever height it is offered (MeasureFirstBaseline). A container's
    /// baseline comes from a layout of its own that is not worked out here.</summary>
    private static bool BaselinesFromTextOnly(Element container, List<FlexLine> lines)
    {
        var align = container.FlexRead.AlignItems;
        foreach (var line in lines)
            foreach (var item in line.Items)
            {
                var e = item.Element;
                if (ResolveAlignSelf(e.FlexRead.AlignSelf, align) != AlignItems.Baseline) continue;
                if (e.Children.Count != 0 || !e.MeasuresAtWidthOnly || HasPercentHeight(e.BoxRead)) return false;
            }
        return true;
    }

    private static float FlexHeightFrom(Element container, bool isRow, bool isWrap, bool baselinesExact,
        float mainDistribute, float childrenFrom, FlexTotals totals)
    {
        if (baselinesExact) return ExactHeight;
        if (isRow) return childrenFrom;
        if (container.BoxRead.OverflowY == Overflow.Scroll) return AnyHeight;
        if (isWrap) return ExactHeight;
        if (totals.Grow > 0f || mainDistribute - totals.Total < 0f) return ExactHeight;
        return HeightFromBoth(childrenFrom, totals.Total);
    }

    private static bool HasPercentHeight(BoxStyle box) =>
        box.Height.Unit == DimensionUnit.Percent
        || box.MinHeight.Unit == DimensionUnit.Percent
        || box.MaxHeight.Unit == DimensionUnit.Percent;

    // The line lists, reused: every flex pass of every container made one, a dozen thousand a
    // frame on a long list resizing. A list is held across the recursion into the children, so a
    // pool (one per engine, which runs on one thread), not a single scratch list.
    private readonly Stack<List<FlexLine>> _linePool = new();

    private void ReturnLines(List<FlexLine> lines)
    {
        lines.Clear();
        _linePool.Push(lines);
    }

    // ── Flex plans: the measure pass's flex resolution, for the layout pass ──
    //
    // The layout pass lays a container's children out with the flex resolution the measure pass
    // already worked out for it — the same children measured at the same sizes, grown and shrunk
    // the same, re-measured and given baselines the same — and ran all of it again: on a 2,000-row
    // resize, a sixth of the layout pass. So the measure pass files the items it resolved,
    // with the inputs they hold for, and the layout pass takes them when its inputs match:
    // the width and the height by the measurement's own ranges (MeasureCache). The lines'
    // cross sizes are left to the layout pass; they read the container's own size, which is
    // definite there and not always in the measure pass.
    //
    // Filed per PerformLayout and dropped at its end, so nothing outlives the pass the inputs
    // were current in. Wrapping containers are not filed: their lines are broken by sizes the
    // items no longer hold once flexed.

    private FlexPlan[] _plans = new FlexPlan[64];
    private int _planCount;
    private FlexItem[] _planItems = new FlexItem[256];
    private int _planItemCount;

    private struct FlexPlan
    {
        public Element Container;
        public WidthRange ContentW;
        public float ContentH, ValidFromH;
        public int Start, Count;
    }

    /// <summary>Files <paramref name="container"/>'s resolved items, worked out with
    /// <paramref name="contentH"/> of content height, which hold for content widths
    /// <paramref name="contentW"/> and heights from <paramref name="validFromH"/> (see
    /// <see cref="MeasureCache"/>).</summary>
    private void FilePlan(Element container, WidthRange contentW, float contentH, float validFromH,
        FlexItem[] items, int count)
    {
        // Grown by replacing, never in place: a line taken from a plan may still be reading the
        // array it was filed in.
        if (_planItemCount + count > _planItems.Length)
            Array.Resize(ref _planItems, Math.Max(_planItems.Length * 2, _planItemCount + count));
        Array.Copy(items, 0, _planItems, _planItemCount, count);

        if (_planCount == _plans.Length)
            Array.Resize(ref _plans, _plans.Length * 2);
        _plans[_planCount] = new FlexPlan
        {
            Container = container,
            ContentW = contentW,
            ContentH = contentH,
            ValidFromH = validFromH,
            Start = _planItemCount,
            Count = count,
        };
        container.FlexPlan = _planCount++;
        _planItemCount += count;
    }

    /// <summary>The items filed for <paramref name="container"/> in this pass, if they hold for
    /// this width and height; null when none were filed or they do not.</summary>
    private ArraySegment<FlexItem>? TakePlan(Element container, float contentW, float contentH)
    {
        // The index may be left over from an earlier pass, and point past the plans or at another
        // container's: the container is the check.
        int i = container.FlexPlan;
        if (i >= _planCount) return null;
        ref var plan = ref _plans[i];
        // `>=` against NaN is false: a plan good at its own height only matches it exactly.
        if (plan.Container != container || !plan.ContentW.Contains(contentW)
            || !(plan.ContentH == contentH || contentH >= plan.ValidFromH))
            return null;
        return new ArraySegment<FlexItem>(_planItems, plan.Start, plan.Count);
    }

    /// <summary>Drops the pass's plans, and with them the elements they hold.</summary>
    private void ClearPlans()
    {
        Array.Clear(_plans, 0, _planCount);
        Array.Clear(_planItems, 0, _planItemCount);
        _planCount = 0;
        _planItemCount = 0;
    }

    private List<FlexLine> CollectLines(FlexItem[] itemsArray, int itemCount,
        float mainAvailable, float gap, bool isWrap)
    {
        var lines = _linePool.TryPop(out var pooled) ? pooled : new List<FlexLine>(1);

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

    private void ResolveFlexGrowShrink(FlexLine line, float mainAvailable, float gap, bool isRow, float crossAvailable)
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
                totalGrow += item.Element.FlexRead.FlexGrow;

            if (totalGrow > 0)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    float grow = item.Element.FlexRead.FlexGrow;
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
            // Automatic minimum size (CSS min-width/height: auto): shrinking never
            // compresses an item below its content's min-content size. Floors are
            // computed lazily — only overflowing lines pay for them — and follow
            // css-flexbox-1 §9.7's shape: distribute, clamp violators to their
            // floor and freeze them, then redistribute the shortfall among the
            // rest. An item with no floor (floor 0) behaves as before.
            float[]? floors = null;
            for (int i = 0; i < items.Count; i++)
            {
                var el = items[i].Element;
                if (el.FlexRead.FlexShrink <= 0) continue;
                float floor = AutomaticMinMain(el, isRow, mainAvailable, crossAvailable);
                if (floor > 0)
                {
                    floors ??= new float[items.Count];
                    floors[i] = floor;
                }
            }

            Span<bool> frozen = items.Count <= 64 ? stackalloc bool[items.Count] : new bool[items.Count];
            float deficit = -remainingSpace;
            while (deficit > 0.01f)
            {
                float totalShrink = 0;
                for (int i = 0; i < items.Count; i++)
                    if (!frozen[i])
                        totalShrink += items[i].Element.FlexRead.FlexShrink * items[i].MainSize;
                if (totalShrink <= 0) break;

                bool anyFroze = false;
                float applied = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    if (frozen[i]) continue;
                    var item = items[i];
                    float shrinkFactor = item.Element.FlexRead.FlexShrink * item.MainSize;
                    if (shrinkFactor <= 0) continue;

                    float reduction = deficit * (shrinkFactor / totalShrink);
                    float floor = floors?[i] ?? 0f;
                    float shrunk = item.MainSize - reduction;
                    if (shrunk <= floor)
                    {
                        applied += item.MainSize - floor;
                        items[i] = item with { MainSize = floor };
                        frozen[i] = true;
                        anyFroze = true;
                    }
                    else
                    {
                        applied += reduction;
                        items[i] = item with { MainSize = shrunk };
                    }
                }

                deficit -= applied;
                if (!anyFroze) break;   // everything distributed without hitting a floor
            }
        }
    }

    /// <summary>
    /// The automatic minimum (margin-box) main size of a shrinking flex item —
    /// CSS <c>min-width/height: auto</c>. An explicit min on the axis wins; otherwise
    /// the content-based minimum, in which scroll containers contribute zero (the
    /// rule whose absence sank the Phase-1-approximation attempt — see
    /// docs/plans/13-min-content.md and the note in MeasureFlexItems).
    /// </summary>
    private float AutomaticMinMain(Element child, bool isRow, float mainAvailable, float crossAvailable)
    {
        var box = child.BoxRead;
        var margin = box.Margin;
        float marginMain = isRow ? margin.Horizontal : margin.Vertical;

        var explicitMin = isRow ? box.MinWidth : box.MinHeight;
        if (!explicitMin.IsNone)
            return (explicitMin.Resolve(mainAvailable) ?? 0f) + marginMain;

        float crossAvail = Math.Max(0f, crossAvailable - (isRow ? margin.Vertical : margin.Horizontal));
        float content = isRow ? MinContentWidth(child) : MinContentHeight(child, crossAvail);

        // A ratio box in a column keeps its shape: its width is set (given, or the column's),
        // and the height that width makes is its floor — CSS's transferred minimum.
        if (!isRow && RatioOf(child) is > 0f and var ratio && !LayoutSpace.IsUnbounded(crossAvail))
        {
            float width = box.Width.Resolve(crossAvail) ?? crossAvail;
            content = Math.Max(content, ApplyMinMax(width / ratio, box.MinHeight, box.MaxHeight, mainAvailable));
        }
        return content + marginMain;
    }

    /// <summary>
    /// Min-content height (border-box) of an element at a given available width: the
    /// height below which its content genuinely breaks. Scroll/hidden overflow on
    /// the axis contributes zero — a scroll container's whole purpose is to be
    /// smaller than its content. A definite height caps the answer (a box that says
    /// it is 40px tall needs at most 40px protected); an explicit min raises it.
    /// </summary>
    private float MinContentHeight(Element e, float availWidth)
    {
        var box = e.BoxRead;
        if (box.OverflowY != Overflow.Visible) return 0f;

        var spacing = box.Padding + box.BorderWidth;
        float inner = Math.Max(0f, availWidth - spacing.Horizontal);

        var children = e.Children;
        bool hasFlow = false;
        for (int i = 0; i < children.Count; i++)
            if (children[i].PositionRead.Type != PositionType.Absolute) { hasFlow = true; break; }

        float content;
        if (!hasFlow)
        {
            content = e.MeasureContent(inner, LayoutSpace.Unbounded, _measureContext).Height;
        }
        else
        {
            bool row = e.FlexRead.Direction is FlexDirection.Row or FlexDirection.RowReverse;
            content = 0f;
            int flowCount = 0;
            for (int i = 0; i < children.Count; i++)
            {
                var c = children[i];
                if (c.PositionRead.Type == PositionType.Absolute) continue;
                // For a row, each child is offered the full inner width — an
                // over-generous width can only shorten a child, so the max stays a
                // floor rather than an overestimate.
                float h = MinContentHeight(c, inner) + c.BoxRead.Margin.Vertical;
                if (row) content = Math.Max(content, h);
                else { content += h; flowCount++; }
            }
            if (!row && flowCount > 1) content += e.FlexRead.ResolvedRowGap * (flowCount - 1);
        }

        float result = content + spacing.Vertical;
        if (box.Height.Resolve(LayoutSpace.Unbounded) is { } definite) result = Math.Min(result, definite);
        if (box.MinHeight.Resolve(LayoutSpace.Unbounded) is { } explicitMin) result = Math.Max(result, explicitMin);
        return result;
    }

    /// <summary>
    /// Min-content width (border-box): the width below which an element's content
    /// genuinely breaks. Leaves answer for themselves — text with its widest
    /// unbreakable segment, everything else by measuring at zero available width.
    /// Scroll/hidden overflow on the axis contributes zero, as in
    /// <see cref="MinContentHeight"/>.
    /// </summary>
    private float MinContentWidth(Element e)
    {
        // Reads no constraint, so it is kept until the element or something in it changes.
        if (e.MeasureCache.TryGetMinContentWidth(out var known)) return known;
        float width = ComputeMinContentWidth(e);
        e.MeasureCache.StoreMinContentWidth(width);
        return width;
    }

    private float ComputeMinContentWidth(Element e)
    {
        var box = e.BoxRead;
        if (box.OverflowX != Overflow.Visible) return 0f;

        var spacing = box.Padding + box.BorderWidth;

        var children = e.Children;
        bool hasFlow = false;
        for (int i = 0; i < children.Count; i++)
            if (children[i].PositionRead.Type != PositionType.Absolute) { hasFlow = true; break; }

        float content;
        if (!hasFlow)
        {
            content = e.MeasureMinContentWidth(_measureContext);
        }
        else
        {
            bool row = e.FlexRead.Direction is FlexDirection.Row or FlexDirection.RowReverse;
            content = 0f;
            int flowCount = 0;
            for (int i = 0; i < children.Count; i++)
            {
                var c = children[i];
                if (c.PositionRead.Type == PositionType.Absolute) continue;
                float w = MinContentWidth(c) + c.BoxRead.Margin.Horizontal;
                if (row) { content += w; flowCount++; }
                else content = Math.Max(content, w);
            }
            if (row && flowCount > 1) content += e.FlexRead.ResolvedColumnGap * (flowCount - 1);
        }

        float result = content + spacing.Horizontal;
        if (box.Width.Resolve(LayoutSpace.Unbounded) is { } definite) result = Math.Min(result, definite);
        if (box.MinWidth.Resolve(LayoutSpace.Unbounded) is { } explicitMin) result = Math.Max(result, explicitMin);
        return result;
    }

    /// <summary>
    /// An item's cross size depends on the main size it ends up with, so it can only be
    /// known once flex grow/shrink has run. Phase 1 measured against the flex base size,
    /// which for <c>flex-basis: 0</c> means measuring a text cell at zero width — it wraps
    /// to one line per character, and that height is what the line, and so the whole
    /// container, would be sized to. CSS orders these the same way: the hypothetical cross
    /// size is determined by laying out with the <em>used</em> main size
    /// (css-flexbox-1 §9.4 step 7, after §9.7 resolves it).
    /// <para>Only items flexing actually moved are re-measured. Everything else already
    /// holds a measurement at the size it is going to be, and re-measuring the whole line
    /// would double Phase 1's cost for no change in the answer.</para>
    /// </summary>
    /// <returns>The least height the re-measured items are offered at which their measurements
    /// hold, and the widths (see <see cref="MeasureFlexItems"/>).</returns>
    private (float HeightFrom, WidthRange WidthFrom) RemeasureFlexedCrossSizes(FlexLine line, bool isRow,
        float mainAvailable, float crossAvailable)
    {
        float heightFrom = AnyHeight;
        var widthFrom = WidthRange.Any;
        var items = line.Items;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            // Sub-pixel drift from distributing free space isn't a re-measure.
            if (float.Abs(item.MainSize - item.BaseMain) < 0.01f) continue;

            var child = item.Element;
            var childBox = child.BoxRead;
            // A cross dimension that was given a value can't depend on the main size.
            if (!(isRow ? childBox.Height : childBox.Width).IsAuto) continue;

            // MainSize is the outer (margin-box) size; the forced dimension is border-box.
            var margin = childBox.Margin;
            float forced = item.MainSize - (isRow ? margin.Horizontal : margin.Vertical);
            if (float.IsNegative(forced)) forced = 0;
            float? fw = isRow ? forced : null;
            float? fh = isRow ? null : forced;

            float constraintW = isRow ? mainAvailable : crossAvailable;
            float constraintH = isRow ? crossAvailable : mainAvailable;
            var measured = child.Children.Count == 0
                ? MeasureLeafSize(child, constraintW, constraintH, fw, fh)
                : MeasureSize(child, new LayoutConstraint(constraintW, constraintH), fw, fh);
            heightFrom = HeightFromBoth(heightFrom, _measuredFromH);
            widthFrom = WidthRange.Both(widthFrom, _measuredW);

            items[i] = item with { CrossSize = isRow ? measured.Height : measured.Width };
        }
        return (heightFrom, widthFrom);
    }

    /// <summary>Sizes each line across: its tallest item, a nowrap container's one line held in
    /// <paramref name="ownCross"/>, the container's own cross size (<see cref="OwnSize"/>) — which
    /// is also what AlignContent.Stretch grows wrapped lines to.</summary>
    private static void ComputeLineCrossSizes(List<FlexLine> lines, SizeRange ownCross, float crossGap,
        AlignContent alignContent, bool isWrap)
    {
        // Each line's cross size is the max cross size of its items — or, for the items that
        // align by baseline, the tallest above the shared baseline plus the deepest below it,
        // which is more when a short item hangs low: text beside an icon, whose descenders go
        // below the icon's bottom edge (CSS Flexbox §9.4 step 8).
        for (int i = 0; i < lines.Count; i++)
        {
            float maxCross = 0, maxAscent = 0, maxDescent = 0;
            foreach (var item in lines[i].Items)
            {
                maxCross = float.MaxNative(maxCross, item.CrossSize);
                if (float.IsNaN(item.Baseline)) continue;
                maxAscent = float.MaxNative(maxAscent, item.Baseline);
                maxDescent = float.MaxNative(maxDescent, item.CrossSize - item.Baseline);
            }
            lines[i] = lines[i] with
            {
                CrossSize = float.MaxNative(maxCross, maxAscent + maxDescent),
                MaxAscent = maxAscent,
            };
        }

        // A single line (nowrap) is held within the container's own cross size, so its items
        // are aligned in the box the container ends up as (CSS Flexbox §9.4 step 8): all of a
        // definite size, else its items' extent within the min and max. Without the min, a row
        // with a min height whose height nothing forced lined its items up along its top, even
        // centred, and a card with a min width left its stretched children their own width.
        if (!isWrap && lines.Count == 1)
        {
            lines[0] = lines[0] with { CrossSize = ownCross.Hold(lines[0].CrossSize) };
            return;
        }

        // AlignContent.Stretch shares out what is left of the container's own cross size after
        // the lines and the gaps between them — for a size taken from the lines, nothing, unless
        // a min size makes room. (It used to share out the space offered, gaps and all: lines in
        // a fixed height overflowed it by their gaps, and an auto-height row grew to fill its
        // parent, or, in a scroll view, to half of float.MaxValue.)
        if (alignContent == AlignContent.Stretch && lines.Count > 0)
        {
            float totalCross = crossGap * (lines.Count - 1);
            foreach (var line in lines)
                totalCross += line.CrossSize;

            float remaining = ownCross.Hold(totalCross) - totalCross;
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
        var pos = child.PositionRead;
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
        // Indexed: a foreach over the IReadOnlyList boxes its enumerator, once per element moved —
        // and a subtree whose layout is kept is moved rather than laid out again.
        var children = element.Children;
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            child.Layout = child.Layout with
            {
                AbsoluteX = child.Layout.AbsoluteX + dx,
                AbsoluteY = child.Layout.AbsoluteY + dy
            };
            OffsetDescendants(child, dx, dy);
        }
    }

    // ── Static helpers ──

    /// <summary>
    /// One axis of a box, settled before its content is laid out: the border-box size when
    /// it is definite (declared, or forced by the parent's flex resolution — null when the
    /// box sizes to its content), and the extent its content gets.
    ///
    /// <para><b>Min and max bind here</b>, not only on the finished box. Applied afterwards
    /// alone they clamped the box and left its content laid out in the unclamped size: a
    /// column stretched across a wide parent with a max width came out narrow with children
    /// as wide as the parent — drawn past its edge and, because hit testing stops at a
    /// parent's bounds, unreachable by the pointer — and text with a max width was measured
    /// on one long line and painted wrapped. The finished box is still clamped where it is
    /// sized from content; for a definite size that clamp is now a no-op.</para>
    /// </summary>
    private static (float? Resolved, float ContentAvailable) ResolveAxis(float? forced, Dimension size,
        Dimension min, Dimension max, float available, float spacing)
    {
        float? resolved = forced ?? size.Resolve(available);
        if (resolved is { } definite)
            resolved = ApplyMinMax(definite, min, max, available);

        // Sizing to content: the content may use what is available, within the same bounds.
        // (An unbounded extent with no max stays unbounded.)
        float extent = (resolved ?? ApplyMinMax(available, min, max, available)) - spacing;
        return (resolved, float.IsNegative(extent) ? 0 : extent);
    }

    /// <summary>
    /// Both axes as <see cref="ResolveAxis"/> resolves them, then the aspect ratio: with one
    /// side definite and the other not, the other follows from it, within its own min and max,
    /// and so does the room it leaves its content. Both definite, the ratio is ignored; neither,
    /// and the callers derive the height from the content's width once they have it — with the
    /// <c>Ratio</c> returned here, so it is looked up once per call, not twice.
    /// </summary>
    private static (float? Width, float ContentW, float? Height, float ContentH, float Ratio) ResolveBox(Element element,
        float? forcedWidth, float? forcedHeight, float availableWidth, float availableHeight, Thickness spacing)
    {
        var box = element.BoxRead;
        var (width, contentW) = ResolveAxis(forcedWidth, box.Width, box.MinWidth, box.MaxWidth,
            availableWidth, spacing.Horizontal);
        var (height, contentH) = ResolveAxis(forcedHeight, box.Height, box.MinHeight, box.MaxHeight,
            availableHeight, spacing.Vertical);

        float ratio = RatioOf(element);
        if (ratio > 0f)
        {
            if (width is { } w && height is null)
            {
                height = ApplyMinMax(w / ratio, box.MinHeight, box.MaxHeight, availableHeight);
                contentH = float.MaxNative(0f, height.Value - spacing.Vertical);
            }
            else if (height is { } h && width is null)
            {
                width = ApplyMinMax(h * ratio, box.MinWidth, box.MaxWidth, availableWidth);
                contentW = float.MaxNative(0f, width.Value - spacing.Horizontal);
            }
        }
        return (width, contentW, height, contentH, ratio);
    }

    /// <summary>
    /// The shape a box keeps: its own <see cref="BoxStyle.AspectRatio"/>, or — for content with a
    /// shape of its own, a picture — that shape when the app gave one side and left the other,
    /// as an HTML image does. Only then: a picture stretched across a column, or given both
    /// sides, is laid out as it always was and drawn by its fit.
    /// </summary>
    private static float RatioOf(Element element)
    {
        var box = element.BoxRead;
        if (box.AspectRatio > 0f) return box.AspectRatio;
        // Layout asks this of every element on every measure, and almost none has a shape of
        // its own: answer them on a field read, before the virtual call.
        if (!element.HasNaturalShape || box.Width.IsAuto == box.Height.IsAuto) return 0f;
        return element.NaturalAspectRatio;
    }

    private static float ApplyMinMax(float value, Dimension min, Dimension max, float reference)
    {
        value = float.MaxNative(value, min.ResolveOr(reference, -1f));
        value = float.MinNative(value, max.ResolveOr(reference, float.MaxValue));
        return value;
    }

    /// <summary>
    /// A flex container's own content-box width and height, each as the range it will settle in
    /// (<see cref="SizeRange"/>): a definite size is both ends of it; a size taken from the
    /// content is wherever the content puts it within the container's min and max.
    /// <para><b>Free space is the space between the lines and this size</b>, never the space
    /// merely offered to the container: JustifyContent distributes it along the main axis and
    /// AlignContent across the wrapped lines. An auto-sized container doesn't take up what it is
    /// offered. Justified against that, a centred column in a 32px row moved its 17px of text
    /// down by half the difference and then, sized from where the text ended, came out 24.6px
    /// tall with the text at its bottom — while the measure pass, which doesn't justify, had
    /// said 17. A scroll container is the other way round on its scrolling axis: its children
    /// are distributed against unbounded space, so they can overflow it, but its own size is
    /// its viewport, and content that fits is centred in that.</para>
    /// </summary>
    private static (SizeRange Width, SizeRange Height) OwnSize(BoxStyle box, Thickness spacing,
        LayoutConstraint constraint, float? resolvedW, float contentAvailW, float? resolvedH, float contentAvailH)
    {
        return (Axis(resolvedW, contentAvailW, box.MinWidth, box.MaxWidth, constraint.AvailableWidth, spacing.Horizontal),
            Axis(resolvedH, contentAvailH, box.MinHeight, box.MaxHeight, constraint.AvailableHeight, spacing.Vertical));

        static SizeRange Axis(float? resolved, float contentAvailable, Dimension min, Dimension max,
            float available, float spacing) =>
            resolved is null
                ? new(min.ResolveOr(available, -1f) - spacing, max.ResolveOr(available, float.MaxValue) - spacing)
                : new(contentAvailable, contentAvailable);
    }

    /// <summary>The main size the lines are justified in: the longest line held in the
    /// container's own main size, the way ComputeLayout sizes an auto container from it.</summary>
    private static float JustifySpace(List<FlexLine> lines, float gap, SizeRange ownMain)
    {
        if (ownMain.Min == ownMain.Max) return ownMain.Min;
        float longest = 0;
        foreach (var line in lines)
            longest = float.MaxNative(longest, LineMain(line, gap));
        return ownMain.Hold(longest);
    }

    /// <summary>The cross size wrapped lines are aligned in: all of them and the gaps between,
    /// held in the container's own cross size. A nowrap container's one line has nothing to be
    /// aligned in — AlignContent is for wrapped lines, as in CSS — so it gets exactly its own
    /// extent, and no free space.</summary>
    private static float AlignContentSpace(float totalCross, SizeRange ownCross, bool isWrap) =>
        isWrap ? ownCross.Hold(totalCross) : totalCross;

    /// <summary>A line's outer main size: its items and the gaps between them.</summary>
    private static float LineMain(FlexLine line, float gap)
    {
        float main = 0;
        for (int i = 0; i < line.Items.Count; i++)
            main += line.Items[i].MainSize + (i > 0 ? gap : 0);
        return main;
    }

    private static float ComputeJustifyOffset(JustifyContent justify, float available, float used, int itemCount)
    {
        if (itemCount == 0) return 0;
        float free = available - used;
        if (float.IsNegative(free)) free = 0;

        return justify switch
        {
            JustifyContent.FlexEnd => free,
            JustifyContent.Center => free / 2,
            JustifyContent.SpaceAround when itemCount > 0 => free / itemCount / 2,
            JustifyContent.SpaceEvenly when itemCount > 0 => free / (itemCount + 1),
            _ => 0
        };
    }

    /// <summary>Whether a column stretches <paramref name="child"/>, a ratio box of auto width,
    /// across the room it has — the case where that width decides its height.</summary>
    private static bool StretchesAcross(Element child, AlignItems containerAlign, float crossAvailable) =>
        RatioOf(child) > 0f
        && child.BoxRead.Width.IsAuto
        && child.BoxRead.Height.IsAuto
        && !LayoutSpace.IsUnbounded(crossAvailable)
        && ResolveAlignSelf(child.FlexRead.AlignSelf, containerAlign) == AlignItems.Stretch;

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
        float free = available - used;
        if (float.IsNegative(free)) free = 0;

        return justify switch
        {
            JustifyContent.SpaceBetween => free / (itemCount - 1) + gap,
            JustifyContent.SpaceAround => free / itemCount + gap,
            JustifyContent.SpaceEvenly => free / (itemCount + 1) + gap,
            _ => gap
        };
    }

    /// <summary>Where an item sits across its line: by its baseline when it has one to align
    /// by — the group's deepest-reaching item flush with the line's top and the rest lowered to
    /// meet it — and otherwise by <see cref="ComputeAlignOffset"/>.</summary>
    private static float ItemCrossOffset(AlignItems align, FlexLine line, FlexItem item, float childCrossSize) =>
        align == AlignItems.Baseline && !float.IsNaN(item.Baseline)
            ? line.MaxAscent - item.Baseline
            : ComputeAlignOffset(align, line.CrossSize, childCrossSize);

    private static float ComputeAlignOffset(AlignItems align, float crossAvailable, float childCrossSize)
    {
        float free = crossAvailable - childCrossSize;
        if (float.IsNegative(free)) free = 0;
        return align switch
        {
            AlignItems.FlexEnd => free,
            AlignItems.Center => free / 2,
            _ => 0
        };
    }

    private static float ComputeAlignContentOffset(AlignContent align, float space, float totalCross, int lineCount)
    {
        if (lineCount == 0) return 0;
        float free = space - totalCross;
        if (float.IsNegative(free)) free = 0;

        return align switch
        {
            AlignContent.FlexEnd => free,
            AlignContent.Center => free / 2,
            AlignContent.SpaceAround when lineCount > 0 => free / lineCount / 2,
            _ => 0
        };
    }

    private static float ComputeAlignContentGap(AlignContent align, float space, float totalCross, int lineCount, float gap)
    {
        if (lineCount <= 1) return 0;
        float free = space - totalCross;
        if (float.IsNegative(free)) free = 0;

        return align switch
        {
            AlignContent.SpaceBetween => free / (lineCount - 1) + gap,
            AlignContent.SpaceAround => free / lineCount + gap,
            _ => gap
        };
    }

    private static void ClearLayoutDirty(Element element)
    {
        // Layout dirt propagates upward (MarkLayoutDirty walks to the root), so a
        // clean element cannot have a layout-dirty descendant — prune there. This
        // final sweep is a safety net; ComputeLayout already clears visited nodes.
        if ((element.DirtyFlags & DirtyFlags.Layout) == 0) return;
        element.DirtyFlags &= ~DirtyFlags.Layout;
        foreach (var child in element.Children)
            ClearLayoutDirty(child);
    }
}

/// <param name="Element">The flex item itself.</param>
/// <param name="MainSize">Its size along the container's main axis.</param>
/// <param name="CrossSize">Its size along the cross axis.</param>
/// <param name="BaseMain">The main size this item was measured at, kept so Phase 3.5 can
/// tell which items flex resolution actually moved and re-measure only those.</param>
/// <param name="Baseline">For an item aligned by baseline in a row: how far below its outer
/// (margin-box) top its first baseline sits. NaN for every other item.</param>
internal record struct FlexItem(Element Element, float MainSize, float CrossSize, float BaseMain, float Baseline = float.NaN);

/// <summary>The range a container's own content-box size along one axis settles in: one value
/// when the size is definite (declared, or forced by its parent), else its min and max less its
/// padding and border, with a size taken from the content landing between them.</summary>
internal readonly record struct SizeRange(float Min, float Max)
{
    /// <summary>Where a size taken from content <paramref name="content"/> long settles: held
    /// between the ends, the max winning over the min as it does in ApplyMinMax.</summary>
    public float Hold(float content) => float.MinNative(float.MaxNative(content, Min), Max);
}

/// <param name="MaxAscent">The largest <see cref="FlexItem.Baseline"/> on the line: where the
/// shared baseline sits below the line's top. 0 when nothing on it aligns by baseline.</param>
internal record struct FlexLine(ArraySegment<FlexItem> Items, float CrossSize = 0, float MaxAscent = 0);
