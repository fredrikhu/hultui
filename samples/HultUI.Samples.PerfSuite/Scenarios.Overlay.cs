using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Dialogs;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace PerfSuite;

static partial class Scenarios
{
    // Overlays open on top of a busy screen (1,000 rows), the way they do in an app, so a
    // step also shows whether opening one disturbs the content underneath. A step times the
    // OPEN; the close is the untimed reset. (Alternating the two in one series made the
    // minimum the close and the median the open — two costs read as one noisy number.)
    // An overlay is laid out and painted outside the window's Layout and Render passes, so
    // its cost shows in "other".
    private static IEnumerable<Scenario> Overlays()
    {
        yield return new Scenario("overlay/popover open",
            "Open a popover (a 12-item list) on an anchor",
            ctx =>
            {
                var anchor = Button.New("Filters", ButtonVariant.Secondary);
                var popover = new Popover(() => MenuList(ctx.Rng, 12));
                ctx.Mount(Busy(ctx, anchor));
                return new Op
                {
                    Reset = _ => popover.Close(),
                    Run = _ => popover.Open(anchor),
                    Check = _ => popover.IsOpen ? null : "the popover did not open",
                };
            });

        yield return new Scenario("overlay/dialog open",
            "Show a modal dialog (a small form) over a scrim",
            ctx =>
            {
                ctx.Mount(Busy(ctx, Text.New("Dialogs").FontSize(13f)));
                IDialogController<bool>? open = null;
                return new Op
                {
                    Reset = _ => open?.Close(true),
                    Run = _ => Dialog.Show<bool>(ctx.Window.Window, c => { open = c; return DialogForm(c); }),
                    Check = _ => ctx.Window.OverlayContents.Count == 1 ? null : $"{ctx.Window.OverlayContents.Count} overlays open, not the dialog alone",
                };
            });

        yield return new Scenario("overlay/menu bar open",
            "Open the first of five menus in a MenuBar (20 items)",
            ctx =>
            {
                var menus = new MenuBarItem[5];
                for (int m = 0; m < menus.Length; m++)
                {
                    var items = new ContextMenuItem[20];
                    for (int i = 0; i < items.Length; i++)
                        items[i] = new ContextMenuItem { Label = Ui.Label(ctx.Rng), Action = () => { }, ShortcutHint = $"Ctrl+{i}" };
                    menus[m] = new MenuBarItem($"&Menu{m}", items);
                }
                var bar = new MenuBar(menus);
                ctx.Mount(Busy(ctx, bar.Mount()));
                return new Op
                {
                    Reset = _ => bar.Close(),
                    Run = _ => bar.Open(0),
                    Check = _ => bar.OpenIndex == 0 && ctx.Window.OverlayContents.Count > 0 ? null : "the menu did not open",
                };
            });

        yield return new Scenario("overlay/command palette filter 2,000",
            "Change the query of an open command palette over 2,000 commands: fuzzy match, rank, rebuild the list",
            ctx =>
            {
                var commands = new List<PaletteCommand>(2_000);
                for (int i = 0; i < 2_000; i++) commands.Add(new PaletteCommand($"{Ui.Label(ctx.Rng)} {i}", () => { }));
                var palette = new CommandPalette(() => commands);
                ctx.Mount(Busy(ctx, Text.New("Palette").FontSize(13f)));
                palette.Open(ctx.Window.Window);
                ctx.Pump(2);
                string[] queries = ["re", "bl", "pz", "ch", "gr", "ta"];
                return new Op
                {
                    Run = step => palette.Query = queries[step % queries.Length],
                    Check = _ => palette.IsOpen && palette.Matches.Count is > 0 and < 2_000 ? null : $"the palette shows {palette.Matches.Count} matches",
                };
            });

        yield return new Scenario("overlay/tooltip show",
            "Hover one of two buttons until its tooltip shows (0.5 s of virtual time over two frames)",
            ctx =>
            {
                var a = Button.New("Save", ButtonVariant.Secondary).Tooltip("Save the document to disk (Ctrl+S)");
                var b = Button.New("Open", ButtonVariant.Secondary).Tooltip("Open a document from disk (Ctrl+O)");
                ctx.Mount(Busy(ctx, Flex.Row(gap: 8).Children(a, b)));
                return new Op
                {
                    Run = step =>
                    {
                        var target = step % 2 == 0 ? a : b;
                        var box = target.WindowBorderBox;
                        ctx.Window.MoveMouse(box.X + box.Width / 2, box.Y + box.Height / 2);
                    },
                    Frames = 2,
                    FrameDelta = TimeSpan.FromMilliseconds(300),
                    Check = _ => ctx.Window.OverlayContents.Count > 0 ? null : "no tooltip showed",
                };
            });

        yield return new Scenario("overlay/dropdown open 200 items",
            "Click a dropdown with 200 items open",
            ctx =>
            {
                var items = new string[200];
                for (int i = 0; i < items.Length; i++) items[i] = Ui.Label(ctx.Rng);
                var dropdown = Dropdown.New(items, 3).Width(Dimension.Px(240));
                ctx.Mount(Busy(ctx, dropdown));
                return new Op
                {
                    Reset = _ => { if (dropdown.IsOpen) ctx.Window.PressKey(KeyCode.Escape); },
                    Run = _ => ctx.Window.Click(dropdown),
                    Check = _ => dropdown.IsOpen ? null : "the dropdown did not open",
                };
            });
    }

    /// <summary>A header holding <paramref name="control"/>, over a scrolled list of 1,000 rows.</summary>
    private static Element Busy(Ctx ctx, Element control)
        => Flex.Column().FlexGrow(1).Children(
            Flex.Row(gap: 8).FlexShrink(0).Padding(10).Background(Ui.CardFill).AlignItems(AlignItems.Center).Children(control),
            ScrollView.New().FlexGrow(1).Children(Ui.Rows(ctx.Rng, 1_000)));

    private static Element MenuList(Random rng, int count)
    {
        var items = new Element[count];
        for (int i = 0; i < count; i++)
            items[i] = Box.New().Padding(new Thickness(6, 12)).OnHover(b => b.Background(Ui.Selected))
                .Children(Text.New(Ui.Label(rng)).FontSize(13f));
        return Flex.Column().Width(Dimension.Px(220)).Padding(4).Children(items);
    }

    private static Element DialogForm(IDialogController<bool> c)
        => Flex.Column(gap: 10).Width(Dimension.Px(420)).Padding(20).Background(Color.White).CornerRadius(10f)
            .BoxShadow(0, 12, 32, new Color(15, 23, 42, 65))
            .Children(
                Text.New("Rename branch").FontSize(18f).FontWeight(FontWeight.Bold),
                Text.New("The branch is renamed locally; the remote keeps its old name until you push.").FontSize(13f),
                TextInput.New("feature/perf-suite"),
                Checkbox.New(true, "Also rename the upstream"),
                Flex.Row(gap: 8).JustifyContent(JustifyContent.FlexEnd).Children(
                    Button.New("Cancel", ButtonVariant.Secondary).OnClick(_ => c.Close(false)),
                    Button.New("Rename").OnClick(_ => c.Close(true))));
}
