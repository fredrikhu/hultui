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
