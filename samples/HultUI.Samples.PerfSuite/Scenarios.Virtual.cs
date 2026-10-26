using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Icons;

namespace PerfSuite;

static partial class Scenarios
{
    private static IEnumerable<Scenario> Virtual()
    {
        yield return new Scenario("virtual/list 100k wheel",
            "Wheel 3 notches through a 100,000-item VirtualList: rows realized as they come into view",
            ctx =>
            {
                var list = VirtualRows(ctx.Rng, 100_000);
                ctx.Mount(list.FlexGrow(1));
                return new Op
                {
                    Run = step => ctx.Window.Wheel(500, 380, 0, WheelDirection(step) * 3),
                    Check = ScrolledTo(() => list.ScrollOffsetY),
                };
            });

        yield return new Scenario("virtual/list 100k jump",
            "Jump a 100,000-item VirtualList to a far offset: a whole viewport of new rows",
            ctx =>
            {
                var list = VirtualRows(ctx.Rng, 100_000);
                ctx.Mount(list.FlexGrow(1));
                return new Op { Run = step => list.ScrollOffsetY = (step * 7_919 % 99_000) * 30f };
            });

        yield return new Scenario("virtual/list 100k replace items",
            "Assign a new 100,000-item list (a refresh from the model): visible rows re-realized",
            ctx =>
            {
                var a = Models(ctx.Rng, 100_000);
                var b = Models(ctx.Rng, 100_000);
                var list = VirtualListElement<RowItem>.New().ItemHeight(30).RenderWith(RenderVirtualRow).Items(a);
                ctx.Mount(list.FlexGrow(1));
                return new Op { Run = step => list.Items = step % 2 == 0 ? b : a };
            });

        yield return new Scenario("virtual/selection walk 1,000 rows",
            "Press Down in a 1,000-row list of two-line rows whose selection the owning component's Build reads: the owner rebuilds, the rows are realized again in the render pass",
            ctx => SelectionWalk(ctx, null));

        yield return new Scenario("virtual/selection walk 1,000 item rows",
            "The same walk with .RenderWith((item, selected) => …): the list keeps the rows whose item and selection did not change and makes only the old and the new selected row (and the one scrolled in)",
            ctx => SelectionWalk(ctx, null, itemRows: true));

        yield return new Scenario("virtual/partner walk beside 1,000 item rows, every row told",
            "Down in the left of two 1,000-row lists on item rows while the right one rings the row at the same index, as a file manager marks the file the other pane's cursor pairs with: the ring is told to the right list with InvalidateRows, so each move makes every row on screen there again",
            ctx => PartnerWalk(ctx, oneRow: false));

        yield return new Scenario("virtual/partner walk beside 1,000 item rows, two rows told",
            "The same walk telling the right list only the row the ring left and the row it reached, with InvalidateRow: each move makes those two there, and the one scrolled in",
            ctx => PartnerWalk(ctx, oneRow: true));

        yield return new Scenario("virtual/rebuild beside a 20,000-row RowHeights list",
            "Type into a find field above 20,000 lines whose list takes its row heights from RowHeights, one component rebuilt per keystroke: the list is handed a new height function over the same heights, and keeps its rows",
            ctx => TypeBesideRowHeightsOp(ctx, rowsTold: false));

        yield return new Scenario("virtual/rebuild beside a 20,000-row RowHeights list, rows told",
            "The same typing when the rows show the query: the component tells the list with InvalidateRows when it changed, as a file viewer does while find is typed in, and the rows on screen are made again",
            ctx => TypeBesideRowHeightsOp(ctx, rowsTold: true));

        yield return new Scenario("table/TableView 10k sort",
            "Re-sort a 10,000-row TableView (4 columns) by its text column, flipping direction each step",
            ctx =>
            {
                var table = new TableBench(Models(ctx.Rng, 10_000));
                ctx.Mount(table.Mount());
                return new Op
                {
                    Run = step => table.Table.SortBy("label", step % 2 == 0 ? SortDirection.Ascending : SortDirection.Descending),
                    Check = step => table.Table.SortColumn == "label"
                        && table.Table.SortDirection == (step % 2 == 0 ? SortDirection.Ascending : SortDirection.Descending)
                        ? null : "the table did not re-sort",
                };
            });

        yield return new Scenario("table/TableView 10k wheel",
            "Wheel through a 10,000-row TableView",
            ctx =>
            {
                var table = new TableBench(Models(ctx.Rng, 10_000));
                ctx.Mount(table.Mount());
                return new Op { Run = step => ctx.Window.Wheel(500, 400, 0, WheelDirection(step) * 3) };
            });

        yield return new Scenario("table/TableView 10k resize column",
            "Drag-resize a TableView column: every visible row re-laid out on the new tracks",
            ctx =>
            {
                var table = new TableBench(Models(ctx.Rng, 10_000));
                ctx.Mount(table.Mount());
                return new Op { Run = step => table.Table.SetWidth("label", 220 + 4 * (step % 40)) };
            });
    }

    private static List<RowItem> Models(Random rng, int count)
    {
        var items = new List<RowItem>(count);
        for (int i = 0; i < count; i++) items.Add(new RowItem(i + 1, Ui.Label(rng)));
        return items;
    }

    private static VirtualListElement<RowItem> VirtualRows(Random rng, int count)
        => VirtualListElement<RowItem>.New().ItemHeight(30).RenderWith(RenderVirtualRow).Items(Models(rng, count));

    private static Element RenderVirtualRow(RowItem m) => Ui.Row(m.Id, m.Label);

    /// <summary>
    /// A key walk down a History-style pane (<see cref="WalkPane"/>), as Junction's History,
    /// Commit and Files views and Vesper's panes are walked: Down at the window, the selection
    /// a signal the pane's Build reads, so every press rebuilds the pane, patches its list and
    /// has the list realize its rows again while it paints. The walk starts on the last row the
    /// list shows whole, so every press also scrolls a row, as it does once a walk is under
    /// way. Each frame comes 120 ms after the last — a key pressed and released, not held — so
    /// with an accessibility client listening every step rebuilds its tree: a scroll's refresh
    /// waits 100 ms. <paramref name="check"/> adds what the bridge must have seen. With
    /// <paramref name="itemRows"/> the rows come from <c>.RenderWith((item, selected) => …)</c>,
    /// which keeps the ones that did not change. The row reports the rows made per step. With
    /// <paramref name="chrome"/> the pane sits in a window's chrome (<see cref="WalkChrome"/>).
    /// </summary>
    internal static Op SelectionWalk(Ctx ctx, Func<int, string?>? check, Action? reset = null, bool itemRows = false,
        WalkChrome chrome = WalkChrome.None)
    {
        var pane = new WalkPane(WalkItems(ctx.Rng, 1_000), itemRows, chrome: chrome);
        var host = pane.Mount();
        host.On<KeyDownEvent>(e =>
        {
            if (e.Key == KeyCode.Down) { pane.Move(1); e.StopPropagation = true; }
            else if (e.Key == KeyCode.Up) { pane.Move(-1); e.StopPropagation = true; }
        });
        ctx.Mount(host);
        pane.Select(pane.LastWholeRow());
        ctx.Session.PumpFrame(WalkFrame);
        int first = pane.Selected;
        int madeBefore = 0;
        return new Op
        {
            Reset = reset is null ? null : _ => reset(),
            Run = _ =>
            {
                madeBefore = pane.RowsMade;
                ctx.Window.PressKey(KeyCode.Down);
            },
            FrameDelta = WalkFrame,
            Figure = () => pane.RowsMade - madeBefore,
            FigureUnit = "rows made",
            Check = step => pane.Verify(first + step + 1) ?? pane.VerifyChrome() ?? check?.Invoke(step),
        };
    }

    private static readonly TimeSpan WalkFrame = TimeSpan.FromMilliseconds(120);

    /// <summary>A key walk down the left list of a <see cref="PartnerPane"/>; the row reports the
    /// rows both lists made per step.</summary>
    private static Op PartnerWalk(Ctx ctx, bool oneRow)
    {
        var pane = new PartnerPane(WalkItems(ctx.Rng, 1_000), oneRow);
        var host = pane.Mount();
        host.On<KeyDownEvent>(e =>
        {
            if (e.Key == KeyCode.Down) { pane.Move(1); e.StopPropagation = true; }
        });
        ctx.Mount(host);
        pane.Select(pane.LastWholeRow());
        ctx.Session.PumpFrame();
        int first = pane.Selected;
        int madeBefore = 0;
        return new Op
        {
            Run = _ =>
            {
                madeBefore = pane.RowsMade;
                ctx.Window.PressKey(KeyCode.Down);
            },
            Figure = () => pane.RowsMade - madeBefore,
            FigureUnit = "rows made",
            Check = step => pane.Verify(first + step + 1),
        };
    }

    private static readonly string[] WalkIcons = ["git-commit", "git-commit", "git-commit", "git-merge", "git-commit", "tag"];
    private static readonly string[] WalkAuthors = ["Fredrik Hult", "Ada Lovelace", "Grace Hopper", "Linus Torvalds", "Barbara Liskov"];

    private static List<WalkItem> WalkItems(Random rng, int count)
    {
        var items = new List<WalkItem>(count);
        var when = new DateTime(2026, 10, 5, 18, 0, 0);
        for (int i = 0; i < count; i++)
        {
            string hash = rng.Next(0x1000000, int.MaxValue).ToString("x7")[..7];
            string author = WalkAuthors[rng.Next(WalkAuthors.Length)];
            when = when.AddMinutes(-rng.Next(5, 600));
            items.Add(new WalkItem(i, WalkIcons[rng.Next(WalkIcons.Length)],
                Ui.Paragraph(rng, 5 + rng.Next(6)), hash, author,
                $"{hash} · {author} · {when:yyyy-MM-dd HH:mm} · +{rng.Next(1, 400)} −{rng.Next(0, 120)}"));
        }
        return items;
    }
}

/// <summary>One commit of <see cref="WalkPane"/>: an icon, a subject, a line of detail.</summary>
sealed record WalkItem(int Index, string Icon, string Subject, string Hash, string Author, string Meta);

/// <summary>
/// The window around a <see cref="WalkPane"/>, shaped like Junction's: a title bar (brand,
/// breadcrumb, search, window buttons), a sidebar (workspaces and repositories with their
/// badges) and a toolbar (seven tabs, five actions) — about 130 elements, none of which a
/// selection move changes.
/// </summary>
enum WalkChrome
{
    /// <summary>No chrome: the pane alone.</summary>
    None,
    /// <summary>Made by the pane's own Build, with the list, on every move — what a window that
    /// is one component does.</summary>
    Built,
    /// <summary>Three child components the pane makes once and hands to every build: a move
    /// moves their hosts into the new tree and the reconciler puts them back.</summary>
    Kept,
}

/// <summary>
/// The shape of Junction's History view: a header, then a <see cref="VirtualListElement{T}"/>
/// of two-line rows (an icon, the subject, a line of detail), no keys on the rows, the
/// selection drawn by the template from the index the pane's Build read, the list told the
/// selected index for assistive technology, and its rows named for it by a label made from
/// the item alone. A selection move writes a signal; the rebuild scrolls the row into view after
/// the build, as Junction's <c>OnAfterBuild</c> does. With <paramref name="itemRows"/> the
/// template is the two-argument one, whose rows the list keeps while they are unchanged.
/// With <paramref name="details"/> the same Build makes a details pane beside the list, for
/// the commit the walk started on (<see cref="DetailsPane"/>): what an app's whole-window
/// Build makes again, unchanged, on every move.
/// </summary>
sealed class WalkPane(List<WalkItem> items, bool itemRows = false, bool details = false,
    WalkChrome chrome = WalkChrome.None) : Component
{
    public const float RowHeight = 46f;

    /// <summary>A part of the kept chrome: builds what it is given, once.</summary>
    private sealed class ChromePart(Func<Element> build) : Component
    {
        public int Builds;

        public override Element Build()
        {
            Builds++;
            return build();
        }
    }

    private ChromePart? _titlePart, _sidePart, _toolPart;

    private readonly Signal<int> _selected = new(0);
    private readonly Ref<VirtualListElement<WalkItem>> _list = new();
    private int _built;
    private bool _reveal;
    private int _opened = -1;

    /// <summary>Rows the template has made.</summary>
    public int RowsMade { get; private set; }

    // Made once, as an app keeps a label that reads only its item: one delegate every build.
    private Func<WalkItem, string?>? _label;

    private static string Label(WalkItem item) => $"{item.Subject}, {item.Author}, {item.Hash}";

    public int Selected => _selected.Value;

    /// <summary>Shows <paramref name="index"/> in the details pane, as opening a commit does.</summary>
    public void Open(int index)
    {
        _opened = index;
        MarkDirty();
    }

    public void Select(int index)
    {
        _reveal = true;
        _selected.Value = Math.Clamp(index, 0, items.Count - 1);
    }

    public void Move(int delta) => Select(_selected.Value + delta);

    /// <summary>The last row the list shows whole, scrolled to the top.</summary>
    public int LastWholeRow() =>
        Math.Max(0, (int)((_list.Current?.Layout.AbsoluteContentBox.Height ?? 0) / RowHeight) - 1);

    public override Element Build()
    {
        int selected = _built = _selected.Value;
        var list = VirtualListElement<WalkItem>.New()
            .ItemHeight(RowHeight)
            .Items(items)
            .AccessibleItemLabel(_label ??= Label)
            .SelectedIndex(selected)
            .FlexGrow(1);
        if (itemRows) list.RenderWith((item, isSelected) => Row(item, isSelected));
        else list.RenderWith(item => Row(item, item.Index == selected));
        var pane = Flex.Column(gap: 0).FlexGrow(1).Children(
            Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center).Padding(new Thickness(9, 16))
                .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 0, 1, 0))
                .Children(
                    Phosphor.Draw("git-branch", 14f, Ui.Muted),
                    Text.New("History").FontSize(13f).FontWeight(FontWeight.SemiBold),
                    Text.New($"{items.Count:N0} commits").FontSize(12f).TextColor(Ui.Muted)),
            list.WithRef(_list));
        Element content = details
            ? Flex.Row(gap: 0).FlexGrow(1).Children(pane, DetailsPane(items[Math.Max(0, _opened)]))
            : pane;
        if (chrome == WalkChrome.None) return content;

        // Keyed throughout, as Junction keys its shell, so each list reconciles by identity.
        Element title, side, tools;
        if (chrome == WalkChrome.Kept)
        {
            title = (_titlePart ??= new ChromePart(TitleBar)).Mount().FlexShrink(0);
            side = (_sidePart ??= new ChromePart(Sidebar)).Mount().FlexShrink(0);
            tools = (_toolPart ??= new ChromePart(Toolbar)).Mount().FlexShrink(0);
        }
        else
        {
            title = TitleBar();
            side = Sidebar();
            tools = Toolbar();
        }
        content.Key = "view";
        var main = Flex.Column(gap: 0).FlexGrow(1).Children(tools, content);
        main.Key = "main";
        var body = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch).Children(side, main);
        body.Key = "body";
        return Flex.Column(gap: 0).FlexGrow(1).Children(title, body);
    }

    /// <summary>Null when the kept chrome was built once and never again, or what happened.</summary>
    public string? VerifyChrome()
    {
        if (chrome != WalkChrome.Kept) return null;
        int builds = (_titlePart?.Builds ?? 0) + (_sidePart?.Builds ?? 0) + (_toolPart?.Builds ?? 0);
        return builds == 3 ? null : $"the kept chrome was built {builds} times";
    }

    private static readonly string[] ChromeTabs = ["Overview", "History", "Commit", "Changes", "Branches", "Tags", "Files"];
    private static readonly string[] ChromeTabIcons = ["squares-four", "git-commit", "git-diff", "pencil", "git-branch", "tag", "folders"];
    private static readonly string[] ChromeRepos = ["hultui", "junction-notes", "vesper", "meridian"];

    private static Element TitleBar()
    {
        var crumb = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center).Padding(new Thickness(0, 0, 0, 14)).Children(
            Text.New("Junction").FontSize(13f).FontWeight(FontWeight.Medium),
            Text.New("·").FontSize(13f).TextColor(Ui.Muted),
            Phosphor.Draw("git-fork", 13f, Ui.Accent),
            Text.New("hultui").FontSize(13f),
            Text.New("·").FontSize(13f).TextColor(Ui.Muted),
            Phosphor.Draw("git-branch", 12f, Ui.Accent),
            Text.New("main").FontSize(12f),
            Text.New("·").FontSize(13f).TextColor(Ui.Muted),
            Text.New("Frameworks").FontSize(13f));
        var search = Flex.Row(gap: 8).Height(26).AlignItems(AlignItems.Center).Padding(new Thickness(0, 8, 0, 10))
            .BorderColor(Ui.Line).BorderWidth(1f).CornerRadius(6f).Children(
                Phosphor.Draw("magnifying-glass", 13f, Ui.Muted),
                Text.New("Search & commands").FontSize(12f).TextColor(Ui.Muted),
                Box.New().Padding(new Thickness(1, 4)).BorderColor(Ui.Line).BorderWidth(1f).CornerRadius(3f)
                    .Children(Text.New("Ctrl+P").FontSize(10f).TextColor(Ui.Muted)));
        var buttons = Flex.Row(gap: 0).AlignItems(AlignItems.Stretch).Children(
            WindowButton("minus"), WindowButton("square"), WindowButton("x"));
        var bar = Flex.Row(gap: 0).Height(40).FlexShrink(0).AlignItems(AlignItems.Stretch)
            .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 0, 1, 0)).Children(
                crumb,
                Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Center).Children(
                    Box.New().FlexGrow(1), search, Box.New().Width(10), buttons));
        bar.Key = "titlebar";
        return bar;

        static Element WindowButton(string icon) =>
            Flex.Row(gap: 0).Width(46).Height(40).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
                .Children(Phosphor.Draw(icon, 13f, Ui.Muted));
    }

    private static Element Sidebar()
    {
        var aside = Flex.Column(gap: 2).Width(266).FlexShrink(0).FlexGrow(1).Padding(new Thickness(12, 10))
            .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 1, 0, 0));
        aside.AddChild(Flex.Row(gap: 9).AlignItems(AlignItems.Center).Padding(new Thickness(0, 4, 8, 4)).Children(
            Box.New().Width(22).Height(22).CornerRadius(5f).Background(Ui.Selected).AlignItems(AlignItems.Center)
                .JustifyContent(JustifyContent.Center).Children(Phosphor.Draw("git-fork", 14f, Ui.Accent)),
            Text.New("Junction").FontSize(15f).FontWeight(FontWeight.Medium).FlexGrow(1),
            Phosphor.Draw("sidebar", 16f, Ui.Muted)));
        aside.AddChild(Section("stack", "Workspaces", "3"));
        foreach (var (name, count, active) in new[] { ("Frameworks", "4", true), ("Apps", "6", false), ("Home lab", "2", false) })
            aside.AddChild(Flex.Row(gap: 9).AlignItems(AlignItems.Center).Padding(new Thickness(7, 8))
                .Background(active ? Ui.Selected : Color.Transparent).CornerRadius(6f).Children(
                    Phosphor.Draw("stack", 15f, Ui.Accent),
                    Text.New(name).FontSize(13f).FlexGrow(1),
                    Text.New(count).FontSize(11f).TextColor(Ui.Muted)));
        aside.AddChild(Section("folders", "Repositories", ChromeRepos.Length.ToString()));
        for (int i = 0; i < ChromeRepos.Length; i++)
        {
            var name = ChromeRepos[i];
            aside.AddChild(Flex.Row(gap: 9).AlignItems(AlignItems.Center).Padding(new Thickness(7, 8))
                .Background(i == 0 ? Ui.Selected : Color.Transparent).CornerRadius(6f).Children(
                    Box.New().Width(24).Height(24).CornerRadius(5f).Background(Ui.CardFill).AlignItems(AlignItems.Center)
                        .JustifyContent(JustifyContent.Center).Children(Text.New(name[..1].ToUpperInvariant()).FontSize(11f)),
                    Flex.Column(gap: 0).FlexGrow(1).FlexShrink(1).Children(
                        Text.New(name).FontSize(13f).FontWeight(FontWeight.Medium).MaxLines(1).TextOverflow(TextOverflow.Ellipsis),
                        Text.New($@"C:\Users\dev\source\{name}").FontSize(10.5f).TextColor(Ui.Muted).MaxLines(1).TextOverflow(TextOverflow.Ellipsis)),
                    Flex.Row(gap: 6).FlexShrink(0).AlignItems(AlignItems.Center).Children(
                        Box.New().Width(7).Height(7).CornerRadius(3.5f).Background(Ui.Accent),
                        Flex.Row(gap: 1).AlignItems(AlignItems.Center).Children(
                            Phosphor.Draw("arrow-up", 9f, Ui.Muted), Text.New($"{i}").FontSize(10f).TextColor(Ui.Muted)),
                        Flex.Row(gap: 1).AlignItems(AlignItems.Center).Children(
                            Phosphor.Draw("arrow-down", 9f, Ui.Muted), Text.New($"{2 * i}").FontSize(10f).TextColor(Ui.Muted)))));
        }
        aside.Key = "sidebar";
        return aside;

        static Element Section(string icon, string text, string count) =>
            Flex.Row(gap: 6).AlignItems(AlignItems.Center).Padding(new Thickness(12, 6, 6, 6)).Children(
                Phosphor.Draw(icon, 12f, Ui.Muted),
                Text.New(text.ToUpperInvariant()).FontSize(10f).TextColor(Ui.Muted).FlexGrow(1),
                Text.New(count).FontSize(11f).TextColor(Ui.Muted),
                Box.New().Width(22).Height(22).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
                    .Children(Phosphor.Draw("plus", 14f, Ui.Muted)));
    }

    private static Element Toolbar()
    {
        var tabs = Flex.Row(gap: 2).FlexShrink(0).Padding(new Thickness(3)).AlignItems(AlignItems.Center)
            .BorderColor(Ui.Line).BorderWidth(1f).CornerRadius(6f);
        for (int i = 0; i < ChromeTabs.Length; i++)
            tabs.AddChild(Flex.Row(gap: 6).FlexShrink(0).AlignItems(AlignItems.Center).Padding(new Thickness(5, 10))
                .BorderColor(i == 1 ? Ui.Accent : Color.Transparent).BorderWidth(1f).CornerRadius(4f).Children(
                    Phosphor.Draw(ChromeTabIcons[i], 13f, i == 1 ? Ui.Accent : Ui.Muted),
                    Text.New(ChromeTabs[i]).FontSize(12.5f).FontWeight(FontWeight.Medium),
                    Box.New().Padding(new Thickness(1, 4)).BorderColor(Ui.Line).BorderWidth(1f).CornerRadius(3f)
                        .Children(Text.New($"{i + 1}").FontSize(9f).TextColor(Ui.Muted))));
        var actions = Flex.Row(gap: 6).FlexShrink(0).AlignItems(AlignItems.Center);
        foreach (var (icon, label) in new[] { ("arrows-clockwise", "Fetch"), ("arrow-down", "Pull"), ("arrow-up", "Push"), ("tray-arrow-down", "Stash"), ("wrench", "Tools") })
            actions.AddChild(Flex.Row(gap: 6).Height(34).AlignItems(AlignItems.Center).Padding(new Thickness(0, 10))
                .BorderColor(Ui.Line).BorderWidth(1f).CornerRadius(6f).Children(
                    Phosphor.Draw(icon, 14f, Ui.Muted), Text.New(label).FontSize(12.5f)));
        var bar = Flex.Row(gap: 14).Height(52).FlexShrink(0).AlignItems(AlignItems.Center).Padding(new Thickness(0, 16))
            .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 0, 1, 0)).Children(tabs, Box.New().FlexGrow(1), actions);
        bar.Key = "toolbar";
        return bar;
    }

    /// <summary>
    /// A commit's details, as an app shows the one that was opened: its subject, who and when,
    /// a message, the files it changed and two actions. About sixty nodes for assistive
    /// technology, none of which a selection move changes.
    /// </summary>
    private static Element DetailsPane(WalkItem item)
    {
        var files = new Element[24];
        for (int i = 0; i < files.Length; i++)
            files[i] = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center).Padding(new Thickness(3, 0))
                .Children(
                    Phosphor.Draw(i % 5 == 0 ? "file-image" : "file-code", 14f, Ui.Muted),
                    Text.New($"src/{item.Author.Split(' ')[0].ToLowerInvariant()}/part{i}/File{item.Index + i}.cs")
                        .FontSize(12f).FlexGrow(1).MaxLines(1).TextOverflow(TextOverflow.Ellipsis),
                    Text.New($"+{(item.Index * 7 + i * 13) % 90} −{(item.Index * 3 + i * 5) % 40}").FontSize(11f).TextColor(Ui.Muted));
        return Flex.Column(gap: 8).Width(Dimension.Px(380)).FlexShrink(0).Padding(16)
            .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 0, 0, 1))
            .Children(
                Text.New("Commit").FontSize(12f).TextColor(Ui.Muted),
                Text.New(item.Subject).FontSize(15f).FontWeight(FontWeight.SemiBold),
                Text.New(item.Meta).FontSize(12f).TextColor(Ui.Muted),
                Text.New($"{item.Subject}. {item.Subject}.").FontSize(13f),
                Flex.Row(gap: 8).Children(Button.New("Checkout"), Button.New("Copy hash", ButtonVariant.Secondary)),
                Text.New($"{files.Length} files changed").FontSize(12f).FontWeight(FontWeight.SemiBold),
                ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(files)));
    }

    protected override void OnAfterBuild()
    {
        if (!_reveal) return;
        _reveal = false;
        _list.Current?.ScrollRangeIntoView(_built * RowHeight, RowHeight);
    }

    private BoxElement Row(WalkItem item, bool selected)
    {
        RowsMade++;
        var row = Flex.Row(gap: 10).AlignItems(AlignItems.Center).Padding(new Thickness(0, 16))
            .Background(selected ? Ui.Selected : Color.Transparent)
            .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 0, 1, 0))
            .Cursor(CursorType.Pointer)
            .Children(
                Phosphor.Draw(item.Icon, 18f, selected ? Ui.Accent : Ui.Muted),
                Flex.Column(gap: 3).FlexGrow(1).FlexShrink(1).Children(
                    Text.New(item.Subject).FontSize(13f).MaxLines(1).TextOverflow(TextOverflow.Ellipsis),
                    Text.New(item.Meta).FontSize(11f).TextColor(Ui.Muted).MaxLines(1).TextOverflow(TextOverflow.Ellipsis)));
        row.OnClick(_ => Select(item.Index));
        return row;
    }

    /// <summary>
    /// Null when row <paramref name="expected"/> is selected, told to the list, on screen
    /// whole and drawn selected, and the row above it no longer is. Reads the rows the list
    /// shows by where they are, not by how it made them.
    /// </summary>
    public string? Verify(int expected)
    {
        if (_selected.Value != expected) return $"the selection is on row {_selected.Value}, not {expected}";
        if (_list.Current is not { } list) return "the list is not mounted";
        if (list.SelectedIndex != expected) return $"the list says row {list.SelectedIndex} is selected";
        var view = list.WindowContentBox;
        float top = view.Y + expected * RowHeight - list.ScrollOffsetY;
        if (top < view.Y - 0.5f || top + RowHeight > view.Bottom + 0.5f)
            return $"row {expected} is not on screen whole (top {top:0.#}, view {view.Y:0.#}–{view.Bottom:0.#})";
        if (RowAt(list, top + RowHeight / 2) is not { } row) return $"no row is drawn where row {expected} is";
        if (row.BackgroundColor != Ui.Selected) return $"row {expected} is not drawn selected";
        if (RowAt(list, top - RowHeight / 2) is { } above && above.BackgroundColor == Ui.Selected)
            return $"row {expected - 1} is still drawn selected";
        return null;
    }

    private static BoxElement? RowAt(Element list, float y)
    {
        var children = list.Children;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] is not BoxElement { Children.Count: > 0 } row) continue;   // not the spacer
            var box = row.WindowBorderBox;
            if (y >= box.Y && y < box.Bottom) return row;
        }
        return null;
    }
}

/// <summary>
/// Two lists of the same commits on item rows, side by side, as Vesper's two panes over folders
/// of the same names: the left one's selection is a signal the Build reads, and the right one
/// rings the row at the selected index — what the row shows beside its item and selection, so
/// the list has to be told when it moves. With <paramref name="oneRow"/> it is told the row the
/// ring left and the row it reached (<see cref="VirtualListElement{T}.InvalidateRow"/>);
/// without, every row (<see cref="VirtualListElement{T}.InvalidateRows"/>), which is what an app
/// that keeps the ring in its "what the rows show" shape does. Both lists follow the selection.
/// </summary>
sealed class PartnerPane(List<WalkItem> items, bool oneRow) : Component
{
    private const float RowHeight = WalkPane.RowHeight;

    private readonly Signal<int> _selected = new(0);
    private readonly Ref<VirtualListElement<WalkItem>> _left = new(), _right = new();
    private int _ringed = -1, _built;
    private bool _reveal;
    private Func<WalkItem, string?>? _label;

    /// <summary>Rows the templates have made, both lists.</summary>
    public int RowsMade { get; private set; }

    public int Selected => _selected.Value;

    public void Select(int index)
    {
        _reveal = true;
        _selected.Value = Math.Clamp(index, 0, items.Count - 1);
    }

    public void Move(int delta) => Select(_selected.Value + delta);

    public int LastWholeRow() =>
        Math.Max(0, (int)((_left.Current?.Layout.AbsoluteContentBox.Height ?? 0) / RowHeight) - 1);

    public override Element Build()
    {
        int selected = _built = _selected.Value;
        // Told before the right list is patched, through its ref, as the ring moves.
        if (_ringed != selected && _right.Current is { } mounted)
        {
            if (oneRow) { mounted.InvalidateRow(_ringed); mounted.InvalidateRow(selected); }
            else mounted.InvalidateRows();
        }
        _ringed = selected;
        _label ??= item => item.Subject;
        var left = VirtualListElement<WalkItem>.New()
            .ItemHeight(RowHeight).Items(items).SelectedIndex(selected).AccessibleItemLabel(_label)
            .RenderWith((item, isSelected) => Row(item, isSelected, ringed: false))
            .FlexGrow(1).FlexBasis(Dimension.Px(0));
        var right = VirtualListElement<WalkItem>.New()
            .ItemHeight(RowHeight).Items(items).AccessibleItemLabel(_label)
            .RenderWith((item, _) => Row(item, false, ringed: item.Index == selected))
            .FlexGrow(1).FlexBasis(Dimension.Px(0));
        return Flex.Row(gap: 0).FlexGrow(1).Children(left.WithRef(_left), right.WithRef(_right));
    }

    protected override void OnAfterBuild()
    {
        if (!_reveal) return;
        _reveal = false;
        _left.Current?.ScrollRangeIntoView(_built * RowHeight, RowHeight);
        _right.Current?.ScrollRangeIntoView(_built * RowHeight, RowHeight);
    }

    private BoxElement Row(WalkItem item, bool selected, bool ringed)
    {
        RowsMade++;
        return Flex.Row(gap: 10).AlignItems(AlignItems.Center).Padding(new Thickness(0, 16))
            .Background(selected ? Ui.Selected : Color.Transparent)
            .BorderColor(ringed ? Ui.Accent : Color.Transparent).BorderWidth(1f)
            .Children(
                Phosphor.Draw(item.Icon, 18f, selected ? Ui.Accent : Ui.Muted),
                Flex.Column(gap: 3).FlexGrow(1).FlexShrink(1).Children(
                    Text.New(item.Subject).FontSize(13f).MaxLines(1).TextOverflow(TextOverflow.Ellipsis),
                    Text.New(item.Meta).FontSize(11f).TextColor(Ui.Muted).MaxLines(1).TextOverflow(TextOverflow.Ellipsis)));
    }

    /// <summary>Null when row <paramref name="expected"/> is selected on the left and ringed on
    /// the right, and the right row above it no longer is.</summary>
    public string? Verify(int expected)
    {
        if (_selected.Value != expected) return $"the selection is on row {_selected.Value}, not {expected}";
        if (_right.Current is not { } right) return "the right list is not mounted";
        BoxElement? ring = null, above = null;
        var view = right.WindowContentBox;
        float top = view.Y + expected * RowHeight - right.ScrollOffsetY;
        for (int i = 0; i < right.Children.Count; i++)
        {
            if (right.Children[i] is not BoxElement { Children.Count: > 0 } row) continue;
            var box = row.WindowBorderBox;
            if (top + RowHeight / 2 >= box.Y && top + RowHeight / 2 < box.Bottom) ring = row;
            if (top - RowHeight / 2 >= box.Y && top - RowHeight / 2 < box.Bottom) above = row;
        }
        if (ring is null) return $"no row is drawn where row {expected} is on the right";
        if (ring.BorderColor != Ui.Accent) return $"row {expected} is not ringed on the right";
        if (above is not null && above.BorderColor == Ui.Accent) return $"row {expected - 1} is still ringed on the right";
        return null;
    }
}

sealed class TableBench : Component
{
    private readonly List<RowItem> _items;

    public TableView<RowItem> Table { get; }

    public TableBench(List<RowItem> items)
    {
        _items = items;
        Table = new TableView<RowItem>(
        [
            new TableColumn<RowItem> { Id = "id", Header = "Id", Width = 80, Text = r => r.Id.ToString(),
                Compare = (a, b) => a.Id.CompareTo(b.Id), Align = ColumnAlign.End },
            new TableColumn<RowItem> { Id = "label", Header = "Label", Width = 260, Text = r => r.Label,
                Compare = (a, b) => string.CompareOrdinal(a.Label, b.Label) },
            new TableColumn<RowItem> { Id = "length", Header = "Length", Width = 90, Text = r => r.Label.Length.ToString(),
                Compare = (a, b) => a.Label.Length.CompareTo(b.Label.Length), Align = ColumnAlign.End },
            new TableColumn<RowItem> { Id = "notes", Header = "Notes", Fill = 1, Text = r => r.Label.ToUpperInvariant() },
        ], MarkDirty);
    }

    public override Element Build()
        => Flex.Column(gap: 0).FlexGrow(1).Children(Table.Header(), Table.Body(_items).FlexGrow(1));
}
