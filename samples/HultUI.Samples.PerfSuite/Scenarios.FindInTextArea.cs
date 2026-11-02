using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;

namespace PerfSuite;

static partial class Scenarios
{
    private const int FindLines = 2_000;

    private static IEnumerable<Scenario> FindInTextAreaScenarios()
    {
        yield return new Scenario("text/find typing in highlighted JSON in a TextArea",
            $"Type a character into the find bar over a {FindLines:N0}-line JSON response, as Meridian's response find does (\"a\" → \"at\"): the response is searched again, the pane rebuilt and its TextArea handed the matches to tint",
            ctx =>
            {
                var pane = FindPane(ctx, "at");
                return new Op
                {
                    Reset = _ => ctx.Window.PressKey(KeyCode.Backspace),
                    Run = _ => ctx.Window.TypeText("t"),
                    Check = _ => pane.Verify("at"),
                };
            });

        yield return new Scenario("text/find Enter in highlighted JSON in a TextArea",
            $"Press Enter in the find bar over the {FindLines:N0}-line JSON response: the next match becomes the current one, its tint changes and the pane rebuilds",
            ctx =>
            {
                var pane = FindPane(ctx, "at");
                int start = pane.Find.Search.Index;
                return new Op
                {
                    Run = _ => ctx.Window.PressKey(KeyCode.Enter),
                    Check = step => pane.Find.Search.Index != (start + step + 1) % pane.Find.Search.Hits.Count
                        ? $"match {pane.Find.Search.Index} is the current one after {step + 1} Enters from {start}"
                        : pane.Verify("at"),
                };
            });
    }

    private static FindInResponse FindPane(Ctx ctx, string query)
    {
        var text = Ui.JsonResponse(ctx.Rng, FindLines);
        var pane = new FindInResponse(text, Ui.JsonSpans(text));
        ctx.Mount(Flex.Column().FlexGrow(1).Padding(8).Children(pane.Mount()));
        pane.Find.Open();
        ctx.Pump(2);
        ctx.Window.TypeText(query);
        ctx.Pump(2);
        return pane;
    }

    /// <summary>
    /// Meridian's response pane with find open: a find bar over a read-only, syntax-coloured
    /// TextArea holding the whole response. A find step rebuilds the pane, and the pane hands the
    /// TextArea the response's colour spans, the same array every time, and the matches as its
    /// highlights. Until round 6 of plan 17 it merged the matches into a copy of the spans, as
    /// Meridian did, and sorted it.
    /// </summary>
    private sealed class FindInResponse : Component
    {
        private readonly string _body;
        private readonly TextSpan[] _spans;
        private readonly int[] _starts;
        private readonly Ref<TextAreaElement> _area = new();

        public FindInResponse(string body, TextSpan[] spans)
        {
            _body = body;
            _spans = spans;
            var lines = body.Split('\n');
            _starts = new int[lines.Length];
            for (int i = 1; i < lines.Length; i++) _starts[i] = _starts[i - 1] + lines[i - 1].Length + 1;
            Find = new FindBar(MarkDirty) { Body = () => _area.Current };
            Find.Search.Bind(lines.Length, lanes: 1, (line, _) => line >= 0 && line < lines.Length ? lines[line] : null);
        }

        public FindBar Find { get; }

        public override Element Build()
        {
            var area = JsonViewer();
            area.Text = _body;
            area.Spans = _spans;
            area.Highlights = Tints();
            var column = Flex.Column(gap: 0).FlexGrow(1);
            if (Find.IsOpen) column.AddChild(Find.Build());
            column.AddChild(area.WithRef(_area));
            return column;
        }

        protected override void OnAfterBuild() => Find.Service();

        private static readonly Color HitTint = Color.FromHex("#613315"), CurrentTint = Color.FromHex("#9E6A03");

        // As Meridian makes them: a highlight for each match, the current one in a colour of its own.
        private TextHighlight[]? Tints()
        {
            var search = Find.Search;
            if (!search.HasHits) return null;
            var current = search.Current;
            var tints = new TextHighlight[search.Hits.Count];
            for (int i = 0; i < tints.Length; i++)
            {
                var hit = search.Hits[i];
                bool isCurrent = current is { } c && c.Line == hit.Line && c.Start == hit.Start;
                tints[i] = new TextHighlight(_starts[hit.Line] + hit.Start, hit.Length, isCurrent ? CurrentTint : HitTint);
            }
            return tints;
        }

        /// <summary>Null when the query is <paramref name="query"/>, it has matches, and the TextArea
        /// on screen tints each of them.</summary>
        public string? Verify(string query)
        {
            var search = Find.Search;
            if (search.Query != query) return $"the query is \"{search.Query}\", not \"{query}\"";
            if (search.Hits.Count < 50) return $"only {search.Hits.Count} matches for \"{query}\"";
            if (_area.Current is not { } area) return "the response is not on screen";
            if (!ReferenceEquals(area.Spans, _spans)) return "the TextArea's spans are not the response's";
            int tinted = area.Highlights?.Count ?? 0;
            return tinted == search.Hits.Count ? null : $"{tinted} tints for {search.Hits.Count} matches";
        }
    }
}
