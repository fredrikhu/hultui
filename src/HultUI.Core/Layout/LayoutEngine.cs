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
        ComputeLayout(root, constraint, absoluteX: 0, absoluteY: 0, forcedWidth: fw, forcedHeight: fh);
        ClearLayoutDirty(root);
    }

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

        // Skip clean subtrees when constraints haven't changed.
        // The forced-dimension check also accepts the case where Phase 1 of an
        // ancestor's LayoutFlexChildren computed this element with forced=null
        // (auto) and Phase 6 now forces the natural size — that produces an
        // identical result, so the cache hit is safe.
        if ((element.DirtyFlags & DirtyFlags.Layout) == 0
            && element.CacheConstraintW == constraint.AvailableWidth
            && element.CacheConstraintH == constraint.AvailableHeight
            && ForcedDimMatches(element.CacheForcedW, forcedWidth, element.Layout.Width)
            && ForcedDimMatches(element.CacheForcedH, forcedHeight, element.Layout.Height))
        {
            return element.CacheResultSize;
        }

        var box = element.BoxRead;
        var padding = box.Padding;
        var border = box.BorderWidth;
        var margin = box.Margin;
        var outerSpacing = padding + border;

        // Resolve definite width/height (a forced size, from flex resolution, takes priority)
        // and the content area available for children — with min and max already applied,
        // and an aspect ratio's missing side filled in.
        var (resolvedWidth, contentAvailW, resolvedHeight, contentAvailH) = ResolveBox(element, forcedWidth, forcedHeight,
            constraint.AvailableWidth, constraint.AvailableHeight, outerSpacing);

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
        var finalW = resolvedWidth ?? (contentSize.Width + outerSpacing.Horizontal);
        var finalH = resolvedHeight ?? (contentSize.Height + outerSpacing.Vertical);
        finalW = ApplyMinMax(finalW, box.MinWidth, box.MaxWidth, constraint.AvailableWidth);
        if (RatioOf(element) is > 0f and var ratio && resolvedWidth is null && resolvedHeight is null)
            finalH = finalW / ratio;
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
    /// Returns true when a forced-dimension cache lookup should hit. Accepts an
    /// exact match, or the case where Phase 1 measured with no forced dimension
    /// (cached=null) and Phase 6 now forces the element's natural size — that
    /// produces an identical layout, so reusing the cached result is safe.
    /// </summary>
    private static bool ForcedDimMatches(float? cached, float? current, float layoutDim)
    {
        if (cached == current) return true;
        if (current.HasValue && !cached.HasValue && current.Value == layoutDim) return true;
        return false;
    }

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
        var (itemsArray, itemCount, anyBaseline) = MeasureFlexItems(children, isRow, mainDistribute, crossAvailable, flex.AlignItems);
        try
        {
            // ── Phase 2: Collect items into lines ──
            var lines = CollectLines(itemsArray, itemCount, mainDistribute, mainGap, isWrap);

            // ── Phase 3: Resolve flex grow/shrink per line ──
            foreach (var line in lines)
                ResolveFlexGrowShrink(line, mainDistribute, mainGap, isRow, crossAvailable);

            // ── Phase 3.5: Re-measure items whose main size flexing changed ──
            foreach (var line in lines)
                RemeasureFlexedCrossSizes(line, isRow, mainDistribute, crossAvailable);

            // ── Phase 4: Compute per-line cross sizes ──
            if (anyBaseline) ResolveBaselines(container, lines, isRow, mainDistribute, crossAvailable);
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
            ArrayPool<FlexItem>.Shared.Return(itemsArray, clearArray: true);
        }
    }

    // ── Flex helpers ──

    /// <returns>The items, and whether any of them aligns by baseline in a row — noted here, in
    /// the loop that already visits every item, so a row that never asks for baseline alignment
    /// pays one comparison per item for it rather than a pass of its own.</returns>
    private (FlexItem[] Array, int Count, bool AnyBaseline) MeasureFlexItems(IReadOnlyList<Element> children, bool isRow,
        float mainAvailable, float crossAvailable, AlignItems containerAlign)
    {
        var array = ArrayPool<FlexItem>.Shared.Rent(Math.Max(1, children.Count));
        int count = 0;
        bool anyBaseline = false;
        bool rowAlignsByBaseline = isRow && containerAlign == AlignItems.Baseline;

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

            float hypotheticalMain;
            float hypotheticalCross;

            // A ratio box in a column is as wide as the column's room for it, as a block is, and
            // so as tall as that width makes it: measured at that width, or it would be measured
            // from its content — a box with none at no height — and Phase 6 would force that.
            // The line it is on is then that wide, so Phase 6 stretches it to the same width.
            float? stretchedWidth = !isRow && StretchesAcross(child, containerAlign, crossAvailable)
                ? float.MaxNative(0f, crossAvailable - childBox.Margin.Horizontal)
                : null;

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

        return (array, count, anyBaseline);
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
        // Cache hit: each ancestor's Phase 1 re-measures this element with a
        // small set of alternating (constraint, forced) inputs, so results are
        // reusable both within a pass and across frames. The cache is cleared
        // by MarkLayoutDirty (which propagates up from any changed descendant),
        // so a hit here means the cached size is still authoritative — no need
        // to consult DirtyFlags.Layout (that flag belongs to ComputeLayout's
        // cache, which gates whether descendant positions need re-writing).
        if (element.MeasureCache.TryGet(constraint.AvailableWidth, constraint.AvailableHeight,
                forcedWidth, forcedHeight, out var cachedSize))
        {
            return cachedSize;
        }

        using var _trace = _tracer is null
            ? default
            : _tracer.Begin("Measure", element.GetType().Name, element.Key ?? element.Name);

        var box = element.BoxRead;
        var outerSpacing = box.Padding + box.BorderWidth;
        var margin = box.Margin;

        // The same resolution ComputeLayout makes, or the measured size is not the laid-out one.
        var (resolvedW, contentAvailW, resolvedH, contentAvailH) = ResolveBox(element, forcedWidth, forcedHeight,
            constraint.AvailableWidth, constraint.AvailableHeight, outerSpacing);

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

        Size contentSize;
        if (!hasFlow)
        {
            contentSize = element.MeasureContent(contentAvailW, contentAvailH, _measureContext);
        }
        else if (element.GridRead.HasTemplate)
        {
            contentSize = MeasureGridChildrenSize(element, children, contentAvailW, contentAvailH);
        }
        else
        {
            bool isRow = element.FlexRead.Direction is FlexDirection.Row or FlexDirection.RowReverse;
            var own = OwnSize(box, outerSpacing, constraint, resolvedW, contentAvailW, resolvedH, contentAvailH);
            contentSize = MeasureFlexChildrenSize(element, children, contentAvailW, contentAvailH,
                isRow ? own.Height : own.Width);
        }

        var finalW = resolvedW ?? (contentSize.Width + outerSpacing.Horizontal);
        var finalH = resolvedH ?? (contentSize.Height + outerSpacing.Vertical);
        finalW = ApplyMinMax(finalW, box.MinWidth, box.MaxWidth, constraint.AvailableWidth);
        if (RatioOf(element) is > 0f and var ratio && resolvedW is null && resolvedH is null)
            finalH = finalW / ratio;
        finalH = ApplyMinMax(finalH, box.MinHeight, box.MaxHeight, constraint.AvailableHeight);

        var resultSize = new Size(finalW + margin.Horizontal, finalH + margin.Vertical);

        element.MeasureCache.Store(constraint.AvailableWidth, constraint.AvailableHeight,
            forcedWidth, forcedHeight, resultSize);

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
    private Size MeasureFlexChildrenSize(Element container, IReadOnlyList<Element> children,
        float contentAvailW, float contentAvailH, SizeRange ownCross)
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

        var (itemsArray, itemCount, anyBaseline) = MeasureFlexItems(children, isRow, mainDistribute, crossAvailable, flex.AlignItems);
        try
        {
            var lines = CollectLines(itemsArray, itemCount, mainDistribute, mainGap, isWrap);

            foreach (var line in lines)
                ResolveFlexGrowShrink(line, mainDistribute, mainGap, isRow, crossAvailable);

            // The measured height of a row is the height its parent will force on it, so
            // this pass needs the same post-flex cross sizes the layout pass takes.
            foreach (var line in lines)
                RemeasureFlexedCrossSizes(line, isRow, mainDistribute, crossAvailable);

            // As the layout pass does, or a row baseline alignment makes taller would be
            // measured shorter than it lays out.
            if (anyBaseline) ResolveBaselines(container, lines, isRow, mainDistribute, crossAvailable);
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
        if (element.MeasureCache.TryGet(constraintW, constraintH, forcedWidth, forcedHeight,
                out var cachedSize))
        {
            return cachedSize;
        }

        var box = element.BoxRead;
        var outerSpacing = box.Padding + box.BorderWidth;
        var margin = box.Margin;

        // As in ComputeLayout: a max width is what text wraps at, so it has to be in the
        // width the text is measured in — not applied to a box measured on one long line.
        var (resolvedW, contentAvailW, resolvedH, contentAvailH) = ResolveBox(element, forcedWidth, forcedHeight,
            constraintW, constraintH, outerSpacing);

        var contentSize = element.MeasureContent(contentAvailW, contentAvailH, _measureContext);

        var finalW = resolvedW ?? (contentSize.Width + outerSpacing.Horizontal);
        var finalH = resolvedH ?? (contentSize.Height + outerSpacing.Vertical);
        finalW = ApplyMinMax(finalW, box.MinWidth, box.MaxWidth, constraintW);
        if (RatioOf(element) is > 0f and var ratio && resolvedW is null && resolvedH is null)
            finalH = finalW / ratio;
        finalH = ApplyMinMax(finalH, box.MinHeight, box.MaxHeight, constraintH);

        var resultSize = new Size(finalW + margin.Horizontal, finalH + margin.Vertical);

        element.MeasureCache.Store(constraintW, constraintH, forcedWidth, forcedHeight, resultSize);

        return resultSize;
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
    private void RemeasureFlexedCrossSizes(FlexLine line, bool isRow, float mainAvailable, float crossAvailable)
    {
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

            items[i] = item with { CrossSize = isRow ? measured.Height : measured.Width };
        }
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
    /// and the callers derive the height from the content's width once they have it.
    /// </summary>
    private static (float? Width, float ContentW, float? Height, float ContentH) ResolveBox(Element element,
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
        return (width, contentW, height, contentH);
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
        if (box.Width.IsAuto == box.Height.IsAuto) return 0f;
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
