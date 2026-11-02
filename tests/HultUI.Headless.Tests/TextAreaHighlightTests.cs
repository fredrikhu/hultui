using System.Reflection;
using System.Text;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A TextArea's <see cref="TextAreaElement.Highlights"/>, the layer find hands its matches to.
/// Until round 6 of plan 17, Meridian and Junction merged their match tints into the syntax
/// spans: a copy of every span of the document, sorted, on every find key and every Enter.
///
/// <para>These pin what the layer has to be to replace that: it draws exactly what the merged
/// tints drew — current and other matches, over syntax colours, under a selection's fill drawn
/// first, on wrapped rows, in a light and a dark theme — and a find step that hands the area
/// other highlights lays nothing out, works out no line starts and shapes nothing.</para>
/// </summary>
public sealed class TextAreaHighlightTests
{
    private static readonly Color Key = Color.FromHex("#4EC9B0"), Str = Color.FromHex("#CE9178"),
        Num = Color.FromHex("#B5CEA8"), Lit = Color.FromHex("#569CD6");
    private static readonly Color HitTint = new(210, 150, 40, 110), CurrentTint = new(230, 120, 20, 200);

    /// <summary>JSON-like lines, one in seven with a note long enough to wrap, and the colour
    /// spans a highlighter gives them: keys and string values with their quotes, numbers,
    /// literals; punctuation and spaces have none.</summary>
    private static (string Text, TextSpan[] Spans) Document(int lines)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lines; i++)
        {
            string note = i % 7 == 3
                ? "lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore"
                : "short";
            sb.Append("  {\"id\": ").Append(i).Append(", \"name\": \"customer ").Append(i)
                .Append("\", \"active\": ").Append(i % 3 == 0 ? "true" : "false")
                .Append(", \"note\": \"").Append(note).Append("\"},\n");
        }
        var text = sb.ToString();
        var spans = new List<TextSpan>();
        for (int i = 0; i < text.Length;)
        {
            char c = text[i];
            int end = i + 1;
            if (c == '"')
            {
                while (text[end] != '"') end++;
                end++;
                spans.Add(new TextSpan(i, end - i, end < text.Length && text[end] == ':' ? Key : Str));
            }
            else if (char.IsAsciiDigit(c))
            {
                while (char.IsAsciiDigit(text[end])) end++;
                spans.Add(new TextSpan(i, end - i, Num));
            }
            else if (char.IsAsciiLetter(c))
            {
                while (char.IsAsciiLetter(text[end])) end++;
                spans.Add(new TextSpan(i, end - i, Lit));
            }
            i = end;
        }
        return (text, [.. spans]);
    }

    /// <summary>Every occurrence of <paramref name="query"/>, none overlapping, as find makes them.</summary>
    private static List<(int Start, int Length)> Matches(string text, string query)
    {
        var hits = new List<(int, int)>();
        for (int at = text.IndexOf(query, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(query, at + query.Length, StringComparison.Ordinal))
            hits.Add((at, query.Length));
        return hits;
    }

    /// <summary>The tints merged into the spans as the apps merged them, sorted by start, here
    /// stably and a span before a tint at the same start (the apps' sort was not stable, so an
    /// equal start came either way).</summary>
    private static List<TextSpan> Merged(TextSpan[] spans, List<(int Start, int Length)> hits, int current) =>
        [.. spans.Select(s => (s, 0))
            .Concat(hits.Select((h, i) => (new TextSpan(h.Start, h.Length, default, i == current ? CurrentTint : HitTint), 1)))
            .OrderBy(p => p.Item1.Start).ThenBy(p => p.Item2)
            .Select(p => p.Item1)];

    private static TextHighlight[] Tints(List<(int Start, int Length)> hits, int current) =>
        [.. hits.Select((h, i) => new TextHighlight(h.Start, h.Length, i == current ? CurrentTint : HitTint))];

    // A frame drawn after the area's spans were set (setting them asks for no frame) and its pixels.
    private static byte[] Pixels(HeadlessSession session, TextAreaElement area)
    {
        area.MarkPaintDirty();
        session.PumpFrames(2);
        using var image = session.Window.CapturePixels();
        using var pixmap = image.PeekPixels();
        return pixmap.GetPixelSpan().ToArray();
    }

    private static int Differing(byte[] a, byte[] b)
    {
        int n = 0;
        for (int i = 0; i < a.Length; i += 4)
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2] || a[i + 3] != b[i + 3]) n++;
        return n;
    }

    /// <summary>
    /// The layer draws what the merged tints drew, pixel for pixel: matches inside tokens
    /// (<c>name</c>, <c>customer 1</c>), matches that start where a token starts and end where
    /// it ends (<c>true</c>, the digit <c>7</c>), matches that run out of one token, across
    /// punctuation and into the next (<c>": "</c>), side by side (two spaces), and across a
    /// soft wrap (the long note). The current match in its own tint, a selection's fill over
    /// several of them, and a light and a dark theme.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HighlightsDrawWhatTheMergedTintsDrew(bool dark)
    {
        var (text, spans) = Document(60);
        using var session = HeadlessSession.Create(640, 900, options: HeadlessOptions.Golden());
        session.Application.Theme = dark ? Theme.Dark : Theme.Light;
        var area = TextArea.New(text).FontFamily(FontFamilies.Monospace).FontSize(13f).FlexGrow(1);
        area.ReadOnly = true;
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Padding(8).Children(area));
        session.PumpFrames(3);

        // A selection from the second line into the sixth, over matches of every query.
        area.Focus();
        area.CursorPosition = text.IndexOf("\n", StringComparison.Ordinal) + 5;
        for (int i = 0; i < 4; i++) session.Window.PressKey(KeyCode.Down, KeyModifiers.Shift);
        session.PumpFrames(2);
        Assert.True(area.SelectedText.Length > 200, $"the selection is {area.SelectedText.Length} characters");

        area.Spans = spans;
        var plain = Pixels(session, area);
        int compared = 0;
        foreach (var query in new[] { "name", "customer 1", "true", "7", "\": \"", "  ", "dolor sit amet, consectetur adipiscing" })
        {
            var hits = Matches(text, query);
            Assert.True(hits.Count >= 3, $"\"{query}\" has {hits.Count} matches");
            int current = Math.Min(2, hits.Count - 1);

            area.Spans = Merged(spans, hits, current);
            area.Highlights = null;
            var merged = Pixels(session, area);

            area.Spans = spans;
            area.Highlights = Tints(hits, current);
            var layered = Pixels(session, area);

            Assert.True(Differing(plain, merged) > 50, $"\"{query}\": the merged tints changed nothing on screen");
            int differing = Differing(merged, layered);
            Assert.True(differing == 0, $"\"{query}\" ({(dark ? "dark" : "light")}): {differing} pixels differ from the merged tints");
            compared++;
        }
        Assert.Equal(7, compared);
    }

    /// <summary>A pane that shows a document with find's matches as the area's highlights, as
    /// Meridian's response pane and Junction's Files view do, rebuilt on each find step.</summary>
    private sealed class Pane(string text, TextSpan[] spans) : Component
    {
        public readonly Ref<TextAreaElement> Area = new();
        public IReadOnlyList<TextHighlight>? Highlights;

        public override Element Build()
        {
            var area = TextArea.New(text).FontFamily(FontFamilies.Monospace).FontSize(12f).FlexGrow(1);
            area.ReadOnly = true;
            area.Spans = spans;
            area.Highlights = Highlights;
            return Flex.Column().FlexGrow(1).Padding(8).Children(area.WithRef(Area));
        }

        public void Rebuild() => MarkDirty();
    }

    private sealed class CountingShaper(ITextShaper inner) : ITextShaper
    {
        public int Calls;

        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
        {
            Calls++;
            return inner.Shape(text, font, fontSize, rtl);
        }
    }

    // The session's own text pipeline (one per session), its shaper counted.
    private static CountingShaper CountShaping(HeadlessSession session)
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var platform = typeof(HeadlessWindow).GetField("_platform", Any)!.GetValue(session.Window)!;
        var surface = platform.GetType().GetProperty("RasterSurface", Any)!.GetValue(platform)!;
        var context = surface.GetType().GetProperty("RenderContext", Any)!.GetValue(surface)!;
        var pipeline = (TextPipeline)context.GetType().GetField("_pipeline", Any)!.GetValue(context)!;
        var field = typeof(TextPipeline).GetField("_shaper", Any)!;
        var counting = new CountingShaper((ITextShaper)field.GetValue(pipeline)!);
        field.SetValue(pipeline, counting);
        return counting;
    }

    private static List<string> LayoutDirty(Element e, List<string>? into = null)
    {
        into ??= [];
        if ((e.DirtyFlags & DirtyFlags.Layout) != 0) into.Add(e.GetType().Name);
        foreach (var c in e.Children) LayoutDirty(c, into);
        return into;
    }

    /// <summary>
    /// A find step is a paint. The pane rebuilds with another list of highlights — a key that
    /// finds other matches, an Enter that moves the current one, the search closed — and the
    /// area is patched with it: nothing is marked for layout, the area works out no line starts
    /// and nothing is shaped, though every step draws the area again with the new tints. The
    /// same list again does not even draw it.
    /// </summary>
    [Fact]
    public void AFindStepLaysNothingOut_WorksOutNoLines_AndShapesNothing()
    {
        var (text, spans) = Document(2_000);
        using var session = HeadlessSession.Create(900, 700);
        var pane = new Pane(text, spans);
        var root = pane.Mount();
        session.Application.SetRoot(root);
        session.PumpFrames(3);
        var area = pane.Area.Current!;
        var shaping = CountShaping(session);
        int lines = area.LineStartPasses;

        string[] queries = ["customer 1", "customer 19", "name", "true", "customer 1"];
        int steps = 0;
        foreach (var query in queries)
            for (int current = 0; current < 3; current++)
            {
                var hits = Matches(text, query);
                pane.Highlights = Tints(hits, current);
                pane.Rebuild();
                Assert.Empty(LayoutDirty(root));
                Assert.True((area.DirtyFlags & DirtyFlags.Paint) != 0, $"\"{query}\" match {current}: the area is not drawn again");
                session.PumpFrames(2);
                Assert.Same(area, pane.Area.Current);
                Assert.Same(pane.Highlights, area.Highlights);
                Assert.Same(spans, area.Spans);
                steps++;
            }
        pane.Highlights = null;
        pane.Rebuild();
        Assert.Empty(LayoutDirty(root));
        session.PumpFrames(2);
        Assert.Null(area.Highlights);

        Assert.Equal(15, steps);
        Assert.Equal(lines, area.LineStartPasses);
        Assert.Equal(0, shaping.Calls);

        // The same list again: nothing to draw.
        pane.Highlights = Tints(Matches(text, "name"), 0);
        pane.Rebuild();
        session.PumpFrames(2);
        var same = pane.Highlights;
        area.Highlights = same;
        Assert.True((area.DirtyFlags & DirtyFlags.Paint) == 0, "the same highlights again drew the area again");
    }
}
