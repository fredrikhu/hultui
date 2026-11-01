using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.RichText;
using HultUI.Headless;

namespace PerfSuite;

static partial class Scenarios
{
    // The rich text editor (docs/plans/16-rich-text-editor.md, stage 8) on documents far longer
    // than a form field holds: opening one, a keystroke, Enter, the wheel, a resize, and a long
    // paragraph's many spans. An edit replaces a run of blocks and keeps the rest, so a keystroke
    // ought to cost about what one block costs, not what the document does.
    private static IEnumerable<Scenario> RichTextScenarios()
    {
        yield return new Scenario("richtext/open 5,000 blocks",
            "Mount an editor on a 5,000-block document: every block laid out once, the visible ones drawn",
            ctx =>
            {
                var doc = LongDocument(ctx.Rng, 5_000);
                RichTextEditorElement? editor = null;
                return new Op
                {
                    Reset = _ => ctx.Window.SetRoot(Box.New().FlexGrow(1)),
                    Run = _ =>
                    {
                        editor = RichTextEditor.New(doc).FlexGrow(1);
                        ctx.Window.SetRoot(Flex.Column().FlexGrow(1).Padding(8).Children(editor));
                    },
                    Check = _ => editor is { Document.BlockCount: 5_000 } && editor.ScrollOffsetY == 0 && editor.CanAutoScroll
                        ? null : "the editor did not lay the document out",
                };
            });

        yield return new Scenario("richtext/type in 5,000 blocks",
            "Type one character in the middle of a 5,000-block document",
            ctx =>
            {
                var editor = MountEditor(ctx, LongDocument(ctx.Rng, 5_000));
                var at = new RichPosition(2_501, 3);
                editor.State.Select(RichSelection.Caret(at));
                ctx.Pump();
                int initial = editor.Document[at.Block].Text.Length;
                return new Op
                {
                    Run = step => ctx.Window.TypeText(((char)('a' + step % 26)).ToString()),
                    Check = step => editor.Document[at.Block].Text.Length == initial + step + 1
                        ? null : "the typed character did not reach the block",
                };
            });

        yield return new Scenario("richtext/enter in 5,000 blocks",
            "Split a block in the middle of a 5,000-block document: every block below moves down",
            ctx =>
            {
                var editor = MountEditor(ctx, LongDocument(ctx.Rng, 5_000));
                var at = new RichPosition(2_501, 3);
                int count = editor.Document.BlockCount;
                return new Op
                {
                    // Joined back untimed, so every step splits the same block.
                    Reset = _ =>
                    {
                        if (editor.Document.BlockCount > count)
                        {
                            editor.State.Select(RichSelection.Caret(at.Block + 1, 0));
                            editor.State.DeleteBackward();
                        }
                        editor.State.Select(RichSelection.Caret(at));
                    },
                    Run = _ => ctx.Window.PressKey(KeyCode.Enter),
                    Check = _ => editor.Document.BlockCount == count + 1 ? null : "Enter did not split the block",
                };
            });

        yield return new Scenario("richtext/wheel through 5,000 blocks",
            "Wheel through a 5,000-block document: only the blocks on screen are drawn",
            ctx =>
            {
                var editor = MountEditor(ctx, LongDocument(ctx.Rng, 5_000));
                ctx.Window.Wheel(500, 380, 0, -400);
                ctx.Pump();
                float before = 0;
                return new Op
                {
                    Reset = _ => before = editor.ScrollOffsetY,
                    Run = step => ctx.Window.Wheel(500, 380, 0, WheelDirection(step) * 3),
                    Check = _ => editor.ScrollOffsetY != before ? null : "the wheel did not scroll the editor",
                };
            });

        yield return new Scenario("richtext/select all 5,000 blocks",
            "Ctrl+A in a 5,000-block document: the selection drawn over what is on screen",
            ctx =>
            {
                var editor = MountEditor(ctx, LongDocument(ctx.Rng, 5_000));
                return new Op
                {
                    Reset = _ => editor.State.Select(RichSelection.Caret(0, 0)),
                    Run = _ => ctx.Window.PressKey(KeyCode.A, KeyModifiers.Ctrl),
                    Check = _ => editor.State.Selection.End == editor.Document.End ? null : "Ctrl+A did not select the document",
                };
            });

        yield return new Scenario("richtext/resize 1,000 blocks",
            "Drag the window narrower with a 1,000-block document open: every block re-wraps",
            ctx =>
            {
                MountEditor(ctx, LongDocument(ctx.Rng, 1_000));
                return new Op { Run = step => ctx.Window.SetClientSize(Ui.DragWidth(step), 760) };
            });

        yield return new Scenario("richtext/type in a paragraph of 600 spans",
            "Type in a 4,000-character paragraph whose every few words carry a mark: the block re-shapes with all its spans",
            ctx =>
            {
                var editor = MountEditor(ctx, new RichDocument([StyledParagraph(ctx.Rng, 600)]));
                editor.State.Select(RichSelection.Caret(0, editor.Document[0].Text.Length / 2));
                ctx.Pump();
                int initial = editor.Document[0].Text.Length;
                return new Op
                {
                    Run = step => ctx.Window.TypeText(((char)('a' + step % 26)).ToString()),
                    Check = step => editor.Document[0].Text.Length == initial + step + 1 ? null : "the typed character did not reach the paragraph",
                };
            });

        yield return new Scenario("richtext/type in a 40x5 table",
            "Type in a cell of a 200-cell table: the table is laid out again as a unit",
            ctx =>
            {
                var cells = new List<RichBlock>();
                for (int r = 0; r < 40; r++)
                    for (int c = 0; c < 5; c++)
                        cells.Add(RichBlock.Cell(r, c, r == 0 ? Ui.Label(ctx.Rng) : Ui.Paragraph(ctx.Rng, 1 + (r * 5 + c) % 6)));
                var editor = MountEditor(ctx, new RichDocument([RichBlock.Paragraph("Before the table"), .. cells]));
                int cell = 1 + 20 * 5 + 2;
                editor.State.Select(RichSelection.Caret(cell, editor.Document[cell].Text.Length));
                ctx.Pump();
                int initial = editor.Document[cell].Text.Length;
                return new Op
                {
                    Run = step => ctx.Window.TypeText(((char)('a' + step % 26)).ToString()),
                    Check = step => editor.Document[cell].Text.Length == initial + step + 1 ? null : "the typed character did not reach the cell",
                };
            });

        // A 300-row, 10-column table of short cells, as a user's example held: scrolled to its
        // bottom a frame ought to cost what one at its top does, so a gap between the twins is
        // work that grows with the rows above the view.
        foreach (var (where, bottom) in new[] { ("top", false), ("bottom", true) })
        {
            yield return new Scenario($"richtext/wheel at the {where} of a 300x10 table",
                $"Wheel at the {where} of a 3,000-cell table: only the rows on screen are drawn",
                ctx =>
                {
                    var editor = MountTable(ctx);
                    // At the bottom, the walk's first twenty steps go down and end at the end.
                    if (bottom)
                    {
                        editor.ScrollOffsetY = float.MaxValue;
                        for (int step = 20; step < 40; step++) ctx.Window.Wheel(500, 380, 0, WheelDirection(step) * 3);
                    }
                    ctx.Pump();
                    float before = 0;
                    return new Op
                    {
                        Reset = _ => before = editor.ScrollOffsetY,
                        Run = step => ctx.Window.Wheel(500, 380, 0, WheelDirection(step) * 3),
                        Check = _ => editor.ScrollOffsetY != before ? null : "the wheel did not scroll the editor",
                    };
                }) { Width = 1200, Height = 800 };

            yield return new Scenario($"richtext/redraw at the {where} of a 300x10 table",
                $"Draw a 3,000-cell table scrolled to its {where} again, nothing changed",
                ctx =>
                {
                    var editor = MountTable(ctx);
                    if (bottom) editor.ScrollOffsetY = float.MaxValue;
                    ctx.Pump();
                    return new Op { Run = _ => editor.MarkPaintDirty() };
                }) { Width = 1200, Height = 800 };
        }

        yield return new Scenario("richtext/type in an auto-height editor",
            "Type in a 1,000-block editor that grows with its document inside a scroll view: measured, then drawn",
            ctx =>
            {
                var editor = RichTextEditor.New(LongDocument(ctx.Rng, 1_000)).AutoHeight(120);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(
                    Flex.Column().FlexShrink(0).Padding(8).Children(editor)));
                // Taller than the window: a click at its centre would land off screen.
                editor.Focus();
                editor.State.Select(RichSelection.Caret(500, 3));
                ctx.Pump();
                int initial = editor.Document[500].Text.Length;
                return new Op
                {
                    Run = step => ctx.Window.TypeText(((char)('a' + step % 26)).ToString()),
                    Check = step => editor.Document[500].Text.Length == initial + step + 1 ? null : "the typed character did not reach the block",
                };
            });

        yield return new Scenario("a11y/type in rich text, bridge on",
            "Type in a 5,000-block document while an accessibility client listens: the value changes with every key",
            ctx =>
            {
                var bridge = ctx.Window.InstallAccessibilityBridge();
                var editor = MountEditor(ctx, LongDocument(ctx.Rng, 5_000));
                editor.State.Select(RichSelection.Caret(2_501, 3));
                ctx.Pump();
                return new Op
                {
                    Reset = _ => bridge.ClearDiffs(),
                    Run = step => ctx.Window.TypeText(((char)('a' + step % 26)).ToString()),
                    Check = _ => bridge.Diffs.Count > 0 ? null : "the bridge received no change",
                };
            });

        yield return new Scenario("a11y/type in a rich text editor inside a VirtualList row, bridge on",
            "Type and erase in a comment card that is a row of a 150-row list, under its formatting bar, while an accessibility client listens: what a key makes again, and the trees it builds",
            ctx =>
            {
                ctx.Window.InstallAccessibilityBridge();
                return TypeInListRow(ctx);
            });

        yield return new Scenario("a11y/type in a rich text editor inside a VirtualList row, UIA",
            "The same comment card with the UIA bridge: what a key makes again, and the trees it builds",
            ctx =>
            {
                UiaRig.Install(ctx.Window);
                return TypeInListRow(ctx);
            });
    }

    /// <summary>Each step a character or an erased one in a comment card that is a row of a list,
    /// twenty of each, so the paragraph stays one length. A reader listens while the key is handled
    /// (<see cref="AccessibilityClients.Force"/>, put back after it), as for a caret walk.</summary>
    private static Op TypeInListRow(Ctx ctx)
    {
        var card = new EditorInListRow(ctx.Rng);
        ctx.Mount(card.Mount());
        ctx.Pump(2);
        ctx.Window.Click(card.Editor!);
        ctx.Pump(2);
        int typed = 0;
        var walk = TypeWalk(ctx, () => typed, n => typed = n);
        return new Op
        {
            Run = walk.Run,
            Figure = walk.Figure,
            FigureUnit = walk.FigureUnit,
            Check = _ => card.Editor is not { } editor ? "the card's row is gone"
                : !ReferenceEquals(ctx.Window.Window.FocusedElement, editor) ? "the editor lost the keyboard"
                : editor.Document[0].Text.Length != typed ? $"the editor holds {editor.Document[0].Text.Length} characters, {typed} typed"
                : null,
        };
    }

    /// <summary>Twenty characters and twenty erased; the figure is the accessibility trees a key
    /// builds.</summary>
    private static Op TypeWalk(Ctx ctx, Func<int> typed, Action<int> setTyped)
    {
        int treesBefore = 0;
        return new Op
        {
            Run = step =>
            {
                treesBefore = ctx.Window.AccessibilityTreesBuilt;
                var was = AccessibilityClients.Force;
                AccessibilityClients.Force = true;
                try
                {
                    if (step % 40 < 20) { ctx.Window.TypeText(((char)('a' + step % 26)).ToString()); setTyped(typed() + 1); }
                    else { ctx.Window.PressKey(HultUI.Core.Events.KeyCode.Backspace); setTyped(typed() - 1); }
                }
                finally { AccessibilityClients.Force = was; }
            },
            Figure = () => ctx.Window.AccessibilityTreesBuilt - treesBefore,
            FigureUnit = "a11y trees/key",
        };
    }

    // A focused editor filling the window.
    private static RichTextEditorElement MountEditor(Ctx ctx, RichDocument doc)
    {
        var editor = RichTextEditor.New(doc).FlexGrow(1);
        ctx.Mount(Flex.Column().FlexGrow(1).Padding(8).Children(editor));
        ctx.Window.Click(editor);
        ctx.Pump();
        return editor;
    }

    // A focused editor holding a paragraph and a 300x10 table of one- and two-word cells under a
    // header row.
    private static RichTextEditorElement MountTable(Ctx ctx)
    {
        var cells = new List<RichBlock>(3_000);
        for (int r = 0; r < 300; r++)
            for (int c = 0; c < 10; c++)
                cells.Add(RichBlock.Cell(r, c, r == 0 ? $"Column {c + 1}" : Ui.Paragraph(ctx.Rng, 1 + (r + c) % 2)));
        return MountEditor(ctx, new RichDocument([RichBlock.Paragraph("Before the table"), .. cells]));
    }

    // A long document shaped like real notes: headings every twenty blocks, paragraphs with a
    // bold word and a link, lists, a quote and a code block in each stretch.
    private static RichDocument LongDocument(Random rng, int count)
    {
        var bold = new RichMarks(InlineStyle.Bold);
        var link = new RichMarks(Link: "https://example.org");
        var blocks = new List<RichBlock>(count);
        for (int i = 0; i < count; i++)
        {
            string text = Ui.Paragraph(rng, 6 + rng.Next(40));
            int word = text.IndexOf(' ');
            blocks.Add((i % 20) switch
            {
                0 => RichBlock.Heading(2, Ui.Label(rng)),
                5 or 6 or 7 => RichBlock.Of(BlockKind.BulletItem, text, indent: i % 20 - 5 == 2 ? 1 : 0),
                10 or 11 => RichBlock.Of(BlockKind.NumberedItem, text),
                14 => RichBlock.Of(BlockKind.Quote, text),
                17 => RichBlock.Of(BlockKind.CodeBlock, string.Join('\n', text.Split(' ').Chunk(4).Select(w => string.Join(' ', w)))),
                _ => RichBlock.Paragraph(text,
                    new RichSpan(0, Math.Max(1, word), bold),
                    new RichSpan(Math.Max(1, word) + 1, Math.Min(10, text.Length - Math.Max(1, word) - 1), link)),
            });
        }
        return new RichDocument(blocks);
    }

    // One paragraph of about 700 words, a mark on every few: bold, italic, code, a link.
    private static RichBlock StyledParagraph(Random rng, int spans)
    {
        var marks = new[]
        {
            new RichMarks(InlineStyle.Bold), new RichMarks(InlineStyle.Italic),
            new RichMarks(InlineStyle.Code), new RichMarks(Link: "https://example.org"),
        };
        var text = new System.Text.StringBuilder();
        var list = new List<RichSpan>();
        for (int i = 0; i < spans; i++)
        {
            if (text.Length > 0) text.Append(' ');
            string plain = Ui.Paragraph(rng, 2);
            text.Append(plain).Append(' ');
            string marked = Ui.Label(rng).Split(' ')[0];
            list.Add(new RichSpan(text.Length, marked.Length, marks[i % marks.Length]));
            text.Append(marked);
        }
        return RichBlock.Paragraph(text.ToString(), [.. list]);
    }

    /// <summary>
    /// A comment being written among a diff's rows, as Junction's review composer is: a card
    /// holding a formatting bar and a rich text editor of fixed height is row 12 of 150 of a
    /// <see cref="VirtualListElement{T}"/> on item rows. An edit or a caret move rebuilds the
    /// component that holds the list only when the bar no longer shows the editor.
    /// </summary>
    private sealed class EditorInListRow(Random rng) : Component
    {
        private const int CardRow = 12, RowCount = 150;
        private const float LineHeight = 22f, BarHeight = 28f, EditorHeight = 72f, CardHeight = 150f;

        private readonly List<int> _rows = Enumerable.Range(0, RowCount).ToList();
        private readonly string[] _lines = Enumerable.Range(0, RowCount).Select(_ => Ui.Paragraph(rng, 6)).ToArray();
        private readonly Ref<RichTextEditorElement> _editor = new();
        private readonly Ref<VirtualListElement<int>> _list = new();
        private readonly Signal<int> _revision = new(0);
        private RichTextToolbar? _bar;
        private RichDocument _document = RichDocument.Empty;
        private bool _edited;

        public RichTextEditorElement? Editor => _editor.Current;

        public override Element Build()
        {
            _ = _revision.Value;
            if (_edited) _list.Current?.InvalidateRow(CardRow);
            _edited = false;
            return VirtualListElement<int>.New()
                .ItemHeight(LineHeight)
                .Items(_rows)
                .RowHeights((_, i) => i == CardRow ? CardHeight : LineHeight)
                .RenderWith((i, _) => i == CardRow ? Card() : Text.New(_lines[i]).FontSize(12f))
                .FlexGrow(1)
                .WithRef(_list);
        }

        private Element Card()
        {
            var bar = (_bar ??= new RichTextToolbar(_editor, Rebuild) { ButtonSize = BarHeight }).Build()
                .FlexWrap(FlexWrap.NoWrap).Height(BarHeight).Overflow(Overflow.Hidden);
            var editor = RichTextEditor.New(_document)
                .OnDocumentChanged(d => { _document = d; Edited(); })
                .OnSelectionChanged(Edited)
                .Height(Dimension.Px(EditorHeight))
                .WithRef(_editor);
            var card = Flex.Column(gap: 6).Padding(10).Children(bar, editor);
            card.Key = "card";
            return card;
        }

        // The editor holds its text and caret, and the card's height does not move: only the
        // bar can show an edit, and most keys leave every button as it is.
        private void Edited()
        {
            if (_bar is { IsStale: false }) return;
            _edited = true;
            Rebuild();
        }

        // A signal write: an edit raises two changes in one event, and they come to one build.
        private void Rebuild() => _revision.Value++;
    }
}
