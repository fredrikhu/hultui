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

        yield return new Scenario("text/bidi wrap 150 paragraphs resize",
            "text/wrap with Hebrew and Latin mixed: 150 paragraphs on the BiDi path broken into lines again",
            ctx =>
            {
                var column = Flex.Column(gap: 10).FlexShrink(0).Padding(16);
                var paragraphs = new Element[150];
                for (int i = 0; i < paragraphs.Length; i++)
                    paragraphs[i] = Text.New(Ui.BidiParagraph(ctx.Rng, 60)).FontSize(13f).LineHeight(19f);
                column.ReplaceChildren(paragraphs);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(column));
                return new Op { Run = step => ctx.Window.SetClientSize(Ui.DragWidth(step), 760) };
            });

        yield return new Scenario("text/bidi ellipsis 1,000 labels resize",
            "text/ellipsis with Hebrew and Latin mixed: 1,000 labels on the BiDi path, truncated again at each width",
            ctx =>
            {
                var rows = new Element[1_000];
                for (int i = 0; i < rows.Length; i++)
                    rows[i] = Flex.Row(gap: 8).FlexShrink(0).Padding(new Thickness(2, 8)).Children(
                        Text.New(Ui.BidiParagraph(ctx.Rng, 14)).FontSize(13f).MaxLines(1)
                            .TextOverflow(TextOverflow.Ellipsis).FlexGrow(1).FlexShrink(1),
                        Text.New($"{i}").FontSize(12f).TextColor(Ui.Muted).FlexShrink(0));
                var pane = Flex.Column().Width(Dimension.Percent(50)).FlexShrink(0).Children(rows);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(pane));
                return new Op { Run = step => ctx.Window.SetClientSize(Ui.DragWidth(step), 760) };
            });

        yield return new Scenario("text/styled wrap 150 paragraphs resize",
            "text/wrap with every third word bold and larger: 150 paragraphs on the styled path broken into lines again",
            ctx =>
            {
                var column = Flex.Column(gap: 10).FlexShrink(0).Padding(16);
                var paragraphs = new Element[150];
                for (int i = 0; i < paragraphs.Length; i++)
                {
                    var text = Ui.Paragraph(ctx.Rng, 60);
                    paragraphs[i] = Text.New(text).FontSize(13f).LineHeight(19f).Spans(Highlights(text, Ui.Accent, metric: true));
                }
                column.ReplaceChildren(paragraphs);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(column));
                return new Op { Run = step => ctx.Window.SetClientSize(Ui.DragWidth(step), 760) };
            });

        yield return new Scenario("text/styled ellipsis 1,000 labels resize",
            "text/ellipsis with every third word bold and larger: 1,000 labels on the styled path, truncated again at each width",
            ctx => StyledLabels(ctx, () => Ui.Paragraph(ctx.Rng, 14)));

        yield return new Scenario("text/styled bidi wrap 150 paragraphs resize",
            "text/bidi wrap with every third word bold and larger: styled text on the BiDi path broken into lines again",
            ctx =>
            {
                var column = Flex.Column(gap: 10).FlexShrink(0).Padding(16);
                var paragraphs = new Element[150];
                for (int i = 0; i < paragraphs.Length; i++)
                {
                    var text = Ui.BidiParagraph(ctx.Rng, 60);
                    paragraphs[i] = Text.New(text).FontSize(13f).LineHeight(19f).Spans(Highlights(text, Ui.Accent, metric: true));
                }
                column.ReplaceChildren(paragraphs);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(column));
                return new Op { Run = step => ctx.Window.SetClientSize(Ui.DragWidth(step), 760) };
            });

        yield return new Scenario("text/styled bidi ellipsis 1,000 labels resize",
            "text/bidi ellipsis with every third word bold and larger: styled labels on the BiDi path, truncated again",
            ctx => StyledLabels(ctx, () => Ui.BidiParagraph(ctx.Rng, 14)));

        yield return new Scenario("text/multi-font ellipsis 1,000 labels resize",
            "text/ellipsis in a monospace face without IPA, an IPA word in every third place: labels of several font runs",
            ctx =>
            {
                var rows = new Element[1_000];
                for (int i = 0; i < rows.Length; i++)
                    rows[i] = Flex.Row(gap: 8).FlexShrink(0).Padding(new Thickness(2, 8)).Children(
                        Text.New(Ui.MultiFontParagraph(ctx.Rng, 14)).FontFamily(FontFamilies.Monospace).FontSize(13f)
                            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis).FlexGrow(1).FlexShrink(1),
                        Text.New($"{i}").FontSize(12f).TextColor(Ui.Muted).FlexShrink(0));
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

        yield return new Scenario("text/open a long document in a TextArea",
            "A TextArea given a 900-line document whose paragraphs run to 1,700 characters: every line wrapped for the first time",
            ctx =>
            {
                // Twelve documents in turn, so no line or prefix is still cached when its
                // document comes round again — opening a file nobody has looked at yet.
                var documents = new string[12];
                for (int d = 0; d < documents.Length; d++) documents[d] = Ui.Notes(ctx.Rng);
                var area = TextArea.New("").FlexGrow(1);
                ctx.Mount(Flex.Column().FlexGrow(1).Padding(8).Children(area));
                return new Op
                {
                    Run = step => area.Text = documents[step % documents.Length],
                    Check = step => area.Text == documents[step % documents.Length] ? null : "the document did not reach the TextArea",
                };
            });

        yield return new Scenario("text/open highlighted JSON in a TextArea",
            $"Meridian's response viewer handed a {JsonLines:N0}-line pretty-printed JSON response and its colour spans: a read-only monospace TextArea measures every line, then draws the first screen",
            ctx =>
            {
                // Three responses in turn. A response's 19,000 lines go through a 2,048-line
                // layout cache, so none of them is still there when it comes round again.
                var responses = new (string Text, TextSpan[] Spans)[3];
                for (int d = 0; d < responses.Length; d++)
                {
                    var text = Ui.JsonResponse(ctx.Rng, JsonLines);
                    responses[d] = (text, Ui.JsonSpans(text));
                }
                var area = JsonViewer();
                ctx.Mount(Flex.Column().FlexGrow(1).Padding(8).Children(area));
                return new Op
                {
                    Run = step =>
                    {
                        var (text, spans) = responses[step % responses.Length];
                        area.Text = text;
                        area.Spans = spans;
                    },
                    Check = step => JsonShown(ctx, area, responses[step % responses.Length].Text),
                };
            });

        yield return new Scenario("text/wheel highlighted JSON in a TextArea",
            $"Wheel 3 notches in the middle of the {JsonLines:N0}-line JSON response: each line drawn takes its colour spans out of the whole response's",
            ctx =>
            {
                var text = Ui.JsonResponse(ctx.Rng, JsonLines);
                var area = JsonViewer();
                area.Text = text;
                area.Spans = Ui.JsonSpans(text);
                ctx.Mount(Flex.Column().FlexGrow(1).Padding(8).Children(area));
                area.ScrollOffsetY = float.MaxValue;
                area.ScrollOffsetY /= 2;
                ctx.Pump();
                var box = area.WindowBorderBox;
                return new Op
                {
                    Run = step => ctx.Window.Wheel(box.X + box.Width / 2, box.Y + box.Height / 2, 0, WheelDirection(step) * 3),
                    Check = ScrolledTo(() => area.ScrollOffsetY),
                };
            });
    }

    /// <summary>
    /// Meridian's response size, near the most it colours: HultUI.Syntax leaves a text of more
    /// than 20,000 lines without spans. Meridian's survey response was 312 KB in ~22,500 lines;
    /// this one is 368 KB in 19,013 (see <see cref="Ui.JsonResponse"/>).
    /// </summary>
    private const int JsonLines = 19_000;

    /// <summary>Meridian's response body: read-only, monospace 12 px, Dark+ colours on a dark field.</summary>
    private static TextAreaElement JsonViewer()
    {
        var area = TextArea.New("").FontFamily(FontFamilies.Monospace).FontSize(12f)
            .TextColor(Color.FromHex("#D4D4D4")).FlexGrow(1);
        area.ReadOnly = true;
        area.Style = new TextInputStyle { Background = Color.FromHex("#1E1E1E") };
        return area;
    }

    /// <summary>
    /// Null when <paramref name="text"/> is the TextArea's, every line of it is laid out (the
    /// scroll range is the whole response, probed and put back) and the first screen shows
    /// keys and string values in their span colours.
    /// </summary>
    private static string? JsonShown(Ctx ctx, TextAreaElement area, string text)
    {
        if (!ReferenceEquals(area.Text, text)) return "the response did not reach the TextArea";

        int lines = 1;
        foreach (char c in text) if (c == '\n') lines++;
        float viewport = area.Layout.AbsoluteContentBox.Height;
        float was = area.ScrollOffsetY;
        area.ScrollOffsetY = float.MaxValue;
        float height = area.ScrollOffsetY + viewport;
        area.ScrollOffsetY = was;
        if (height / lines is < 10f or > 30f)
            return $"the TextArea is {height:0} px tall for {lines:N0} lines";

        // Anti-aliased glyphs on the dark field: a key's teal and a string's orange each show as
        // pixels well off the grey of uncoloured text. Two dozen of each on the first screen.
        using var image = ctx.Window.CapturePixels();
        using var pixels = image.PeekPixels();
        var box = area.WindowContentBox;
        int keys = 0, strings = 0;
        for (int y = (int)box.Y; y < (int)Math.Min(box.Bottom, box.Y + 240); y++)
            for (int x = (int)box.X; x < (int)Math.Min(box.Right, pixels.Width); x++)
            {
                var p = pixels.GetPixelColor(x, y);
                if (p.Green - p.Red > 60 && p.Blue - p.Red > 40) keys++;
                else if (p.Red - p.Blue > 45 && p.Red > 100) strings++;
            }
        return keys >= 24 && strings >= 24 ? null
            : $"the first screen shows {keys} key-coloured and {strings} string-coloured pixels";
    }

    // 1,000 one-line labels with metric spans in half a window: truncated at every width of the drag.
    private static Op StyledLabels(Ctx ctx, Func<string> label)
    {
        var rows = new Element[1_000];
        for (int i = 0; i < rows.Length; i++)
        {
            var text = label();
            rows[i] = Flex.Row(gap: 8).FlexShrink(0).Padding(new Thickness(2, 8)).Children(
                Text.New(text).FontSize(13f).MaxLines(1).TextOverflow(TextOverflow.Ellipsis)
                    .Spans(Highlights(text, Ui.Accent, metric: true)).FlexGrow(1).FlexShrink(1),
                Text.New($"{i}").FontSize(12f).TextColor(Ui.Muted).FlexShrink(0));
        }
        var pane = Flex.Column().Width(Dimension.Percent(50)).FlexShrink(0).Children(rows);
        ctx.Mount(ScrollView.New().FlexGrow(1).Children(pane));
        return new Op { Run = step => ctx.Window.SetClientSize(Ui.DragWidth(step), 760) };
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
