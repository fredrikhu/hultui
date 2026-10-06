using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace PerfSuite;

static partial class Scenarios
{
    // Input enters through the headless driver, which feeds the same funnel as SDL: hit
    // testing, capture, hover enter/leave, focus, click synthesis — production code paths.
    private static IEnumerable<Scenario> Input()
    {
        yield return new Scenario("input/hover across 1,000 rows",
            "Move the pointer to another of 1,000 rows with hover styles: hit test, leave/enter, restyle, repaint",
            ctx =>
            {
                var body = Flex.Column().FlexShrink(0);
                var rows = new Element[1_000];
                for (int i = 0; i < rows.Length; i++)
                    rows[i] = Ui.Row(i, Ui.Label(ctx.Rng)).OnHover(r => r.Background(Ui.Selected));
                body.ReplaceChildren(rows);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(body));
                // ~28 px rows: a 63 px step lands on a different row each time.
                return new Op { Run = step => ctx.Window.MoveMouse(400, 20 + step * 63 % 700) };
            });

        yield return new Scenario("input/click checkbox in 500 rows",
            "Click a checkbox in a 500-row settings list: hit test, press/release, toggle, repaint",
            ctx =>
            {
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
                    Check = step =>
                    {
                        bool initially = step % 12 % 3 == 0, clickedOddTimes = (step / 12) % 2 == 0;
                        return boxes[step % 12].IsChecked == (initially ^ clickedOddTimes) ? null : "the click did not toggle the checkbox";
                    },
                };
            });

        yield return new Scenario("input/type in TextInput",
            "Type one character into a focused single-line TextInput",
            ctx =>
            {
                var input = TextInput.New("").Width(Dimension.Px(400));
                ctx.Mount(Flex.Column(gap: 8).Padding(16).Children(
                    Text.New("Name").FontSize(13f), input, Ui.Rows(ctx.Rng, 200)));
                ctx.Window.Click(input);
                ctx.Pump();
                return new Op
                {
                    Run = step => ctx.Window.TypeText(((char)('a' + step % 26)).ToString()),
                    Check = step => input.Text.Length == step + 1 ? null : $"input holds {input.Text.Length} characters after {step + 1} typed",
                };
            });

        yield return new Scenario("input/type in 2,000-line TextArea",
            "Type one character in the middle of a 2,000-line TextArea",
            ctx =>
            {
                var lines = new string[2_000];
                for (int i = 0; i < lines.Length; i++) lines[i] = Ui.Paragraph(ctx.Rng, 8);
                var area = TextArea.New(string.Join('\n', lines)).FlexGrow(1);
                ctx.Mount(Flex.Column().FlexGrow(1).Padding(8).Children(area));
                ctx.Window.Click(area);
                area.CursorPosition = area.Text.Length / 2;
                ctx.Pump();
                int initial = area.Text.Length;
                return new Op
                {
                    Run = step => ctx.Window.TypeText(((char)('a' + step % 26)).ToString()),
                    Check = step => area.Text.Length == initial + step + 1 ? null : "the typed character did not reach the TextArea",
                };
            });

        yield return new Scenario("input/type in a wrapped 900-line TextArea",
            "Type one character into a 900-line notes file whose paragraphs wrap: every line wrapped again",
            ctx =>
            {
                var area = TextArea.New(Ui.Notes(ctx.Rng)).FlexGrow(1);
                ctx.Mount(Flex.Column().FlexGrow(1).Padding(8).Children(area));
                ctx.Window.Click(area);
                area.CursorPosition = area.Text.Length / 2;
                ctx.Pump();
                int initial = area.Text.Length;
                return new Op
                {
                    Run = step => ctx.Window.TypeText(((char)('a' + step % 26)).ToString()),
                    Check = step => area.Text.Length == initial + step + 1 ? null : "the typed character did not reach the TextArea",
                };
            });

        yield return new Scenario("input/undo-redo in TextArea",
            "Ctrl+Z / Ctrl+Y in a 2,000-line TextArea that has 40 edits of history",
            ctx =>
            {
                var lines = new string[2_000];
                for (int i = 0; i < lines.Length; i++) lines[i] = Ui.Paragraph(ctx.Rng, 8);
                var area = TextArea.New(string.Join('\n', lines)).FlexGrow(1);
                ctx.Mount(Flex.Column().FlexGrow(1).Padding(8).Children(area));
                ctx.Window.Click(area);
                area.CursorPosition = area.Text.Length / 2;
                // Typing breaks into one undo step per word.
                for (int i = 0; i < 40; i++) ctx.Window.TypeText("word ");
                ctx.Pump();
                int typed = area.Text.Length;
                return new Op
                {
                    Run = step => ctx.Window.PressKey(step % 2 == 0 ? KeyCode.Z : KeyCode.Y, KeyModifiers.Ctrl),
                    Check = step => (step % 2 == 0 ? area.Text.Length < typed : area.Text.Length == typed)
                        ? null : $"{(step % 2 == 0 ? "undo" : "redo")} left {area.Text.Length} characters (typed: {typed})",
                };
            });

        yield return new Scenario("input/tab through 300 fields",
            "Press Tab in a form of 300 text inputs: focus moves, rings repaint, the view scrolls to follow",
            ctx =>
            {
                var rows = new Element[300];
                for (int i = 0; i < rows.Length; i++)
                    rows[i] = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center).Padding(new Thickness(3, 12)).Children(
                        Text.New($"Field {i}").FontSize(12f).Width(Dimension.Px(80)),
                        TextInput.New(Ui.Label(ctx.Rng)).Width(Dimension.Px(260)));
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(rows)));
                ctx.Window.PressKey(KeyCode.Tab);
                ctx.Pump();
                var focused = ctx.Window.Window.FocusedElement;
                return new Op
                {
                    Run = step => ctx.Window.PressKey(KeyCode.Tab, (step / 20) % 2 == 0 ? KeyModifiers.None : KeyModifiers.Shift),
                    Check = _ =>
                    {
                        var now = ctx.Window.Window.FocusedElement;
                        bool moved = now is not null && now != focused;
                        focused = now;
                        return moved ? null : "Tab did not move the focus";
                    },
                };
            });

        yield return new Scenario("input/drag to reorder 200 rows",
            "Drag a row onto another (8 pointer moves, drag ghost, drop) and reorder the list",
            ctx =>
            {
                var body = Flex.Column().FlexShrink(0);
                var order = new List<Element>();
                for (int i = 0; i < 200; i++)
                {
                    var row = Ui.Row(i, Ui.Label(ctx.Rng)).Draggable();
                    row.OnDragStart(e => e.DragData.Set("row", row));
                    row.OnDragOver(e => e.Accepted = true);
                    row.OnDrop(e =>
                    {
                        if (!e.DragData.TryGet<BoxElement>("row", out var moved) || moved is null || moved == row) return;
                        order.Remove(moved);
                        order.Insert(order.IndexOf(row), moved);
                        body.ReplaceChildren(order.ToArray());
                    });
                    order.Add(row);
                }
                body.ReplaceChildren(order.ToArray());
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(body));
                var before = order.ToArray();
                return new Op
                {
                    Run = step =>
                    {
                        float y0 = step % 2 == 0 ? 45 : 245, y1 = step % 2 == 0 ? 245 : 45;
                        ctx.Window.Drag(new Point(300, y0), new Point(300, y1), steps: 8);
                    },
                    Check = _ =>
                    {
                        bool moved = !before.SequenceEqual(order);
                        before = order.ToArray();
                        return moved ? null : "the drop did not reorder the rows";
                    },
                };
            });
    }
}
