using HultUI.Core.Builders;
using HultUI.Core.Elements;
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
    }
}
