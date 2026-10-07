using System.Text;
using HultUI.Core.RichText;
using Xunit;

namespace HultUI.Core.Tests.RichText;

/// <summary>
/// Pictures and tables in the flat block model (docs/plans/16-rich-text-editor.md, stage 9): a
/// table is a run of cell blocks that never join, a picture a block selected whole.
/// </summary>
public class RichTableTests
{
    private static RichEditState Edit(params RichBlock[] blocks) => new(new RichDocument(blocks));
    private static RichEditState Edit(RichTextFeatures features, params RichBlock[] blocks) => new(new RichDocument(blocks), features);

    private static RichBlock P(string text, params RichSpan[] spans) => RichBlock.Paragraph(text, spans);

    // A table `columns` wide holding `texts`, row by row.
    private static RichBlock[] Table(int columns, params string[] texts) =>
        [.. texts.Select((t, i) => RichBlock.Cell(i / columns, i % columns, t))];

    private static RichBlock Pic(string source) => RichBlock.Picture(new RichImage(source, "alt " + source));

    private static void Caret(RichEditState s, int block, int offset) => s.Select(RichSelection.Caret(block, offset));

    private static void Select(RichEditState s, int b0, int o0, int b1, int o1) =>
        s.Select(new RichPosition(b0, o0), new RichPosition(b1, o1));

    private static string Show(RichEditState s) => string.Join(" | ", s.Document.Blocks);

    // ── The shape ──

    [Fact]
    public void ATable_IsARunOfCells_AndACellAtTheOriginStartsAnother()
    {
        var doc = new RichDocument([P("x"), .. Table(2, "a", "b", "c", "d"), .. Table(3, "e", "f", "g"), P("y")]);
        Assert.Null(doc.TableAt(0));
        Assert.Equal(new RichTableRange(1, 2, 2), doc.TableAt(3));
        Assert.Equal(new RichTableRange(5, 1, 3), doc.TableAt(6));
        Assert.Equal(4, doc.TableAt(1)!.Value.IndexOf(1, 1));
        Assert.Null(doc.TableAt(8));
    }

    [Fact]
    public void Normalize_MakesARaggedTableRectangular_AndLeavesAGoodOneAlone()
    {
        var good = new RichDocument([.. Table(2, "a", "b", "c", "d")]);
        Assert.Same(good, RichTable.Normalize(good));

        // A row one short, then a row numbered as if it went on.
        var ragged = new RichDocument([
            RichBlock.Cell(0, 0, "h1"), RichBlock.Cell(0, 1, "h2") with { Align = BlockAlign.End },
            RichBlock.Cell(1, 0, "a"),
            RichBlock.Cell(7, 0, "b"), RichBlock.Cell(7, 1, "c"),
        ]);
        var fixedDoc = RichTable.Normalize(ragged);
        Assert.Equal(
            "cell[0,0] \"h1\" | cell[0,1]@End \"h2\" | cell[1,0] \"a\" | cell[1,1]@End \"\" | cell[2,0] \"b\" | cell[2,1] \"c\"",
            fixedDoc.ToString());
        Assert.Same(ragged[0], fixedDoc[0]);
    }

    [Fact]
    public void LoadingARaggedTable_MakesItRectangular()
    {
        var s = Edit(RichBlock.Cell(0, 0, "a"), RichBlock.Cell(0, 1, "b"), RichBlock.Cell(1, 0, "c"));
        Assert.Equal(4, s.Document.BlockCount);
        Assert.Equal(new RichTableRange(0, 2, 2), s.Document.TableAt(0));
    }

    [Fact]
    public void PlainText_OfACopy_PutsARowOnOneLine()
    {
        var fragment = new RichFragment([P("x"), .. Table(2, "a", "b", "c", "d")]);
        Assert.Equal("x\na\tb\nc\td", fragment.ToPlainText());
    }

    // ── Inserting ──

    [Fact]
    public void InsertTable_SplitsTheParagraph_AndPutsTheCaretInTheFirstCell()
    {
        var s = Edit(P("abcd"));
        Caret(s, 0, 2);
        Assert.True(s.InsertTable(2, 2));
        Assert.Equal(
            "Paragraph \"ab\" | cell[0,0] \"\" | cell[0,1] \"\" | cell[1,0] \"\" | cell[1,1] \"\" | Paragraph \"cd\"",
            Show(s));
        Assert.Equal(RichSelection.Caret(1, 0), s.Selection);

        s.Undo();
        Assert.Equal("Paragraph \"abcd\"", Show(s));
    }

    [Fact]
    public void InsertTable_InAnEmptyParagraph_LeavesALineAfterIt_AndNotInsideATable()
    {
        var s = Edit(P(""));
        s.InsertTable(1, 2);
        Assert.Equal("cell[0,0] \"\" | cell[0,1] \"\" | Paragraph \"\"", Show(s));
        Assert.False(s.InsertTable(2, 2));
        Assert.False(s.InsertRule());
        Assert.False(s.InsertImage(new RichImage("a.png")));
    }

    [Fact]
    public void Tables_AreAFeature()
    {
        var s = Edit(RichTextFeatures.All & ~RichTextFeatures.Tables, P(""));
        Assert.False(s.InsertTable());
        // Pasted cells keep their text, as paragraphs.
        s.InsertFragment(new RichFragment([P("x"), .. Table(2, "a", "b")]));
        Assert.Equal("Paragraph \"x\" | Paragraph \"a\" | Paragraph \"b\"", Show(s));
    }

    // ── Typing and moving ──

    [Fact]
    public void TypingAndFormatting_WorkInACell_AsInAnyBlock()
    {
        var s = Edit(Table(2, "a", "b"));
        Caret(s, 1, 1);
        s.InsertText("c");
        s.Select(new RichPosition(1, 0), new RichPosition(1, 2));
        s.ToggleStyle(InlineStyle.Bold);
        Assert.Equal("cell[0,0] \"a\" | cell[0,1] \"bc\" {0+2:Bold}", Show(s));
    }

    [Fact]
    public void Enter_GoesToTheCellBelow_AndFromTheLastRowOutOfTheTable()
    {
        var s = Edit([.. Table(2, "a", "b", "c", "d"), P("after")]);
        Caret(s, 0, 1);
        s.SplitBlock();
        Assert.Equal(RichSelection.Caret(2, 1), s.Selection);

        s.SplitBlock();
        Assert.Equal("cell[0,0] \"a\" | cell[0,1] \"b\" | cell[1,0] \"c\" | cell[1,1] \"d\" | Paragraph \"\" | Paragraph \"after\"", Show(s));
        Assert.Equal(RichSelection.Caret(4, 0), s.Selection);

        // An empty line already there is used, not doubled.
        Caret(s, 3, 1);
        s.SplitBlock();
        Assert.Equal(6, s.Document.BlockCount);
        Assert.Equal(RichSelection.Caret(4, 0), s.Selection);
    }

    [Fact]
    public void ShiftEnter_BreaksTheLineInsideTheCell()
    {
        var s = Edit(Table(1, "ab"));
        Caret(s, 0, 1);
        s.InsertLineBreak();
        Assert.Equal("cell[0,0] \"a\nb\"", Show(s));
    }

    [Fact]
    public void Tab_GoesCellToCell_AndAddsARowAfterTheLast()
    {
        var s = Edit(Table(2, "a", "b"));
        Caret(s, 0, 0);
        Assert.True(s.NextCell());
        Assert.Equal(RichSelection.Caret(1, 1), s.Selection);

        Assert.True(s.NextCell());
        Assert.Equal("cell[0,0] \"a\" | cell[0,1] \"b\" | cell[1,0] \"\" | cell[1,1] \"\"", Show(s));
        Assert.Equal(RichSelection.Caret(2, 0), s.Selection);

        Assert.True(s.NextCell(backward: true));
        Assert.Equal(RichSelection.Caret(1, 1), s.Selection);
        Caret(s, 0, 0);
        Assert.False(s.NextCell(backward: true));

        var outside = Edit(P("x"));
        Assert.False(outside.NextCell());
    }

    // ── Deleting ──

    [Fact]
    public void BackspaceAndDelete_NeverJoinCells()
    {
        var s = Edit(Table(2, "a", "b"));
        Caret(s, 1, 0);
        Assert.False(s.DeleteBackward());
        Caret(s, 0, 1);
        Assert.False(s.DeleteForward());
        Assert.Equal("cell[0,0] \"a\" | cell[0,1] \"b\"", Show(s));
    }

    [Fact]
    public void BackspaceAfterATable_GoesIntoItsLastCell_AndAnEmptyLineGoes()
    {
        var s = Edit([.. Table(2, "a", "b"), P("x"), P("")]);
        Caret(s, 2, 0);
        Assert.True(s.DeleteBackward());
        Assert.Equal(4, s.Document.BlockCount);
        Assert.Equal(RichSelection.Caret(1, 1), s.Selection);

        var t = Edit([.. Table(2, "a", "b"), P("")]);
        Caret(t, 2, 0);
        t.DeleteBackward();
        Assert.Equal("cell[0,0] \"a\" | cell[0,1] \"b\"", Show(t));
        Assert.Equal(RichSelection.Caret(1, 1), t.Selection);
    }

    [Fact]
    public void DeleteBeforeATable_DoesNotJoinIt_ButAnEmptyLineGoes()
    {
        var s = Edit([P("x"), .. Table(1, "a")]);
        Caret(s, 0, 1);
        Assert.False(s.DeleteForward());

        var t = Edit([P(""), .. Table(1, "a")]);
        Caret(t, 0, 0);
        t.DeleteForward();
        Assert.Equal("cell[0,0] \"a\"", Show(t));
        Assert.Equal(RichSelection.Caret(0, 0), t.Selection);
    }

    [Fact]
    public void ASelectionAcrossCells_EmptiesThem_AndKeepsEveryCell()
    {
        var s = Edit(Table(2, "abc", "def", "ghi", "jkl"));
        Select(s, 0, 1, 2, 2);
        s.InsertText("X");
        Assert.Equal("cell[0,0] \"aX\" | cell[0,1] \"\" | cell[1,0] \"i\" | cell[1,1] \"jkl\"", Show(s));
        Assert.Equal(RichSelection.Caret(0, 2), s.Selection);
    }

    [Fact]
    public void ASelectionFromTextIntoATable_TakesTheTextAndEmptiesTheCells()
    {
        var s = Edit([P("hello"), .. Table(2, "ab", "cd"), P("world")]);
        Select(s, 0, 2, 2, 1);
        s.DeleteSelection();
        Assert.Equal("Paragraph \"he\" | cell[0,0] \"\" | cell[0,1] \"d\" | Paragraph \"world\"", Show(s));
        Assert.Equal(RichSelection.Caret(0, 2), s.Selection);

        // And out of one into text after it.
        var t = Edit([.. Table(2, "ab", "cd"), P("world")]);
        Select(t, 1, 1, 2, 2);
        t.DeleteSelection();
        Assert.Equal("cell[0,0] \"ab\" | cell[0,1] \"c\" | Paragraph \"rld\"", Show(t));
    }

    [Fact]
    public void ATableInsideTheSelection_GoesWithIt()
    {
        var s = Edit([P("hello"), .. Table(2, "ab", "cd"), P("world")]);
        Select(s, 0, 2, 3, 2);
        s.DeleteSelection();
        Assert.Equal("Paragraph \"herld\"", Show(s));
    }

    // ── Paste ──

    [Fact]
    public void PasteIntoACell_MakesTheBlocksItsLines()
    {
        var s = Edit(Table(1, "[]"));
        Caret(s, 0, 1);
        s.InsertFragment(new RichFragment([P("one", new RichSpan(0, 3, new RichMarks(InlineStyle.Bold))), Pic("p"), P("two")]));
        Assert.Equal("cell[0,0] \"[one\ntwo]\" {1+3:Bold}", Show(s));
    }

    [Fact]
    public void PastingPartOfATable_PastesATable()
    {
        var s = Edit(P("xy"));
        Caret(s, 0, 1);
        // What a copy from inside a table's second row to the paragraph after gives.
        s.InsertFragment(new RichFragment([RichBlock.Cell(1, 1, "d"), RichBlock.Cell(2, 0, "e"), RichBlock.Cell(2, 1, "f"), P("z")]));
        Assert.Equal(
            "Paragraph \"x\" | cell[0,0] \"d\" | cell[0,1] \"\" | cell[1,0] \"e\" | cell[1,1] \"f\" | Paragraph \"zy\"",
            Show(s));
    }

    [Fact]
    public void TextCopiedFromACell_PastesAsText()
    {
        var s = Edit([.. Table(1, "abc"), P("xy")]);
        Select(s, 0, 1, 0, 2);
        var copy = s.SelectedFragment;
        Caret(s, 1, 1);
        s.InsertFragment(copy);
        Assert.Equal("Paragraph \"xby\"", s.Document[1].ToString());
    }

    // ── Shape ──

    [Fact]
    public void InsertRow_AboveOrBelow_RenumbersAndMovesTheCaretIntoIt()
    {
        var s = Edit(Table(2, "a", "b", "c", "d"));
        Caret(s, 3, 0);
        s.InsertRow(below: false);
        Assert.Equal("cell[0,0] \"a\" | cell[0,1] \"b\" | cell[1,0] \"\" | cell[1,1] \"\" | cell[2,0] \"c\" | cell[2,1] \"d\"", Show(s));
        Assert.Equal(RichSelection.Caret(3, 0), s.Selection);

        Caret(s, 0, 0);
        s.InsertRow();
        Assert.Equal(RichSelection.Caret(2, 0), s.Selection);
        Assert.Equal(new RichTableRange(0, 4, 2), s.CurrentTable);
    }

    [Fact]
    public void InsertColumn_AndDeleteColumn()
    {
        var s = Edit(Table(2, "a", "b", "c", "d"));
        Caret(s, 2, 0);
        s.InsertColumn();
        Assert.Equal("cell[0,0] \"a\" | cell[0,1] \"\" | cell[0,2] \"b\" | cell[1,0] \"c\" | cell[1,1] \"\" | cell[1,2] \"d\"", Show(s));
        Assert.Equal(RichSelection.Caret(4, 0), s.Selection);

        Caret(s, 2, 0);
        s.DeleteColumn();
        Assert.Equal("cell[0,0] \"a\" | cell[0,1] \"\" | cell[1,0] \"c\" | cell[1,1] \"\"", Show(s));
    }

    [Fact]
    public void DeleteRow_OfTheHeader_MakesTheNextRowTheHeader()
    {
        var s = Edit([.. Table(2, "a", "b", "c", "d"), P("x")]);
        Caret(s, 1, 0);
        s.DeleteRow();
        Assert.Equal("cell[0,0] \"c\" | cell[0,1] \"d\" | Paragraph \"x\"", Show(s));

        // The last row, then the table.
        s.DeleteRow();
        Assert.Equal("Paragraph \"x\"", Show(s));
        Assert.Equal(RichSelection.Caret(0, 0), s.Selection);
    }

    [Fact]
    public void DeleteTable_AtTheEnd_PutsTheCaretAfterWhatCameBefore()
    {
        var s = Edit([P("x"), .. Table(2, "a", "b")]);
        Caret(s, 2, 0);
        s.DeleteTable();
        Assert.Equal("Paragraph \"x\"", Show(s));
        Assert.Equal(RichSelection.Caret(0, 1), s.Selection);

        var only = Edit(Table(1, "a"));
        only.DeleteTable();
        Assert.Equal("Paragraph \"\"", Show(only));
    }

    [Fact]
    public void SetAlign_InATable_AlignsTheColumn_EvenWhereBlocksCannotBeAligned()
    {
        var s = Edit(RichTextFeatures.Markdown, [P("x"), .. Table(2, "a", "b", "c", "d")]);
        Caret(s, 4, 0);
        Assert.True(s.SetAlign(BlockAlign.Center));
        Assert.Equal("Paragraph \"x\" | cell[0,0] \"a\" | cell[0,1]@Center \"b\" | cell[1,0] \"c\" | cell[1,1]@Center \"d\"", Show(s));

        Caret(s, 0, 0);
        Assert.False(s.SetAlign(BlockAlign.Center));
    }

    [Fact]
    public void BlockFormats_PassOverCells()
    {
        var s = Edit([P("x"), .. Table(1, "a"), P("y")]);
        Select(s, 0, 0, 2, 1);
        s.SetBlockKind(BlockKind.Heading, 2);
        Assert.Equal("h2 \"x\" | cell[0,0] \"a\" | h2 \"y\"", Show(s));
        Assert.True(s.SelectionIs(BlockKind.Heading, 2));
        Assert.False(s.SetBlockKind(BlockKind.CodeBlock));

        Caret(s, 1, 0);
        Assert.False(s.SelectionIs(BlockKind.Paragraph));
        Assert.False(s.Indent());
    }

    // ── Pictures ──

    [Fact]
    public void InsertImage_SplitsTheBlock_AndSelectsThePicture()
    {
        var s = Edit(P("abcd"));
        Caret(s, 0, 2);
        Assert.True(s.InsertImage(new RichImage("cat.png", "A cat")));
        Assert.Equal("Paragraph \"ab\" | image(cat.png) \"\" | Paragraph \"cd\"", Show(s));
        Assert.Equal(RichSelection.Caret(1, 0), s.Selection);
        Assert.Equal((1, new RichImage("cat.png", "A cat")), s.CurrentImage!.Value);

        // Typing on it types after it.
        s.InsertText("z");
        Assert.Equal("Paragraph \"ab\" | image(cat.png) \"\" | Paragraph \"z\" | Paragraph \"cd\"", Show(s));
    }

    [Fact]
    public void BackspaceAfterAPicture_SelectsItFirst()
    {
        var s = Edit(P("a"), Pic("p"), P("b"));
        Caret(s, 2, 0);
        Assert.True(s.DeleteBackward());
        Assert.Equal(3, s.Document.BlockCount);
        Assert.Equal(RichSelection.Caret(1, 0), s.Selection);

        s.DeleteBackward();
        Assert.Equal("Paragraph \"a\" | Paragraph \"b\"", Show(s));
    }

    [Fact]
    public void DeleteBeforeAPicture_SelectsIt_AndAnEmptyLineGoes()
    {
        var s = Edit(P("a"), Pic("p"));
        Caret(s, 0, 1);
        s.DeleteForward();
        Assert.Equal(RichSelection.Caret(1, 0), s.Selection);

        var t = Edit(P(""), Pic("p"));
        Caret(t, 0, 0);
        t.DeleteForward();
        Assert.Equal("image(p) \"\"", Show(t));
    }

    [Fact]
    public void SetImage_ChangesThePicture()
    {
        var s = Edit(Pic("p"));
        Assert.True(s.SetImage(0, new RichImage("q", "Q", Width: 120)));
        Assert.Equal(new RichImage("q", "Q", Width: 120), s.Document[0].Image);
        Assert.False(s.SetImage(0, new RichImage("q", "Q", Width: 120)));
        s.Undo();
        Assert.Equal("p", s.Document[0].Image!.Source);
    }

    [Fact]
    public void Pictures_AreAFeature()
    {
        var s = Edit(RichTextFeatures.All & ~RichTextFeatures.Images, P(""));
        Assert.False(s.InsertImage(new RichImage("p")));
        s.InsertFragment(new RichFragment([P("x"), Pic("p"), P("y")]));
        Assert.DoesNotContain(s.Document.Blocks, b => b.Kind == BlockKind.Image);
    }

    // ── Clipboard ──

    [Fact]
    public void TheEditorsOwnFormat_CarriesPicturesAndCells()
    {
        var fragment = new RichFragment([
            RichBlock.Picture(new RichImage("a b.png", "Alt", "T", 300, "https://x")),
            .. Table(2, "h", "i"),
        ]);
        Assert.Equal(fragment, RichClipboard.Read(RichClipboard.Write(fragment)));
    }

    [Fact]
    public void Html_WritesPicturesAndTables()
    {
        var html = RichHtml.Write([
            RichBlock.Picture(new RichImage("p.png", "A & B", Width: 64)),
            .. Table(2, "h1", "h2", "a", "b"),
            P("after"),
        ]);
        Assert.Equal(
            "<p><img src=\"p.png\" alt=\"A &amp; B\" width=\"64\"></p>"
            + "<table><thead><tr><th>h1</th><th>h2</th></tr></thead><tbody><tr><td>a</td><td>b</td></tr></tbody></table><p>after</p>",
            html);
    }

    // A random walk of edits over a document with tables in it: whatever is typed, deleted or
    // pasted, every table stays rectangular and numbered, and undoing everything gives back the
    // start.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RandomEdits_KeepEveryTableWhole(int seed)
    {
        var rng = new Random(seed);
        var start = new RichDocument([P("intro"), .. Table(3, "a", "b", "c", "d", "e", "f"), P("mid"), Pic("p"), .. Table(2, "x", "y"), P("end")]);
        var s = new RichEditState(start);
        int steps = 0;
        for (int i = 0; i < 1500; i++)
        {
            var doc = s.Document;
            RichPosition Any()
            {
                int b = rng.Next(doc.BlockCount);
                return new RichPosition(b, rng.Next(doc[b].Text.Length + 1));
            }
            if (rng.Next(3) == 0) s.Select(Any(), Any());
            else s.Select(RichSelection.Caret(Any()));
            var before = s.Document;
            switch (rng.Next(14))
            {
                case 0: s.InsertText("q"); break;
                case 1: s.InsertText("w\nv"); break;
                case 2: s.DeleteBackward(); break;
                case 3: s.DeleteForward(); break;
                case 4: s.SplitBlock(); break;
                case 5: s.InsertLineBreak(); break;
                case 6: s.NextCell(rng.Next(2) == 0); break;
                case 7: s.InsertRow(rng.Next(2) == 0); break;
                case 8: s.InsertColumn(rng.Next(2) == 0); break;
                case 9: if (rng.Next(3) == 0) s.DeleteRow(); else s.DeleteColumn(); break;
                case 10: s.InsertFragment(s.Document.Slice(Any(), Any())); break;
                case 11: s.InsertTable(rng.Next(1, 3), rng.Next(1, 3)); break;
                case 12: s.SetBlockKind(rng.Next(2) == 0 ? BlockKind.BulletItem : BlockKind.Paragraph); break;
                case 13: s.InsertImage(new RichImage("r")); break;
            }
            if (!ReferenceEquals(before, s.Document)) steps++;
            Assert.Same(s.Document, RichTable.Normalize(s.Document));
        }
        while (s.Undo()) { }
        Assert.True(steps > 0);
        // The history holds the last MaxSteps edits; the walk is longer, so undo reaches a
        // document from partway through — and that, too, is whole.
        Assert.Same(s.Document, RichTable.Normalize(s.Document));
    }
}
