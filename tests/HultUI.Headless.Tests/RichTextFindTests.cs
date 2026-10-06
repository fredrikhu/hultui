using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.RichText;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Find in the rich text editor (docs/plans/16-rich-text-editor.md, stage 8): a FindBar's search
/// bound to the document a block to a line, kept bound as it is edited, its matches tinted and
/// the current one selected.
/// </summary>
public class RichTextFindTests
{
    private static RichBlock P(string text) => RichBlock.Paragraph(text);

    private static (HeadlessSession Session, RichTextEditorElement Editor) Mount(RichDocument doc, float height = 300)
    {
        var session = HeadlessSession.Create(width: 600, height: 400);
        var editor = RichTextEditor.New(doc).Width(400).Height(height);
        session.Window.SetRoot(Box.New().Width(600).Height(400).Children(editor));
        session.PumpFrame();
        return (session, editor);
    }

    [Fact]
    public void TheSearch_ReadsTheBlocks_AndFollowsEdits()
    {
        var (session, editor) = Mount(new RichDocument([P("alpha beta"), RichBlock.Heading(2, "beta gamma beta")]));
        using var _ = session;
        var search = new TextSearch(() => { });
        editor.Search = search;
        search.SetQuery("beta");
        Assert.Equal([new TextSearch.Hit(0, 0, 6, 4), new TextSearch.Hit(1, 0, 0, 4), new TextSearch.Hit(1, 0, 11, 4)], search.Hits);

        editor.State.Select(RichSelection.Caret(0, 0));
        editor.State.InsertText("beta ");
        Assert.Equal(4, search.Hits.Count);
        Assert.Equal(new TextSearch.Hit(0, 0, 11, 4), search.Hits[1]);
    }

    [Fact]
    public void RevealSearchMatch_SelectsTheMatch_AndScrollsToIt()
    {
        var blocks = Enumerable.Range(0, 200).Select(i => P(i == 150 ? "a haystack with the needle in it" : $"line {i}")).ToList();
        var (session, editor) = Mount(new RichDocument(blocks));
        using var _ = session;
        var search = new TextSearch(() => { });
        editor.Search = search;
        search.SetQuery("needle");
        editor.RevealSearchMatch();
        session.PumpFrame();

        Assert.Equal(new RichSelection(new RichPosition(150, 20), new RichPosition(150, 26)), editor.State.Selection);
        var caret = editor.CaretBounds(new RichPosition(150, 20));
        var box = editor.Layout.AbsoluteContentBox;
        Assert.True(caret.Y >= box.Y && caret.Bottom <= box.Bottom, "the match is in view");
    }

    [Fact]
    public void Matches_AreTintedBehindTheText_TheCurrentOneStronger()
    {
        var (session, editor) = Mount(new RichDocument([P("one two"), P("two three")]));
        using var _ = session;
        var search = new TextSearch(() => { });
        editor.Search = search;
        search.SetQuery("two");

        // Captured on a frame drawn now, whatever was marked dirty.
        using var shot = session.Window.CapturePixels();
        using var bitmap = SkiaSharp.SKBitmap.FromImage(shot);
        SkiaSharp.SKColor At(int block, int offset)
        {
            var c = editor.CaretBounds(new RichPosition(block, offset));
            // Above the glyphs' x-height, inside the line box: tint, not ink.
            return bitmap.GetPixel((int)c.X + 1, (int)(c.Y + 2));
        }
        var plain = At(0, 1);
        var current = At(0, 5);
        var other = At(1, 1);
        Assert.True(current.Blue < other.Blue && other.Blue < plain.Blue,
            $"amber under the matches, more under the current one: plain {plain}, match {other}, current {current}");
    }

    [Fact]
    public void AFindBar_OpensWithTheSelection_AndWalksTheMatches()
    {
        var (session, editor) = Mount(new RichDocument([P("cat dog"), P("dog cat dog")]));
        using var _ = session;
        var bar = new FindBar(() => { })
        {
            Body = () => editor,
            Selection = () => editor.FindSelection,
            Reveal = editor.RevealSearchMatch,
            RevealAfterLayout = false,
        };
        editor.Search = bar.Search;
        editor.State.Select(new RichPosition(0, 4), new RichPosition(0, 7));
        Assert.Equal(new FindSelection("dog", 0, 0, 4), editor.FindSelection);

        bar.Open();
        Assert.Equal("dog", bar.Search.Query);
        bar.Next();
        bar.Service();
        Assert.Equal(new RichSelection(new RichPosition(1, 0), new RichPosition(1, 3)), editor.State.Selection);
        bar.Next();
        bar.Service();
        Assert.Equal(new RichSelection(new RichPosition(1, 8), new RichPosition(1, 11)), editor.State.Selection);
    }
}
