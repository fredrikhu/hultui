using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using SkiaSharp;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A selectable or linked label keeps the paragraph it hit-tests against from frame to frame and
/// draws its text from it (<see cref="IRenderContext.DrawParagraph"/>), where it used to lay a new
/// paragraph out every frame — ~320 B a label, 10 KB a frame over a screen of Junction's diff or
/// Vesper's reader — and then draw through DrawText, which looked the layout up again (plan 17,
/// round 2). The paragraph is laid out again when anything it was laid out from changes: the text,
/// the style, the width, the spans, the line limit, the overflow.
/// </summary>
public class SelectableLabelPaintTests
{
    // ── Pixels: a kept paragraph draws what DrawText drew ──

    /// <summary>
    /// What a selectable label drew before it kept its paragraph: a paragraph laid out for the
    /// selection boxes, then the text through DrawText — TextElement.Render as it was.
    /// </summary>
    private sealed class DrawTextLabel : TextElement
    {
        public (int Start, int End)? Selection { get; init; }

        public override void Render(IRenderContext ctx)
        {
            var box = Layout.AbsoluteContentBox;
            var style = ResolvedInherited;
            if (string.IsNullOrEmpty(Content)) return;
            if (Selection is { } selection)
            {
                var paragraph = ctx.LayoutParagraph(Content, style, box.Width, Spans, MaxLines, Overflow);
                var boxes = new List<Rect>();
                paragraph.SelectionBoxes(selection.Start, selection.End, boxes, style.FontSize * 0.35f);
                foreach (var b in boxes)
                    ctx.DrawRect(b with { X = box.X + b.X, Y = box.Y + b.Y }, SelectionColor);
            }
            if (Spans is { Count: > 0 } spans)
                ctx.DrawText(Content, box, style, Overflow, MaxLines, spans);
            else
                ctx.DrawText(Content, box, style, Overflow, MaxLines);
        }
    }

    private const string Latin = "The quick brown fox jumps over the lazy dog";
    private const string Hebrew = "שלום עולם, זו שורה שנקראת מימין לשמאל";
    private const string Mixed = "עברית בתחילת השורה and English after it";
    private const string Wrapping =
        "A paragraph long enough to wrap onto a second and a third line inside the box it is given, centred.";

    private static readonly TextSpan[] Styled =
    [
        new(4, 5, new Color(200, 30, 30, 255)),
        new(10, 5, default, Background: new Color(250, 230, 120, 255)),
        new(16, 3, new Color(30, 30, 200, 255), Decoration: TextDecoration.Underline),
        new(20, 5, default, Weight: FontWeight.Bold),
        new(31, 8, new Color(20, 140, 60, 255), new Color(210, 230, 255, 255)),
    ];

    /// <summary>One of each kind of line a label draws differently: aligned four ways, right to
    /// left and mixed, truncated both ways, wrapped and centred, spanned, underlined, ending in a
    /// newline, selected on one line and across wrapped ones, and a linked label that is not
    /// selectable. <paramref name="make"/> builds a label from its text and selection.</summary>
    private static (Element Root, List<TextElement> Labels) Scene(Func<string, (int, int)?, TextElement> make)
    {
        var labels = new List<TextElement>();
        TextElement Add(string text, (int, int)? selection = null)
        {
            var label = make(text, selection);
            labels.Add(label);
            return label;
        }

        var column = Flex.Column(gap: 6).Padding(10).Width(Dimension.Px(380)).Children(
            Add(Latin),
            Add(Latin).TextAlign(TextAlign.Center),
            Add(Latin).TextAlign(TextAlign.Right),
            Add(Latin).TextAlign(TextAlign.End),
            Rtl(Add(Hebrew), TextDirection.Rtl),
            Rtl(Add(Mixed), TextDirection.Auto).TextAlign(TextAlign.Center),
            Box.New().Width(Dimension.Px(170)).Children(Add(Latin).MaxLines(1).TextOverflow(TextOverflow.Ellipsis)),
            Box.New().Width(Dimension.Px(170)).Children(Rtl(Add(Hebrew), TextDirection.Rtl).MaxLines(1).TextOverflow(TextOverflow.Ellipsis)),
            Box.New().Width(Dimension.Px(170)).Children(Add(Latin).MaxLines(1)),
            Add(Wrapping).TextAlign(TextAlign.Center),
            Add(Wrapping).MaxLines(2).TextOverflow(TextOverflow.Ellipsis),
            Add(Latin).Spans(Styled),
            Add(Latin).Spans(Styled).TextAlign(TextAlign.Right).FontSize(15f),
            Add(Latin).Underline(),
            Add("Two lines,\nthen a final newline\n"),
            Add(Latin, (4, 25)),
            Add(Wrapping, (12, 80)).TextAlign(TextAlign.Center),
            Add(Latin, (2, 30)).Spans(Styled));
        return (Box.New().Background(Color.White).Children(column), labels);
    }

    private static TextElement Rtl(TextElement label, TextDirection direction)
    {
        label.Inherited.TextDirection = direction;
        return label;
    }

    private static readonly TextLink Link = new(4, 5, "fox://quick");

    private static byte[] Pixels(HeadlessSession session)
    {
        using var image = session.Window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.Bytes;
    }

    /// <summary>
    /// Every label drawn from the paragraph it keeps is pixel for pixel what DrawText drew: the
    /// alignments (a line's LineLeft at the box's width is DrawText's offset), right to left,
    /// ellipsized and clipped lines, spans, a final newline, selections. The first frame lays the
    /// paragraphs out; the second draws the same instances again. In the fonts the golden images
    /// pin as well as the installed ones, and at a fractional scale.
    /// </summary>
    [Theory]
    [InlineData(false, 1f)]
    [InlineData(true, 1f)]
    [InlineData(false, 1.5f)]
    public void AKeptParagraph_DrawsWhatDrawTextDrew(bool pinnedText, float scale)
    {
        var options = pinnedText ? HeadlessOptions.Golden() : null;

        // One application at a time in a process: the DrawText frame first, then the kept one.
        byte[] expected;
        using (var before = HeadlessSession.Create(400, 640, scale, options: options))
        {
            var (expectedRoot, _) = Scene((text, selection) =>
                new DrawTextLabel { Content = text, Selectable = true, Selection = selection });
            before.Window.SetRoot(expectedRoot);
            before.PumpFrame();
            expected = Pixels(before);
        }

        using var after = HeadlessSession.Create(400, 640, scale, options: options);
        var (root, labels) = Scene((text, selection) =>
        {
            var label = TextElement.New(text);
            label.Selectable = true;
            if (selection is { } range) label.SetSelectionRange(range.Item1, range.Item2);
            return label;
        });
        // The first label is linked and not selectable: the other way into keeping a paragraph.
        labels[0].Selectable = false;
        labels[0].Links = [Link];
        after.Window.SetRoot(root);
        after.PumpFrame();
        var first = Pixels(after);
        var kept = labels.Select(l => l.DrawnParagraph).ToList();
        Assert.All(kept, p => Assert.NotNull(p));

        root.MarkPaintDirty();
        Assert.True(after.PumpFrame());
        var second = Pixels(after);

        for (int i = 0; i < labels.Count; i++)
            Assert.Same(kept[i], labels[i].DrawnParagraph);
        Assert.Equal(expected, first);
        Assert.Equal(expected, second);
    }

    // ── When the paragraph is kept, and when it is laid out again ──

    private sealed class Mounted : IDisposable
    {
        public HeadlessSession Session { get; } = HeadlessSession.Create(500, 300);
        public TextElement Label { get; }
        public BoxElement Holder { get; }
        public ScrollViewElement View { get; }

        public Mounted(TextElement label)
        {
            Label = label;
            label.Selectable = true;
            Holder = Box.New().Width(Dimension.Px(300)).Children(label);
            View = ScrollView.New().FlexGrow(1).Children(
                Flex.Column().FlexShrink(0).Padding(8).Children(Holder, Box.New().Height(Dimension.Px(800))));
            Session.Window.SetRoot(View);
            Session.PumpFrame();
        }

        /// <summary>The paragraph the next frame draws, the frame forced if nothing asked for one.</summary>
        public TextParagraph Frame()
        {
            Session.PumpFrame(Session.DefaultFrameDelta, forceRender: true);
            return Label.DrawnParagraph ?? throw new Xunit.Sdk.XunitException("the label drew no paragraph");
        }

        public void Dispose() => Session.Dispose();
    }

    [Fact]
    public void ALabelRepaintedOrMoved_DrawsTheParagraphItKept()
    {
        using var m = new Mounted(TextElement.New(Latin).Spans(Styled));
        var kept = m.Frame();

        m.Label.MarkPaintDirty();
        Assert.Same(kept, m.Frame());

        float top = m.Label.WindowBorderBox.Y;
        m.View.ScrollOffsetY = 40;
        Assert.Same(kept, m.Frame());
        Assert.Equal(top - 40, m.Label.WindowBorderBox.Y, 0.01f);

        m.Label.SetSelectionRange(3, 12);
        Assert.Same(kept, m.Frame());
    }

    [Fact]
    public void NewText_IsLaidOutAgain()
    {
        using var m = new Mounted(TextElement.New(Latin));
        var kept = m.Frame();

        m.Label.Content = "Another line of text entirely";
        var drawn = m.Frame();

        Assert.NotSame(kept, drawn);
        Assert.Equal("Another line of text entirely", drawn.Text);
    }

    [Fact]
    public void ANewWidth_IsLaidOutAgain()
    {
        using var m = new Mounted(TextElement.New(Wrapping));
        var kept = m.Frame();

        m.Holder.Width(Dimension.Px(220));
        m.Holder.MarkLayoutDirty();
        var drawn = m.Frame();

        Assert.NotSame(kept, drawn);
        Assert.Equal(220f, drawn.MaxWidth);
        Assert.True(drawn.Lines.Count > kept.Lines.Count, "the narrower box should wrap into more lines");
    }

    /// <summary>A colour change lays out nothing differently, but the paragraph carries the style
    /// it is drawn in: kept, the label would go on drawing in the old colour.</summary>
    [Fact]
    public void ANewStyle_IsLaidOutAgain_AndDrawsInIt()
    {
        using var m = new Mounted(TextElement.New("Selectable text in a colour").FontSize(20f));
        var kept = m.Frame();

        m.Label.Inherited.TextColor = new Color(220, 0, 0, 255);
        m.Label.MarkStyleDirty();
        var drawn = m.Frame();

        Assert.NotSame(kept, drawn);
        Assert.Equal(new Color(220, 0, 0, 255), drawn.Style.TextColor);
        Assert.True(RedPixels(m) > 50, "the label is not drawn in its new colour");
    }

    private static int RedPixels(Mounted m)
    {
        using var image = m.Session.Window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        var box = m.Label.WindowBorderBox;
        int red = 0;
        for (int y = (int)box.Y; y < (int)box.Bottom; y++)
            for (int x = (int)box.X; x < (int)box.Right; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Red > 150 && c.Green < 90 && c.Blue < 90) red++;
            }
        return red;
    }

    [Fact]
    public void NewSpans_AreLaidOutAgain_AndAnEqualListIsNot()
    {
        using var m = new Mounted(TextElement.New(Latin).Spans(Styled));
        var kept = m.Frame();

        // An equal list, built anew as every rebuild builds one: the label keeps the one it has.
        m.Label.Spans = [.. Styled];
        Assert.Same(kept, m.Frame());

        TextSpan[] bolder = [new(4, 5, new Color(200, 30, 30, 255), Weight: FontWeight.Bold)];
        m.Label.Spans = bolder;
        var drawn = m.Frame();

        Assert.NotSame(kept, drawn);
        Assert.Same(bolder, drawn.Spans);
    }

    [Fact]
    public void ANewLineLimit_IsLaidOutAgain()
    {
        using var m = new Mounted(TextElement.New(Wrapping));
        var kept = m.Frame();

        m.Label.MaxLines = 1;
        m.Label.MarkLayoutDirty();
        var drawn = m.Frame();

        Assert.NotSame(kept, drawn);
        Assert.Equal(1, drawn.MaxLines);
        Assert.Single(drawn.Lines);
    }

    [Fact]
    public void ANewOverflow_IsLaidOutAgain()
    {
        using var m = new Mounted(TextElement.New(Wrapping).MaxLines(1));
        var kept = m.Frame();

        m.Label.Overflow = TextOverflow.Ellipsis;
        m.Label.MarkLayoutDirty();
        var drawn = m.Frame();

        Assert.NotSame(kept, drawn);
        Assert.Equal(TextOverflow.Ellipsis, drawn.Overflow);
    }
}
