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

    public void PerformLayout(Element root, float availableWidth, float availableHeight)
    {
        var constraint = new LayoutConstraint(availableWidth, availableHeight);
        ComputeLayout(root, constraint, absoluteX: 0, absoluteY: 0, forcedWidth: availableWidth, forcedHeight: availableHeight);
        ClearLayoutDirty(root);
    }

    /// <param name="forcedWidth">If set, used instead of auto width (for root element or flex-resolved sizes)</param>
    /// <param name="forcedHeight">If set, used instead of auto height (for root element or flex-resolved sizes)</param>
    private Size ComputeLayout(Element element, LayoutConstraint constraint, float absoluteX, float absoluteY,
        float? forcedWidth = null, float? forcedHeight = null)
    {
        var box = element.Box;
        var flex = element.Flex;
        var padding = box.Padding;
        var border = box.BorderWidth;
        var margin = box.Margin;
        var outerSpacing = padding + border;

        // Resolve definite width/height. Forced size overrides auto.
        var resolvedWidth = box.Width.Resolve(constraint.AvailableWidth) ?? (box.Width.IsAuto ? forcedWidth : null);
        var resolvedHeight = box.Height.Resolve(constraint.AvailableHeight) ?? (box.Height.IsAuto ? forcedHeight : null);

        // Content area available for children
        var contentAvailW = (resolvedWidth ?? constraint.AvailableWidth) - outerSpacing.Horizontal;
        var contentAvailH = (resolvedHeight ?? constraint.AvailableHeight) - outerSpacing.Vertical;
        contentAvailW = Math.Max(0, contentAvailW);
        contentAvailH = Math.Max(0, contentAvailH);

        // Separate flow children from absolutely positioned
        List<Element>? absChildren = null;
        var flowChildren = new List<Element>();
        foreach (var child in element.Children)
        {
            if (child.Position.Type == PositionType.Absolute)
            {
                absChildren ??= [];
                absChildren.Add(child);
            }
            else
            {
                flowChildren.Add(child);
            }
        }

        // If leaf node, measure intrinsic content
        Size contentSize;
        if (flowChildren.Count == 0)
        {
            contentSize = element.MeasureContent(contentAvailW, contentAvailH, _measureContext);
        }
        else
        {
            contentSize = LayoutFlexChildren(element, flowChildren, contentAvailW, contentAvailH,
                absoluteX + outerSpacing.Left + margin.Left,
                absoluteY + outerSpacing.Top + margin.Top);
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

        // Layout absolute children
        if (absChildren is not null)
        {
            var contentBox = element.Layout.AbsoluteContentBox;
            foreach (var absChild in absChildren)
                LayoutAbsoluteChild(absChild, contentBox);
        }

        return new Size(finalW + margin.Horizontal, finalH + margin.Vertical);
    }

    private Size LayoutFlexChildren(Element container, List<Element> children,
        float contentAvailW, float contentAvailH, float contentAbsX, float contentAbsY)
    {
        var flex = container.Flex;
        bool isRow = flex.Direction is FlexDirection.Row or FlexDirection.RowReverse;
        bool isReverse = flex.Direction is FlexDirection.RowReverse or FlexDirection.ColumnReverse;

        float mainAvailable = isRow ? contentAvailW : contentAvailH;
        float crossAvailable = isRow ? contentAvailH : contentAvailW;
        float gap = isRow ? flex.ResolvedColumnGap : flex.ResolvedRowGap;

        // Phase 1: Measure all children to get their hypothetical sizes
        var items = new List<FlexItem>(children.Count);
        float totalMainSize = 0;
        float maxCrossSize = 0;

        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var childBox = child.Box;
            var childMargin = childBox.Margin;

            // Determine hypothetical main size from flex basis or explicit size
            float? basisOrSize = child.Flex.FlexBasis.IsAuto
                ? (isRow ? childBox.Width.Resolve(mainAvailable) : childBox.Height.Resolve(mainAvailable))
                : child.Flex.FlexBasis.Resolve(mainAvailable);

            // If no explicit size, measure content
            float hypotheticalMain;
            float hypotheticalCross;

            if (basisOrSize.HasValue)
            {
                hypotheticalMain = basisOrSize.Value;
                // Measure cross size — use container's available space as constraint
                // so percentage dimensions resolve against the container, not the basis
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
                // Measure with full available space
                var childConstraint = isRow
                    ? new LayoutConstraint(mainAvailable, crossAvailable)
                    : new LayoutConstraint(crossAvailable, mainAvailable);
                var measured = ComputeLayout(child, childConstraint, 0, 0);
                hypotheticalMain = isRow ? measured.Width : measured.Height;
                hypotheticalCross = isRow ? measured.Height : measured.Width;
            }

            items.Add(new FlexItem(child, hypotheticalMain, hypotheticalCross));
            totalMainSize += hypotheticalMain;
            maxCrossSize = Math.Max(maxCrossSize, hypotheticalCross);
        }

        // Add gaps to total
        if (children.Count > 1)
            totalMainSize += gap * (children.Count - 1);

        // Phase 2: Distribute flex grow/shrink (basic version for Phase 1)
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

        // Phase 3: Position items along main axis
        float mainOffset = ComputeJustifyOffset(container.Flex.JustifyContent, mainAvailable, totalMainSize, children.Count);
        float justifyGap = ComputeJustifyGap(container.Flex.JustifyContent, mainAvailable, totalMainSize, children.Count, gap);

        // Recompute with final sizes and positions
        float actualMainSize = 0;
        float actualMaxCross = 0;

        for (int i = 0; i < items.Count; i++)
        {
            int idx = isReverse ? items.Count - 1 - i : i;
            var item = items[idx];
            var child = item.Element;

            // Re-layout child with resolved main size
            // Apply AlignItems for cross axis sizing
            var alignSelf = child.Flex.AlignSelf != AlignSelf.Auto
                ? (AlignItems)(byte)child.Flex.AlignSelf
                : container.Flex.AlignItems;

            // Use container's content dimensions as constraint so percentage
            // dimensions resolve against the container, not the flex-resolved size
            var childConstraint = new LayoutConstraint(contentAvailW, contentAvailH);
            float childAbsX = isRow ? contentAbsX + mainOffset : contentAbsX;
            float childAbsY = isRow ? contentAbsY : contentAbsY + mainOffset;

            // Pass forced sizes so auto-sized flex items use the resolved flex size
            float? forcedW = isRow ? item.MainSize : (alignSelf == AlignItems.Stretch ? (float?)contentAvailW : null);
            float? forcedH = isRow ? (alignSelf == AlignItems.Stretch ? (float?)contentAvailH : null) : item.MainSize;
            ComputeLayout(child, childConstraint, childAbsX, childAbsY, forcedWidth: forcedW, forcedHeight: forcedH);

            // Update child position with cross-axis alignment
            float childMainSize = isRow ? child.Layout.Width + child.Layout.Margin.Horizontal : child.Layout.Height + child.Layout.Margin.Vertical;
            float childCrossSize = isRow ? child.Layout.Height + child.Layout.Margin.Vertical : child.Layout.Width + child.Layout.Margin.Horizontal;

            float crossOffset = ComputeAlignOffset(alignSelf,
                isRow ? contentAvailH : contentAvailW,
                childCrossSize);

            // Set final position
            float finalX, finalY;
            if (isRow)
            {
                finalX = contentAbsX + mainOffset;
                finalY = contentAbsY + crossOffset;
            }
            else
            {
                finalX = contentAbsX + crossOffset;
                finalY = contentAbsY + mainOffset;
            }

            child.Layout = child.Layout with
            {
                X = isRow ? mainOffset : crossOffset,
                Y = isRow ? crossOffset : mainOffset,
                AbsoluteX = finalX + child.Layout.Margin.Left,
                AbsoluteY = finalY + child.Layout.Margin.Top
            };

            mainOffset += childMainSize + justifyGap;
            actualMainSize = mainOffset - justifyGap;
            actualMaxCross = Math.Max(actualMaxCross, childCrossSize);
        }

        return isRow
            ? new Size(actualMainSize, actualMaxCross)
            : new Size(actualMaxCross, actualMainSize);
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

        child.Layout = child.Layout with { AbsoluteX = x, AbsoluteY = y };
    }

    private static float ApplyMinMax(float value, Dimension min, Dimension max, float reference)
    {
        if (min.Resolve(reference) is float minVal)
            value = Math.Max(value, minVal);
        if (max.Resolve(reference) is float maxVal)
            value = Math.Min(value, maxVal);
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
            _ => 0
        };
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

    private static void ClearLayoutDirty(Element element)
    {
        element.DirtyFlags &= ~DirtyFlags.Layout;
        foreach (var child in element.Children)
            ClearLayoutDirty(child);
    }
}

internal record struct FlexItem(Element Element, float MainSize, float CrossSize);
