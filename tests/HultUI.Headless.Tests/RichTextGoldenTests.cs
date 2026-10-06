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
}
