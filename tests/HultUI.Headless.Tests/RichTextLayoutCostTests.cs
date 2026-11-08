using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.RichText;
using HultUI.Testing;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// What the rich text editor itself allocates for each block when it lays a document out again,
/// beside what the block's paragraph costs (plan 17, round 8).
/// </summary>
public class RichTextLayoutCostTests
{
    // A document of `blocks` paragraphs of the same word: after the first block, every block's
    // text at a width is a hit in the layout cache, so what is left is the editor's own.
    private static HeadlessSession Open(int blocks, out RichTextEditorElement editor)
    {
        var session = HeadlessSession.Create(width: 600, height: 400);
        var doc = new RichDocument(Enumerable.Range(0, blocks).Select(_ => RichBlock.Paragraph("word")));
        editor = RichTextEditor.New(doc).FlexGrow(1);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(editor));
        session.PumpFrame();
        return session;
    }

    [Fact]
    public void AWidthChange_CostsATextBlockItsParagraphAndASmallLayout()
    {
        static long Bytes(int blocks)
        {
            using var session = Open(blocks, out _);
            int width = 600;
            return Allocations.Least(() =>
            {
                session.Window.SetClientSize(--width, 400);
                session.PumpFrame();
            });
        }
        long perBlock = (Bytes(400) - Bytes(200)) / 200;
        // Each block: its paragraph (144 B) and the array of its one line (32 B: a one-line
        // layout's lines are not an array the paragraph can share); its layout, 72 B now that a
        // cell's and a picture's fields and a second copy of its style are not in it (152 B with
        // them); its place in a new array of layouts (8 B).
        Assert.Equal(144 + 32 + 72 + 8, perBlock);
    }
}
