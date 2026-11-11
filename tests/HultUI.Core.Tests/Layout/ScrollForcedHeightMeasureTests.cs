using System.Text.Json;
using HultUI.Core.Builders;
using HultUI.Core.DevTools;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Layout;

/// <summary>
/// A column that scrolls down lays its children out against unbounded height, so a height forced
/// on it changes only its own height: its measurement forced to a height is its unforced one at
/// the same width, given that height. Each forced height used to be an entry of its own in the
/// four-entry measure cache, and every miss measured all the children again: Meridian's map list,
/// forced to four heights at each of four widths, measured its rows 24 times a click.
/// </summary>
public class ScrollForcedHeightMeasureTests
{
    [Fact]
    public void AScrollingColumnForcedToManyHeightsMeasuresItsRowsOnce()
    {
        var tracer = new FrameTracer();
        var engine = new LayoutEngine(new StubMeasureContext(), tracer);
        var (root, _, _) = Pane();

        // The list grows into what the header leaves it: forced to a new height at each size, in
        // the layout pass and in its pane's measure pass. Its rows fit at both widths.
        tracer.StartRecording();
        foreach (var width in new[] { 400f, 360f })
            foreach (var height in new[] { 300f, 320f, 340f, 360f, 380f })
                engine.PerformLayout(root, width, height);
        var measured = MeasureCounts(StopAndCapture(tracer));

        Assert.Equal(1, measured.GetValueOrDefault("list"));
        for (int i = 0; i < RowCount; i++)
            Assert.Equal(1, measured.GetValueOrDefault($"row-{i}"));
    }

    [Fact]
    public void AScrollingColumnForcedToAHeightIsLaidOutAtIt()
    {
        var engine = new LayoutEngine(new StubMeasureContext());
        var (root, list, footer) = Pane();
        foreach (var height in new[] { 300f, 340f, 320f })
        {
            engine.PerformLayout(root, 400, height);

            // Header 14, footer 14, the list's margin 4 above and below; its rows (30 × 14) overflow
            // it and scroll.
            Assert.Equal(height - 14 - 14 - 8, list.Layout.Height);
            Assert.Equal(list.Layout.AbsoluteY + list.Layout.Height + 4, footer.Layout.AbsoluteY);
            Assert.Equal(RowCount * 14f, list.ContentHeight);
        }
    }

    [Fact]
    public void AScrollingColumnOfASetHeightComesWithItsMaxAndMarginInItsColumn()
    {
        // Its height is the size its column measures it at (a flex basis), forced on it: the
        // measurement it gives back is that height held in its max, and its margins, or the
        // footer would sit on top of it.
        var engine = new LayoutEngine(new StubMeasureContext());
        var (_, list, footer) = Pane();
        list.Flex.FlexGrow = 0;
        list.Height(Dimension.Px(200)).MaxHeight(Dimension.Px(180));
        var root = (Element)list.Parent!;
        foreach (var width in new[] { 400f, 360f })
        {
            engine.PerformLayout(root, width, 600);

            Assert.Equal(180f, list.Layout.Height);
            Assert.Equal(14f + 4f, list.Layout.AbsoluteY);
            Assert.Equal(14f + 4f + 180f + 4f, footer.Layout.AbsoluteY);
        }
    }

    private const int RowCount = 30;

    private static (Element Root, ScrollViewElement List, Element Footer) Pane()
    {
        var rows = new Element[RowCount];
        for (int i = 0; i < RowCount; i++)
        {
            rows[i] = Flex.Row().FlexShrink(0).Children(Text.New($"row {i}"));
            rows[i].Key = $"row-{i}";
        }
        var list = ScrollView.New().FlexGrow(1).Margin(new Thickness(4, 0)).Children(rows);
        list.Key = "list";
        var footer = Text.New("footer");
        var pane = Flex.Column().FlexGrow(1).Children(Text.New("header"), list, footer);
        return (Flex.Column().Children(pane), list, footer);
    }

    private static string StopAndCapture(FrameTracer tracer)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hultui-scroll-forced-{Guid.NewGuid():N}.json");
        tracer.StopRecording(path);
        var json = File.ReadAllText(path);
        File.Delete(path);
        return json;
    }

    /// <summary>Measurements that missed the cache (the "Measure" scopes), by element key.</summary>
    private static Dictionary<string, int> MeasureCounts(string json)
    {
        var counts = new Dictionary<string, int>();
        using var doc = JsonDocument.Parse(json);
        foreach (var evt in doc.RootElement.EnumerateArray())
        {
            if (!evt.TryGetProperty("ph", out var ph) || ph.GetString() != "X") continue;
            if (!evt.TryGetProperty("cat", out var cat) || cat.GetString() != "Measure") continue;
            if (!evt.TryGetProperty("args", out var args) || !args.TryGetProperty("detail", out var detail)) continue;
            if (detail.GetString() is { Length: > 0 } key) counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        return counts;
    }
}
