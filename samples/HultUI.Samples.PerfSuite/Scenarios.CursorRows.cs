using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;

namespace PerfSuite;

static partial class Scenarios
{
    private static IEnumerable<Scenario> CursorRows()
    {
        yield return new Scenario("component/cursor key over a plain 200-row list built through the root, every row made",
            "A cursor walks down and up a plain scroll view of 200 rows of seven cells — a name, a size and a time on each side and a direction glyph between them (Vesper's Synchronize) — that the window's one Build makes, with a toolbar, a column header and a commit bar; each key builds the window and makes every row again, the cursor's with a fill and a ring, as Vesper's Synchronize did until plan 17's round 10",
            ctx => CursorRowWalk(ctx, keepRows: false)) { Width = 1240, Height = 820 };

        yield return new Scenario("component/cursor key over a plain 200-row list built through the root, rows kept",
            "The same walk with the rows in a KeptElements keyed by their path: each key builds the window, hands back the 198 rows the key did not change and makes the two the cursor moved between",
            ctx => CursorRowWalk(ctx, keepRows: true)) { Width = 1240, Height = 820 };
    }

    /// <summary>The cursor walks 20 rows down and back up; each step is one key. The row reports the rows
    /// made a step.</summary>
    private static Op CursorRowWalk(Ctx ctx, bool keepRows)
    {
        const int Turn = 20;
        var view = new CursorRowsView(PairRows(ctx.Rng, 200), keepRows);
        var host = view.Mount();
        host.On<KeyDownEvent>(e =>
        {
            view.Press(e.Key);
            e.StopPropagation = true;
        });
        ctx.Mount(host);
        int madeBefore = 0;
        return new Op
        {
            Run = i =>
            {
                madeBefore = view.RowsMade;
                ctx.Window.PressKey(i / Turn % 2 == 0 ? KeyCode.Down : KeyCode.Up);
            },
            Figure = () => view.RowsMade - madeBefore,
            FigureUnit = "rows made",
            Check = i =>
            {
                int steps = (i + 1) % (2 * Turn), there = steps <= Turn ? steps : 2 * Turn - steps;
                return view.Verify(there);
            },
        };
    }

    private static List<PairRowItem> PairRows(Random rng, int count)
    {
        string[] glyphs = ["→", "←", "+→", "←+", "≠"];
        var list = new List<PairRowItem>(count);
        for (int i = 0; i < count; i++)
        {
            string path = $"src/area{i % 7}/{Ui.Label(rng).Replace(' ', '-')}-{i:000}.cs";
            list.Add(new PairRowItem(path, 1_000 + rng.Next(90_000), 1_000 + rng.Next(90_000),
                $"11-{1 + rng.Next(28):00} {rng.Next(24):00}:{rng.Next(60):00}", $"10-{1 + rng.Next(28):00} {rng.Next(24):00}:{rng.Next(60):00}",
                glyphs[rng.Next(glyphs.Length)]));
        }
        return list;
    }
}

/// <summary>A pair of files the list compares: its path, each side's size and time, the direction.</summary>
sealed record PairRowItem(string Path, long LeftSize, long RightSize, string LeftTime, string RightTime, string Glyph);

/// <summary>
/// The shape of Vesper's Synchronize: a toolbar, a column header, a plain scroll view of rows (not a
/// virtual list: every row is in the tree) and a commit bar, all made by the window's one Build, which
/// runs again on each cursor key. With <paramref name="keepRows"/> each row goes through a
/// <see cref="KeptElements{TState}"/>, keyed by its path, and a build hands back each row whose pair and
/// cursor did not change.
/// </summary>
sealed partial class CursorRowsView(List<PairRowItem> rows, bool keepRows) : Component
{
    private const string Cols = "1fr 60px 96px 34px 96px 60px 1fr";

    private readonly record struct RowState(PairRowItem Item, int Index, bool Selected);

    private static readonly Color Fill = Color.FromHex("#DBEAFE"), Ring = Color.FromHex("#2563EB");

    private readonly KeptElements<RowState> _kept = new();
    private int _builds;

    /// <summary>The row the cursor is on; a write builds the window.</summary>
    [Reactive] public partial int Cursor { get; set; }

    /// <summary>Rows the window has made.</summary>
    public int RowsMade { get; private set; }

    public void Press(KeyCode key)
    {
        if (key == KeyCode.Down) Cursor = Math.Min(rows.Count - 1, Cursor + 1);
        else if (key == KeyCode.Up) Cursor = Math.Max(0, Cursor - 1);
    }

    public override Element Build()
    {
        int cursor = Cursor;
        _kept.Begin(++_builds);
        var toolbar = Flex.Row(gap: 10).Height(40).FlexShrink(0).AlignItems(AlignItems.Center).Padding(new Thickness(0, 14))
            .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 0, 1, 0)).Children(
                Text.New("Synchronize").FontSize(13f).FontWeight(FontWeight.Medium),
                Text.New("C:/work/left").FontSize(10.5f).TextColor(Ui.Muted).FlexGrow(1).MaxLines(1).TextOverflow(TextOverflow.Ellipsis),
                Text.New("C:/work/right").FontSize(10.5f).TextColor(Ui.Muted).FlexGrow(1).MaxLines(1).TextOverflow(TextOverflow.Ellipsis),
                Button.New("Date + size"), Button.New("Content"), Button.New("Show equal"));

        var header = Grid.Of(Cols, gap: 8).FlexShrink(0).Padding(new Thickness(4, 12))
            .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 0, 1, 0));
        string[] titles = ["LEFT", "SIZE", "MODIFIED", "", "MODIFIED", "SIZE", "RIGHT"];
        for (int c = 0; c < titles.Length; c++) header.AddChild(Text.New(titles[c]).FontSize(9.5f).TextColor(Ui.Muted).GridColumn(c));

        var list = Flex.Column(gap: 0);
        for (int i = 0; i < rows.Count; i++)
        {
            var item = rows[i];
            bool selected = i == cursor;
            if (!keepRows)
            {
                list.AddChild(Row(item, i, selected));
                continue;
            }
            var state = new RowState(item, i, selected);
            list.AddChild(_kept.TryKeep(item.Path, state, out var kept) ? kept : _kept.Made(item.Path, state, Row(item, i, selected)));
        }
        var scroll = ScrollView.Vertical().FlexGrow(1).Padding(new Thickness(4)).Children(list);

        var bar = Flex.Row(gap: 10).Height(36).FlexShrink(0).AlignItems(AlignItems.Center).Padding(new Thickness(0, 12))
            .BorderColor(Ui.Line).BorderWidth(new Thickness(1, 0, 0, 0)).Children(
                Text.New($"{rows.Count} →").FontSize(11f), Text.New("0 ←").FontSize(11f), Box.New().FlexGrow(1),
                Button.New("Diff"), Button.New($"Synchronize {rows.Count} files…"));
        return Flex.Column(gap: 0).FlexGrow(1).Background(Color.White).Children(toolbar, header, scroll, bar);
    }

    private BoxElement Row(PairRowItem item, int index, bool selected)
    {
        RowsMade++;
        var row = Grid.Of(Cols, rows: "1fr", gap: 8).Height(24).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 8)).CornerRadius(4f).AccessibleRole(AccessibilityRole.Button);
        if (selected) row.Background(Fill).BorderColor(Ring).BorderWidth(1f);
        row.AutomationId = $"pair-{index}";
        row.AddChild(Text.New(item.Path).FontSize(11.5f).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).GridColumn(0));
        row.AddChild(Text.New($"{item.LeftSize:N0}").FontSize(10.5f).TextColor(Ui.Muted).TextAlign(TextAlign.Right).GridColumn(1));
        row.AddChild(Text.New(item.LeftTime).FontSize(10.5f).TextColor(Ui.Muted).GridColumn(2));
        row.AddChild(Box.New().AlignSelf(AlignSelf.Stretch).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Children(Text.New(item.Glyph).FontSize(12f).TextColor(Ui.Accent)).GridColumn(3));
        row.AddChild(Text.New(item.RightTime).FontSize(10.5f).TextColor(Ui.Muted).GridColumn(4));
        row.AddChild(Text.New($"{item.RightSize:N0}").FontSize(10.5f).TextColor(Ui.Muted).TextAlign(TextAlign.Right).GridColumn(5));
        row.AddChild(Text.New(item.Path).FontSize(11.5f).TextAlign(TextAlign.Right).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).GridColumn(6));
        row.OnClick(_ => Cursor = rows.IndexOf(item));
        return row;
    }

    /// <summary>Why the list on screen does not show the 200 rows in order with the ring on row
    /// <paramref name="cursor"/> alone, if it does not.</summary>
    public string? Verify(int cursor)
    {
        if (Cursor != cursor) return $"the cursor is on row {Cursor}, not {cursor}";
        var shown = new List<BoxElement>();
        void Walk(Element e)
        {
            if (e.AutomationId is { } id && id.StartsWith("pair-", StringComparison.Ordinal)) { shown.Add((BoxElement)e); return; }
            foreach (var c in e.Children) Walk(c);
        }
        Walk(Mount());
        if (shown.Count != rows.Count) return $"{shown.Count} rows shown, not {rows.Count}";
        for (int i = 0; i < shown.Count; i++)
        {
            if (shown[i].Children[0] is not TextElement name || name.Content != rows[i].Path) return $"row {i} does not show {rows[i].Path}";
            if ((shown[i].BorderColor == Ring) != (i == cursor)) return $"row {i} {(i == cursor ? "has no" : "has the")} ring";
        }
        return null;
    }
}
