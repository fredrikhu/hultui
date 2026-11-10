using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;

namespace PerfSuite;

static partial class Scenarios
{
    private static IEnumerable<Scenario> AppendRows()
    {
        yield return new Scenario("component/append a row to 200 rows built through the root, every row made",
            "A run's result lands, one a frame, below 200 others (Meridian's Runner): the window's Build makes every result row again — a glyph, a method chip, the name, the status, the time and the checks — and the unkeyed list patches them by place, as Meridian's Runner did until plan 17's round 10",
            ctx => AppendRow(ctx, keepRows: false)) { Width = 1440, Height = 900 };

        yield return new Scenario("component/append a row to 200 rows built through the root, rows kept",
            "The same with the rows in a KeptElements keyed by their request: the window's Build hands back the 200 rows it made before and makes the one that landed",
            ctx => AppendRow(ctx, keepRows: true)) { Width = 1440, Height = 900 };
    }

    /// <summary>The list shows 200 results; each step lands the 201st and the reset takes it off again,
    /// so every step appends one row to the same 200. The row reports the rows made a step.</summary>
    private static Op AppendRow(Ctx ctx, bool keepRows)
    {
        var view = new RunResultsView(RunResults(ctx.Rng, 201), keepRows) { Shown = 200 };
        ctx.Mount(view.Mount());
        int madeBefore = 0;
        return new Op
        {
            Reset = _ => view.Shown = 200,
            Run = _ =>
            {
                madeBefore = view.RowsMade;
                view.Shown = 201;
            },
            Figure = () => view.RowsMade - madeBefore,
            FigureUnit = "rows made",
            Check = _ => view.Verify(201),
        };
    }

    private static List<RunResultItem> RunResults(Random rng, int count)
    {
        string[] methods = ["GET", "POST", "PUT", "DELETE"];
        var list = new List<RunResultItem>(count);
        for (int i = 0; i < count; i++)
        {
            bool pass = rng.Next(10) != 0;
            list.Add(new RunResultItem($"collection/area{i % 5}/req-{i:000}.req", Ui.Label(rng), methods[rng.Next(methods.Length)],
                pass, pass ? 200 : 500, 18 + rng.Next(30), 2, pass ? 2 : 1));
        }
        return list;
    }
}

/// <summary>A result of a run: its request, what the row shows of it.</summary>
sealed record RunResultItem(string Path, string Name, string Method, bool Pass, int Status, int Ms, int Checks, int Passed);

/// <summary>
/// The shape of Meridian's Runner during a run: a scope rail, a progress line over the result list, and
/// the list, all made by the window's one Build, which runs again as each result lands. With
/// <paramref name="keepRows"/> each result row goes through a <see cref="KeptElements{TState}"/>, keyed by
/// its request, and a build hands back each row whose result and place did not change.
/// </summary>
sealed partial class RunResultsView(List<RunResultItem> results, bool keepRows) : Component
{
    private readonly record struct RowState(RunResultItem Result, int Index);

    private static readonly Color Pass = Color.FromHex("#16A34A"), Fail = Color.FromHex("#DC2626"), Chip = Color.FromHex("#E0E7FF");

    private readonly KeptElements<RowState> _kept = new();
    private int _builds;

    /// <summary>How many results have landed; a write builds the window.</summary>
    [Reactive] public partial int Shown { get; set; }

    /// <summary>Result rows the window has made.</summary>
    public int RowsMade { get; private set; }

    public override Element Build()
    {
        int shown = Shown;
        _kept.Begin(++_builds);
        var rail = Flex.Column(gap: 6).Width(Dimension.Px(260)).FlexShrink(0).Padding(12)
            .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 1, 0, 0));
        rail.AddChild(Text.New("Run").FontSize(15f).FontWeight(FontWeight.SemiBold));
        for (int f = 0; f < 5; f++)
            rail.AddChild(Flex.Row(gap: 8).Height(26).AlignItems(AlignItems.Center).Children(
                Text.New($"area{f}/").FontSize(11.5f).FlexGrow(1),
                Text.New($"{results.Count / 5} req").FontSize(10f).TextColor(Ui.Muted)));
        rail.AddChild(Button.New("Abort"));

        int passed = 0;
        for (int i = 0; i < shown; i++) if (results[i].Pass) passed++;
        var progress = Flex.Row(gap: 12).Height(32).FlexShrink(0).AlignItems(AlignItems.Center).Padding(new Thickness(0, 12))
            .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 0, 1, 0)).Children(
                Text.New($"{shown} / {results.Count} · running…").FontSize(11.5f),
                Text.New($"✓ {passed}  ✗ {shown - passed}").FontSize(11f).TextColor(Ui.Muted));

        var list = Flex.Column(gap: 1).FlexShrink(0).Padding(new Thickness(4, 8));
        for (int i = 0; i < shown; i++)
        {
            var result = results[i];
            if (!keepRows)
            {
                list.AddChild(Row(result, i));
                continue;
            }
            var state = new RowState(result, i);
            list.AddChild(_kept.TryKeep(result.Path, state, out var kept) ? kept : _kept.Made(result.Path, state, Row(result, i)));
        }
        var main = Flex.Column(gap: 0).FlexGrow(1).Children(progress, ScrollView.New().FlexGrow(1).Children(list));
        return Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch).Background(Color.White).Children(rail, main);
    }

    private BoxElement Row(RunResultItem result, int index)
    {
        RowsMade++;
        var color = result.Pass ? Pass : Fail;
        var row = Flex.Row(gap: 8).Height(26).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 8)).CornerRadius(4f)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName($"{(result.Pass ? "✓" : "✗")} {result.Name}, {result.Status}")
            .AutomationId($"run-row-{index}");
        row.AddChild(Text.New(result.Pass ? "✓" : "✗").FontSize(12f).TextColor(color).Width(Dimension.Px(16)).FlexShrink(0));
        row.AddChild(Box.New().Padding(new Thickness(2, 5)).CornerRadius(3f).Background(Chip).FlexShrink(0)
            .Children(Text.New(result.Method).FontSize(8f).FontWeight(FontWeight.SemiBold).TextColor(Ui.Accent)));
        row.AddChild(Text.New(result.Name).FontSize(12f).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).FlexGrow(1));
        row.AddChild(Text.New($"{result.Status}").FontSize(11f).TextColor(color).FlexShrink(0));
        row.AddChild(Text.New($"{result.Ms} ms").FontSize(10.5f).TextColor(Ui.Muted).FlexShrink(0));
        row.AddChild(Text.New($"{result.Passed}/{result.Checks} ✓").FontSize(10.5f).TextColor(result.Passed == result.Checks ? Pass : Fail).FlexShrink(0));
        row.OnClick(_ => { });
        row.OnHover(b => b.BackgroundColor = Ui.Line);
        return row;
    }

    /// <summary>Why the list on screen does not show the first <paramref name="count"/> results in order,
    /// if it does not.</summary>
    public string? Verify(int count)
    {
        var rows = new List<Element>();
        void Walk(Element e)
        {
            if (e.AutomationId is { } id && id.StartsWith("run-row-", StringComparison.Ordinal)) { rows.Add(e); return; }
            foreach (var c in e.Children) Walk(c);
        }
        Walk(Mount());
        if (rows.Count != count) return $"{rows.Count} rows shown, not {count}";
        for (int i = 0; i < count; i++)
        {
            if (rows[i].AutomationId != $"run-row-{i}") return $"row {i} is {rows[i].AutomationId}";
            if (rows[i].Children[2] is not TextElement name || name.Content != results[i].Name) return $"row {i} does not show {results[i].Name}";
        }
        return null;
    }
}
