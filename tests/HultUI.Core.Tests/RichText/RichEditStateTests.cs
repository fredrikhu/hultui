using HultUI.Core.RichText;
using Xunit;

namespace HultUI.Core.Tests.RichText;

/// <summary>
/// The editing rules of docs/plans/16-rich-text-editor.md, stage 2, on the engine alone: what
/// typing, Enter, Backspace, Delete, paste, formatting, undo and the Markdown shortcuts do to a
/// document. Blocks are compared through <see cref="RichBlock.ToString"/> — kind, depth, text and
/// spans in one line.
/// </summary>
public class RichEditStateTests
{
    private static readonly RichMarks Bold = new(InlineStyle.Bold);

    private static RichEditState Edit(params RichBlock[] blocks) => new(new RichDocument(blocks));

    private static RichBlock P(string text, params RichSpan[] spans) => RichBlock.Paragraph(text, spans);
    private static RichBlock Item(BlockKind kind, string text, int indent = 0) => RichBlock.Of(kind, text, indent);

    private static void Caret(RichEditState s, int block, int offset) => s.Select(RichSelection.Caret(block, offset));

    private static void Select(RichEditState s, int b0, int o0, int b1, int o1) =>
        s.Select(new RichPosition(b0, o0), new RichPosition(b1, o1));

    // One keystroke per character, as a person types, so the shortcuts see each one.
    private static void Type(RichEditState s, string text)
    {
        foreach (var rune in text.EnumerateRunes())
            s.InsertText(rune.ToString());
    }

    private static string Show(RichEditState s) => string.Join(" | ", s.Document.Blocks);

    // ── Typing ──

    [Fact]
    public void Typing_ContinuesTheMarksBeforeTheCaret()
    {
        var s = Edit(P("bold plain", new RichSpan(0, 4, Bold)));
        Caret(s, 0, 4);
        Type(s, "er");
        Assert.Equal("Paragraph \"bolder plain\" {0+6:Bold}", Show(s));
    }

    [Fact]
    public void TypingAfterALink_IsPlain_ButInsideItExtendsIt()
    {
        var s = Edit(P("see docs", new RichSpan(4, 4, new RichMarks(Link: "u"))));
        Caret(s, 0, 8);
        Type(s, "!");
        Assert.Equal("Paragraph \"see docs!\" {4+4:link=u}", Show(s));

        Caret(s, 0, 6);
        Type(s, "x");
        Assert.Equal("Paragraph \"see doxcs!\" {4+5:link=u}", Show(s));
    }

    [Fact]
    public void TypingAtTheEndOfInlineCode_IsPlain()
    {
        var s = Edit(P("run x", new RichSpan(4, 1, new RichMarks(InlineStyle.Code))));
        Caret(s, 0, 5);
        Type(s, "y");
        Assert.Equal("Paragraph \"run xy\" {4+1:Code}", Show(s));
    }

    [Fact]
    public void AStyleToggledWithNoSelection_IsTypedUntilTheCaretMoves()
    {
        var s = Edit(P("ab"));
        Caret(s, 0, 2);
        Assert.True(s.ToggleStyle(InlineStyle.Bold));
        Assert.Equal("Paragraph \"ab\"", Show(s));   // nothing to make bold yet
        Assert.True(s.TypingMarks.Has(InlineStyle.Bold));

        Type(s, "cd");
        Assert.Equal("Paragraph \"abcd\" {2+2:Bold}", Show(s));

        Caret(s, 0, 1);
        Assert.Null(s.PendingMarks);
        Type(s, "z");
        Assert.Equal("Paragraph \"azbcd\" {3+2:Bold}", Show(s));
    }

    [Fact]
    public void TypingOverASelectionAcrossBlocks_JoinsThem_InTheFirstBlocksFormat()
    {
        var s = Edit(P("hello"), RichBlock.Heading(1, "world"));
        Select(s, 0, 2, 1, 3);
        Type(s, "X");
        Assert.Equal("Paragraph \"heXld\"", Show(s));
        Assert.Equal(RichSelection.Caret(0, 3), s.Selection);
    }

    [Fact]
    public void InsertingALineBreak_StartsANewParagraph()
    {
        var s = Edit(P("ab"));
        Caret(s, 0, 1);
        s.InsertText("x\ny");
        Assert.Equal("Paragraph \"ax\" | Paragraph \"yb\"", Show(s));
        Assert.Equal(RichSelection.Caret(1, 1), s.Selection);
    }

    [Fact]
    public void InACodeBlock_TextTakesNoMarks()
    {
        var s = Edit(Item(BlockKind.CodeBlock, "ab"));
        Caret(s, 0, 2);
        Assert.False(s.ToggleStyle(InlineStyle.Bold));
        Type(s, "c");
        Assert.Equal("CodeBlock \"abc\"", Show(s));
    }

    // ── Enter ──

    [Fact]
    public void Enter_SplitsTheBlock_AndItsSpans()
    {
        var s = Edit(P("hello world", new RichSpan(4, 3, Bold)));
        Caret(s, 0, 5);
        s.SplitBlock();
        Assert.Equal("Paragraph \"hello\" {4+1:Bold} | Paragraph \" world\" {0+2:Bold}", Show(s));
        Assert.Equal(RichSelection.Caret(1, 0), s.Selection);
    }

    [Theory]
    [InlineData(5, "h1 \"Title\" | Paragraph \"\"")]          // at the end: body text follows
    [InlineData(2, "h1 \"Ti\" | h1 \"tle\"")]                  // in the middle: two headings
    [InlineData(0, "Paragraph \"\" | h1 \"Title\"")]           // at the start: a line above
    public void Enter_InAHeading(int offset, string expected)
    {
        var s = Edit(RichBlock.Heading(1, "Title"));
        Caret(s, 0, offset);
        s.SplitBlock();
        Assert.Equal(expected, Show(s));
    }

    [Fact]
    public void Enter_OnAnEmptyItem_ComesOutALevel_ThenLeavesTheList()
    {
        var s = Edit(Item(BlockKind.BulletItem, "a"), Item(BlockKind.BulletItem, "", indent: 1));
        Caret(s, 1, 0);
        s.SplitBlock();
        Assert.Equal("BulletItem \"a\" | BulletItem \"\"", Show(s));
        s.SplitBlock();
        Assert.Equal("BulletItem \"a\" | Paragraph \"\"", Show(s));
    }

    [Fact]
    public void Enter_InATask_StartsAnUncheckedOne()
    {
        var s = Edit(Item(BlockKind.TaskItem, "done") with { Checked = true });
        Caret(s, 0, 4);
        s.SplitBlock();
        Assert.Equal("task[x] \"done\" | task[ ] \"\"", Show(s));
    }

    [Fact]
    public void Enter_InACodeBlock_IsALineBreak_AndOnAnEmptyLastLineLeavesIt()
    {
        var s = Edit(Item(BlockKind.CodeBlock, "x"));
        Caret(s, 0, 1);
        s.SplitBlock();
        Assert.Equal("CodeBlock \"x\n\"", Show(s));
        s.SplitBlock();
        Assert.Equal("CodeBlock \"x\" | Paragraph \"\"", Show(s));
        Assert.Equal(RichSelection.Caret(1, 0), s.Selection);
    }

    [Fact]
    public void ShiftEnter_IsALineBreakInsideTheBlock()
    {
        var s = Edit(P("ab"));
        Caret(s, 0, 1);
        s.InsertLineBreak();
        Assert.Equal("Paragraph \"a\nb\"", Show(s));
    }

    // ── Backspace and Delete ──

    [Fact]
    public void Backspace_AtAnItemsStart_DropsTheMarker_ThenOutdents_ThenJoins()
    {
        var s = Edit(P("a"), Item(BlockKind.BulletItem, "item"));
        Caret(s, 1, 0);

        s.DeleteBackward();
        Assert.Equal("Paragraph \"a\" | Paragraph>1 \"item\"", Show(s));   // the text stays where it was
        s.DeleteBackward();
        Assert.Equal("Paragraph \"a\" | Paragraph \"item\"", Show(s));
        s.DeleteBackward();
        Assert.Equal("Paragraph \"aitem\"", Show(s));
        Assert.Equal(RichSelection.Caret(0, 1), s.Selection);
    }

    [Fact]
    public void Backspace_JoinsIntoTheBlockBefore_InItsFormat_WithTheSpansMoved()
    {
        var s = Edit(RichBlock.Heading(2, "Head"), P("body", new RichSpan(0, 4, Bold)));
        Caret(s, 1, 0);
        s.DeleteBackward();
        Assert.Equal("h2 \"Headbody\" {4+4:Bold}", Show(s));
        Assert.Equal(RichSelection.Caret(0, 4), s.Selection);
    }

    [Fact]
    public void Backspace_TakesARuleAsAWhole()
    {
        var s = Edit(P("a"), Item(BlockKind.Rule, ""), P("b"));
        Caret(s, 2, 0);
        s.DeleteBackward();
        Assert.Equal("Paragraph \"a\" | Paragraph \"b\"", Show(s));
        Assert.Equal(RichSelection.Caret(1, 0), s.Selection);

        s = Edit(P("a"), Item(BlockKind.Rule, ""), P("b"));
        Caret(s, 1, 0);
        s.DeleteBackward();
        Assert.Equal("Paragraph \"a\" | Paragraph \"b\"", Show(s));
        Assert.Equal(RichSelection.Caret(0, 1), s.Selection);
    }

    [Fact]
    public void CtrlBackspace_DeletesAWord_AndBackspaceASurrogatePairWhole()
    {
        var s = Edit(P("one two"));
        Caret(s, 0, 7);
        s.DeleteBackward(word: true);
        Assert.Equal("Paragraph \"one \"", Show(s));

        s = Edit(P("a😀"));
        Caret(s, 0, 3);
        s.DeleteBackward();
        Assert.Equal("Paragraph \"a\"", Show(s));
    }

    [Fact]
    public void Delete_AtABlocksEnd_JoinsTheNext_OrFromAnEmptyBlockKeepsTheNextsFormat()
    {
        var s = Edit(P("ab"), P("cd"));
        Caret(s, 0, 2);
        s.DeleteForward();
        Assert.Equal("Paragraph \"abcd\"", Show(s));

        s = Edit(P(""), RichBlock.Heading(1, "T"));
        Caret(s, 0, 0);
        s.DeleteForward();
        Assert.Equal("h1 \"T\"", Show(s));
    }

    // ── Paste ──

    [Fact]
    public void PastingInlineContent_KeepsItsMarks()
    {
        var s = Edit(P("ab"));
        Caret(s, 0, 1);
        s.InsertFragment(new RichFragment([P("XY", new RichSpan(0, 2, Bold))]));
        Assert.Equal("Paragraph \"aXYb\" {1+2:Bold}", Show(s));
        Assert.Equal(RichSelection.Caret(0, 3), s.Selection);
    }

    [Fact]
    public void PastingBlocks_JoinsTheFirstToTheTextBefore_AndTheLastToTheTextAfter()
    {
        var s = Edit(P("abcd"));
        Caret(s, 0, 2);
        s.InsertFragment(new RichFragment([P("1"), Item(BlockKind.BulletItem, "2"), RichBlock.Heading(1, "3")]));
        Assert.Equal("Paragraph \"ab1\" | BulletItem \"2\" | h1 \"3cd\"", Show(s));
        Assert.Equal(RichSelection.Caret(2, 1), s.Selection);
    }

    [Fact]
    public void PastingIntoCode_PastesPlainLines()
    {
        var s = Edit(Item(BlockKind.CodeBlock, "ab"));
        Caret(s, 0, 1);
        s.InsertFragment(new RichFragment([P("x", new RichSpan(0, 1, Bold)), P("y")]));
        Assert.Equal("CodeBlock \"ax\nyb\"", Show(s));
    }

    [Fact]
    public void Paste_DropsWhatTheEditorsFeaturesDoNotAllow()
    {
        var s = new RichEditState(new RichDocument([P("")]), RichTextFeatures.Markdown);
        s.InsertFragment(new RichFragment([P("u", new RichSpan(0, 1,
            new RichMarks(InlineStyle.Bold | InlineStyle.Underline, Color: new(1, 2, 3, 255))))]));
        Assert.Equal("Paragraph \"u\" {0+1:Bold}", Show(s));
    }

    // ── Character formatting ──

    [Fact]
    public void ToggleStyle_TurnsOnWhenAnyPartLacksIt_AndOffWhenAllHaveIt()
    {
        var s = Edit(P("abcd", new RichSpan(0, 2, Bold)));
        s.SelectAll();
        s.ToggleStyle(InlineStyle.Bold);
        Assert.Equal("Paragraph \"abcd\" {0+4:Bold}", Show(s));
        s.ToggleStyle(InlineStyle.Bold);
        Assert.Equal("Paragraph \"abcd\"", Show(s));
    }

    [Fact]
    public void SelectionMarks_HoldWhatEverySelectedCharacterHas()
    {
        var s = Edit(P("abcd",
            new RichSpan(0, 2, new RichMarks(InlineStyle.Bold | InlineStyle.Italic)),
            new RichSpan(2, 2, Bold)));
        s.SelectAll();
        Assert.Equal(InlineStyle.Bold, s.SelectionMarks.Style);
    }

    [Fact]
    public void Links_AreSetOverASelection_AndRemovedWholeFromACaretInside()
    {
        var s = Edit(P("go here"));
        Select(s, 0, 3, 0, 7);
        s.SetLink("https://example.org");
        Assert.Equal("Paragraph \"go here\" {3+4:link=https://example.org}", Show(s));

        var range = s.LinkRangeAt(new RichPosition(0, 7));
        Assert.Equal((new RichPosition(0, 3), new RichPosition(0, 7), "https://example.org"), range);

        Caret(s, 0, 5);
        s.SetLink(null);
        Assert.Equal("Paragraph \"go here\"", Show(s));
    }

    [Fact]
    public void InsertLink_TypesLinkedText_AndWhatFollowsIsPlain()
    {
        var s = Edit(P("see "));
        Caret(s, 0, 4);
        s.InsertLink("docs", "u");
        Type(s, ".");
        Assert.Equal("Paragraph \"see docs.\" {4+4:link=u}", Show(s));
    }

    // ── Block formatting ──

    [Fact]
    public void SetBlockKind_AppliesToEveryBlockTheSelectionTouches()
    {
        var s = Edit(P("a"), P("b"), P("c"));
        Select(s, 0, 0, 1, 1);
        s.SetBlockKind(BlockKind.Heading, 2);
        Assert.Equal("h2 \"a\" | h2 \"b\" | Paragraph \"c\"", Show(s));
    }

    [Fact]
    public void ParagraphsMadeCode_AreOneBlock_AndCodeMadeParagraphsIsALineEach()
    {
        var s = Edit(P("x"), P("y"));
        Select(s, 0, 1, 1, 0);
        s.SetBlockKind(BlockKind.CodeBlock);
        Assert.Equal("CodeBlock \"x\ny\"", Show(s));
        Assert.Equal(new RichSelection(new(0, 1), new(0, 2)), s.Selection);

        s.SetBlockKind(BlockKind.Paragraph);
        Assert.Equal("Paragraph \"x\" | Paragraph \"y\"", Show(s));
        Assert.Equal(new RichSelection(new(0, 1), new(1, 0)), s.Selection);
    }

    [Fact]
    public void ToggleBlockKind_OnAListThatIsOne_MakesParagraphs()
    {
        var s = Edit(Item(BlockKind.BulletItem, "a"), Item(BlockKind.BulletItem, "b"));
        s.SelectAll();
        Assert.True(s.SelectionIs(BlockKind.BulletItem));
        s.ToggleBlockKind(BlockKind.BulletItem);
        Assert.Equal("Paragraph \"a\" | Paragraph \"b\"", Show(s));
    }

    [Fact]
    public void Indent_GoesNoDeeperThanOnePastTheBlockBefore()
    {
        var s = Edit(Item(BlockKind.BulletItem, "a"), Item(BlockKind.BulletItem, "b"));
        Caret(s, 1, 0);
        Assert.True(s.Indent());
        Assert.False(s.Indent());
        Assert.Equal("BulletItem \"a\" | BulletItem>1 \"b\"", Show(s));

        Caret(s, 0, 0);
        Assert.False(s.Indent());   // nothing above to nest under

        Caret(s, 1, 0);
        Assert.True(s.Outdent());
        Assert.Equal("BulletItem \"a\" | BulletItem \"b\"", Show(s));
    }

    [Fact]
    public void ToggleChecked_TicksATask()
    {
        var s = Edit(Item(BlockKind.TaskItem, "t"));
        s.ToggleChecked(0);
        Assert.Equal("task[x] \"t\"", Show(s));
    }

    [Fact]
    public void InsertRule_SplitsTheBlockAroundIt()
    {
        var s = Edit(P("abcd"));
        Caret(s, 0, 2);
        s.InsertRule();
        Assert.Equal("Paragraph \"ab\" | Rule \"\" | Paragraph \"cd\"", Show(s));
        Assert.Equal(RichSelection.Caret(2, 0), s.Selection);
    }

    [Fact]
    public void Alignment_IsAFeature()
    {
        var s = new RichEditState(new RichDocument([P("a")]), RichTextFeatures.Markdown);
        Assert.False(s.SetAlign(BlockAlign.Center));

        s = Edit(P("a"));
        Assert.True(s.SetAlign(BlockAlign.Center));
        Assert.Equal("Paragraph@Center \"a\"", Show(s));
    }

    // ── Undo ──

    [Fact]
    public void Typing_UndoesAWordAtATime()
    {
        var s = Edit(P(""));
        Type(s, "hello world");
        s.Undo();
        Assert.Equal("Paragraph \"hello \"", Show(s));
        s.Undo();
        Assert.Equal("Paragraph \"\"", Show(s));
        s.Redo();
        Assert.Equal("Paragraph \"hello \"", Show(s));
        Assert.Equal(RichSelection.Caret(0, 6), s.Selection);
    }

    [Fact]
    public void AFormat_IsAStepOfItsOwn_ThatPutsTheSelectionBack()
    {
        var s = Edit(P("abc"));
        s.SelectAll();
        s.ToggleStyle(InlineStyle.Bold);
        Caret(s, 0, 3);
        s.Undo();
        Assert.Equal("Paragraph \"abc\"", Show(s));
        Assert.Equal(new RichSelection(new(0, 0), new(0, 3)), s.Selection);
    }

    [Fact]
    public void Enter_IsAStepOfItsOwn()
    {
        var s = Edit(P(""));
        Type(s, "ab");
        s.SplitBlock();
        Type(s, "c");
        s.Undo();
        Assert.Equal("Paragraph \"ab\" | Paragraph \"\"", Show(s));
        s.Undo();
        Assert.Equal("Paragraph \"ab\"", Show(s));
        s.Undo();
        Assert.Equal("Paragraph \"\"", Show(s));
    }

    [Fact]
    public void Backspaces_UndoAsOneRun()
    {
        var s = Edit(P("abcd"));
        Caret(s, 0, 4);
        s.DeleteBackward();
        s.DeleteBackward();
        s.Undo();
        Assert.Equal("Paragraph \"abcd\"", Show(s));
    }

    [Fact]
    public void Load_ForgetsTheHistory()
    {
        var s = Edit(P(""));
        Type(s, "x");
        Assert.True(s.CanUndo);
        s.Load(new RichDocument([P("other")]));
        Assert.False(s.CanUndo);
        Assert.False(s.Undo());
    }

    // ── Markdown shortcuts ──

    [Theory]
    [InlineData("# x", "h1 \"x\"")]
    [InlineData("### x", "h3 \"x\"")]
    [InlineData("- x", "BulletItem \"x\"")]
    [InlineData("* x", "BulletItem \"x\"")]
    [InlineData("1. x", "NumberedItem \"x\"")]
    [InlineData("[ ] x", "task[ ] \"x\"")]
    [InlineData("[x] x", "task[x] \"x\"")]
    [InlineData("- [ ] x", "task[ ] \"x\"")]
    [InlineData("> x", "Quote \"x\"")]
    [InlineData("####### x", "Paragraph \"####### x\"")]   // seven is not a heading
    [InlineData("a - x", "Paragraph \"a - x\"")]           // only at the start of a block
    public void BlockShortcuts(string typed, string expected)
    {
        var s = Edit(P(""));
        Type(s, typed);
        Assert.Equal(expected, Show(s));
    }

    [Fact]
    public void AShortcut_UndoesToTheCharactersAsTyped()
    {
        var s = Edit(P(""));
        Type(s, "# ");
        Assert.Equal("h1 \"\"", Show(s));
        s.Undo();
        Assert.Equal("Paragraph \"# \"", Show(s));
        Assert.Equal(RichSelection.Caret(0, 2), s.Selection);
    }

    [Theory]
    [InlineData("a **b** c", "Paragraph \"a b c\" {2+1:Bold}")]
    [InlineData("a __b__ c", "Paragraph \"a b c\" {2+1:Bold}")]
    [InlineData("*it* x", "Paragraph \"it x\" {0+2:Italic}")]
    [InlineData("_it_ x", "Paragraph \"it x\" {0+2:Italic}")]
    [InlineData("run `ls` now", "Paragraph \"run ls now\" {4+2:Code}")]
    [InlineData("~~old~~ new", "Paragraph \"old new\" {0+3:Strike}")]
    [InlineData("snake_case_name", "Paragraph \"snake_case_name\"")]
    [InlineData("2 * 3 * 4", "Paragraph \"2 * 3 * 4\"")]
    [InlineData("** b**", "Paragraph \"** b**\"")]
    public void InlineShortcuts(string typed, string expected)
    {
        var s = Edit(P(""));
        Type(s, typed);
        Assert.Equal(expected, Show(s));
    }

    [Fact]
    public void FenceAndRule_TakeEffectOnEnter()
    {
        var s = Edit(P(""));
        Type(s, "```cs");
        s.SplitBlock();
        Assert.Equal("CodeBlock(cs) \"\"", Show(s));

        s = Edit(P(""));
        Type(s, "---");
        s.SplitBlock();
        Assert.Equal("Rule \"\" | Paragraph \"\"", Show(s));
        Assert.Equal(RichSelection.Caret(1, 0), s.Selection);
    }

    [Fact]
    public void Shortcuts_RespectTheFeatures()
    {
        var s = new RichEditState(new RichDocument([P("")]), RichTextFeatures.All & ~RichTextFeatures.Headings);
        Type(s, "# x");
        Assert.Equal("Paragraph \"# x\"", Show(s));

        s = new RichEditState(new RichDocument([P("")]), RichTextFeatures.All & ~RichTextFeatures.MarkdownShortcuts);
        Type(s, "**b** - x");
        Assert.Equal("Paragraph \"**b** - x\"", Show(s));
    }

    [Fact]
    public void AMarkdownEditor_OffersNoUnderlineOrColour()
    {
        var s = new RichEditState(new RichDocument([P("abc")]), RichTextFeatures.Markdown);
        s.SelectAll();
        Assert.False(s.ToggleStyle(InlineStyle.Underline));
        Assert.False(s.SetColor(new(255, 0, 0, 255)));
        Assert.True(s.ToggleStyle(InlineStyle.Bold));
    }

    // ── Events ──

    [Fact]
    public void Edits_RaiseDocumentChanged_AndCaretMovesOnlySelectionChanged()
    {
        var s = Edit(P("ab"));
        int docs = 0, sels = 0;
        s.DocumentChanged += () => docs++;
        s.SelectionChanged += () => sels++;

        Caret(s, 0, 1);
        Assert.Equal((0, 1), (docs, sels));
        Type(s, "x");
        Assert.Equal((1, 2), (docs, sels));
    }
}
