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
