using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Headless;

namespace PerfSuite;

static partial class Scenarios
{
    // With an assistive technology attached (this repo's Windows desk always has one: a
    // background UIA client), every structural or a11y-visible change rebuilds and diffs the
    // accessibility tree in the frame. A recording bridge stands in for UIA / AT-SPI: what
    // is timed is the framework's tree build and diff, not the platform's marshalling.
    private static IEnumerable<Scenario> Accessibility()
    {
        yield return new Scenario("a11y/create 1,000 rows, bridge on",
            "Replace a list with 1,000 fresh rows while an accessibility client listens: tree rebuilt and diffed",
            ctx =>
            {
                var bridge = ctx.Window.InstallAccessibilityBridge();
                var body = Flex.Column().FlexShrink(0);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(body));
                int next = 1;
                return new Op
                {
                    Reset = _ => { body.ReplaceChildren(); bridge.ClearDiffs(); },
                    Run = _ =>
                    {
                        var rows = new Element[1_000];
                        for (int i = 0; i < rows.Length; i++) rows[i] = Ui.Row(next++, Ui.Label(ctx.Rng));
                        body.ReplaceChildren(rows);
                    },
                    Check = _ => bridge.Diffs.Count > 0 ? null : "the bridge received no change",
                };
            });

        yield return new Scenario("a11y/edit every 10th of 1,000, bridge on",
            "Change the label of every 10th of 1,000 rows while an accessibility client listens: 100 name changes",
            ctx =>
            {
                var bridge = ctx.Window.InstallAccessibilityBridge();
                var labels = new List<TextElement>();
                var body = Flex.Column().FlexShrink(0);
                var rows = new Element[1_000];
                for (int i = 0; i < rows.Length; i++)
                {
                    var label = Text.New(Ui.Label(ctx.Rng)).FontSize(13f);
                    labels.Add(label);
                    rows[i] = Flex.Row(gap: 12).FlexShrink(0).Padding(4).Children(Text.New($"{i}").FontSize(13f), label);
                }
                body.ReplaceChildren(rows);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(body));
                return new Op
                {
                    Reset = _ => bridge.ClearDiffs(),
                    Run = step =>
                    {
                        for (int i = step % 10; i < labels.Count; i += 10) labels[i].Content += " !";
                    },
                    Check = _ => bridge.Diffs.Count > 0 ? null : "the bridge received no change",
                };
            });

        yield return new Scenario("a11y/tab through 300 fields, bridge on",
            "Press Tab in a 300-field form while an accessibility client listens: focus events, the view scrolls to follow",
            ctx =>
            {
                var bridge = ctx.Window.InstallAccessibilityBridge();
                var rows = new Element[300];
                for (int i = 0; i < rows.Length; i++)
                    rows[i] = Flex.Row(gap: 8).FlexShrink(0).Padding(new HultUI.Core.Primitives.Thickness(3, 12)).Children(
                        Text.New($"Field {i}").FontSize(12f).Width(HultUI.Core.Primitives.Dimension.Px(80)),
                        TextInput.New(Ui.Label(ctx.Rng)).Width(HultUI.Core.Primitives.Dimension.Px(260)));
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(rows)));
                ctx.Window.PressKey(HultUI.Core.Events.KeyCode.Tab);
                ctx.Pump();
                var focused = bridge.FocusedNode;
                return new Op
                {
                    Reset = _ => bridge.ClearDiffs(),
                    Run = _ => ctx.Window.PressKey(HultUI.Core.Events.KeyCode.Tab),
                    Check = _ =>
                    {
                        bool moved = bridge.FocusedNode is not null && bridge.FocusedNode != focused;
                        focused = bridge.FocusedNode;
                        return moved ? null : "the bridge was not told the focus moved";
                    },
                };
            });

        yield return new Scenario("a11y/selection walk 1,000 rows, bridge on",
            "virtual/selection walk with an accessibility client listening: every Down rebuilds and diffs the tree, an item node for each of the list's 1,000 rows",
            ctx =>
            {
                var bridge = ctx.Window.InstallAccessibilityBridge();
                return SelectionWalk(ctx, _ =>
                    bridge.Diffs.Count == 0 ? "the bridge received no change"
                    : !HoldsItems(bridge.CurrentTree, 1_000) ? "the tree does not hold the list's 1,000 items"
                    : null,
                    reset: bridge.ClearDiffs);
            });

        yield return new Scenario("a11y/selection walk beside details, keys 60 ms apart, real loop, bridge on",
            "Down every 60 ms through a 1,000-commit list on item rows beside a details pane the same Build makes again, in Application.Run's own loop on the virtual clock, an accessibility client listening: the trees a key builds (all under build)",
            ctx =>
            {
                var bridge = ctx.Window.InstallAccessibilityBridge();
                return RealLoopWalk(ctx, index => ToldSelected(bridge.CurrentTree, index));
            });

        yield return new Scenario("a11y/type beside a 150-line diff, rows made in Build, bridge on",
            "Type beside a 150-line diff whose rows Build flattens anew, an accessibility client listening: the list has no item label, so every item is named by its record's ToString()",
            ctx =>
            {
                ctx.Window.InstallAccessibilityBridge();
                return TypeBesideDiffOp(ctx, kept: false);
            });

        yield return new Scenario("a11y/type beside a 150-line diff, rows kept and labelled, bridge on",
            "The same with the rows flattened once, on item rows, and named by a label made once per row",
            ctx =>
            {
                ctx.Window.InstallAccessibilityBridge();
                return TypeBesideDiffOp(ctx, kept: true);
            });

        yield return new Scenario("a11y/progress bar ticks over 1,000 rows, bridge on",
            "A progress bar above 1,000 rows moves on every frame while an accessibility client listens, as a download's or a playing video's does",
            ctx =>
            {
                ctx.Window.InstallAccessibilityBridge();
                var (bar, root) = ProgressOverRows(ctx);
                ctx.Mount(root);
                return ProgressTicks(bar);
            });

        if (OperatingSystem.IsWindows())
            foreach (var scenario in UiaAccessibility())
                yield return scenario;
    }

    /// <summary>A progress bar over 1,000 benchmark rows in a scroll view.</summary>
    private static (ProgressBarElement Bar, Element Root) ProgressOverRows(Ctx ctx)
    {
        var bar = ProgressBar.New().Width(Dimension.Px(400));
        return (bar, Flex.Column(gap: 8).FlexGrow(1).Padding(16).Children(
            Text.New("Downloading").FontSize(13f), bar,
            ScrollView.New().FlexGrow(1).Children(Ui.Rows(ctx.Rng, 1_000))));
    }

    /// <summary>Each step moves the bar a thousandth, one 60 Hz frame apart.</summary>
    private static Op ProgressTicks(ProgressBarElement bar) => new()
    {
        Run = step => bar.Value = (step % 1_000 + 1) / 1_000.0,
        Check = step => bar.Value == (step % 1_000 + 1) / 1_000.0 ? null : $"the bar reads {bar.Value}",
    };

    /// <summary>
    /// The selection walk as a person walks a History list, in the application's own loop
    /// (<c>HeadlessSession.RunLoop</c>, on the virtual clock): Down every 60 ms through 1,000
    /// commits on item rows, beside a details pane the same Build makes again, unchanged, on
    /// every move, as an app's whole-window Build does. A step is one key and the loop's frames
    /// until the next. The figure is the accessibility trees a key builds: the tree asks each
    /// item's label once a build and for nothing else, so the labels made over the 1,000 items
    /// count the builds. A scroll's refresh of the tree waits 100 ms, so keys 60 ms apart need
    /// a build at most every other key for what moved; a selection move itself needs none.
    /// <paramref name="told"/> checks the bridge was told the selected item.
    /// </summary>
    private static Op RealLoopWalk(Ctx ctx, Func<int, string?> told)
    {
        var pane = new WalkPane(WalkItems(ctx.Rng, 1_000), itemRows: true, details: true);
        var host = pane.Mount();
        host.On<HultUI.Core.Events.KeyDownEvent>(e =>
        {
            if (e.Key == HultUI.Core.Events.KeyCode.Down) { pane.Move(1); e.StopPropagation = true; }
        });
        ctx.Mount(host);
        pane.Select(pane.LastWholeRow());
        pane.Open(pane.Selected);
        ctx.Session.PumpFrame(TimeSpan.FromMilliseconds(120));
        int first = pane.Selected;
        int labelsBefore = 0;
        bool nextKey = false;
        var keyEvery = TimeSpan.FromMilliseconds(60);
        return new Op
        {
            Run = _ =>
            {
                labelsBefore = pane.LabelsMade;
                ctx.Window.PressKey(HultUI.Core.Events.KeyCode.Down);
                nextKey = false;
                ctx.Session.RunLoop(() => nextKey, eventEvery: keyEvery, onEvent: () => nextKey = true);
            },
            Frames = 0,
            Figure = () => (pane.LabelsMade - labelsBefore) / (double)pane.ItemCount,
            FigureUnit = "a11y trees/key",
            Check = step => pane.Verify(first + step + 1) ?? told(first + step + 1),
        };
    }

    /// <summary>Null when the bridge's tree has item <paramref name="index"/> of the 1,000-item
    /// list selected, or what it has instead.</summary>
    private static string? ToldSelected(AccessibilityNode? tree, int index)
    {
        if (FindItems(tree, 1_000) is not { } list) return "the bridge's tree holds no list of 1,000 items";
        return list.Children[index].IsSelected == true ? null : $"the bridge's tree does not have item {index} selected";
    }

    private static AccessibilityNode? FindItems(AccessibilityNode? node, int count)
    {
        if (node is null) return null;
        if (node.Role == AccessibilityRole.List && node.Children.Count == count) return node;
        var children = node.Children;
        for (int i = 0; i < children.Count; i++)
            if (FindItems(children[i], count) is { } found) return found;
        return null;
    }

    /// <summary>
    /// Whether a node of <paramref name="node"/>'s tree publishes a list of
    /// <paramref name="count"/> items: one node each, or (a longer list) on demand.
    /// </summary>
    private static bool HoldsItems(AccessibilityNode? node, int count)
    {
        if (node is null) return false;
        if (node.IsVirtualItemContainer || node.Children.Count >= count) return true;
        var children = node.Children;
        for (int i = 0; i < children.Count; i++)
            if (HoldsItems(children[i], count)) return true;
        return false;
    }

    // The same kind of frames with the real UI Automation bridge behind them, on a hidden
    // window (Windows only): what a Windows screen reader adds on top of the build and diff,
    // its wrapper upkeep and the events it raises. "other" holds the whole a11y phase.
    [SupportedOSPlatform("windows")]
    private static IEnumerable<Scenario> UiaAccessibility()
    {
        yield return new Scenario("a11y/click checkbox in 500 rows, UIA",
            "Click a checkbox in a 500-row settings list with the UIA bridge: toggle and focus events over 1,001 nodes",
            ctx =>
            {
                var rig = UiaRig.Install(ctx.Window);
                var boxes = new CheckboxElement[500];
                var rows = new Element[boxes.Length];
                for (int i = 0; i < boxes.Length; i++)
                {
                    boxes[i] = Checkbox.New(i % 3 == 0, Ui.Label(ctx.Rng));
                    rows[i] = Flex.Row(gap: 8).FlexShrink(0).Padding(new Thickness(4, 12)).Children(
                        boxes[i], Text.New(Ui.Paragraph(ctx.Rng, 6)).FontSize(12f).TextColor(Ui.Muted));
                }
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(rows)));
                return new Op
                {
                    Run = step => ctx.Window.Click(boxes[step % 12]),
                    Check = Applied(rig),
                };
            });

        yield return new Scenario("a11y/type in TextInput, UIA",
            "Type one character into a TextInput above 200 rows with the UIA bridge: value and text events over 603 nodes",
            ctx =>
            {
                var rig = UiaRig.Install(ctx.Window);
                var input = TextInput.New("").Width(Dimension.Px(400));
                ctx.Mount(Flex.Column(gap: 8).Padding(16).Children(
                    Text.New("Name").FontSize(13f), input, Ui.Rows(ctx.Rng, 200)));
                ctx.Window.Click(input);
                ctx.Pump();
                return new Op
                {
                    Run = step => ctx.Window.TypeText(((char)('a' + step % 26)).ToString()),
                    Check = Applied(rig),
                };
            });

        yield return new Scenario("a11y/edit 1 label of 1,000 rows, UIA",
            "Change one row's label in 1,000 rows with the UIA bridge: one name event over 2,001 nodes",
            ctx =>
            {
                var rig = UiaRig.Install(ctx.Window);
                var labels = new List<TextElement>();
                var rows = new Element[1_000];
                for (int i = 0; i < rows.Length; i++)
                {
                    var label = Text.New(Ui.Label(ctx.Rng)).FontSize(13f);
                    labels.Add(label);
                    rows[i] = Flex.Row(gap: 12).FlexShrink(0).Padding(4).Children(Text.New($"{i}").FontSize(13f), label);
                }
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(rows)));
                return new Op
                {
                    Run = step => labels[step * 37 % labels.Count].Content += " !",
                    Check = Applied(rig),
                };
            });

        yield return new Scenario("a11y/tab through 300 fields, UIA",
            "Press Tab in a 300-field form with the UIA bridge: the focus moves, no rebuild",
            ctx =>
            {
                var rig = UiaRig.Install(ctx.Window);
                var rows = new Element[300];
                for (int i = 0; i < rows.Length; i++)
                    rows[i] = Flex.Row(gap: 8).FlexShrink(0).Padding(new Thickness(3, 12)).Children(
                        Text.New($"Field {i}").FontSize(12f).Width(Dimension.Px(80)),
                        TextInput.New(Ui.Label(ctx.Rng)).Width(Dimension.Px(260)));
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(rows)));
                ctx.Window.PressKey(HultUI.Core.Events.KeyCode.Tab);
                ctx.Pump();
                int moves = rig.FocusMoves;
                return new Op
                {
                    // Forward 20, back 20, as input/tab does: the focus stays near the top.
                    Run = step => ctx.Window.PressKey(HultUI.Core.Events.KeyCode.Tab,
                        (step / 20) % 2 == 0 ? HultUI.Core.Events.KeyModifiers.None : HultUI.Core.Events.KeyModifiers.Shift),
                    Check = _ =>
                    {
                        bool moved = rig.FocusMoves > moves;
                        moves = rig.FocusMoves;
                        return moved ? null : "the UIA bridge was not told the focus moved";
                    },
                };
            });

        yield return new Scenario("a11y/wheel 1,000 rows, UIA",
            "Wheel 3 notches through 1,000 rows with the UIA bridge, each frame 120 ms apart so each rebuilds: bounds over ~130 rows on screen",
            ctx =>
            {
                var rig = UiaRig.Install(ctx.Window);
                var view = ScrollView.New().FlexGrow(1).Children(Ui.Rows(ctx.Rng, 1_000));
                ctx.Mount(view);
                float offset = view.ScrollOffsetY;
                var applied = Applied(rig);
                return new Op
                {
                    Run = step => ctx.Window.Wheel(500, 380, 0, ((step / 20) % 2 == 0 ? -1 : 1) * 3),
                    // Past the 100 ms a scroll's accessibility refresh waits for, so every step's
                    // frame is a coalesced rebuild, as each one is in a slow wheel turn.
                    FrameDelta = TimeSpan.FromMilliseconds(120),
                    Check = step =>
                    {
                        bool moved = view.ScrollOffsetY != offset;
                        offset = view.ScrollOffsetY;
                        return moved ? applied(step) : $"the view did not scroll (offset {offset})";
                    },
                };
            });

        yield return new Scenario("a11y/selection walk 1,000 rows, UIA",
            "virtual/selection walk with the UIA bridge: every Down a rebuild over the list's 1,000 item nodes, its wrapper upkeep and events",
            ctx =>
            {
                var rig = UiaRig.Install(ctx.Window);
                return SelectionWalk(ctx, Applied(rig));
            });

        yield return new Scenario("a11y/selection walk beside details, keys 60 ms apart, real loop, UIA",
            "The real-loop walk with the UIA bridge: the trees a key builds, the bridge's upkeep and the IsSelected events (all under build)",
            ctx =>
            {
                var rig = UiaRig.Install(ctx.Window);
                var applied = Applied(rig);
                return RealLoopWalk(ctx, applied);
            });

        yield return new Scenario("a11y/type in rich text, UIA",
            "Type in a 5,000-block document with the UIA bridge: the text and caret events, and whatever the keystroke's value change carries",
            ctx =>
            {
                var rig = UiaRig.Install(ctx.Window);
                var editor = MountEditor(ctx, LongDocument(ctx.Rng, 5_000));
                editor.State.Select(HultUI.Core.RichText.RichSelection.Caret(2_501, 3));
                ctx.Pump();
                return new Op
                {
                    Run = step => ctx.Window.TypeText(((char)('a' + step % 26)).ToString()),
                    Check = Applied(rig),
                };
            });

        yield return new Scenario("a11y/progress bar ticks over 1,000 rows, UIA",
            "A progress bar above 1,000 rows moves on every frame with the UIA bridge, as a download's or a playing video's does",
            ctx =>
            {
                UiaRig.Install(ctx.Window);
                var (bar, root) = ProgressOverRows(ctx);
                ctx.Mount(root);
                return ProgressTicks(bar);
            });
    }

    /// <summary>A check that the bridge applied a diff since the last time it was asked.</summary>
    [SupportedOSPlatform("windows")]
    private static Func<int, string?> Applied(UiaRig rig)
    {
        int seen = rig.Applies;
        return _ =>
        {
            bool applied = rig.Applies > seen;
            seen = rig.Applies;
            return applied ? null : "the UIA bridge received no change";
        };
    }
}
