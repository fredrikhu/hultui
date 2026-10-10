using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Testing;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// What a frame allocates when little changes: an idle pass of the loop, and a frame that
/// repaints, restyles or lays out one element again, allocate nothing. Each frame used to leave
/// ~0.5 KB behind however little it drew: a delegate per pass of the loop, a LinkedList node per
/// DevTools peak tracker (eight per frame, HUD shown or not), and a boxed enumerator for every
/// container the dirty-flag walks, the style pass and every scroll view's extent visited
/// (plan 17, runtime-frame-floor).
///
/// <para>These drive <see cref="Application.RunFrame"/> directly, the call
/// <see cref="Application.Run"/> makes each pass, and count what this thread allocated: the
/// least of a few rounds (<see cref="Allocations"/>), so a one-off does not count.</para>
/// </summary>
public class FrameAllocationTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    /// <summary>A screen with what the per-frame walks visit: a scroll view, rows nested a few
    /// containers deep, text. Returns the root and a box at the bottom of one row.</summary>
    private static (Element Root, BoxElement Deep) Screen()
    {
        var rows = new Element[40];
        BoxElement deep = null!;
        for (int i = 0; i < rows.Length; i++)
        {
            var inner = Box.New().Width(Dimension.Px(20)).Height(Dimension.Px(12)).Background(Color.Blue);
            if (i == 7) deep = inner;
            rows[i] = Flex.Row(gap: 6).FlexShrink(0).Padding(4).Children(
                TextElement.New($"Row {i}"),
                Flex.Column().Children(Flex.Row().Children(inner)));
        }
        var root = Flex.Column().FlexGrow(1).Children(
            TextElement.New("Header"),
            ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(rows)));
        return (root, deep);
    }

    private static HeadlessSession Session(Element root)
    {
        var session = HeadlessSession.Create(500, 400);
        session.Window.SetRoot(root);
        for (int i = 0; i < 4; i++) session.Application.RunFrame(Frame);
        return session;
    }

    [Fact]
    public void AnIdleLoopPass_AllocatesNothing()
    {
        var (root, _) = Screen();
        using var session = Session(root);
        var app = session.Application;
        for (int i = 0; i < 10; i++) { _ = app.IsRunning; app.RunFrame(Frame); }

        bool running = true, rendered = false;
        long allocated = Allocations.Least(() =>
        {
            for (int i = 0; i < 200; i++)
            {
                running &= app.IsRunning;
                rendered |= app.RunFrame(Frame);
            }
        });

        Assert.True(running);
        Assert.False(rendered, "the screen was meant to be idle");
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ASteadyRepaint_AllocatesNothing()
    {
        var (root, deep) = Screen();
        using var session = Session(root);
        var app = session.Application;

        double perFrame = PerFrame(app, () => deep.MarkPaintDirty());

        Assert.True(perFrame < 1, $"a repaint allocated {perFrame:0.#} B per frame");
    }

    [Fact]
    public void ARestyleOfOneElement_AllocatesNothing()
    {
        var (root, deep) = Screen();
        using var session = Session(root);
        var app = session.Application;

        double perFrame = PerFrame(app, () => deep.MarkStyleDirty());

        Assert.True(perFrame < 1, $"restyling one box allocated {perFrame:0.#} B per frame");
    }

    [Fact]
    public void ARelayoutOfOneElement_AllocatesNothing()
    {
        var (root, deep) = Screen();
        using var session = Session(root);
        var app = session.Application;

        double perFrame = PerFrame(app, () => deep.MarkLayoutDirty());

        Assert.True(perFrame < 1, $"laying one box out again allocated {perFrame:0.#} B per frame");
    }

    /// <summary>Sliders, one cut into chapters as a video's scrubber is, and number fields,
    /// repainted. A slider worked its pieces out through an iterator and, with chapter marks, a
    /// LINQ chain; a number field formatted its value into two new strings: ~1.3 KB a frame on
    /// the perf suite's control screen, and the scrubber repaints with every video frame.</summary>
    [Fact]
    public void ASteadyRepaintOfSlidersAndNumberFields_AllocatesNothing()
    {
        var chapters = Slider.New(30, 0, 100);
        chapters.Segments = [80d, 10d, 35d, 35d];
        var root = Flex.Column(gap: 8).Padding(8).Children(
            Slider.New(40, 0, 100), chapters,
            NumericUpDown.New(2.5, 0, 10, 0.5), NumericUpDown.New(7, 0, 100, 1));
        using var session = Session(root);

        double perFrame = PerFrame(session.Application, () => root.MarkPaintDirty());

        Assert.True(perFrame < 1, $"a repaint allocated {perFrame:0.#} B per frame");
    }

    /// <summary>A screen of selectable lines as Junction's diff and Vesper's reader draw them —
    /// syntax spans, one selection group with a live selection, a bold word — and a linked label,
    /// a truncated one and a right-to-left one, repainted. Each label laid a new paragraph out
    /// every frame for its hit-testing, ~320 B a label: 10 KB a frame over a diff (plan 17,
    /// round 2).</summary>
    [Fact]
    public void ASteadyRepaintOfSelectableLabels_AllocatesNothing()
    {
        var lines = new string[40];
        var group = new TextSelectionGroup();
        var rows = new Element[lines.Length + 3];
        Core.Rendering.TextSpan[] spans =
        [
            new(0, 3, Color.FromHex("#569CD6")),
            new(4, 6, Color.FromHex("#9CDCFE"), Weight: Core.Styling.FontWeight.Bold),
            new(13, 5, Color.FromHex("#CE9178"), Color.FromHex("#3A3D41")),
        ];
        // On screen first, above the lines: the session shows ~20 rows and culls the rest.
        rows[0] = TextElement.New("See ABCD-4291 for the details").Links([new TextLink(4, 9, "issue://4291")], _ => { });
        rows[1] = TextElement.New("A long line that is cut short with an ellipsis at the box's edge, far before its end")
            .Selectable().MaxLines(1).TextOverflow(TextOverflow.Ellipsis);
        var hebrew = TextElement.New("שלום עולם, שורה שנקראת מימין לשמאל").Selectable();
        hebrew.Inherited.TextDirection = Core.Styling.TextDirection.Rtl;
        rows[2] = hebrew;
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = $"var line{i,-3} = \"text\"; // the line's comment, number {i}";
            rows[3 + i] = TextElement.New(lines[i]).Selectable().SelectionGroup(group, i).Spans(spans);
        }
        group.Bind(lines, lines.Length, i => lines[i]);
        group.Anchor(3, 5);
        group.ExtendTo(6, 12);
        var root = ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(rows));
        using var session = Session(root);
        for (int i = 0; i < 10; i++)
            Assert.NotNull(((TextElement)rows[i]).DrawnParagraph);

        double perFrame = PerFrame(session.Application, () => root.MarkPaintDirty());

        Assert.True(perFrame < 1, $"repainting 43 selectable and linked labels allocated {perFrame:0.#} B per frame");
    }

    /// <summary>A TextArea of syntax-coloured code, read-only as a viewer is and editable with the
    /// caret on a line, repainted, as a caret blink or a search tint repaints it. It cut each
    /// line it drew out of its text again every frame, and the caret's line too: ~3 KB a frame
    /// over a screen of JSON. It keeps last frame's line strings while its line starts stand.
    /// (A selection still allocates the array of its boxes on a line it does not fill.)</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASteadyRepaintOfATextArea_AllocatesNothing(bool readOnly)
    {
        var text = string.Join('\n', Enumerable.Range(0, 300).Select(i => $"    \"field_{i}\": \"value {i * 7919 % 10_007}\","));
        var spans = new List<Core.Rendering.TextSpan>();
        int at = 0;
        foreach (var line in text.Split('\n'))
        {
            spans.Add(new(at + 4, line.IndexOf(':') - 4, Color.FromHex("#4EC9B0")));
            spans.Add(new(at + line.IndexOf(':') + 2, line.Length - line.IndexOf(':') - 3, Color.FromHex("#CE9178")));
            at += line.Length + 1;
        }
        var area = TextArea.New(text).FlexGrow(1).FontFamily(Core.Styling.FontFamilies.Monospace).FontSize(12f);
        area.Spans = spans;
        area.ReadOnly = readOnly;
        using var session = Session(Flex.Column().FlexGrow(1).Children(area));
        area.Focus();
        area.CursorPosition = 40;
        session.Window.PressKey(KeyCode.Down);
        for (int i = 0; i < 4; i++) session.Application.RunFrame(Frame);
        Assert.True(area.IsFocused);

        double perFrame = PerFrame(session.Application, () => area.MarkPaintDirty());

        Assert.True(perFrame < 1, $"repainting a TextArea allocated {perFrame:0.#} B per frame");
    }

    /// <summary>Tab through a form of 300 fields, a frame after each: what is left is the key
    /// and focus events. It was ~28 KB per Tab (a tree walk with a boxed enumerator per
    /// container, a fresh list, and a sort of keys that were all equal); the bound is far above
    /// the events and far below any of those.</summary>
    [Fact]
    public void ATab_AllocatesLittleMoreThanItsEvents()
    {
        var rows = new Element[300];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = Flex.Row(gap: 8).FlexShrink(0).Children(
                TextElement.New($"Field {i}").Width(Dimension.Px(80)),
                TextInput.New("value").Width(Dimension.Px(200)));
        using var session = Session(ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(rows)));
        var window = session.Window;
        for (int i = 0; i < 10; i++) { window.PressKey(KeyCode.Tab); session.Application.RunFrame(Frame); }

        var focusedBefore = window.Window.FocusedElement;
        double perTab = Allocations.PerCall(20, () =>
        {
            window.PressKey(KeyCode.Tab);
            session.Application.RunFrame(Frame);
        }, goal: 1023);

        Assert.NotSame(focusedBefore, window.Window.FocusedElement);
        Assert.True(perTab < 1024, $"a Tab allocated {perTab:0} B");
    }

    /// <summary>A screen with sticky section headers in a scroll view and a raised panel at
    /// the root, as Junction's panels and dialogs are (ZIndex 42 on the root's backdrop), drawn,
    /// scrolled and hovered: each parent sorted its children for every paint and every hit test —
    /// three sorts a frame on the perf suite's sticky sections, 1.3 of its 1.6 KB — and scanned
    /// them all to see whether it had to. The order is kept now, so the frames allocate nothing and
    /// sort nothing; a ZIndex changed in place (a repaint is all it asks for) re-orders the next
    /// frame's paint and hit test (plan 17, round 2, layout-zorder-sorted-allocs).</summary>
    [Fact]
    public void FramesOverStickyHeadersAndARaisedPanel_SortNothing_UntilAZIndexChanges()
    {
        var panelFill = new Color(200, 30, 30);
        var rowFill = new Color(30, 30, 200);
        var sections = new Element[6];
        for (int s = 0; s < sections.Length; s++)
        {
            var rows = new Element[12];
            rows[0] = Box.New().Height(Dimension.Px(24)).FlexShrink(0).Background(new Color(128, 128, 128)).Sticky();
            for (int i = 1; i < rows.Length; i++)
                rows[i] = Box.New().Height(Dimension.Px(20)).FlexShrink(0).Background(rowFill);
            sections[s] = Flex.Column(gap: 0).FlexShrink(0).Children(rows);
        }
        var scroll = ScrollView.Vertical().FlexGrow(1).Children(Flex.Column(gap: 0).FlexShrink(0).Children(sections));
        var panel = Box.New().Width(Dimension.Px(120)).Height(Dimension.Px(120)).Background(panelFill).ZIndex(42);
        panel.Position.Type = PositionType.Absolute;
        panel.Position.Top = Dimension.Px(0);
        panel.Position.Left = Dimension.Px(0);
        // The panel first: only its ZIndex puts it over the scroll view.
        var root = Flex.Column(gap: 0).FlexGrow(1).Children(panel, scroll);
        using var session = Session(root);
        var app = session.Application;
        var window = session.Window;
        scroll.ScrollOffsetY = 130;   // the first section's header held at the top, rows under it
        for (int i = 0; i < 4; i++) app.RunFrame(Frame);

        var rootOrder = ZOrderHelper.GetChildrenInZOrder(root);
        var sectionOrder = ZOrderHelper.GetChildrenInZOrder(sections[0]);
        Assert.Equal([scroll, panel], rootOrder);
        Assert.Same(sections[0].Children[0], sectionOrder[^1]);

        float y = 130;
        double repaint = PerFrame(app, () => root.MarkPaintDirty());
        double scrolled = PerFrame(app, () => scroll.ScrollOffsetY = y = y == 130 ? 150 : 130);
        // The pointer over a row of the second section, moving within it: the platform's input
        // and the move event are all a move should allocate.
        for (int i = 0; i < 10; i++) { window.MoveMouse(300 + i % 2, 200); app.RunFrame(Frame); }
        long events = Allocations.Least(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                _sink = new InputEvent.MouseMoveInput(300, 200, 1, 0, KeyModifiers.None);
                _sink = new PointerMoveEvent { Position = new Point(300, 200), Delta = new Point(1, 0) };
            }
        });
        long moves = Allocations.Least(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                window.MoveMouse(300 + i % 2, 200);
                app.RunFrame(Frame);
            }
        }, goal: events);

        Assert.True(repaint < 1, $"a repaint allocated {repaint:0.#} B per frame");
        Assert.True(scrolled < 1, $"a scrolled frame allocated {scrolled:0.#} B");
        Assert.True(moves <= events, $"100 moves allocated {moves} B; their events alone are {events} B");
        Assert.Same(rootOrder, ZOrderHelper.GetChildrenInZOrder(root));
        Assert.Same(sectionOrder, ZOrderHelper.GetChildrenInZOrder(sections[0]));
        Assert.Same(panel, HitTester.HitTest(root, new Point(60, 60)));
        Assert.Equal(panelFill, PixelAt(session, 60, 60));

        panel.Position.ZIndex = 0;
        panel.MarkPaintDirty();
        app.RunFrame(Frame);

        Assert.Same(root.Children, ZOrderHelper.GetChildrenInZOrder(root));
        Assert.NotSame(panel, HitTester.HitTest(root, new Point(60, 60)));
        Assert.Equal(rowFill, PixelAt(session, 60, 60));
    }

    private static Color PixelAt(HeadlessSession session, int x, int y)
    {
        using var image = session.Window.CapturePixels();
        using var bitmap = SkiaSharp.SKBitmap.FromImage(image);
        var c = bitmap.GetPixel(x, y);
        return new Color(c.Red, c.Green, c.Blue);
    }

    private static object? _sink;

    /// <summary>The pointer moving within one element allocates the platform's input and the
    /// move event, nothing else. Looking for a tooltip to show allocated a closure for the
    /// tooltip's delayed show on every move, whether there was a tooltip or not.</summary>
    [Fact]
    public void APointerMoveWithinOneElement_AllocatesOnlyItsEvents()
    {
        var (root, _) = Screen();
        using var session = Session(root);
        var window = session.Window;
        for (int i = 0; i < 10; i++) { window.MoveMouse(480 + i % 2, 100); session.Application.RunFrame(Frame); }

        long events = Allocations.Least(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                _sink = new InputEvent.MouseMoveInput(480, 100, 1, 0, KeyModifiers.None);
                _sink = new PointerMoveEvent { Position = new Point(480, 100), Delta = new Point(1, 0) };
            }
        });

        long moves = Allocations.Least(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                window.MoveMouse(480 + i % 2, 100);
                session.Application.RunFrame(Frame);
            }
        }, goal: events);

        Assert.True(moves <= events, $"100 moves allocated {moves} B; their events alone are {events} B");
    }

    /// <summary>Bytes this thread allocated per rendered frame, <paramref name="change"/>
    /// applied before each: the least of a few rounds of 100 frames.</summary>
    private static double PerFrame(Application app, Action change)
    {
        for (int i = 0; i < 10; i++) { change(); app.RunFrame(Frame); }

        int ran = 0, rendered = 0;
        double perFrame = Allocations.PerCall(100, () =>
        {
            change();
            ran++;
            if (app.RunFrame(Frame)) rendered++;
        });

        Assert.Equal(ran, rendered);
        return perFrame;
    }
}
