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
    /// A character typed into a find field above a 20,000-line view whose lines are one or two
    /// rows tall, given to its list through <c>RowHeights</c>, as Vesper's file viewer and
    /// Junction's wrapped diffs give theirs. The field's change rebuilds the one component that
    /// holds both, so every keystroke hands the list a new height function over the same heights.
    /// With <paramref name="rowsTold"/> the rows show the query, so the component tells the list
    /// with <c>InvalidateRows</c> when it changed, as Vesper's viewer does while find is typed in.
    /// The figure is the rows the list's template made for the keystroke.
    /// </summary>
    private static Op TypeBesideRowHeightsOp(Ctx ctx, bool rowsTold)
    {
        var view = new FindAboveLines(ctx.Rng, rowsTold);
        ctx.Mount(view.Mount());
        ctx.Window.Click(view.Input!);
        ctx.Pump(2);
        var list = view.List!;
        int buildsAtStart = view.Builds, madeBefore = 0;
        return new Op
        {
            Run = step =>
            {
                madeBefore = view.RowsMade;
                ctx.Window.TypeText(((char)('a' + step % 26)).ToString());
            },
            Figure = () => view.RowsMade - madeBefore,
            FigureUnit = "rows made/key",
            Check = step =>
            {
                if (view.Query.Length != step + 1)
                    return $"the field holds {view.Query.Length} characters after {step + 1} typed";
                if (view.Builds - buildsAtStart != step + 1)
                    return $"{view.Builds - buildsAtStart} rebuilds for {step + 1} characters typed";
                if (!ReferenceEquals(view.List, list))
                    return "the rebuild replaced the list instead of patching it";
                // The rows stand where the heights put them: the tenth row's top is the sum of
                // the nine above it, from the top of the list's content.
                var rows = list.Children;
                for (int i = 1; i < rows.Count; i++)
                {
                    float top = rows[i].Layout.AbsoluteBorderBox.Y - list.Layout.AbsoluteContentBox.Y;
                    if (Math.Abs(top - FindAboveLines.Top(i - 1)) > 0.5f)
                        return $"row {i - 1} stands at {top:0.#}, not {FindAboveLines.Top(i - 1):0.#}";
                }
                return rowsTold && view.RowsMade == 0 ? "the rows that show the query were never made" : null;
            },
        };
    }

    /// <summary>
    /// A find field over a list of 20,000 lines on item rows, every seventh line two rows tall,
    /// made by one component whose Build every keystroke in the field reruns.
    /// </summary>
    private sealed class FindAboveLines(Random rng, bool rowsTold) : Component
    {
        public const int Lines = 20_000;
        private const float RowHeight = 18f;

        private readonly string[] _lines = MakeLines(rng);
        private readonly Ref<TextInputElement> _input = new();
        private readonly Ref<VirtualListElement<int>> _list = new();
        private readonly int[] _items = Enumerable.Range(0, Lines).ToArray();
        private string _rowsQuery = "";

        public string Query { get; private set; } = "";

        public int Builds { get; private set; }

        /// <summary>Rows the list's template has made.</summary>
        public int RowsMade { get; private set; }

        public TextInputElement? Input => _input.Current;

        public VirtualListElement<int>? List => _list.Current;

        /// <summary>The height of line <paramref name="index"/>: every seventh wraps to two rows.</summary>
        public static float HeightOf(int index) => index % 7 == 3 ? 2 * RowHeight : RowHeight;

        /// <summary>Where line <paramref name="index"/> starts.</summary>
        public static float Top(int index) => index * RowHeight + (index + 3) / 7 * RowHeight;

        public override Element Build()
        {
            Builds++;
            // The rows show the query: when it changed, the list is told, as an app tells it
            // what its rows show beyond their item.
            if (rowsTold && Query != _rowsQuery)
            {
                _rowsQuery = Query;
                _list.Current?.InvalidateRows();
            }

            var field = TextInput.New(Query).PlaceholderText("Find")
                .OnTextChanged(text => { Query = text; MarkDirty(); })
                .Height(Dimension.Px(30)).WithRef(_input);
            field.Key = "find";   // keeps its focus and caret through the rebuild

            string query = rowsTold ? Query : "";
            var list = VirtualListElement<int>.New()
                .ItemHeight(RowHeight)
                .RowHeights((_, index) => HeightOf(index))
                .Items(_items)
                .RenderWith((index, _) => Row(index, query))
                .FlexGrow(1)
                .WithRef(_list);

            return Flex.Column(gap: 0).FlexGrow(1).Children(
                Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center).Padding(new Thickness(6, 12)).Children(
                    Text.New("server.log").FontSize(12f).FlexGrow(1), field.Width(Dimension.Px(260))),
                list);
        }

        private Element Row(int index, string query)
        {
            RowsMade++;
            var text = Text.New(_lines[index]).FontSize(12f).MaxLines(2).Padding(new Thickness(0, 12));
            if (query.Length > 0 && _lines[index].IndexOf(query, StringComparison.OrdinalIgnoreCase) is int at and >= 0)
                text.Spans([new TextSpan(at, query.Length, default, Color.FromHex("#FFE58F"))]);
            return text;
        }

        private static string[] MakeLines(Random rng)
        {
            var lines = new string[Lines];
            for (int i = 0; i < lines.Length; i++)
                lines[i] = $"{i + 1,6}  " + Ui.Paragraph(rng, i % 7 == 3 ? 18 : 7);
            return lines;
        }
    }
}
