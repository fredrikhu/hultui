using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
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

        yield return new Scenario("text/create 2,000 unique emoji labels",
            "text/create with emoji in every label: half with emoji alone in the BMP (the plain path), half with faces, "
            + "a heart, a keycap and a trade mark asking for emoji presentation, a family, a skin tone and a flag (the BiDi path)",
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
                        {
                            var set = i % 2 == 0 ? BmpEmoji : SequenceEmoji;
                            labels[i] = Text.New($"{set[i % set.Length]} Item {next++} — {next * 7919 % 100_003} units {set[i / 2 % set.Length]}")
                                .FontSize(13f);
                        }
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

        yield return new Scenario("text/resize a wrapped TextArea",
            "Drag the window narrower with the 900-line notes file open in a TextArea: every line too long for the new width wrapped again",
            ctx =>
            {
                var area = TextArea.New(Ui.Notes(ctx.Rng)).FlexGrow(1);
                ctx.Mount(Flex.Column().FlexGrow(1).Padding(8).Children(area));
                return new Op
                {
                    Run = step => ctx.Window.SetClientSize(Ui.DragWidth(step), 760),
                    // The rows are worked out for the width the TextArea is drawn at.
                    Check = step => Math.Abs(area.WindowBorderBox.Width - (Ui.DragWidth(step) - 16)) < 0.5f ? null
                        : $"the TextArea is {area.WindowBorderBox.Width:0} px wide, not the window's {Ui.DragWidth(step)} px less the padding",
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

        yield return new Scenario("text/rebuild around highlighted JSON in a TextArea",
            $"A keystroke elsewhere rebuilds the pane holding the {JsonLines:N0}-line JSON response, as Meridian's URL and body editors rebuild the app: the TextArea is patched with the same text and spans",
            ctx =>
            {
                var text = Ui.JsonResponse(ctx.Rng, JsonLines);
                var pane = new ResponsePane(text, Ui.JsonSpans(text));
                ctx.Mount(Flex.Column().FlexGrow(1).Padding(8).Children(pane.Mount()));
                var area = pane.Area!;
                return new Op
                {
                    Run = _ => pane.Edit(),
                    Check = step => pane.Verify(area, step + 1) ?? JsonShown(ctx, area, text),
                };
            });

        yield return new Scenario("text/selectable viewer hover",
            $"Move the pointer to another line of a file viewer of {ViewerLines} selectable, syntax-coloured lines in one selection group, as Junction's diff and Vesper's reader are: every line on screen drawn again",
            ctx =>
            {
                var viewer = new SelectableViewer(ctx);
                // ~18 px lines: a 63 px step lands on another line each time.
                static float PointerY(int step) => 30 + step * 63 % 660;
                return new Op
                {
                    Run = step => ctx.Window.MoveMouse(500, PointerY(step)),
                    Check = step => viewer.Hovered(PointerY(step)) ?? viewer.Drawn(),
                };
            });

        yield return new Scenario("text/selectable viewer wheel",
            $"Wheel 3 notches through the file viewer of {ViewerLines} selectable lines: every line on screen drawn at a new place",
            ctx =>
            {
                var viewer = new SelectableViewer(ctx);
                var scrolled = ScrolledTo(() => viewer.View.ScrollOffsetY);
                return new Op
                {
                    Run = step => ctx.Window.Wheel(500, 380, 0, WheelDirection(step) * 3),
                    Check = step => scrolled(step) ?? viewer.Drawn(),
                };
            });
    }

    private const int ViewerLines = 400;

    // Emoji of the Basic Multilingual Plane that ask for emoji presentation on their own:
    // ⭐ ✅ ☕ ⚡ ❌ ⌛ ⛔ ⚽. Nothing in them sends a label to the BiDi path.
    private static readonly string[] BmpEmoji =
        [Cp(0x2B50), Cp(0x2705), Cp(0x2615), Cp(0x26A1), Cp(0x274C), Cp(0x231B), Cp(0x26D4), Cp(0x26BD)];

    // 😀 👍🏽 ❤️ #️⃣ 👨‍👩‍👧 🇸🇪 ™️ 🎉 🚀 🙂 ©️ 1️⃣: surrogates, variation selectors, a keycap, joiners,
    // a skin tone, a flag.
    private static readonly string[] SequenceEmoji =
    [
        Cp(0x1F600), Cp(0x1F44D, 0x1F3FD), Cp(0x2764, 0xFE0F), Cp('#', 0xFE0F, 0x20E3),
        Cp(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467), Cp(0x1F1F8, 0x1F1EA), Cp(0x2122, 0xFE0F), Cp(0x1F389),
        Cp(0x1F680), Cp(0x1F642), Cp(0xA9, 0xFE0F), Cp('1', 0xFE0F, 0x20E3),
    ];

    private static string Cp(params int[] codePoints) => string.Concat(codePoints.Select(char.ConvertFromUtf32));

    /// <summary>
    /// A file viewer as Junction's diff and Vesper's reader build one: a line number, then the
    /// line itself, monospace 12 px in its syntax colours, selectable, every line in one
    /// <see cref="TextSelectionGroup"/>, and a hover tint on the row. Not virtualized: the same
    /// labels stay on screen from frame to frame, and only where they are drawn changes.
    /// </summary>
    private sealed class SelectableViewer
    {
        private static readonly Color Field = Color.FromHex("#1E1E1E");
        private static readonly Color Plain = Color.FromHex("#D4D4D4");
        private static readonly Color HoverLine = Color.FromHex("#2A2D2E");

        private readonly Element[] _rows = new Element[ViewerLines];
        private readonly TextElement[] _code = new TextElement[ViewerLines];

        public ScrollViewElement View { get; }

        public SelectableViewer(Ctx ctx)
        {
            var lines = new string[ViewerLines];
            var group = new TextSelectionGroup();
            for (int i = 0; i < lines.Length; i++)
            {
                var (text, spans) = CodeLine(ctx.Rng, i);
                lines[i] = text;
                _code[i] = Text.New(text).FontFamily(FontFamilies.Monospace).FontSize(12f).TextColor(Plain)
                    .Selectable().SelectionGroup(group, i).Spans(spans).Padding(new Thickness(2, 8)).FlexGrow(1);
                _rows[i] = Flex.Row(gap: 0).FlexShrink(0).AlignItems(AlignItems.Stretch)
                    .OnHover(r => r.Background(HoverLine))
                    .Children(
                        Text.New($"{i + 1}").FontFamily(FontFamilies.Monospace).FontSize(12f).TextColor(Ui.Muted)
                            .Width(Dimension.Px(48)).Padding(new Thickness(2, 8)),
                        _code[i]);
            }
            group.Bind(lines, lines.Length, i => lines[i]);
            View = ScrollView.New().FlexGrow(1).Background(Field).Children(Flex.Column().FlexShrink(0).Children(_rows));
            ctx.Mount(View);
        }

        /// <summary>Null when the row under the pointer, and only it, is hovered.</summary>
        public string? Hovered(float pointerY)
        {
            int hovered = -1;
            for (int i = 0; i < _rows.Length; i++)
            {
                if (!_rows[i].IsHovered) continue;
                if (hovered >= 0) return $"lines {hovered + 1} and {i + 1} are both hovered";
                hovered = i;
            }
            if (hovered < 0) return "no line is hovered";
            var box = _rows[hovered].WindowBorderBox;
            return pointerY >= box.Y && pointerY < box.Bottom ? null
                : $"line {hovered + 1} is hovered, but the pointer is at y = {pointerY}, not over it";
        }

        /// <summary>Null when every line on screen drew its text through the paragraph a click
        /// hit-tests, and a screenful of them did.</summary>
        public string? Drawn()
        {
            var viewport = View.WindowBorderBox;
            int drawn = 0;
            for (int i = 0; i < _code.Length; i++)
            {
                var box = _code[i].WindowBorderBox;
                if (box.Bottom <= viewport.Y || box.Y >= viewport.Bottom) continue;
                if (HultUI.Headless.LayoutProbe.DrawnLines(_code[i]).Count != 1)
                    return $"line {i + 1} is on screen but did not draw one line of selectable text";
                drawn++;
            }
            return drawn >= 30 ? null : $"only {drawn} lines are on screen";
        }

        // A line of code in a viewer's colours: indentation, then a keyword, names, a call with a
        // number and a string, and on some lines a comment, each a span of its own colour.
        private static (string Text, TextSpan[] Spans) CodeLine(Random rng, int line)
        {
            var keyword = Color.FromHex("#569CD6");
            var name = Color.FromHex("#9CDCFE");
            var number = Color.FromHex("#B5CEA8");
            var str = Color.FromHex("#CE9178");
            var comment = Color.FromHex("#6A9955");

            var text = new System.Text.StringBuilder();
            var spans = new List<TextSpan>();
            void Add(string s, Color? colour = null)
            {
                if (colour is { } c) spans.Add(new TextSpan(text.Length, s.Length, c));
                text.Append(s);
            }

            Add(new string(' ', 4 * (1 + line % 3)));
            if (line % 7 == 0)
            {
                Add("// " + Ui.Paragraph(rng, 9), comment);
                return (text.ToString(), spans.ToArray());
            }
            var words = Ui.Label(rng).Split(' ');
            if (line % 5 == 0)
                Add("return ", keyword);
            else
            {
                Add("var ", keyword);
                Add(words[0] + char.ToUpperInvariant(words[1][0]) + words[1][1..], name);
                Add(" = ");
            }
            Add(words[2]);
            Add(".Make(");
            Add($"{rng.Next(10_000)}", number);
            Add(", ");
            Add($"\"{Ui.Label(rng)}\"", str);
            Add(");");
            if (line % 3 == 0) Add(" // " + Ui.Paragraph(rng, 4), comment);
            return (text.ToString(), spans.ToArray());
        }
    }

    /// <summary>
    /// Meridian's response pane as its editors rebuild it: a header whose label reads a signal
    /// the request editor writes on every keystroke, over the read-only TextArea of the
    /// response, handed the same text and spans on every build (Meridian keeps them per
    /// response, so a rebuild formats and tokenizes nothing).
    /// </summary>
    private sealed class ResponsePane(string body, TextSpan[] spans) : Component
    {
        private readonly Signal<int> _edits = new(0);
        private readonly Ref<TextAreaElement> _area = new();
        private readonly Ref<TextElement> _label = new();

        public TextAreaElement? Area => _area.Current;

        public void Edit() => _edits.Value++;

        public override Element Build()
        {
            var area = JsonViewer();
            area.Text = body;
            area.Spans = spans;
            return Flex.Column(gap: 0).FlexGrow(1).Children(
                Flex.Row(gap: 12).FlexShrink(0).AlignItems(AlignItems.Center).Padding(new Thickness(6, 12)).Children(
                    Text.New("200 OK").FontSize(12f).TextColor(Ui.Accent),
                    Text.New($"request edited {_edits.Value} times").FontSize(12f).TextColor(Ui.Muted).WithRef(_label)),
                area.WithRef(_area));
        }

        /// <summary>Null when <paramref name="edits"/> rebuilds reached the header and the
        /// TextArea that shows the response is still <paramref name="area"/>, patched rather
        /// than mounted anew.</summary>
        public string? Verify(TextAreaElement area, int edits)
        {
            if (_label.Current?.Content != $"request edited {edits} times")
                return $"the header reads \"{_label.Current?.Content}\" after {edits} edits: the pane did not rebuild";
            return ReferenceEquals(_area.Current, area) ? null : "the rebuild replaced the TextArea instead of patching it";
        }
    }

    /// <summary>
    /// Meridian's response size, near the most it coloured while it tokenized on the UI thread:
    /// HultUI.Syntax leaves a text of more than 20,000 lines without spans unless the caller
    /// asks for more, as Meridian has since its tokenize moved to a worker (200,000). Meridian's
    /// survey response was 312 KB in 24,005 lines; this one is 368 KB in 19,013 (see
    /// <see cref="Ui.JsonResponse"/>).
    /// </summary>
    private const int JsonLines = 19_000;

    /// <summary>Meridian's response body: read-only, monospace 12 px, Dark+ colours on a dark field.</summary>
    private static TextAreaElement JsonViewer()
    {
        var area = TextArea.New("").FontFamily(FontFamilies.Monospace).FontSize(12f)
            .TextColor(Color.FromHex("#D4D4D4")).FlexGrow(1);
        area.ReadOnly = true;
        area.Style = JsonField;
        return area;
    }

    // One instance for every build: a TextArea compares its style by reference.
    private static readonly TextInputStyle JsonField = new() { Background = Color.FromHex("#1E1E1E") };

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
