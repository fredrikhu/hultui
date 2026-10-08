using System.Text.Json;
using HultUI.Core.Builders;
using HultUI.Core.DevTools;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Core.Tests.Elements;
using Xunit;

namespace HultUI.Core.Tests.DevTools;

/// <summary>
/// The layout and render passes open a trace scope for every element they visit, every
/// frame, recording or not; a window's tracer is never null. The scope is named after the
/// element's type. On NativeAOT <c>Type.Name</c> builds a new string on every call — 683 of
/// the 686 KB a frame of a 2,000-row resize allocated, and 7 of 7.7 KB a frame of 400 tiles —
/// so the passes hand the element to <see cref="FrameTracer.BeginElement"/>, which looks the
/// name up only while recording.
///
/// <para>The JIT caches <c>Type.Name</c>, so an allocation count here would read zero with
/// the name built eagerly too. The tracer counts its lookups instead: none while not
/// recording, and one for every element scope while recording, which a pass that built the
/// name itself and called <see cref="FrameTracer.Begin"/> would not reach.</para>
/// </summary>
public class ElementTraceScopeTests
{
    [Fact]
    public void NotRecording_LayoutAndRenderLookUpNoElementNames()
    {
        var tracer = new FrameTracer();
        var root = BuildTree();
        var engine = new LayoutEngine(new StubMeasureContext(), tracer);

        // Three frames at different widths, so every element is measured and laid out
        // again rather than skipped by the caches.
        foreach (var width in new[] { 400f, 360f, 420f })
        {
            engine.PerformLayout(root, width, 300);
            ElementRenderer.RenderTree(root, new NullRenderContext(), tracer);
        }

        Assert.Equal(0, tracer.ElementNameLookups);
    }

    [Fact]
    public void Recording_EveryElementScopeIsNamedAfterItsTypeWithItsKeyOrName()
    {
        var tracer = new FrameTracer();
        var root = BuildTree();
        var engine = new LayoutEngine(new StubMeasureContext(), tracer);

        tracer.StartRecording();
        engine.PerformLayout(root, 400, 300);
        ElementRenderer.RenderTree(root, new NullRenderContext(), tracer);
        var events = ElementEvents(StopAndCapture(tracer));

        // The names and details the scopes carried before the lookup moved into the tracer.
        var expected = new List<(string Name, string? Detail)>();
        Walk(root, e => expected.Add((e.GetType().Name, e.Key ?? e.Name)));

        // Nothing is culled (no scroll offset, everything fits), so render visits each
        // element exactly once.
        Assert.Equal(
            expected.Order().ToArray(),
            events.Where(e => e.Category == "Render").Select(e => (e.Name, e.Detail)).Order().ToArray());

        // Layout visits each element at least once; layout and measure visit nothing else.
        var laidOut = events.Where(e => e.Category == "Layout").Select(e => (e.Name, e.Detail)).ToList();
        var measured = events.Where(e => e.Category == "Measure").Select(e => (e.Name, e.Detail)).ToList();
        Assert.All(expected, x => Assert.Contains(x, laidOut));
        Assert.All(laidOut, s => Assert.Contains(s, expected));
        Assert.NotEmpty(measured);
        Assert.All(measured, s => Assert.Contains(s, expected));

        // Every element scope went through BeginElement: one name looked up per event.
        Assert.Equal(events.Count, tracer.ElementNameLookups);
    }

    /// <summary>
    /// A column of keyed rows, a named label, an unlabelled box and a scroll view: three
    /// element types, and details from <see cref="Element.Key"/>, from
    /// <see cref="Element.Name"/>, and absent.
    /// </summary>
    private static Element BuildTree()
    {
        var label = Text.New("named");
        label.Name = "the-label";

        var rows = new List<Element>();
        for (int i = 0; i < 6; i++)
        {
            var row = Flex.Row(gap: 4).Children(Text.New($"row {i}"), Box.New().Width(20).Height(10));
            row.Key = $"row-{i}";
            rows.Add(row);
        }
        var list = Flex.Column().Children([.. rows]);

        var scroll = ScrollView.New().Height(Dimension.Px(200));
        scroll.ReplaceChildren(list);

        var root = Flex.Column(gap: 2).Children(label, Box.New().Height(8), scroll);
        StyleResolver.Resolve(root, Theme.Light);
        return root;
    }

    private static void Walk(Element element, Action<Element> visit)
    {
        visit(element);
        foreach (var child in element.Children)
            Walk(child, visit);
    }

    private static string StopAndCapture(FrameTracer tracer)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hultui-element-scopes-{Guid.NewGuid():N}.json");
        tracer.StopRecording(path);
        var json = File.ReadAllText(path);
        File.Delete(path);
        return json;
    }

    private static List<(string Category, string Name, string? Detail)> ElementEvents(string json)
    {
        var events = new List<(string, string, string?)>();
        using var doc = JsonDocument.Parse(json);
        foreach (var evt in doc.RootElement.EnumerateArray())
        {
            if (evt.GetProperty("ph").GetString() != "X") continue;
            var category = evt.GetProperty("cat").GetString()!;
            if (category is not ("Layout" or "Measure" or "Render")) continue;
            string? detail = evt.GetProperty("args").TryGetProperty("detail", out var d) ? d.GetString() : null;
            events.Add((category, evt.GetProperty("name").GetString()!, detail));
        }
        return events;
    }
}
