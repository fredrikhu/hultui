using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.RichText;
using HultUI.Rendering.Skia;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Tables and pictures in the rich text editor (docs/plans/16-rich-text-editor.md, stage 9): the
/// grid's geometry, the keys that move through it, and pictures drawn from the app's resolver.
/// The editing rules are <c>RichTableTests</c>; these check what needs layout.
/// </summary>
public class RichTextTableTests
{
    private static RichBlock P(string text) => RichBlock.Paragraph(text);

    private static RichBlock[] Table(int columns, params string[] texts) =>
        [.. texts.Select((t, i) => RichBlock.Cell(i / columns, i % columns, t))];

    private sealed class Rig : IDisposable
    {
        public required HeadlessSession Session;
        public required RichTextEditorElement Editor;
        public HeadlessWindow Window => Session.Window;
        public RichEditState State => Editor.State;
        public void Dispose() => Session.Dispose();

        public Rect Caret(int block, int offset = 0)
        {
            var caret = Editor.CaretBounds(new RichPosition(block, offset));
            Assert.True(caret.Height > 0, "the editor has not been drawn");
            return caret;
        }

        public void Press(KeyCode key, KeyModifiers modifiers = KeyModifiers.None)
        {
            Window.PressKey(key, modifiers);
            Session.PumpFrame();
        }
    }

    private static Rig Build(RichDocument doc, Action<RichTextEditorElement>? configure = null, float height = 300)
    {
        var session = HeadlessSession.Create(width: 600, height: 400);
        var editor = RichTextEditor.New(doc).Width(400).Height(height);
        configure?.Invoke(editor);
        session.Window.SetRoot(Box.New().Width(600).Height(400).Children(editor, Button.New("Next")));
        session.PumpFrame();
        session.Window.Click(editor);
        session.PumpFrame();
        return new Rig { Session = session, Editor = editor };
    }

    // ── The grid ──

    [Fact]
    public void ARow_SharesALine_AndAWideCellWidensItsColumn()
    {
        using var rig = Build(new RichDocument([.. Table(3, "#", "Description of the change", "x", "1", "a", "b")]));
        var h0 = rig.Caret(0);
        var h1 = rig.Caret(1);
        var h2 = rig.Caret(2);
        var r1 = rig.Caret(3);

        Assert.Equal(h0.Y, h1.Y);
        Assert.Equal(h1.Y, h2.Y);
        Assert.True(r1.Y > h0.Y + h0.Height, "the second row is below the first");
        Assert.Equal(h0.X, r1.X);
        // The long header's column takes more of the width than either short one.
        Assert.True(h2.X - h1.X > h1.X - h0.X);
    }

    [Fact]
    public void AClick_LandsInTheCellUnderIt()
    {
        using var rig = Build(new RichDocument([.. Table(2, "aa", "bb", "cc", "dd")]));
        var target = rig.Caret(3, 1);
        rig.Window.Click(target.X, target.Y + target.Height / 2);
        rig.Session.PumpFrame();
        Assert.Equal(RichSelection.Caret(3, 1), rig.State.Selection);
    }

    [Fact]
    public void ARowGrows_WithItsTallestCell()
    {
        using var rig = Build(new RichDocument([.. Table(2, "a", "b"), P("after")]));
        float before = rig.Caret(2).Y;
        rig.State.Select(RichSelection.Caret(1, 1));
        rig.State.InsertLineBreak();
        rig.State.InsertText("second line");
        rig.Session.PumpFrame();
        Assert.True(rig.Caret(2).Y > before, "the paragraph after the table moved down");
        Assert.Equal(rig.Caret(0).Y, rig.Caret(1).Y);
    }

    // ── Keys ──

    [Fact]
    public void UpAndDown_MoveByRow_InTheSameColumn_AndInAndOutOfTheTable()
    {
        using var rig = Build(new RichDocument([P("above"), .. Table(2, "a", "b", "c", "d"), P("below")]));
        // Into the second column from above: the cell under the caret's x.
        var b = rig.Caret(2);
        rig.State.Select(RichSelection.Caret(0, 0));
        var aboveFirst = rig.Caret(0, 0);
        Assert.True(b.X > aboveFirst.X);

        rig.State.Select(RichSelection.Caret(2, 1));
        rig.Press(KeyCode.Down);
        Assert.Equal(4, rig.State.Selection.Focus.Block);
        rig.Press(KeyCode.Down);
        Assert.Equal(5, rig.State.Selection.Focus.Block);
        rig.Press(KeyCode.Up);
        Assert.Equal(4, rig.State.Selection.Focus.Block);
        rig.Press(KeyCode.Up);
        rig.Press(KeyCode.Up);
        Assert.Equal(0, rig.State.Selection.Focus.Block);
    }

    [Fact]
    public void Tab_MovesThroughTheCells_AndEnterGoesDown()
    {
        using var rig = Build(new RichDocument([.. Table(2, "a", "b")]));
        rig.State.Select(RichSelection.Caret(0, 1));
        rig.Press(KeyCode.Tab);
        Assert.Equal(RichSelection.Caret(1, 1), rig.State.Selection);
        rig.Press(KeyCode.Tab);
        Assert.Equal(4, rig.State.Document.BlockCount);
        Assert.Equal(RichSelection.Caret(2, 0), rig.State.Selection);
        rig.Window.TypeText("c");
        rig.Press(KeyCode.Tab, KeyModifiers.Shift);
        Assert.Equal(RichSelection.Caret(1, 1), rig.State.Selection);

        rig.Press(KeyCode.Enter);
        Assert.Equal(RichSelection.Caret(3, 0), rig.State.Selection);
        rig.Press(KeyCode.Enter);
        Assert.Equal("cell[0,0] \"a\" | cell[0,1] \"b\" | cell[1,0] \"c\" | cell[1,1] \"\" | Paragraph \"\"",
            string.Join(" | ", rig.State.Document.Blocks));
    }

    [Fact]
    public void ARightClick_MovesTheCaretUnderIt_AndTheMenuOffersTheTablesShape()
    {
        using var rig = Build(new RichDocument([P("before"), .. Table(2, "a", "b")]));
        rig.State.Select(RichSelection.Caret(0, 0));
        var cell = rig.Caret(2, 1);
        rig.Window.Click(cell.X, cell.Y + cell.Height / 2, MouseButton.Right);
        rig.Session.PumpFrame();
        Assert.Equal(RichSelection.Caret(2, 1), rig.State.Selection);
        var items = rig.Editor.ContextMenuItems!();
        var insert = items.First(i => i.Label == "Insert row below");
        insert.Action!();
        Assert.Equal(new RichTableRange(1, 2, 2), rig.State.CurrentTable);
        Assert.Contains(items, i => i.Label == "Delete table");
    }

    [Fact]
    public void TheToolbar_InsertsATable_AndOffersNoBlockFormatsInsideOne()
    {
        var editorRef = new Ref<RichTextEditorElement>();
        int rebuilds = 0;
        var toolbar = new RichTextToolbar(editorRef, () => rebuilds++);
        using var rig = Build(new RichDocument([P("")]), configure: e => e.WithRef(editorRef));
        Assert.True(RichTextToolbar.Execute(rig.Editor, RichTextCommand.Table));
        Assert.Equal(new RichTableRange(0, 3, 3), rig.State.CurrentTable);

        var bar = toolbar.Build();
        var buttons = bar.Children.OfType<ButtonElement>().ToList();
        Assert.False(buttons.First(b => b.AccessibleName == "Heading 1").IsEnabled);
        Assert.False(buttons.First(b => b.AccessibleName == "Picture").IsEnabled);
        Assert.True(buttons.First(b => b.AccessibleName == "Bold").IsEnabled);
    }

    [Fact]
    public void ThePictureEditor_InsertsAPicture_AndChangesTheOneSelected()
    {
        var editorRef = new Ref<RichTextEditorElement>();
        var toolbar = new RichTextToolbar(editorRef, () => { });
        using var session = HeadlessSession.Create(width: 600, height: 400);
        var editor = RichTextEditor.New(RichDocument.Empty).Width(400).Height(200);
        editor.WithRef(editorRef);
        // The toolbar is on screen: its editor opens against the Picture button.
        session.Window.SetRoot(Box.New().Width(600).Height(400).Children(toolbar.Build(), editor));
        session.PumpFrame();
        var window = session.Window;

        toolbar.OpenPictureEditor();
        session.PumpFrames(2);
        Assert.True(toolbar.IsPictureEditorOpen);
        window.FindByAutomationId("richtext-picture-source")!.Click();
        window.TypeText("cat.png");
        window.FindByAutomationId("richtext-picture-alt")!.Click();
        window.TypeText("A cat");
        window.PressKey(KeyCode.Enter);
        session.PumpFrames(2);

        Assert.False(toolbar.IsPictureEditorOpen);
        Assert.Equal((0, new RichImage("cat.png", "A cat")), editor.State.CurrentImage!.Value);

        toolbar.OpenPictureEditor();
        session.PumpFrames(2);
        window.FindByAutomationId("richtext-picture-alt")!.Click();
        window.PressKey(KeyCode.A, KeyModifiers.Ctrl);
        window.TypeText("A black cat");
        window.FindByAutomationId("richtext-picture-apply")!.Click();
        session.PumpFrames(2);
        Assert.Equal("A black cat", editor.State.Document[0].Image!.Alt);
        Assert.Equal(2, editor.State.Document.BlockCount);
    }

    // ── Layout after an edit ──

    // After an edit only the blocks it replaced are laid out again; everything else keeps its
    // layout and is moved. Whatever the edit, every block must then sit where a layout from
    // scratch puts it: a fresh editor on the same document, beside it, is the reference.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AfterAnyEdit_EveryBlockSitsWhereAFreshLayoutPutsIt(int seed)
    {
        var rng = new Random(seed);
        var start = new RichDocument([
            RichBlock.Heading(1, "Title"), P("intro"),
            RichBlock.Of(BlockKind.NumberedItem, "one"), RichBlock.Of(BlockKind.NumberedItem, "two"),
            .. Table(3, "a", "b", "c", "d", "e", "f"),
            RichBlock.Picture(new RichImage("p")), RichBlock.Of(BlockKind.Quote, "quoted"),
            .. Table(2, "x", "y"), P("end"),
        ]);
        using var session = HeadlessSession.Create(width: 900, height: 2400);
        var editor = RichTextEditor.New(start).Width(400).Height(2300);
        var row = Flex.Row(gap: 20).Width(900).Height(2400).Children(editor, Box.New());
        session.Window.SetRoot(row);
        session.PumpFrame();

        for (int step = 0; step < 300; step++)
        {
            var s = editor.State;
            var doc = s.Document;
            RichPosition Any()
            {
                int b = rng.Next(doc.BlockCount);
                return new RichPosition(b, rng.Next(doc[b].Text.Length + 1));
            }
            s.Select(rng.Next(4) == 0 ? new RichSelection(Any(), Any()) : RichSelection.Caret(Any()));
            switch (rng.Next(12))
            {
                case 0: case 1: s.InsertText(rng.Next(3) == 0 ? "a much longer run of words that wraps the line " : "q"); break;
                case 2: s.SplitBlock(); break;
                case 3: s.DeleteBackward(); break;
                case 4: s.DeleteForward(); break;
                case 5: s.InsertRow(rng.Next(2) == 0); break;
                case 6: s.InsertColumn(); break;
                case 7: s.InsertTable(2, 2); break;
                case 8: s.InsertImage(new RichImage("r")); break;
                case 9: s.ToggleBlockKind(BlockKind.NumberedItem); break;
                case 10: s.ToggleBlockKind(BlockKind.Heading, 2); break;
                case 11: s.Undo(); break;
            }
            // The reference: a new editor, laid out from scratch.
            var fresh = RichTextEditor.New(s.Document).Width(400).Height(2300);
            row.ReplaceChildren(editor, fresh);
            session.PumpFrame();

            var a = editor.Layout.AbsoluteContentBox;
            var b = fresh.Layout.AbsoluteContentBox;
            for (int i = 0; i < s.Document.BlockCount; i++)
                foreach (int offset in new[] { 0, s.Document[i].Text.Length })
                {
                    var p = new RichPosition(i, offset);
                    var got = editor.CaretBounds(p);
                    var want = fresh.CaretBounds(p);
                    Assert.True(Math.Abs(got.X - a.X - (want.X - b.X)) < 0.01f && Math.Abs(got.Y - a.Y - (want.Y - b.Y)) < 0.01f
                        && Math.Abs(got.Height - want.Height) < 0.01f,
                        $"step {step}: block {i} offset {offset} at {got} in the edited editor, {want} laid out afresh\n{s.Document}");
                }
        }
    }

    // ── Pictures ──

    private static SkiaImageSource Red(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 3] = 255;
        }
        return SkiaImageSource.FromPixels(pixels, width, height, width * 4, PixelFormat.Rgba8888);
    }

    [Fact]
    public void APicture_IsDrawnFromTheResolver_AtItsOwnSize()
    {
        var red = Red(120, 60);
        using var rig = Build(new RichDocument([RichBlock.Picture(new RichImage("red.png", "Red")), P("after")]),
            e => e.Images(src => src == "red.png" ? red : null));
        var after = rig.Caret(1);
        var top = rig.Caret(0);
        // The picture's height, and the block gap under it.
        Assert.InRange(after.Y - top.Y, 60, 60 + 16);

        using var shot = rig.Window.CapturePixels();
        using var bitmap = SkiaSharp.SKBitmap.FromImage(shot);
        var c = bitmap.GetPixel((int)(top.X + 60), (int)(top.Y + 30));
        Assert.Equal((byte)255, c.Red);
        Assert.True(c.Green < 40 && c.Blue < 40, $"the picture's pixels, not {c}");
    }

    [Fact]
    public void APictureThatArrives_TakesItsPlace_OnTheNextBuild()
    {
        SkiaImageSource? loaded = null;
        using var rig = Build(new RichDocument([RichBlock.Picture(new RichImage("late.png")), P("after")]),
            e => e.Images(_ => loaded));
        float placeholder = rig.Caret(1).Y;

        loaded = Red(100, 200);
        rig.Editor.ImageResolver = rig.Editor.ImageResolver;   // what a rebuild's PatchFrom does
        rig.Session.PumpFrame();
        Assert.True(rig.Caret(1).Y > placeholder + 100, "the paragraph moved down under the picture");
    }

    [Fact]
    public void AWidePicture_IsScaledDownToFit()
    {
        var wide = Red(1600, 400);
        using var rig = Build(new RichDocument([RichBlock.Picture(new RichImage("w")), P("after")]), e => e.Images(_ => wide));
        float height = rig.Caret(1).Y - rig.Caret(0).Y;
        // The content box is under 400 wide, so a quarter as tall at most, plus the gap.
        Assert.InRange(height, 80, 100 + 16);
    }

    [Fact]
    public void ClickingAPicture_SelectsIt_AndBackspaceRemovesIt()
    {
        var red = Red(80, 40);
        using var rig = Build(new RichDocument([P("a"), RichBlock.Picture(new RichImage("r")), P("b")]), e => e.Images(_ => red));
        var pic = rig.Caret(1);
        rig.Window.Click(pic.X + 40, pic.Y + 20);
        rig.Session.PumpFrame();
        Assert.Equal(RichSelection.Caret(1, 0), rig.State.Selection);

        rig.Press(KeyCode.Backspace);
        Assert.Equal("a\nb", rig.State.Document.ToPlainText());
    }

    // ── Long tables ──

    [Fact]
    public void ALongTable_DrawsWhatIsOnScreen_AtItsBottomAsAtItsTop()
    {
        // 300 rows of 10 short cells, as a user's example held: a frame at the bottom drew no
        // more than one at the top — no cell, and no grid line, of the rows above the view.
        var cells = new List<RichBlock>(3_000);
        for (int r = 0; r < 300; r++)
            for (int c = 0; c < 10; c++)
                cells.Add(RichBlock.Cell(r, c, r == 0 ? $"Column {c + 1}" : $"cell {r}.{c}"));
        using var session = HeadlessSession.Create(1200, 800, options: new HeadlessOptions { RecordDrawCalls = true });
        var editor = RichTextEditor.New(new RichDocument([P("Before the table"), .. cells])).FlexGrow(1);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Padding(8).Children(editor));
        session.PumpFrame();

        (int Paragraphs, int Rects) Frame(float depth)
        {
            editor.ScrollOffsetY = float.MaxValue;
            editor.ScrollOffsetY *= depth;
            editor.MarkPaintDirty();
            session.Window.RenderLog.Clear();
            session.PumpFrame();
            var log = session.Window.RenderLog;
            return (log.Named("DrawParagraph").Count(), log.Named("DrawRect").Count());
        }

        var top = Frame(0);
        var middle = Frame(0.5f);
        var bottom = Frame(1);
        // The paragraph above the table is on screen at the top only; a row is ten cells.
        Assert.InRange(middle.Paragraphs, top.Paragraphs - 20, top.Paragraphs + 20);
        Assert.InRange(bottom.Paragraphs, top.Paragraphs - 20, top.Paragraphs + 20);
        // The grid: the header's tint, a line under each row on screen and the 11 between and
        // beside the columns — not one for each of the table's 301 row edges.
        Assert.InRange(top.Rects, 1, 60);
        Assert.InRange(middle.Rects, 1, 60);
        Assert.InRange(bottom.Rects, 1, 60);
    }
}
