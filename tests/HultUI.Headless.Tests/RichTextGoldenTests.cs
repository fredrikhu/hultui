using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.RichText;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Golden images of the rich text editor (docs/plans/16-rich-text-editor.md, stage 3): every
/// block kind and every mark, in the light and dark themes, and a selection over a block break.
/// Baselines are in <c>Golden/</c>; <c>HULTUI_UPDATE_GOLDENS=1</c> rewrites them.
/// </summary>
public sealed class RichTextGoldenTests
{
    private static RichSpan S(int start, int length, RichMarks marks) => new(start, length, marks);

    internal static RichDocument Showcase()
    {
        const string marks = "Bold, italic, underline, struck, code, a link, red and highlighted.";
        int At(string word) => marks.IndexOf(word, StringComparison.Ordinal);
        return new RichDocument(
        [
            RichBlock.Heading(1, "Release notes"),
            RichBlock.Paragraph(marks,
                S(At("Bold"), 4, new(InlineStyle.Bold)),
                S(At("italic"), 6, new(InlineStyle.Italic)),
                S(At("underline"), 9, new(InlineStyle.Underline)),
                S(At("struck"), 6, new(InlineStyle.Strike)),
                S(At("code"), 4, new(InlineStyle.Code)),
                S(At("a link"), 6, new(Link: "https://example.org")),
                S(At("red"), 3, new(Color: new Color(220, 38, 38))),
                S(At("highlighted"), 11, new(Highlight: new Color(253, 224, 71)))),
            RichBlock.Heading(2, "Lists"),
            RichBlock.Of(BlockKind.BulletItem, "A bullet"),
            RichBlock.Of(BlockKind.BulletItem, "Nested under it", indent: 1),
            RichBlock.Of(BlockKind.BulletItem, "And deeper still", indent: 2),
            RichBlock.Of(BlockKind.NumberedItem, "First"),
            RichBlock.Of(BlockKind.NumberedItem, "Second"),
            RichBlock.Of(BlockKind.NumberedItem, "Its own first", indent: 1),
            RichBlock.Of(BlockKind.TaskItem, "Done") with { Checked = true },
            RichBlock.Of(BlockKind.TaskItem, "Still to do"),
            RichBlock.Of(BlockKind.Quote, "A quote, long enough to wrap onto a second line inside its bar."),
            RichBlock.Of(BlockKind.CodeBlock, "var doc = RichDocument.Empty;\nConsole.WriteLine(doc);") with { Info = "cs" },
            RichBlock.Of(BlockKind.Rule),
            RichBlock.Paragraph("Centred") with { Align = BlockAlign.Center },
        ]);
    }

    private static HeadlessSession Session(Theme theme)
    {
        var session = HeadlessSession.Create(480, 600, options: HeadlessOptions.Golden());
        session.Application.Theme = theme;
        return session;
    }

    private static RichTextEditorElement Editor(RichDocument doc) =>
        RichTextEditor.New(doc).Width(440).Height(560);

    private static Element Frame(Element editor) =>
        Box.New().Width(480).Height(600).Padding(new Thickness(20)).Children(editor);

    [Fact]
    public void Showcase_Light()
    {
        using var session = Session(Theme.Light);
        session.Window.SetRoot(Frame(Editor(Showcase())));
        session.PumpFrames(3);
        GoldenImage.Match(session.Window, "richtext-light");
    }

    [Fact]
    public void Showcase_Dark()
    {
        using var session = Session(Theme.Dark);
        session.Window.SetRoot(Frame(Editor(Showcase())));
        session.PumpFrames(3);
        GoldenImage.Match(session.Window, "richtext-dark");
    }

    [Fact]
    public void ASelectionAcrossBlocks_AndTheCaret()
    {
        using var session = HeadlessSession.Create(320, 160, options: HeadlessOptions.Golden());
        var doc = new RichDocument(
        [
            RichBlock.Paragraph("One paragraph of text"),
            RichBlock.Paragraph(""),
            RichBlock.Of(BlockKind.BulletItem, "and a list item"),
        ]);
        var editor = RichTextEditor.New(doc).Width(280).Height(120);
        session.Window.SetRoot(Box.New().Width(320).Height(160).Padding(new Thickness(20)).Children(editor));
        session.PumpFrame();
        session.Window.Click(editor);
        editor.State.Select(new RichPosition(0, 4), new RichPosition(2, 5));
        session.PumpFrames(3);
        GoldenImage.Match(session.Window, "richtext-selection");
    }

    // Stage 9: a table — header tint, grid, a column aligned to the end, a cell on two lines,
    // a selection across cells — then a picture drawn from the resolver, one still loading and
    // one with nothing to show, the first selected.
    [Fact]
    public void TablesAndPictures()
    {
        using var session = HeadlessSession.Create(480, 520, options: HeadlessOptions.Golden());
        session.Application.Theme = Theme.Light;
        var picture = Checkers(160, 80);
        var loading = new Loading();
        var doc = new RichDocument(
        [
            RichBlock.Cell(0, 0, "Owner"), RichBlock.Cell(0, 1, "Task"), RichBlock.Cell(0, 2, "Days") with { Align = BlockAlign.End },
            RichBlock.Cell(1, 0, "Ana"), RichBlock.Cell(1, 1, "Pictures as blocks"), RichBlock.Cell(1, 2, "2") with { Align = BlockAlign.End },
            RichBlock.Cell(2, 0, "Bo"), RichBlock.Cell(2, 1, "Tables,\nwith Tab between cells", S(0, 6, new(InlineStyle.Bold))),
            RichBlock.Cell(2, 2, "13") with { Align = BlockAlign.End },
            RichBlock.Picture(new RichImage("checkers")),
            RichBlock.Picture(new RichImage("late.png", "A picture on its way")),
            RichBlock.Picture(new RichImage("docs/missing.png")),
        ]);
        var editor = RichTextEditor.New(doc).Width(440).Height(480)
            .Images(src => src switch { "checkers" => picture, "late.png" => loading, _ => null });
        session.Window.SetRoot(Box.New().Width(480).Height(520).Padding(new Thickness(20)).Children(editor));
        session.PumpFrame();
        session.Window.Click(editor);
        editor.State.Select(new RichPosition(1, 2), new RichPosition(4, 5));
        session.PumpFrames(3);
        GoldenImage.Match(session.Window, "richtext-table");

        editor.State.Select(RichSelection.Caret(9, 0));
        session.PumpFrames(3);
        GoldenImage.Match(session.Window, "richtext-picture-selected");
    }

    // A checkerboard of two blues in 20-pixel squares: a picture whose scaling and placement
    // show at a glance.
    private static Rendering.Skia.SkiaImageSource Checkers(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                bool dark = ((x / 20) + (y / 20)) % 2 == 0;
                int at = (y * width + x) * 4;
                pixels[at] = dark ? (byte)30 : (byte)140;
                pixels[at + 1] = dark ? (byte)64 : (byte)180;
                pixels[at + 2] = dark ? (byte)175 : (byte)240;
                pixels[at + 3] = 255;
            }
        return Rendering.Skia.SkiaImageSource.FromPixels(pixels, width, height, width * 4, Core.Rendering.PixelFormat.Rgba8888);
    }

    // A source the resolver has started on and not finished.
    private sealed class Loading : Core.Rendering.IImageSource
    {
        public Size IntrinsicSize => default;
        public bool IsLoaded => false;
        public string? AlternateText => null;
    }
}
