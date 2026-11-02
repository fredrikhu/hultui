using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using HultUI.Icons;

namespace PerfSuite;

static partial class Scenarios
{
    // The reactive path the js-framework-benchmark sample deliberately skips: a Component
    // whose Build() returns a fresh tree that the keyed differ reconciles against the live
    // one. A [Reactive] write rebuilds synchronously, so the rebuild lands in "build".
    private static IEnumerable<Scenario> Components()
    {
        yield return new Scenario("component/edit one of 1,000 keyed rows",
            "A [Reactive] change re-runs Build() for 1,000 keyed rows; the differ patches the one that changed",
            ctx =>
            {
                var list = new RowList(keyed: true);
                list.Fill(ctx.Rng, 1_000);
                ctx.Mount(list.Mount());
                return new Op
                {
                    Run = step =>
                    {
                        int i = step * 37 % list.Items.Count;
                        list.Items[i] = list.Items[i] with { Label = list.Items[i].Label + " !" };
                        list.Version++;
                    },
                };
            });

        yield return new Scenario("component/edit one of 1,000 unkeyed rows",
            "The same edit without keys: the differ matches rows by position",
            ctx =>
            {
                var list = new RowList(keyed: false);
                list.Fill(ctx.Rng, 1_000);
                ctx.Mount(list.Mount());
                return new Op
                {
                    Run = step =>
                    {
                        int i = step * 37 % list.Items.Count;
                        list.Items[i] = list.Items[i] with { Label = list.Items[i].Label + " !" };
                        list.Version++;
                    },
                };
            });

        yield return new Scenario("component/reverse 1,000 keyed rows",
            "Reverse the order of 1,000 keyed rows: every row moves, none is rebuilt",
            ctx =>
            {
                var list = new RowList(keyed: true);
                list.Fill(ctx.Rng, 1_000);
                ctx.Mount(list.Mount());
                return new Op { Run = _ => { list.Items.Reverse(); list.Version++; } };
            });

        yield return new Scenario("component/counter in a 6,000-element tree",
            "A small component's state changes inside a large static tree: only it should rebuild",
            ctx =>
            {
                var counter = new Counter();
                ctx.Mount(Flex.Column().FlexGrow(1).Children(
                    counter.Mount(),
                    ScrollView.New().FlexGrow(1).Children(Ui.Rows(ctx.Rng, 1_000))));
                return new Op { Run = _ => counter.Count++ };
            });

        yield return new Scenario("component/mount 300 components",
            "Mount 300 small components (each a card with its own state) into a panel",
            ctx =>
            {
                var panel = Flex.Row(gap: 12).FlexWrap(FlexWrap.Wrap).FlexShrink(0).Padding(12);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(panel));
                return new Op
                {
                    Reset = _ => panel.ReplaceChildren(),
                    Run = _ =>
                    {
                        var cards = new Element[300];
                        for (int i = 0; i < cards.Length; i++) cards[i] = new CardComponent(i, Ui.Label(ctx.Rng)).Mount();
                        panel.ReplaceChildren(cards);
                    },
                };
            });

        yield return new Scenario("component/list walk beside chrome built with it",
            "virtual/selection walk 1,000 item rows inside a window's chrome — a title bar, a sidebar of workspaces and repositories, a toolbar of tabs and actions, about 130 elements — that the walk's own Build makes again, unchanged, on every move, as Junction's whole-window Build did until plan 17's round 4",
            ctx => SelectionWalk(ctx, null, itemRows: true, chrome: WalkChrome.Built));

        yield return new Scenario("component/list walk beside chrome in kept parts",
            "The same walk with the chrome in three child components the walk's Build hands on unchanged: a move moves their hosts into the new tree and the reconciler puts them back, building nothing in them and marking nothing around them",
            ctx => SelectionWalk(ctx, null, itemRows: true, chrome: WalkChrome.Kept));

        yield return new Scenario("component/worker tick beside kept parts, every part built",
            "A worker's progress tick, posted to the UI thread as a copy's worker posts one every 40 ms, rebuilds a window of three kept chrome parts and a progress card over 1,000 item rows with the full rebuild, which builds every part as well: Vesper's transfer repaint until plan 17's round 6",
            ctx => WorkerTick(ctx, full: true));

        yield return new Scenario("component/worker tick beside kept parts, view only",
            "The same tick through the view-only path: the window and its progress card build, the parts stay as they are",
            ctx => WorkerTick(ctx, full: false));

        yield return new Scenario("component/signal fan-out to 500 labels",
            "One Signal read by 500 Effects, each setting a label: a write updates all 500",
            ctx =>
            {
                var source = new Signal<int>(0);
                var labels = new TextElement[500];
                var effects = new Effect[labels.Length];
                for (int i = 0; i < labels.Length; i++)
                {
                    var label = labels[i] = Text.New("").FontSize(12f).Width(Dimension.Px(90));
                    int offset = i;
                    effects[i] = new Effect(() => label.Content = $"{source.Value + offset}");
                }
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(
                    Flex.Row(gap: 4).FlexWrap(FlexWrap.Wrap).FlexShrink(0).Padding(8).Children(labels)));
                GC.KeepAlive(effects);
                return new Op { Run = _ => source.Value++ };
            });
    }

    /// <summary>A tick posted the way a worker posts one (<c>Application.InvokeOnMainThread</c>), applied
    /// by the frame that follows, with the full rebuild (every part builds) or the view-only one.</summary>
    private static Op WorkerTick(Ctx ctx, bool full)
    {
        var window = new TickWindow(WalkItems(ctx.Rng, 1_000));
        ctx.Mount(window.Mount());
        ctx.Session.PumpFrame();
        int parts = window.PartBuilds, done = window.Done;
        Action tick = full ? window.FullTick : window.ViewTick;
        return new Op
        {
            Run = _ => HultUI.Application.InvokeOnMainThread(tick),
            Check = step =>
            {
                int ticks = step + 1;
                if (window.Done != done + ticks) return $"{window.Done - done} ticks applied of {ticks}";
                if (window.ShownDone != window.Done) return $"the card shows {window.ShownDone}, the tick is {window.Done}";
                int built = window.PartBuilds - parts, expected = full ? 3 * ticks : 0;
                return built == expected ? null : $"the parts were built {built} times, {expected} expected";
            },
        };
    }
}

/// <summary>
/// A window shaped like Vesper's during a copy: three chrome parts (the walk's title bar, sidebar and
/// toolbar) kept from build to build, which build again when the revision they read moves, as every
/// full rebuild moves it; and a view of a progress card over a list of 1,000 item rows. A tick moves
/// the copy on and rebuilds the window; the full one moves the revision too.
/// </summary>
sealed class TickWindow(List<WalkItem> items) : Component
{
    private sealed class TickPart(TickWindow window, Func<Element> build) : Component
    {
        public override Element Build()
        {
            _ = window._revision.Value;
            window.PartBuilds++;
            return build();
        }
    }

    private readonly Signal<int> _revision = new(0);
    private TickPart? _title, _side, _tools;
    private readonly Ref<TextElement> _count = new();
    private Func<WalkItem, string?>? _label;

    public int PartBuilds, Done;

    /// <summary>What the card on screen says, for the check.</summary>
    public int ShownDone => _count.Current is { } t && int.TryParse(t.Content.Split(' ')[0], out var n) ? n : -1;

    public void FullTick()
    {
        Done++;
        _revision.Value++;
        MarkDirty();
    }

    public void ViewTick()
    {
        Done++;
        MarkDirty();
    }

    public override Element Build()
    {
        const int Files = 400, FileBytes = 3_000_000;
        int file = Done % Files;
        double fileFrac = Done % 10 / 10.0, total = (double)Done / (Files * 10);
        var card = Flex.Column(gap: 10).FlexShrink(0).Padding(16).Margin(new Thickness(12)).Width(Dimension.Px(560))
            .BorderColor(Ui.Line).BorderWidth(1f).CornerRadius(8f).Background(Ui.CardFill).Children(
                Flex.Row(gap: 8).AlignItems(AlignItems.Center).Children(
                    Phosphor.Draw("copy", 18f, Ui.Accent),
                    Text.New("Copying").FontSize(15f).FontWeight(FontWeight.Medium),
                    Text.New($"{Done} of {Files * 10} chunks").FontSize(11f).TextColor(Ui.Muted).WithRef(_count)),
                Bar("File", $"file{file:000}.bin", fileFrac),
                Bar("Total", $"{(long)(total * Files * FileBytes) >> 20} MB of {(long)Files * FileBytes >> 20} MB", total));
        var queue = Flex.Column(gap: 0).FlexShrink(0);
        for (int i = Math.Max(0, file - 4); i < Math.Min(Files, file + 5); i++)
            queue.AddChild(Flex.Row(gap: 8).Height(22).AlignItems(AlignItems.Center).Children(
                Phosphor.Draw(i < file ? "check" : i == file ? "arrow-right" : "circle-notch", 12f, Ui.Muted),
                Text.New($"file{i:000}.bin").FontSize(12f).FlexGrow(1),
                Text.New(i < file ? "done" : i == file ? $"{(int)(fileFrac * 100)}%" : "queued").FontSize(11f).TextColor(Ui.Muted)));
        card.AddChild(queue);
        var list = VirtualListElement<WalkItem>.New()
            .ItemHeight(30f)
            .Items(items)
            .AccessibleItemLabel(_label ??= item => item.Subject)
            .RenderWith((item, _) => Flex.Row(gap: 10).AlignItems(AlignItems.Center).Padding(new Thickness(0, 16)).Children(
                Phosphor.Draw(item.Icon, 16f, Ui.Muted),
                Text.New(item.Subject).FontSize(13f).FlexGrow(1).MaxLines(1).TextOverflow(TextOverflow.Ellipsis)))
            .FlexGrow(1);
        var view = Flex.Column(gap: 0).FlexGrow(1).Children(card, list);
        view.Key = "view";
        var tools = (_tools ??= new TickPart(this, WalkPane.Toolbar)).Mount().FlexShrink(0);
        var main = Flex.Column(gap: 0).FlexGrow(1).Children(tools, view);
        main.Key = "main";
        var body = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch).Children(
            (_side ??= new TickPart(this, WalkPane.Sidebar)).Mount().FlexShrink(0), main);
        body.Key = "body";
        return Flex.Column(gap: 0).FlexGrow(1).Children((_title ??= new TickPart(this, WalkPane.TitleBar)).Mount().FlexShrink(0), body);
    }

    private static Element Bar(string label, string detail, double fraction) =>
        Flex.Column(gap: 5).Children(
            Flex.Row(gap: 8).AlignItems(AlignItems.Center).Children(
                Text.New(label.ToUpperInvariant()).FontSize(10f).TextColor(Ui.Muted),
                Text.New(detail).FontSize(11f).FlexGrow(1).MaxLines(1).TextOverflow(TextOverflow.Ellipsis),
                Text.New($"{(int)(fraction * 100)}%").FontSize(10.5f).TextColor(Ui.Muted)),
            ProgressBar.New(Math.Clamp(fraction, 0, 1)));
}

sealed record RowItem(int Id, string Label);

sealed partial class RowList(bool keyed) : Component
{
    [Reactive] public partial int Version { get; set; }

    public List<RowItem> Items { get; } = [];

    public void Fill(Random rng, int count)
    {
        for (int i = 0; i < count; i++) Items.Add(new RowItem(i + 1, Ui.Label(rng)));
    }

    public override Element Build()
    {
        _ = Version;
        var rows = new Element[Items.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            var row = Ui.Row(Items[i].Id, Items[i].Label);
            if (keyed) row.Key = Items[i].Id.ToString();
            rows[i] = row;
        }
        return ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(rows));
    }
}

sealed partial class Counter : Component
{
    [Reactive] public partial int Count { get; set; }

    public override Element Build()
        => Flex.Row(gap: 8).Padding(8).AlignItems(AlignItems.Center).Children(
            Text.New("Clicks").FontSize(13f),
            Text.New($"{Count}").FontSize(13f).FontWeight(FontWeight.Bold).TextColor(Ui.Accent));
}

sealed partial class CardComponent(int index, string label) : Component
{
    [Reactive] public partial bool Starred { get; set; }

    public override Element Build()
        => Flex.Column(gap: 6).Width(Dimension.Px(180)).FlexShrink(0).Padding(12)
            .Background(Color.White).CornerRadius(8f).BorderColor(Ui.Line).BorderWidth(1f)
            .Children(
                Text.New($"Card {index}").FontSize(14f).FontWeight(FontWeight.SemiBold),
                Text.New(label).FontSize(12f).TextColor(Ui.Muted),
                Button.New(Starred ? "Unstar" : "Star").OnClick(_ => Starred = !Starred));
}
