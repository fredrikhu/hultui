using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;

namespace PerfSuite;

static partial class Scenarios
{
    // The same kind of frames with the real AT-SPI bridge behind them, in process with its signals
    // counted (AtSpiRig), on any OS: what a Linux screen reader adds on top of the build and diff,
    // the bridge's objects and the signals it decides to send. "other" holds the whole a11y phase.
    private static IEnumerable<Scenario> AtSpiAccessibility()
    {
        yield return new Scenario("a11y/click checkbox in 500 rows, AT-SPI",
            "Click a checkbox in a 500-row settings list with the AT-SPI bridge: toggle and focus signals over 1,001 nodes",
            ctx => ClickCheckbox(ctx, AtSpiRig.Install(ctx.Window)));

        yield return new Scenario("a11y/type in TextInput, AT-SPI",
            "Type one character into a TextInput above 200 rows with the AT-SPI bridge: value and text signals over 603 nodes",
            ctx => TypeInInput(ctx, AtSpiRig.Install(ctx.Window)));

        yield return new Scenario("a11y/edit 1 label of 1,000 rows, AT-SPI",
            "Change one row's label in 1,000 rows with the AT-SPI bridge: one name signal over 2,001 nodes",
            ctx => EditOneLabel(ctx, AtSpiRig.Install(ctx.Window)));

        yield return new Scenario("a11y/tab through 300 fields, AT-SPI",
            "Press Tab in a 300-field form with the AT-SPI bridge: the focus moves, no rebuild",
            ctx => TabThroughFields(ctx, AtSpiRig.Install(ctx.Window)));

        yield return new Scenario("a11y/wheel 1,000 rows, AT-SPI",
            "Wheel 3 notches through 1,000 rows with the AT-SPI bridge, each frame 120 ms apart so each refreshes the tree's boxes",
            ctx => WheelRows(ctx, AtSpiRig.Install(ctx.Window)));

        yield return new Scenario("a11y/selection walk 1,000 rows, AT-SPI",
            "virtual/selection walk with the AT-SPI bridge: every Down over the list's 1,000 item nodes, the bridge's upkeep and signals",
            ctx => SelectionWalk(ctx, Applied(AtSpiRig.Install(ctx.Window))));

        yield return new Scenario("a11y/selection walk 3,000 item rows, AT-SPI",
            "The item-rows walk over 3,000 commits with the AT-SPI bridge: items served on demand, the upkeep and signals of a move and the scroll it makes",
            ctx => SelectionWalk(ctx, Applied(AtSpiRig.Install(ctx.Window)), itemRows: true, count: 3_000));

        yield return new Scenario("a11y/context menu walk over 1,000 rows, AT-SPI",
            "Down through an open 20-item context menu over 1,000 rows with the AT-SPI bridge: what a move of its selection tells, and the trees a key builds",
            ctx =>
            {
                AtSpiRig.Install(ctx.Window);
                return ContextMenuWalk(ctx);
            });

        yield return new Scenario("a11y/command palette typing over 2,000 commands, AT-SPI",
            "Type one letter into an open command palette over 2,000 commands and 1,000 rows with the AT-SPI bridge (a Backspace between keys, untimed): the field's value and the new list told, and the trees a key builds",
            ctx =>
            {
                AtSpiRig.Install(ctx.Window);
                return PaletteTyping(ctx);
            });

        yield return new Scenario("a11y/command palette Down over 2,000 commands, AT-SPI",
            "Press Down in an open command palette over 2,000 commands and 1,000 rows with the AT-SPI bridge, each key scrolling one row in: the choice told, and the trees a key builds",
            ctx =>
            {
                AtSpiRig.Install(ctx.Window);
                return PaletteDown(ctx, trees: true);
            });

        yield return new Scenario("a11y/label ticking in a dialog over 1,000 rows, AT-SPI",
            "A label in an open dialog (Dialog.Show, whose scrim a click closes) changes every 50 ms over 1,000 rows with the AT-SPI bridge, as a fetch's progress does: what a tick tells, and the trees it builds",
            ctx =>
            {
                var rig = AtSpiRig.Install(ctx.Window);
                return LabelTicks(ctx, MountDialogTick(ctx), Applied(rig));
            });

        yield return new Scenario("a11y/label ticking in an in-tree modal over 1,000 rows, AT-SPI",
            "A label in a modal card the app keeps in its own tree (a Dialog-role card on a backdrop whose click closes it, as Meridian's Options) changes every 50 ms over 1,000 rows with the AT-SPI bridge: what a tick tells, and the trees it builds",
            ctx =>
            {
                var rig = AtSpiRig.Install(ctx.Window);
                return LabelTicks(ctx, MountInTreeModal(ctx), Applied(rig));
            });

        yield return new Scenario("a11y/filter a list below 1,000 rows, keys 500 ms apart, AT-SPI",
            "A 200-item list below 1,000 rows loses or gains items on each key, 500 ms apart, with the AT-SPI bridge: each key builds the tree, whose diff takes the items that went or came",
            ctx =>
            {
                var rig = AtSpiRig.Install(ctx.Window);
                return FilterBelowRows(ctx, Applied(rig));
            });

        yield return new Scenario("a11y/add a row to 1,000 rows, AT-SPI",
            "Add one row to the end of 1,000 rows with the AT-SPI bridge, 500 ms apart (the row taken out again between steps, untimed): a diff of one added row over 4,001 nodes",
            ctx =>
            {
                var rig = AtSpiRig.Install(ctx.Window);
                var body = Ui.Rows(ctx.Rng, 1_000);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(body));
                Element? added = null;
                var applied = Applied(rig);
                return new Op
                {
                    Reset = _ =>
                    {
                        if (added is null) return;
                        body.RemoveChild(added);
                        added = null;
                    },
                    Run = step => body.AddChild(added = Ui.Row(5_000 + step, "added row")),
                    FrameDelta = TimeSpan.FromMilliseconds(500),
                    Check = step => body.Children.Count == 1_001 ? applied(step) : $"the list has {body.Children.Count} rows",
                };
            });

        yield return new Scenario("a11y/switch between 3 kept tabs of 300 rows, AT-SPI",
            "Select the next of three tabs whose pages (300 rows each) are kept alive, 500 ms apart, with the AT-SPI bridge: one page leaves the tree and another comes back",
            ctx =>
            {
                var rig = AtSpiRig.Install(ctx.Window);
                var tabs = Tabs.New();
                for (int t = 0; t < 3; t++)
                {
                    int seed = t;
                    tabs.Tab($"Tab {t + 1}", () => ScrollView.New().FlexGrow(1).Children(Ui.Rows(new Random(seed), 300)), keepAlive: true);
                }
                ctx.Mount(Flex.Column().FlexGrow(1).Children(tabs.FlexGrow(1)));
                // Each page built once before the clock starts.
                for (int t = 1; t <= 3; t++)
                {
                    tabs.Selected(t % 3);
                    ctx.Pump();
                }
                var applied = Applied(rig);
                return new Op
                {
                    Run = step => tabs.Selected((step + 1) % 3),
                    FrameDelta = TimeSpan.FromMilliseconds(500),
                    Check = step => tabs.SelectedIndex == (step + 1) % 3 ? applied(step) : $"tab {tabs.SelectedIndex} selected, not {(step + 1) % 3}",
                };
            });

        yield return new Scenario("a11y/create 1,000 rows, AT-SPI",
            "Replace a list with 1,000 fresh rows with the AT-SPI bridge (the list emptied between steps, untimed): an object made for each of the 4,000 nodes added",
            ctx =>
            {
                var rig = AtSpiRig.Install(ctx.Window);
                var body = Flex.Column().FlexShrink(0);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(body));
                int next = 1;
                return new Op
                {
                    Reset = _ => body.ReplaceChildren(),
                    Run = _ =>
                    {
                        var rows = new Element[1_000];
                        for (int i = 0; i < rows.Length; i++) rows[i] = Ui.Row(next++, Ui.Label(ctx.Rng));
                        body.ReplaceChildren(rows);
                    },
                    Check = Applied(rig),
                };
            });
    }

    /// <summary>A 200-item list below 1,000 rows that loses or gains items on each key, 500 ms apart.</summary>
    private static Op FilterBelowRows(Ctx ctx, Func<int, IReadOnlyList<string>, string?> told)
    {
        var all = Enumerable.Range(0, 200).Select(i => $"tag {i}").ToList();
        // Made up front: the step times the refresh, not the list it is handed.
        var filtered = Enumerable.Range(0, 40).Select(i => (IReadOnlyList<string>)all.Take(150 + i).ToList()).ToArray();
        var list = VirtualListElement<string>.New().ItemHeight(24).Items(all).Height(Dimension.Px(200))
            .RenderWith((tag, selected) => Flex.Row().Padding(new Thickness(2, 8)).Children(Text.New(tag).FontSize(12f)));
        ctx.Mount(Flex.Column().FlexGrow(1).Children(ScrollView.New().FlexGrow(1).Children(Ui.Rows(ctx.Rng, 1_000)), list));
        int treesBefore = 0;
        return new Op
        {
            Run = step =>
            {
                treesBefore = ctx.Window.AccessibilityTreesBuilt;
                list.Items = filtered[(step * 7) % filtered.Length];
            },
            FrameDelta = TimeSpan.FromMilliseconds(500),
            Figure = () => ctx.Window.AccessibilityTreesBuilt - treesBefore,
            FigureUnit = "a11y trees/key",
            Check = step => told(step, filtered[(step * 7) % filtered.Length]),
        };
    }

    private static Op FilterBelowRows(Ctx ctx, Func<int, string?> applied) => FilterBelowRows(ctx, (step, _) => applied(step));
}
