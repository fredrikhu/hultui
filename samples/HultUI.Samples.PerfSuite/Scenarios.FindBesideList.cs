using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;

namespace PerfSuite;

static partial class Scenarios
{
    private static IEnumerable<Scenario> FindBesideListScenarios()
    {
        yield return new Scenario("text/find Enter beside a 3,000-row list, bar in the view",
            $"Press Enter in the find bar over a {FindBesideList.Rows:N0}-row list whose rows tint their matches, the current one in a colour of its own, as Junction's diff and Vesper's reader do. The bar is built in the view beside a header and a {FindBesideList.Files}-file side list: the step builds the view, which tells the list the row the match left and the row it reached",
            ctx => FindBesideListOp(ctx, ownPart: false));

        yield return new Scenario("text/find Enter beside a 3,000-row list, bar in its own part",
            "The same step with FindBar.Moved telling the list the two rows: the bar writes its count into the label on screen, and the step builds nothing",
            ctx => FindBesideListOp(ctx, ownPart: true));
    }

    private static Op FindBesideListOp(Ctx ctx, bool ownPart)
    {
        var view = new FindBesideList(ctx.Rng, ownPart);
        ctx.Mount(view.Mount());
        view.Find.Open();
        ctx.Pump(2);
        ctx.Window.TypeText(FindBesideList.Query);
        ctx.Pump(2);
        int start = view.Find.Search.Index, builds = 0;
        return new Op
        {
            Run = _ =>
            {
                builds = view.Builds;
                ctx.Window.PressKey(KeyCode.Enter);
            },
            Figure = () => view.Builds - builds,
            FigureUnit = "view builds/step",
            Check = step => view.Verify((start + step + 1) % view.Find.Search.Hits.Count),
        };
    }
}

/// <summary>
/// A find bar over a long list of lines, beside the rest of a view — the shape of Junction's diff
/// pane and Vesper's reader: a header, a side list of files, the bar, the list on item rows and a
/// footer, all made by one component. A row tints its matches with <see cref="TextSearch.Decorate"/>,
/// the current one in a colour of its own; what every row shows (the query, its options, how many
/// matches) is the rows' shape, and the current match is not: the view tells the list the row it
/// left and the row it reached. Twelve matches, all on the first screen, so a step scrolls nothing
/// and wraps after the last.
/// </summary>
sealed class FindBesideList : Component
{
    public const int Rows = 3_000;
    public const int Files = 40;
    public const string Query = "fox";
    private const float RowHeight = 20f;

    private static readonly Color HitTint = Color.FromHex("#FFE08A"), CurrentTint = Color.FromHex("#FF9F1C");

    private readonly string[] _lines = new string[Rows];
    private readonly string[] _files = new string[Files];
    private readonly int[] _items = Enumerable.Range(0, Rows).ToArray();
    private readonly int[] _madeWith = new int[Rows];
    private readonly Ref<ScrollViewElement> _list = new();
    private (string, bool, bool, int) _shape;
    private (int Index, int Line) _hitWas = (-1, -1);

    public FindBesideList(Random rng, bool ownPart)
    {
        for (int i = 0; i < Rows; i++)
            _lines[i] = i < 24 && i % 2 == 1 ? $"the quick brown fox jumps over {Ui.Label(rng)}" : Ui.Paragraph(rng, 9);
        for (int i = 0; i < Files; i++) _files[i] = $"src/{Ui.Label(rng).Replace(' ', '-')}.cs";
        Find = new FindBar(MarkDirty)
        {
            Body = () => _list.Current,
            Reveal = RevealHit,
            RevealAfterLayout = false,
            LetterKeys = true,
        };
        if (ownPart) Find.Moved = Moved;
        Find.Search.Bind(Rows, 1, (line, _) => line >= 0 && line < Rows ? _lines[line] : null);
    }

    /// <summary>The current match as the rows show it: its index and its line, or -1 for both.</summary>
    private (int Index, int Line) Hit() =>
        Find.Search.Current is { } current ? (Find.Search.Index, current.Line) : (-1, -1);

    /// <summary>A step (<see cref="FindBar.Moved"/>): what the build does when it sees the match
    /// move, without the build.</summary>
    private void Moved()
    {
        var hit = Hit();
        if (hit != _hitWas && _list.Current is VirtualListElement<int> list)
        {
            list.InvalidateRow(_hitWas.Line);
            list.InvalidateRow(hit.Line);
        }
        _hitWas = hit;
    }

    public FindBar Find { get; }

    /// <summary>Builds of the view.</summary>
    public int Builds { get; private set; }

    public override Element Build()
    {
        Builds++;
        var search = Find.Search;
        var shape = (search.Query, search.MatchCase, search.UseRegex, search.Hits.Count);
        var hit = Hit();
        if (_list.Current is VirtualListElement<int> mounted)
        {
            if (shape != _shape) mounted.InvalidateRows();
            else if (hit != _hitWas)
            {
                mounted.InvalidateRow(_hitWas.Line);   // -1 does nothing
                mounted.InvalidateRow(hit.Line);
            }
        }
        _shape = shape;
        _hitWas = hit;

        var list = VirtualListElement<int>.New()
            .ItemHeight(RowHeight)
            .Items(_items)
            .RenderWith((line, _) => Row(line))
            .FlexGrow(1);
        list.Key = "lines";
        list.WithRef(_list);

        var main = Flex.Column(gap: 0).FlexGrow(1).Children(
            Flex.Row(gap: 8).FlexShrink(0).Padding(new Thickness(8, 12)).AlignItems(AlignItems.Center)
                .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 0, 1, 0))
                .Children(
                    Text.New(_files[0]).FontSize(12f).FlexGrow(1).MaxLines(1),
                    Button.New("Unified", ButtonVariant.Secondary),
                    Button.New("Wrap", ButtonVariant.Secondary),
                    Button.New("Open", ButtonVariant.Secondary)));
        if (Find.IsOpen)
            main.AddChild(Find.Build());
        main.AddChild(list);
        main.AddChild(Text.New($"{Rows:N0} lines").FontSize(11f).TextColor(Ui.Muted).Padding(new Thickness(4, 12)));

        var side = Flex.Column(gap: 0).Width(Dimension.Px(260)).FlexShrink(0)
            .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 1, 0, 0));
        for (int i = 0; i < Files; i++)
        {
            side.AddChild(Flex.Row(gap: 6).Height(Dimension.Px(22)).AlignItems(AlignItems.Center).Padding(new Thickness(0, 10))
                .Background(i == 0 ? Ui.Line : Color.Transparent)
                .Children(
                    Box.New().Width(Dimension.Px(10)).Height(Dimension.Px(10)).CornerRadius(2f).Background(Ui.Accent),
                    Text.New(_files[i]).FontSize(11.5f).FlexGrow(1).MaxLines(1),
                    Text.New(i % 3 == 0 ? "M" : "A").FontSize(11f).TextColor(Ui.Muted)));
        }
        return Flex.Row(gap: 0).FlexGrow(1).Children(side, main);
    }

    protected internal override void OnAfterBuild() => Find.Service();

    private Element Row(int line)
    {
        _madeWith[line] = Find.Search.Index;
        string text = _lines[line];
        return Flex.Row(gap: 0).Height(Dimension.Px(RowHeight)).AlignItems(AlignItems.Center).Children(
            Text.New((line + 1).ToString()).FontSize(11f).TextColor(Ui.Muted).Width(Dimension.Px(48)).FlexShrink(0),
            Text.New(text).FontSize(12f).MaxLines(1).FlexGrow(1)
                .Spans(Find.Search.Decorate(null, line, 0, text.Length, HitTint, CurrentTint)));
    }

    private void RevealHit()
    {
        if (Find.Search.Current is { } hit && _list.Current is { } list)
            list.ScrollRangeIntoView(hit.Line * RowHeight, RowHeight);
    }

    /// <summary>Null when match <paramref name="index"/> is the current one, the count says so,
    /// and the row it reached and the row it left were both made since it moved.</summary>
    public string? Verify(int index)
    {
        var search = Find.Search;
        if (search.Hits.Count != 12) return $"{search.Hits.Count} matches for \"{Query}\", not 12";
        if (search.Index != index) return $"match {search.Index} is the current one, not {index}";
        string want = FindBar.Status(search);
        if (FindStatus(Mount()) is not { } status) return "the bar's count is not on screen";
        if (status.Content != want) return $"the count says \"{status.Content}\", not \"{want}\"";
        int reached = search.Hits[index].Line, left = search.Hits[(index - 1 + search.Hits.Count) % search.Hits.Count].Line;
        if (_madeWith[reached] != index) return $"the row of the current match (line {reached}) was made with match {_madeWith[reached]}";
        if (_madeWith[left] != index) return $"the row the match left (line {left}) was made with match {_madeWith[left]}";
        return null;
    }

    private static TextElement? FindStatus(Element e)
    {
        if (e is TextElement { AutomationId: "find-status" } t) return t;
        foreach (var c in e.Children)
            if (FindStatus(c) is { } hit) return hit;
        return null;
    }
}
