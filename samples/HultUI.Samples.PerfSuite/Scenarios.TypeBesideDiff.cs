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
    /// <summary>
    /// A character typed into the summary field beside a 150-line diff, as Junction's Changes
    /// view is typed in: the field's change rebuilds the one component that holds both, and
    /// what the rebuild costs is what the diff's rows cost. The figure is the rows the list's
    /// template made for the keystroke.
    /// </summary>
    private static Op TypeBesideDiffOp(Ctx ctx, bool kept)
    {
        var pane = new TypeBesideDiff(ctx.Rng, kept);
        ctx.Mount(pane.Mount());
        ctx.Window.Click(pane.Input!);
        ctx.Pump(2);
        int madeBefore = 0;
        return new Op
        {
            Run = step =>
            {
                madeBefore = pane.RowsMade;
                ctx.Window.TypeText(((char)('a' + step % 26)).ToString());
            },
            Figure = () => pane.RowsMade - madeBefore,
            FigureUnit = "rows made/key",
            Check = step => pane.Summary.Length == step + 1 ? null : $"the summary holds {pane.Summary.Length} characters after {step + 1} typed",
        };
    }
}

/// <summary>A line of the diff as git gives it: what kind it is, its numbers, its text.</summary>
enum DiffKind : byte { Hunk, Context, Removed, Added }

sealed record DiffSourceLine(DiffKind Kind, int? Old, int? New, string Text);

/// <summary>
/// A diff row as an app flattens it for its list: the line and the word-diff spans of a
/// changed pair. A record, so an unlabelled list names its items by the compiler's dump.
/// </summary>
sealed record DiffLine(int Index, DiffKind Kind, int? Old, int? New, string Text, IReadOnlyList<TextSpan>? Spans);

/// <summary>
/// The shape of Junction's Changes view while a commit summary is typed: a composer with a
/// <see cref="TextInputElement"/> beside a diff of 150 lines in a
/// <see cref="VirtualListElement{T}"/>, both made by one component whose Build every keystroke
/// reruns (the field writes the summary and marks the component dirty).
///
/// <para>Not <paramref name="kept"/>: the rows as an app first writes them. Build flattens the
/// diff anew (a record per line, a word diff per changed pair), the one-argument
/// <c>RenderWith</c> makes every row on screen again, and the list has no item label, so
/// assistive technology is told each record's <c>ToString()</c>. Kept: the rows are flattened
/// once and stay the same objects, the list is on item rows (two-argument <c>RenderWith</c>),
/// and each row's label is made once, when it is first asked for.</para>
/// </summary>
sealed class TypeBesideDiff(Random rng, bool kept) : Component
{
    public const float RowHeight = 22f;

    private readonly List<DiffSourceLine> _source = MakeSource(rng, 150);
    private readonly Ref<TextInputElement> _input = new();
    private List<DiffLine>? _rows;
    private string?[]? _labels;
    private Func<DiffLine, string?>? _label;

    public string Summary { get; private set; } = "";

    public TextInputElement? Input => _input.Current;

    /// <summary>Rows the list's template has made.</summary>
    public int RowsMade { get; private set; }

    public override Element Build()
    {
        var rows = kept ? _rows ??= Flatten(_source) : Flatten(_source);
        var list = VirtualListElement<DiffLine>.New()
            .ItemHeight(RowHeight)
            .Items(rows)
            .FlexGrow(1);
        if (kept) list.RenderWith((row, _) => Row(row)).AccessibleItemLabel(_label ??= Label);
        else list.RenderWith(Row);

        var summary = TextInput.New(Summary).PlaceholderText("Summary (required)")
            .OnTextChanged(text => { Summary = text; MarkDirty(); })
            .Height(Dimension.Px(34)).WithRef(_input);
        summary.Key = "summary";   // keeps its focus and caret through the rebuild

        return Flex.Row(gap: 0).FlexGrow(1).Children(
            Flex.Column(gap: 8).Width(Dimension.Px(320)).FlexShrink(0).Padding(12)
                .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 1, 0, 0))
                .Children(
                    Text.New("Commit").FontSize(13f).FontWeight(FontWeight.SemiBold),
                    summary,
                    Text.New($"{Summary.Length} characters").FontSize(11f).TextColor(Ui.Muted),
                    Button.New("Commit")),
            Flex.Column(gap: 0).FlexGrow(1).Children(
                Flex.Row(gap: 8).FlexShrink(0).Padding(new Thickness(8, 12))
                    .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 0, 1, 0))
                    .Children(Text.New("README.md").FontSize(12f).FlexGrow(1), Text.New("Unified").FontSize(11f).TextColor(Ui.Muted)),
                list));
    }

    private Element Row(DiffLine r)
    {
        RowsMade++;
        if (r.Kind == DiffKind.Hunk)
            return Box.New().Padding(new Thickness(5, 14)).Background(Color.FromHex("#EEF2FF"))
                .Children(Text.New(r.Text).FontSize(11.5f).TextColor(Ui.Accent).MaxLines(1));
        var (bg, sign) = r.Kind switch
        {
            DiffKind.Added => (Color.FromHex("#E6FFEC"), "+"),
            DiffKind.Removed => (Color.FromHex("#FFEBE9"), "−"),
            _ => (Color.Transparent, " "),
        };
        return Flex.Row(gap: 0).AlignItems(AlignItems.Stretch).Background(bg).Children(
            Number(r.Old), Number(r.New),
            Box.New().Width(Dimension.Px(16)).FlexShrink(0).Children(Text.New(sign).FontSize(12f)),
            Text.New(r.Text.Length == 0 ? " " : r.Text).FontSize(12f).Spans(r.Spans)
                .MaxLines(1).FlexGrow(1).Padding(new Thickness(2, 14, 2, 0)));
    }

    private static Element Number(int? n) =>
        Box.New().Width(Dimension.Px(46)).FlexShrink(0).AlignItems(AlignItems.FlexEnd).Padding(new Thickness(0, 8, 0, 0))
            .Children(Text.New(n?.ToString() ?? "").FontSize(11f).TextColor(Ui.Muted));

    private string Label(DiffLine r) =>
        (_labels ??= new string?[_rows!.Count])[r.Index] ??= r.Kind switch
        {
            DiffKind.Hunk => "hunk " + r.Text,
            DiffKind.Added => $"added, line {r.New}: {r.Text}",
            DiffKind.Removed => $"removed, line {r.Old}: {r.Text}",
            _ => $"line {r.New}: {r.Text}",
        };

    /// <summary>One hunk: runs of context between runs of removed lines and the lines that
    /// replace them, each changed pair differing by a word or two.</summary>
    private static List<DiffSourceLine> MakeSource(Random rng, int count)
    {
        var lines = new List<DiffSourceLine>(count);
        int oldNo = 1, newNo = 1;
        while (lines.Count < count)
        {
            for (int k = rng.Next(1, 4); k > 0 && lines.Count < count; k--)
            {
                lines.Add(new DiffSourceLine(DiffKind.Context, oldNo++, newNo++, Ui.Paragraph(rng, 6 + rng.Next(8))));
            }
            int pairs = rng.Next(1, 5);
            var removed = new string[pairs];
            for (int k = 0; k < pairs && lines.Count < count; k++)
            {
                removed[k] = Ui.Paragraph(rng, 6 + rng.Next(8));
                lines.Add(new DiffSourceLine(DiffKind.Removed, oldNo++, null, removed[k]));
            }
            for (int k = 0; k < pairs && lines.Count < count; k++)
            {
                var words = (removed[k] ?? Ui.Paragraph(rng, 8)).Split(' ');
                words[rng.Next(words.Length)] = Ui.Label(rng).Split(' ')[0];
                lines.Add(new DiffSourceLine(DiffKind.Added, null, newNo++, string.Join(' ', words)));
            }
        }
        return lines;
    }

    /// <summary>The rows the list shows: a hunk header, then every line, a changed pair's two
    /// lines each carrying the spans of the words that differ.</summary>
    private static List<DiffLine> Flatten(List<DiffSourceLine> source)
    {
        var rows = new List<DiffLine>(source.Count + 1)
        {
            new(0, DiffKind.Hunk, null, null, $"@@ -1,{source.Count(l => l.Kind != DiffKind.Added)} +1,{source.Count(l => l.Kind != DiffKind.Removed)} @@", null),
        };
        for (int i = 0; i < source.Count;)
        {
            if (source[i].Kind == DiffKind.Context) { Add(source[i++], null); continue; }
            int removed = i;
            while (i < source.Count && source[i].Kind == DiffKind.Removed) i++;
            int added = i;
            while (i < source.Count && source[i].Kind == DiffKind.Added) i++;
            int dels = added - removed, adds = i - added;
            for (int k = 0; k < dels; k++)
                Add(source[removed + k], k < adds ? WordDiff(source[removed + k].Text, source[added + k].Text) : null);
            for (int k = 0; k < adds; k++)
                Add(source[added + k], k < dels ? WordDiff(source[added + k].Text, source[removed + k].Text) : null);
        }
        return rows;

        void Add(DiffSourceLine line, IReadOnlyList<TextSpan>? spans) =>
            rows.Add(new DiffLine(rows.Count, line.Kind, line.Old, line.New, line.Text, spans));
    }

    private static readonly Color WordTint = Color.FromHex("#FFD8A8");

    /// <summary>The words of <paramref name="text"/> that differ from the same place in
    /// <paramref name="other"/>, as background spans.</summary>
    private static List<TextSpan> WordDiff(string text, string other)
    {
        var a = text.Split(' ');
        var b = other.Split(' ');
        var spans = new List<TextSpan>();
        int at = 0;
        for (int w = 0; w < a.Length; w++)
        {
            if (w >= b.Length || a[w] != b[w])
                spans.Add(new TextSpan(at, a[w].Length, default, WordTint));
            at += a[w].Length + 1;
        }
        return spans;
    }
}
