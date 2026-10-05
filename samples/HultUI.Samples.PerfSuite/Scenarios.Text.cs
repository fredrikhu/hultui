using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace PerfSuite;

static partial class Scenarios
{
    private static IEnumerable<Scenario> TextScenarios()
    {
        yield return new Scenario("text/wrap 150 paragraphs resize",
            "150 paragraphs of 60 words during a resize drag: every paragraph broken into lines again",
            ctx =>
            {
                var column = Flex.Column(gap: 10).FlexShrink(0).Padding(16);
                var paragraphs = new Element[150];
                for (int i = 0; i < paragraphs.Length; i++)
                    paragraphs[i] = Text.New(Ui.Paragraph(ctx.Rng, 60)).FontSize(13f).LineHeight(19f);
                column.ReplaceChildren(paragraphs);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(column));
                return new Op { Run = step => ctx.Window.SetClientSize(Ui.DragWidth(step), 760) };
            });

        yield return new Scenario("text/create 2,000 unique labels",
            "2,000 labels no cache has seen (unique numbers in each): shaping and measuring cold",
            ctx =>
            {
                var column = Flex.Column().FlexShrink(0);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(column));
                int next = 1_000_000;
                return new Op
                {
                    Reset = _ => column.ReplaceChildren(),
                    Run = _ =>
                    {
                        var labels = new Element[2_000];
                        for (int i = 0; i < labels.Length; i++)
                            labels[i] = Text.New($"Item {next++} — {next * 7919 % 100_003} units").FontSize(13f);
                        column.ReplaceChildren(labels);
                    },
                };
            });

        yield return new Scenario("text/ellipsis 1,000 labels resize",
            "1,000 one-line labels truncated with an ellipsis, resize drag: every truncation point found again",
            ctx =>
            {
                var rows = new Element[1_000];
                for (int i = 0; i < rows.Length; i++)
                    rows[i] = Flex.Row(gap: 8).FlexShrink(0).Padding(new Thickness(2, 8)).Children(
                        Text.New(Ui.Paragraph(ctx.Rng, 14)).FontSize(13f).MaxLines(1)
                            .TextOverflow(TextOverflow.Ellipsis).FlexGrow(1).FlexShrink(1),
                        Text.New($"{i}").FontSize(12f).TextColor(Ui.Muted).FlexShrink(0));
                // Half the window wide, so the labels are truncated at every width of the drag.
                var pane = Flex.Column().Width(Dimension.Percent(50)).FlexShrink(0).Children(rows);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(pane));
                return new Op { Run = step => ctx.Window.SetClientSize(Ui.DragWidth(step), 760) };
            });

        yield return new Scenario("text/spans recolor (paint only)",
            "Re-colour the highlight spans of 40 of 200 paragraphs — syntax highlighting, search hits: paint-only spans",
            ctx =>
            {
                var paragraphs = SpanParagraphs(ctx.Rng, 200);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(Flex.Column(gap: 8).FlexShrink(0).Padding(12).Children(paragraphs)));
                return new Op
                {
                    Run = step =>
                    {
                        // Five groups of 40 take turns, and a group's colour flips on each of its turns.
                        var color = (step / 5) % 2 == 0 ? Color.FromHex("#DC2626") : Ui.Accent;
                        for (int i = 0; i < 40; i++)
                        {
                            var p = paragraphs[(step * 40 + i) % paragraphs.Length];
                            p.Spans(Highlights(p.Content, color, metric: false));
                        }
                    },
                };
            });

        yield return new Scenario("text/spans bold (metric)",
            "Toggle bold, larger spans in 40 of 200 paragraphs: spans that change shaping and line breaks",
            ctx =>
            {
                var paragraphs = SpanParagraphs(ctx.Rng, 200);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(Flex.Column(gap: 8).FlexShrink(0).Padding(12).Children(paragraphs)));
                return new Op
                {
                    Run = step =>
                    {
                        for (int i = 0; i < 40; i++)
                        {
                            var p = paragraphs[(step * 40 + i) % paragraphs.Length];
                            p.Spans(Highlights(p.Content, Ui.Accent, metric: (step / 5) % 2 == 0));
                        }
                    },
                };
            });
    }

    private static TextElement[] SpanParagraphs(Random rng, int count)
    {
        var paragraphs = new TextElement[count];
        for (int i = 0; i < count; i++)
        {
            var text = Ui.Paragraph(rng, 40);
            paragraphs[i] = Text.New(text).FontSize(13f).Spans(Highlights(text, Ui.Accent, metric: false));
        }
        return paragraphs;
    }

    /// <summary>Every third word highlighted.</summary>
    private static TextSpan[] Highlights(string text, Color color, bool metric)
    {
        var spans = new List<TextSpan>();
        int word = 0, start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != ' ') continue;
            if (word++ % 3 == 0)
                spans.Add(metric
                    ? new TextSpan(start, i - start, color, Weight: FontWeight.Bold, FontSize: 15f)
                    : new TextSpan(start, i - start, color));
            start = i + 1;
        }
        return spans.ToArray();
    }
}
