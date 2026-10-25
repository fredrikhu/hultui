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

        yield return new Scenario("input/type beside a 150-line diff, rows made in Build",
            "Type into a summary field beside a 150-line diff in a list, one component rebuilt per keystroke, whose Build flattens the diff anew (a record and word-diff spans per line) and whose one-argument template makes every row on screen again",
            ctx => TypeBesideDiffOp(ctx, kept: false));

        yield return new Scenario("input/type beside a 150-line diff, rows kept",
            "The same keystroke with the diff flattened once and the list on item rows (two-argument RenderWith): the rebuild makes no row",
            ctx => TypeBesideDiffOp(ctx, kept: true));

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

        yield return new Scenario("input/key press, down and up 70 ms apart, real loop",
            "Down pressed and let go 70 ms later, as a keyboard sends them, in a 1,000-commit list on item rows beside a details pane the same Build makes again, an app key-down handler on the window's root moving the selection: Application.Run's own loop on the virtual clock from the press until 70 ms after the release (all under build); reports the frames a key press draws",
            ctx =>
            {
                var pane = new WalkPane(WalkItems(ctx.Rng, 1_000), itemRows: true, details: true);
                int frames = 0;
                var root = Flex.Column().FlexGrow(1).Children(
                    pane.Mount().FlexGrow(1),
                    Canvas.New(_ => frames++).Width(Dimension.Px(1)).Height(Dimension.Px(1)));
                root.On<KeyDownEvent>(e =>
                {
                    if (e.Key == KeyCode.Down) { pane.Move(1); e.StopPropagation = true; }
                });
                ctx.Mount(root);
                pane.Select(pane.LastWholeRow());
                pane.Open(pane.Selected);
                ctx.Session.PumpFrame(TimeSpan.FromMilliseconds(120));
                int first = pane.Selected, drawn = 0;
                var apart = TimeSpan.FromMilliseconds(70);
                return new Op
                {
                    Run = _ =>
                    {
                        int f0 = frames, events = 0;
                        ctx.Window.KeyDown(KeyCode.Down);
                        // The release arrives 70 ms after the press; the run ends 70 ms after it.
                        ctx.Session.RunLoop(() => events >= 2, eventEvery: apart, onEvent: () =>
                        {
                            if (++events == 1) HultUI.Application.InvokeOnMainThread(() => ctx.Window.KeyUp(KeyCode.Down));
                        });
                        drawn = frames - f0;
                    },
                    Frames = 0,
                    Figure = () => drawn,
                    FigureUnit = "frames/press",
                    Check = step => drawn < 1 ? "the key press drew no frame" : pane.Verify(first + step + 1),
                };
            });

        yield return new Scenario("input/wheel over a list that cannot scroll, real loop",
            "Twenty wheel notches 16 ms apart over a pane of 8 rows that has nothing to scroll, beside a 1,000-row pane, as over a short folder in a two-pane file manager: Application.Run's own loop on the virtual clock (all under build); reports the frames a notch draws",
            ctx =>
            {
                var shortPane = VirtualListElement<RowItem>.New().ItemHeight(30).RenderWith(RenderVirtualRow)
                    .Items(Models(ctx.Rng, 8)).FlexGrow(1);
                int heard = 0, frames = 0;
                // Passive: counting the notches must not itself ask for a frame.
                shortPane.OnPassive<ScrollEvent>(_ => heard++);
                ctx.Mount(Flex.Column().FlexGrow(1).Children(
                    Flex.Row().FlexGrow(1).AlignItems(AlignItems.Stretch).Children(
                        shortPane,
                        VirtualRows(ctx.Rng, 1_000).FlexGrow(1)),
                    Canvas.New(_ => frames++).Width(Dimension.Px(1)).Height(Dimension.Px(1))));
                double perNotch = 0;
                int heardInStep = 0;
                return new Op
                {
                    Run = _ =>
                    {
                        var box = shortPane.WindowBorderBox;
                        float x = box.X + box.Width / 2, y = box.Y + 45;
                        int f0 = frames, h0 = heard, events = 0;
                        // The 21st event only ends the run, 16 ms after the last notch: time for its frame.
                        ctx.Session.RunLoop(() => events > 20, eventEvery: TimeSpan.FromMilliseconds(16), onEvent: () =>
                        {
                            float delta = ++events % 2 == 0 ? 1 : -1;
                            if (events <= 20) HultUI.Application.InvokeOnMainThread(() => ctx.Window.Wheel(x, y, 0, delta));
                        });
                        perNotch = (frames - f0) / 20.0;
                        heardInStep = heard - h0;
                    },
                    Frames = 0,
                    Figure = () => perNotch,
                    FigureUnit = "frames/notch",
                    Check = _ => heardInStep < 19 ? $"the pane heard {heardInStep} notches of 20"
                        : shortPane.ScrollOffsetY != 0 ? "the short pane scrolled" : null,
                };
            });

        yield return new Scenario("input/splitter drag, 1 kHz mouse, real loop",
            "Drag a PaneSplitter between two lists of 40 rows its owner rebuilds at the new size, the pointer moving every millisecond as a 1,000 Hz mouse does: Application.Run's own loop on the virtual clock for 10 frames, press to release (all under build); reports the owner's rebuilds per frame drawn",
            ctx =>
            {
                var screen = new SplitScreen(ctx.Rng);
                var host = screen.Mount();
                screen.Split.AttachDrag(host);
                int frames = 0;
                ctx.Mount(Flex.Column().FlexGrow(1).Children(
                    host.FlexGrow(1),
                    Canvas.New(_ => frames++).Width(Dimension.Px(1)).Height(Dimension.Px(1))));
                int builds = 0, drawn = 0;
                float expected = 0;
                return new Op
                {
                    Run = step =>
                    {
                        // Away and back on alternate steps, so the pane stays inside its range.
                        var handle = host.Children[0].Children[1].WindowBorderBox;
                        float x = handle.X + handle.Width / 2, y = handle.Y + handle.Height / 2, start = x;
                        float dx = step % 2 == 0 ? 0.75f : -0.75f;
                        ctx.Window.MouseDown(x, y);
                        int b0 = screen.Builds, f0 = frames;
                        float size0 = screen.Split.Size;
                        Action move = () => { x += dx; ctx.Window.MoveMouse(x, y); };
                        ctx.Session.RunLoop(() => frames - f0 >= 10, eventEvery: TimeSpan.FromMilliseconds(1),
                            onEvent: () => HultUI.Application.InvokeOnMainThread(move));
                        builds = screen.Builds - b0;
                        drawn = frames - f0;
                        ctx.Window.MouseUp(x, y);
                        expected = size0 + (x - start);
                    },
                    Frames = 0,
                    Figure = () => builds / (double)Math.Max(1, drawn),
                    FigureUnit = "rebuilds/frame",
                    Check = _ => Math.Abs(screen.Split.Size - expected) > 0.5f
                        ? $"the drag left the pane at {screen.Split.Size:0.0}, not {expected:0.0}"
                        : screen.Split.IsDragging ? "the release did not end the drag" : null,
                };
            });

        yield return new Scenario("input/slider drag, 1 kHz mouse, real loop",
            "Drag a slider whose owner rebuilds itself and 40 rows from the value, the pointer moving every millisecond as a 1,000 Hz mouse does: Application.Run's own loop on the virtual clock for 10 frames, press to release (all under build); reports the owner's rebuilds per frame drawn",
            ctx =>
            {
                var screen = new SliderScreen(ctx.Rng);
                int frames = 0;
                ctx.Mount(Flex.Column().FlexGrow(1).Children(
                    screen.Mount().FlexGrow(1),
                    Canvas.New(_ => frames++).Width(Dimension.Px(1)).Height(Dimension.Px(1))));
                int builds = 0, drawn = 0;
                double expected = 0;
                return new Op
                {
                    Run = step =>
                    {
                        var slider = (SliderElement)ctx.Root!.Children[0].Children[0].Children[0].Children[0];
                        var track = slider.WindowContentBox;
                        float y = track.Y + track.Height / 2;
                        float x = step % 2 == 0 ? track.X + 100 : track.X + track.Width - 100;
                        float dx = step % 2 == 0 ? 0.75f : -0.75f;
                        ctx.Window.MouseDown(x, y);
                        int b0 = screen.Builds, f0 = frames;
                        Action move = () => { x += dx; ctx.Window.MoveMouse(x, y); };
                        ctx.Session.RunLoop(() => frames - f0 >= 10, eventEvery: TimeSpan.FromMilliseconds(1),
                            onEvent: () => HultUI.Application.InvokeOnMainThread(move));
                        builds = screen.Builds - b0;
                        drawn = frames - f0;
                        ctx.Window.MouseUp(x, y);
                        float travel = track.Width - 2 * slider.ThumbRadius;
                        expected = Math.Round(Math.Clamp((x - track.X - slider.ThumbRadius) / travel, 0, 1) * 1000);
                    },
                    Frames = 0,
                    Figure = () => builds / (double)Math.Max(1, drawn),
                    FigureUnit = "rebuilds/frame",
                    Check = _ => screen.Value != expected ? $"the owner holds {screen.Value:0}, not {expected:0}" : null,
                };
            });
    }

    /// <summary>Two lists of rows either side of a <see cref="PaneSplitter"/>, rebuilt at the
    /// pane's size whenever it moves — the owner's <c>MarkDirty</c> is the splitter's
    /// <c>onChanged</c>, as Junction's and every app's is.</summary>
    private sealed class SplitScreen : HultUI.Core.Components.Component
    {
        private readonly string[] _labels;
        public readonly PaneSplitter Split;
        public int Builds;

        public SplitScreen(Random rng)
        {
            _labels = new string[80];
            for (int i = 0; i < _labels.Length; i++) _labels[i] = Ui.Label(rng);
            Split = new PaneSplitter(MarkDirty, null, 100, 800, 300);
        }

        public override Element Build()
        {
            Builds++;
            var left = new Element[40];
            var right = new Element[40];
            for (int i = 0; i < 40; i++)
            {
                left[i] = Ui.Row(i, _labels[i]);
                right[i] = Ui.Row(40 + i, _labels[40 + i]);
            }
            return Flex.Row().FlexGrow(1).AlignItems(AlignItems.Stretch).Children(
                Flex.Column().Width(Dimension.Px(Split.Size)).FlexShrink(0).Children(left),
                Split.Handle(),
                Flex.Column().FlexGrow(1).Children(right));
        }
    }

    /// <summary>A slider whose value its owner keeps and rebuilds from, with 40 rows beside it.</summary>
    private sealed class SliderScreen : HultUI.Core.Components.Component
    {
        private readonly string[] _labels;
        public double Value = 500;
        public int Builds;

        public SliderScreen(Random rng)
        {
            _labels = new string[40];
            for (int i = 0; i < _labels.Length; i++) _labels[i] = Ui.Label(rng);
        }

        public override Element Build()
        {
            Builds++;
            var rows = new Element[40];
            for (int i = 0; i < rows.Length; i++) rows[i] = Ui.Row(i, _labels[i]);
            return Flex.Column().FlexGrow(1).Children(
                Flex.Row(gap: 12).FlexShrink(0).Padding(10).AlignItems(AlignItems.Center).Children(
                    Slider.New(Value, 0, 1000).Width(Dimension.Px(800)).OnValueChanged(v => { Value = v; MarkDirty(); }),
                    Text.New($"{Value:0}").FontSize(13f)),
                Flex.Column().FlexGrow(1).Children(rows));
        }
    }
}
