using System.Buffers;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Core.Layout;

// Baseline alignment (AlignItems.Baseline / AlignSelf.Baseline), CSS Flexbox §8.3 and §9.4.
//
// In a row, the items that align by baseline are placed so their first baselines sit on one
// line: the item reaching furthest above its baseline goes flush with the line's top and the
// others are lowered to meet it, and the line grows when that pushes something past its
// bottom. In a column, as in CSS, there is no baseline across the line and it is FlexStart.
//
// Where an item's baseline is: an element that draws text says (Element.MeasureBaseline);
// a flex container takes its first line's — the shared baseline when something on it aligns
// by baseline, else the first item's; anything else, or a container with no items, gets one
// synthesized at the bottom of its border box. Grid containers synthesize too.
//
// Everything here is measure-only, built from MeasureSize and MeasureLeafSize like the rest of
// Phase 1, so the measure pass and the layout pass agree on how tall a row is: a parent sizing
// its children has to see the growth the layout will produce. It only runs for items that
// align by baseline, so a tree that never asks for it pays nothing.
public sealed partial class LayoutEngine
{
    /// <summary>Gives every baseline-aligned item on a row's lines its
    /// <see cref="FlexItem.Baseline"/>, at the size it will be laid out at.</summary>
    private void ResolveBaselines(Element container, List<FlexLine> lines, bool isRow,
        float mainDistribute, float crossAvailable)
    {
        if (!isRow) return;
        var containerAlign = container.FlexRead.AlignItems;

        foreach (var line in lines)
        {
            var items = line.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (ResolveAlignSelf(item.Element.FlexRead.AlignSelf, containerAlign) != AlignItems.Baseline)
                    continue;

                // Baseline items are never stretched, so the cross size is the measured one;
                // the main size is the flexed one Phase 6 will force.
                var margin = item.Element.BoxRead.Margin;
                float? fw = Math.Max(0, item.MainSize - margin.Horizontal);
                float baseline = MeasureFirstBaseline(item.Element,
                        new LayoutConstraint(mainDistribute, crossAvailable), fw, null)
                    ?? item.CrossSize - margin.Bottom;
                items[i] = item with { Baseline = baseline };
            }
        }
    }

    /// <summary>
    /// The distance from <paramref name="element"/>'s outer (margin-box) top to its first
    /// baseline at the given size, or null when it has none of its own to offer — the caller
    /// then synthesizes one at the bottom of the border box.
    /// </summary>
    private float? MeasureFirstBaseline(Element element, LayoutConstraint constraint, float? forcedWidth, float? forcedHeight)
    {
        var box = element.BoxRead;
        var margin = box.Margin;

        bool hasFlow = false;
        foreach (var child in element.Children)
        {
            if (child.PositionRead.Type == PositionType.Absolute) continue;
            hasFlow = true;
            break;
        }

        if (!hasFlow)
        {
            var outer = MeasureLeafSize(element, constraint.AvailableWidth, constraint.AvailableHeight, forcedWidth, forcedHeight);
            var borderBox = new Size(outer.Width - margin.Horizontal, outer.Height - margin.Vertical);
            return element.MeasureBaseline(borderBox, _measureContext) is { } own ? margin.Top + own : null;
        }

        if (element.GridRead.HasTemplate) return null;

        return FlexContainerBaseline(element, constraint, forcedWidth, forcedHeight) is { } inner
            ? margin.Top + box.BorderWidth.Top + box.Padding.Top + inner
            : null;
    }

    /// <summary>
    /// A flex container's first baseline, below the top of its content box: its first line's
    /// shared baseline when something on that line aligns by baseline, else the baseline of the
    /// item placed first (synthesized from its border box when it has none). Null for a
    /// container with no items. Mirrors the measure pass up to line sizes, then places just
    /// that one item the way Phase 6 would.
    /// </summary>
    private float? FlexContainerBaseline(Element container, LayoutConstraint constraint, float? forcedWidth, float? forcedHeight)
    {
        var box = container.BoxRead;
        var outerSpacing = box.Padding + box.BorderWidth;
        var (resolvedW, contentAvailW, resolvedH, contentAvailH, _) = ResolveBox(container, forcedWidth, forcedHeight,
            constraint.AvailableWidth, constraint.AvailableHeight, outerSpacing);

        var flex = container.FlexRead;
        bool isRow = flex.Direction is FlexDirection.Row or FlexDirection.RowReverse;
        bool isReverse = flex.Direction is FlexDirection.RowReverse or FlexDirection.ColumnReverse;
        bool isWrap = flex.Wrap is not FlexWrap.NoWrap;
        var ownSize = OwnSize(box, outerSpacing, constraint, resolvedW, contentAvailW, resolvedH, contentAvailH);
        var ownMain = isRow ? ownSize.Width : ownSize.Height;
        var ownCross = isRow ? ownSize.Height : ownSize.Width;

        float mainAvailable = isRow ? contentAvailW : contentAvailH;
        float crossAvailable = isRow ? contentAvailH : contentAvailW;
        float mainGap = isRow ? flex.ResolvedColumnGap : flex.ResolvedRowGap;
        float crossGap = isRow ? flex.ResolvedRowGap : flex.ResolvedColumnGap;
        bool scrollOnMain = (isRow
                ? box.OverflowX == Overflow.Scroll
                : box.OverflowY == Overflow.Scroll)
            || LayoutSpace.IsUnbounded(mainAvailable);
        float mainDistribute = scrollOnMain ? LayoutSpace.Unbounded : mainAvailable;

        var (itemsArray, itemCount, anyBaseline, _, _) = MeasureFlexItems(container.Children, isRow, mainDistribute, crossAvailable, flex.AlignItems);
        List<FlexLine>? lines = null;
        try
        {
            if (itemCount == 0) return null;

            lines = CollectLines(itemsArray, itemCount, mainDistribute, mainGap, isWrap);
            foreach (var l in lines)
                ResolveFlexGrowShrink(l, mainDistribute, mainGap, isRow, crossAvailable);
            foreach (var l in lines)
                RemeasureFlexedCrossSizes(l, isRow, mainDistribute, crossAvailable);
            if (anyBaseline) ResolveBaselines(container, lines, isRow, mainDistribute, crossAvailable);
            ComputeLineCrossSizes(lines, ownCross, crossGap, flex.AlignContent, isWrap);

            float totalCross = 0;
            for (int i = 0; i < lines.Count; i++)
                totalCross += lines[i].CrossSize + (i > 0 ? crossGap : 0);
            var line = lines[0];

            // The item Phase 6 places first, and the size it lays it out at.
            var item = isReverse ? line.Items[^1] : line.Items[0];
            var child = item.Element;
            var margin = child.BoxRead.Margin;
            var align = ResolveAlignSelf(child.FlexRead.AlignSelf, flex.AlignItems);
            bool stretch = align == AlignItems.Stretch;
            var childConstraint = isRow
                ? new LayoutConstraint(mainDistribute, contentAvailH)
                : new LayoutConstraint(contentAvailW, mainDistribute);

            if (isRow)
            {
                float lineTop = ComputeAlignContentOffset(flex.AlignContent,
                    AlignContentSpace(totalCross, ownCross, isWrap), totalCross, lines.Count);
                if (HasBaselineItems(line))
                    return lineTop + line.MaxAscent;

                // As Phase 6: a ratio box is not stretched down.
                float? fh = stretch && child.BoxRead.Height.IsAuto && !(RatioOf(child) > 0f)
                    ? line.CrossSize - margin.Vertical : null;
                float borderH = fh ?? item.CrossSize - margin.Vertical;
                float own = MeasureFirstBaseline(child, childConstraint, item.MainSize - margin.Horizontal, fh)
                    ?? margin.Top + borderH;
                return lineTop + ComputeAlignOffset(align, line.CrossSize, item.CrossSize) + own;
            }
            else
            {
                float top = ComputeJustifyOffset(flex.JustifyContent, JustifySpace(lines, mainGap, ownMain),
                    LineMain(line, mainGap), line.Items.Count);

                float? fw = stretch && child.BoxRead.Width.IsAuto
                        && (!(RatioOf(child) > 0f) || (child.BoxRead.Height.IsAuto && !LayoutSpace.IsUnbounded(crossAvailable)))
                    ? line.CrossSize - margin.Horizontal : null;
                float borderH = item.MainSize - margin.Vertical;
                float own = MeasureFirstBaseline(child, childConstraint, fw, borderH) ?? margin.Top + borderH;
                return top + own;
            }
        }
        finally
        {
            ArrayPool<FlexItem>.Shared.Return(itemsArray, clearArray: true);
            if (lines is not null) ReturnLines(lines);
        }
    }

    /// <summary>Whether anything on the line aligns by baseline.</summary>
    private static bool HasBaselineItems(FlexLine line)
    {
        foreach (var item in line.Items)
            if (!float.IsNaN(item.Baseline)) return true;
        return false;
    }
}

