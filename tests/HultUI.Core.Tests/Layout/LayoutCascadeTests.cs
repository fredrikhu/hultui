using System.Text.Json;
using HultUI.Core.Builders;
using HultUI.Core.DevTools;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
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
    public void DeepNesting_EachElementIsMeasuredAndLaidOutAtMostOncePerPass()
    {
        // 8 nested column containers, each holding a row with two flex
        // children. Pre-fix: leaf laid out 2^16 ≈ 65k times. Post-Phase1-fix:
        // 1× layout but O(depth) measures. With the measure cache: 1× each.
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

        foreach (var (key, count) in layoutPerKey)
            Assert.True(count == 1, $"Layout cascade: '{key}' was laid out {count}× (expected 1).");

        foreach (var (key, count) in measurePerKey)
            Assert.True(count == 1, $"Measure cascade: '{key}' was measured {count}× (expected 1).");
    }

    [Fact]
    public void GrandchildSizeChange_PropagatesIntoFlexDistribution()
    {
        // The bug: a non-leaf parent has CacheMeasureValid=true after layout 1.
        // A grandchild's intrinsic size then changes. The parent's measure
        // cache must be invalidated (via MarkLayoutDirty propagation) so that
        // its sibling's flex distribution sees the new size — otherwise the
        // sibling steals the parent's growth and the parent gets squished.
        var engine = new LayoutEngine(new StubMeasureContext());

        var contentBox = Box.New().Width(100).Height(50);
        var autoSized = Box.New().Height(50).Children(contentBox); // sizes to content
        var growable = Box.New().FlexGrow(1).Height(50);
        var root = Flex.Row().Width(800).Height(50).Children(autoSized, growable);

        engine.PerformLayout(root, 800, 600);
        Assert.Equal(100, autoSized.Layout.Width);
        Assert.Equal(700, growable.Layout.Width);

        // Change the grandchild's intrinsic width and mark dirty.
        contentBox.Box.Width = Dimension.Px(250);
        contentBox.MarkLayoutDirty();

        engine.PerformLayout(root, 800, 600);

        // autoSized's measure cache must be invalidated by MarkLayoutDirty,
        // otherwise Phase 1 returns the stale 100 px and growable keeps 700.
        Assert.Equal(250, autoSized.Layout.Width);
        Assert.Equal(550, growable.Layout.Width);
    }

    [Fact]
    public void ChildReplacedAfterFirstLayout_NewChildGetsPositioned()
    {
        // Reconciliation scenario: a subtree gets a new child between layout
        // passes, but the parent's own size and constraint stay the same.
        // The measure cache must not let ComputeLayout short-circuit and
        // skip positioning the new child — that was the bug where commit
        // detail / status bar didn't render until the window was resized.
        var engine = new LayoutEngine(new StubMeasureContext());

        var root = Flex.Column().Width(800).Height(600);
        var middle = Flex.Row().FlexGrow(1);
        root.AddChild(middle);
        var firstChild = Box.New().Width(100).Height(50);
        middle.AddChild(firstChild);

        engine.PerformLayout(root, 800, 600);
        Assert.Equal(100, firstChild.Layout.Width);

        // Replace the child — same outer size constraint, same parent size,
        // but different element instance.
        var newChild = Box.New().Width(100).Height(50);
        middle.RemoveChild(firstChild);
        middle.AddChild(newChild);

        engine.PerformLayout(root, 800, 600);

        // The new child must have been laid out (Width set, AbsoluteX correct).
        Assert.Equal(100, newChild.Layout.Width);
        Assert.Equal(50, newChild.Layout.Height);
        // AbsoluteX should be 0 (first child of a left-aligned row at the root).
        Assert.Equal(0, newChild.Layout.AbsoluteX);
    }

    [Fact]
    public void StableTree_SecondLayoutPassShortCircuitsAtRoot()
    {
        // When nothing changes between two PerformLayout calls, the root's
        // ComputeLayout cache should hit and skip the entire subtree. Only
        // the root itself emits a (cache-hit) trace event; no descendant
        // Layout or Measure events should fire.
        var tracer = new FrameTracer();
        var engine = new LayoutEngine(new StubMeasureContext(), tracer);

        Element node = Box.New().Width(50).Height(20);
        node.Key = "leaf";
        for (int d = 0; d < 4; d++)
        {
            var inner = Flex.Row().FlexGrow(1).Children(node, Box.New().FlexGrow(1));
            var outer = Flex.Column().FlexGrow(1).Children(inner);
            outer.Key = $"nest-{d}";
            node = outer;
        }

        engine.PerformLayout(node, 800, 600); // warm caches

        tracer.StartRecording();
        engine.PerformLayout(node, 800, 600);
        var (layoutPerKey, measurePerKey) = ReadKeyedCounts(StopAndCapture(tracer));

        // Only the root short-circuits at the cache check; descendants are
        // never visited so they have zero events.
        Assert.Equal(new[] { "nest-3" }, layoutPerKey.Keys.OrderBy(k => k).ToArray());
        Assert.Equal(1, layoutPerKey["nest-3"]);
        Assert.Empty(measurePerKey);
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
