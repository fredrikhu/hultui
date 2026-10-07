using HultUI.Core.RichText;
using Xunit;

namespace HultUI.Core.Tests.RichText;

/// <summary>The document model under the editor: spans stay normalized whatever edits made
/// them, and global offsets read the document as its plain text.</summary>
public class RichModelTests
{
    private static readonly RichMarks Bold = new(InlineStyle.Bold);
    private static readonly RichMarks Italic = new(InlineStyle.Italic);

    [Fact]
    public void Spans_AreSortedMergedAndNeverPlainOrEmpty()
    {
        var block = RichBlock.Paragraph("abcdefgh",
            new RichSpan(4, 2, Bold), new RichSpan(0, 2, Bold), new RichSpan(2, 2, Bold),
            new RichSpan(6, 0, Italic), new RichSpan(6, 2, RichMarks.None));

        Assert.Equal([new RichSpan(0, 6, Bold)], block.Spans.ToArray());
    }

    [Fact]
    public void OverlappingSpans_TheLaterOneWinsItsRange()
    {
        var block = RichBlock.Paragraph("abcdefgh", new RichSpan(0, 8, Bold), new RichSpan(2, 2, Italic));

        Assert.Equal([new RichSpan(0, 2, Bold), new RichSpan(2, 2, Italic), new RichSpan(4, 4, Bold)], block.Spans.ToArray());
    }

    [Fact]
    public void BlocksThatReadTheSame_AreEqual_HoweverTheyWereMade()
    {
        var a = RichBlock.Paragraph("hello", new RichSpan(0, 2, Bold), new RichSpan(2, 3, Bold));
        var b = RichBlock.Paragraph("he", new RichSpan(0, 2, Bold))
            .Append(RichBlock.Paragraph("llo", new RichSpan(0, 3, Bold)));

        Assert.Equal(a, b);
        Assert.NotSame(a, b);
        Assert.NotEqual(a, a with { Kind = BlockKind.Quote });
    }

    [Fact]
    public void Slice_AndAppend_MoveSpansWithTheirText()
    {
        var block = RichBlock.Paragraph("one two three", new RichSpan(4, 3, Bold));

        var tail = block.Slice(4, 13);
        Assert.Equal("two three", tail.Text);
        Assert.Equal([new RichSpan(0, 3, Bold)], tail.Spans.ToArray());

        var joined = RichBlock.Paragraph("x", new RichSpan(0, 1, Bold)).Append(tail);
        Assert.Equal([new RichSpan(0, 4, Bold)], joined.Spans.ToArray());   // merged where they meet
    }

    [Fact]
    public void GlobalOffsets_ReadTheBlocksJoinedByNewlines()
    {
        var doc = new RichDocument([RichBlock.Paragraph("ab"), RichBlock.Heading(1, "cde"), RichBlock.Paragraph("")]);

        Assert.Equal("ab\ncde\n", doc.ToPlainText());
        Assert.Equal(doc.ToPlainText().Length, doc.Length);
        Assert.Equal(3, doc.ToOffset(new RichPosition(1, 0)));
        Assert.Equal(new RichPosition(1, 2), doc.ToPosition(5));
        Assert.Equal(new RichPosition(0, 2), doc.ToPosition(2));   // the separator ends the block before it
        Assert.Equal(new RichPosition(2, 0), doc.ToPosition(doc.Length));
        for (int o = 0; o <= doc.Length; o++)
            Assert.Equal(o, doc.ToOffset(doc.ToPosition(o)));
    }

    [Fact]
    public void Slice_AcrossBlocks_CutsBothEnds_AndKeepsFormats()
    {
        var doc = new RichDocument([
            RichBlock.Paragraph("first"),
            RichBlock.Of(BlockKind.BulletItem, "middle"),
            RichBlock.Heading(2, "last"),
        ]);

        var fragment = doc.Slice(new RichPosition(0, 2), new RichPosition(2, 2));

        Assert.Equal(["rst", "middle", "la"], fragment.Blocks.Select(b => b.Text));
        Assert.Equal(BlockKind.Heading, fragment.Blocks[2].Kind);
        Assert.Equal("rst\nmiddle\nla", fragment.ToPlainText());
    }

    [Fact]
    public void ADocumentIsNeverEmpty()
    {
        Assert.Equal(1, new RichDocument([]).BlockCount);
        Assert.Equal(1, RichDocument.Empty.ReplaceBlocks(0, 1, []).BlockCount);
    }

    [Fact]
    public void Map_SplitsRunsAtTheRangesEdges()
    {
        var block = RichBlock.Paragraph("abcdef", new RichSpan(0, 4, Italic));
        var spans = RichSpans.Map(block.Spans, 6, 2, 6, m => m.With(InlineStyle.Bold, true));

        Assert.Equal([
            new RichSpan(0, 2, Italic),
            new RichSpan(2, 2, new RichMarks(InlineStyle.Italic | InlineStyle.Bold)),
            new RichSpan(4, 2, Bold),
        ], spans.ToArray());
    }

    [Fact]
    public void Features_Restrict_DropsWhatTheyDoNotAllow()
    {
        var block = RichBlock.Paragraph("abc",
            new RichSpan(0, 3, new RichMarks(InlineStyle.Bold | InlineStyle.Underline, Color: new(255, 0, 0, 255))))
            with { Kind = BlockKind.TaskItem, Align = BlockAlign.Center };

        var restricted = RichTextFeatures.Markdown.Restrict(block);
        Assert.Equal([new RichSpan(0, 3, Bold)], restricted.Spans.ToArray());
        Assert.Equal(BlockAlign.Start, restricted.Align);
        Assert.Equal(BlockKind.TaskItem, restricted.Kind);

        var noTasks = (RichTextFeatures.Markdown & ~RichTextFeatures.TaskLists).Restrict(block);
        Assert.Equal(BlockKind.BulletItem, noTasks.Kind);
    }
}
