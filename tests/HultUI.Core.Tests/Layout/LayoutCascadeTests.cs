using System.Text.Json;
using HultUI.Core.Builders;
using HultUI.Core.DevTools;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using Xunit;

namespace HultUI.Core.Tests.Layout;

/// <summary>
/// Regression coverage for the exponential layout cascade. Phase 1 of
/// LayoutFlexChildren must measure children without writing element.Layout or
/// touching the layout cache; Phase 6 is the single authoritative
/// ComputeLayout pass. If Phase 1 calls ComputeLayout instead, each container
/// layer doubles the work and a single keyed element shows up 2^depth times.
/// </summary>
public class LayoutCascadeTests
{
    [Fact]
    public void DeepNesting_LayoutPassWritesEachElementOnce()
    {
        // 8 nested column containers, each holding a row with two flex
        // children. The deepest leaf would be laid out 2^16 ≈ 65k times under
        // the pre-fix cascade (8 keyed columns + 8 unkeyed rows = 16 levels).
        const int Depth = 8;

        var tracer = new FrameTracer();
        var engine = new LayoutEngine(new StubMeasureContext(), tracer);

        Element node = Box.New().Width(50).Height(20);
        node.Key = "leaf";
        for (int d = 0; d < Depth; d++)
        {
            var inner = Flex.Row().FlexGrow(1).Children(node, Box.New().FlexGrow(1));
            var outer = Flex.Column().FlexGrow(1).Children(inner);
            outer.Key = $"nest-{d}";
            node = outer;
        }

        tracer.StartRecording();
        engine.PerformLayout(node, 800, 600);
        var (layoutPerKey, measurePerKey) = ReadKeyedCounts(StopAndCapture(tracer));

        // Strict invariant: ComputeLayout writes element.Layout, so each
        // element must be laid out exactly once per PerformLayout call.
        foreach (var (key, count) in layoutPerKey)
            Assert.True(count == 1, $"Layout cascade: '{key}' was laid out {count}× (expected 1).");

        // Looser invariant for measurement: each ancestor level may measure
        // its descendants once during its Phase 1, so the count for the
        // deepest leaf grows linearly with depth — not exponentially.
        // Pre-fix this would be 2^Depth; post-fix it's bounded by ~2·Depth.
        int maxMeasure = measurePerKey.Values.DefaultIfEmpty(0).Max();
        Assert.True(maxMeasure <= 4 * Depth,
            $"Measure cascade: max measure count {maxMeasure} exceeds linear bound 4·{Depth}.");
    }

    private static string StopAndCapture(FrameTracer tracer)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hultui-cascade-{Guid.NewGuid():N}.json");
        tracer.StopRecording(path);
        var json = File.ReadAllText(path);
        File.Delete(path);
        return json;
    }

    private static (Dictionary<string, int> Layout, Dictionary<string, int> Measure) ReadKeyedCounts(string json)
    {
        var layout = new Dictionary<string, int>();
        var measure = new Dictionary<string, int>();
        using var doc = JsonDocument.Parse(json);
        foreach (var evt in doc.RootElement.EnumerateArray())
        {
            if (!evt.TryGetProperty("ph", out var ph) || ph.GetString() != "X") continue;
            if (!evt.TryGetProperty("cat", out var catProp)) continue;
            var cat = catProp.GetString();
            if (cat != "Layout" && cat != "Measure") continue;
            if (!evt.TryGetProperty("args", out var args)) continue;
            if (!args.TryGetProperty("detail", out var detail)) continue;
            var key = detail.GetString();
            if (string.IsNullOrEmpty(key)) continue;

            var bucket = cat == "Layout" ? layout : measure;
            bucket[key] = bucket.GetValueOrDefault(key) + 1;
        }
        return (layout, measure);
    }
}
