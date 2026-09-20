using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.DevTools;

public sealed class DevToolsOverlay
{
    private static readonly Color BoxOutlineColor = new(64, 160, 255, 120);
    private static readonly Color PaddingColor = new(100, 200, 100, 40);
    private static readonly Color MarginColor = new(255, 160, 64, 40);
    private static readonly Color HoverHighlight = new(64, 160, 255, 50);
    private static readonly Color PickHighlight = new(120, 220, 160, 70);
    private static readonly Color HudBackground = new(0, 0, 0, 200);
    private static readonly Color HudText = new(220, 220, 220, 255);

    public bool IsEnabled { get; set; }
    public bool ShowBoundingBoxes { get; set; }

    /// <summary>
    /// When true the window renders every loop iteration regardless of dirty
    /// state, so the HUD's FPS number reflects raw render throughput instead of
    /// sitting idle between changes. Only meaningful while <see cref="IsEnabled"/>.
    /// </summary>
    public bool ContinuousRedraw { get; set; }

    public Element? HoveredElement { get; set; }

    /// <summary>
    /// The inspector this window's highlight follows, when one is open. The
    /// inspector's panel lives in its own window; all that belongs over the app is
    /// the box-model outline of whatever is selected or being picked. Independent of
    /// <see cref="IsEnabled"/>: inspecting an element shouldn't drag the FPS HUD
    /// along with it.
    /// </summary>
    public ElementInspector? Inspector { get; set; }

    /// <summary>True when something needs painting over the app that isn't the F12 HUD.</summary>
    public bool HasHighlight => Inspector?.Highlighted is not null;

    private readonly PerformanceCounters _perf;

    public DevToolsOverlay(PerformanceCounters perf)
    {
        _perf = perf;
    }

    public void Render(IRenderContext ctx, Element root, float viewportWidth, float viewportHeight)
    {
        var highlighted = Inspector?.Highlighted;
        if (highlighted is not null)
        {
            // A live pick preview reads as "this is what you'd get", the committed
            // selection as "this is what you have" — different colors so the two
            // are never confused mid-pick.
            bool isPreview = ReferenceEquals(highlighted, Inspector!.Hovered);
            RenderHighlight(ctx, highlighted, viewportWidth, isPreview ? PickHighlight : HoverHighlight);
        }

        if (!IsEnabled) return;

        if (ShowBoundingBoxes)
            RenderBoundingBoxes(ctx, root, 0, 0);

        if (HoveredElement is not null && !ReferenceEquals(HoveredElement, highlighted))
            RenderHighlight(ctx, HoveredElement, viewportWidth, HoverHighlight);

        RenderPerformanceHud(ctx, viewportWidth);
    }

    private void RenderBoundingBoxes(IRenderContext ctx, Element element, float scrollOffsetX, float scrollOffsetY)
    {
        var box = element.Layout.AbsoluteBorderBox;
        var shifted = new Rect(box.X - scrollOffsetX, box.Y - scrollOffsetY, box.Width, box.Height);

        if (shifted.Width > 0 && shifted.Height > 0)
        {
            ctx.DrawRect(new Rect(shifted.X, shifted.Y, shifted.Width, 1), BoxOutlineColor);
            ctx.DrawRect(new Rect(shifted.X, shifted.Y + shifted.Height - 1, shifted.Width, 1), BoxOutlineColor);
            ctx.DrawRect(new Rect(shifted.X, shifted.Y, 1, shifted.Height), BoxOutlineColor);
            ctx.DrawRect(new Rect(shifted.X + shifted.Width - 1, shifted.Y, 1, shifted.Height), BoxOutlineColor);
        }

        // When entering a ScrollView, clip to its content box and accumulate scroll offset
        if (element is ScrollViewElement sv)
        {
            var clip = sv.Layout.AbsoluteContentBox;
            var shiftedClip = new Rect(clip.X - scrollOffsetX, clip.Y - scrollOffsetY, clip.Width, clip.Height);
            ctx.PushClip(shiftedClip);

            float childOffsetX = scrollOffsetX + sv.ScrollOffsetX;
            float childOffsetY = scrollOffsetY + sv.ScrollOffsetY;

            foreach (var child in element.Children)
                RenderBoundingBoxes(ctx, child, childOffsetX, childOffsetY);

            ctx.Pop();
            return;
        }

        foreach (var child in element.Children)
            RenderBoundingBoxes(ctx, child, scrollOffsetX, scrollOffsetY);
    }

    private void RenderHighlight(IRenderContext ctx, Element element, float viewportWidth, Color fill)
    {
        var layout = element.Layout;

        // Compute visual offset by walking up the tree and accumulating scroll offsets
        var (soX, soY) = ComputeScrollOffset(element);
        var box = new Rect(
            layout.AbsoluteBorderBox.X - soX,
            layout.AbsoluteBorderBox.Y - soY,
            layout.AbsoluteBorderBox.Width,
            layout.AbsoluteBorderBox.Height);

        // Highlight the element
        ctx.DrawRect(box, fill);

        // Draw margin area
        var marginBox = new Rect(
            box.X - layout.Margin.Left,
            box.Y - layout.Margin.Top,
            box.Width + layout.Margin.Left + layout.Margin.Right,
            box.Height + layout.Margin.Top + layout.Margin.Bottom);
        ctx.DrawRect(new Rect(marginBox.X, marginBox.Y, marginBox.Width, layout.Margin.Top), MarginColor);
        ctx.DrawRect(new Rect(marginBox.X, box.Y + box.Height, marginBox.Width, layout.Margin.Bottom), MarginColor);
        ctx.DrawRect(new Rect(marginBox.X, box.Y, layout.Margin.Left, box.Height), MarginColor);
        ctx.DrawRect(new Rect(box.X + box.Width, box.Y, layout.Margin.Right, box.Height), MarginColor);

        // Draw padding area
        var contentBox = layout.AbsoluteContentBox;
        var cx = contentBox.X - soX;
        var cy = contentBox.Y - soY;
        ctx.DrawRect(new Rect(cx - layout.Padding.Left, cy - layout.Padding.Top, contentBox.Width + layout.Padding.Left + layout.Padding.Right, layout.Padding.Top), PaddingColor);
        ctx.DrawRect(new Rect(cx - layout.Padding.Left, cy + contentBox.Height, contentBox.Width + layout.Padding.Left + layout.Padding.Right, layout.Padding.Bottom), PaddingColor);
        ctx.DrawRect(new Rect(cx - layout.Padding.Left, cy, layout.Padding.Left, contentBox.Height), PaddingColor);
        ctx.DrawRect(new Rect(cx + contentBox.Width, cy, layout.Padding.Right, contentBox.Height), PaddingColor);

        // Info tooltip
        var label = $"{ElementProperties.Summarize(element)}  {box.Width:F0} × {box.Height:F0}";
        var style = new ResolvedInheritedStyle("monospace", 11, FontWeight.Regular, HudText, TextDirection.Ltr, CursorType.Default);

        float tooltipW = ctx.MeasureText(label, style).Width + 16;
        float tooltipH = 22;
        float tooltipX = Math.Max(4, Math.Min(box.X, viewportWidth - tooltipW - 4));
        float tooltipY = box.Y > tooltipH + 4 ? box.Y - tooltipH - 4 : box.Y + box.Height + 4;

        ctx.DrawRect(new Rect(tooltipX, tooltipY, tooltipW, tooltipH), HudBackground);
        ctx.DrawText(label, new Rect(tooltipX + 8, tooltipY + 4, tooltipW - 16, tooltipH - 8), style, TextOverflow.Clip, 1);
    }

    /// <summary>
    /// Walks up the element tree from the given element, accumulating scroll offsets
    /// from any ancestor ScrollViewElements.
    /// </summary>
    private static (float X, float Y) ComputeScrollOffset(Element element)
    {
        float x = 0, y = 0;
        var current = element.Parent;
        while (current is not null)
        {
            if (current is ScrollViewElement sv)
            {
                x += sv.ScrollOffsetX;
                y += sv.ScrollOffsetY;
            }
            current = current.Parent;
        }
        return (x, y);
    }

    private void RenderPerformanceHud(IRenderContext ctx, float viewportWidth)
    {
        float hudW = 260;
        float hudH = ContinuousRedraw ? 166 : 152;
        float hudX = viewportWidth - hudW - 8;
        float hudY = 8;

        ctx.DrawRect(new Rect(hudX, hudY, hudW, hudH), HudBackground);

        var style = new ResolvedInheritedStyle("monospace", 11, FontWeight.Regular, HudText, TextDirection.Ltr, CursorType.Default);
        float lineH = 14;
        float x = hudX + 8;
        float y = hudY + 6;

        int peakSeconds = (int)PerformanceCounters.PeakWindow.TotalSeconds;
        void Line(string text)
        {
            ctx.DrawText(text, new Rect(x, y, hudW - 16, lineH), style, TextOverflow.Clip, 1);
            y += lineH;
        }

        string display = _perf.Unpaced ? "unpaced" : _perf.DisplayHz > 0 ? $"of {_perf.DisplayHz:F0} Hz" : "display rate unknown";
        Line($"FPS: {_perf.Fps:F0} {display}  (frame work {_perf.AverageFrameTimeMs:F1} ms avg, {_perf.FrameTimePeakMs:F1} max)");
        Line($"(peak = slowest pass in last {peakSeconds}s)");
        Line($"Style:   {_perf.StyleTimeMs,6:F2} ms  (peak {_perf.StyleTimePeakMs:F2})");
        Line($"Layout:  {_perf.LayoutTimeMs,6:F2} ms  (peak {_perf.LayoutTimePeakMs:F2})");
        Line($"Render:  {_perf.RenderTimeMs,6:F2} ms  (peak {_perf.RenderTimePeakMs:F2})");
        Line($"Flush:   {_perf.FlushTimeMs,6:F2} ms  (peak {_perf.FlushTimePeakMs:F2})");
        Line($"Present: {_perf.PresentTimeMs,6:F2} ms  (peak {_perf.PresentTimePeakMs:F2})");
        Line($"Overlay: {_perf.OverlayTimeMs,6:F2} ms  (peak {_perf.OverlayTimePeakMs:F2})");
        Line($"Other:   {_perf.OtherTimeMs,6:F2} ms  (peak {_perf.OtherTimePeakMs:F2})");
        Line($"Elements: {_perf.ElementCount}");
        if (ContinuousRedraw)
            Line("Redraw: continuous");
    }
}
