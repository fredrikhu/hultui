# Rich Text Editor — Design

Plan 12 shipped display-only rich spans and named editing "a separate, much larger
project". This is that project. Scope decided 2026-10-06:

- **An editor.** `ReadOnly` mode is the viewer; there is no separate viewer control.
- **v1 formats:** inline styles (bold, italic, underline, strikethrough, inline code,
  color, highlight, size, font family), links, and block formats (paragraph, headings
  1–6, bullet / numbered / task lists with nesting, quote, code block, alignment).
  Inline images and tables were out of v1; pictures as blocks and GFM tables came in as stage 9
  (2026-10-07).
- **Storage:** a neutral document model in `HultUI.Core`; Markdown import/export in a
  separate package, so Markdig stays out of Core.
- **First consumers:** a Fluent gallery page (every feature) and Junction's Create MR
  description (a real Markdown round-trip against GitLab).

## What exists, what is missing

| Piece | State | Used by the editor as |
|---|---|---|
| `TextSpan` weight / slant / size / decoration / colors | shipped (plan 12) | inline marks, minus family |
| Span-aware wrap in `TextPipeline.Layout` | shipped | per-block line layout |
| `TextLine.CaretXAt` / `OffsetAtX` / `VisualRanges` | shipped (plan 14), glyph-exact | caret, hit test, selection |
| `TextUndoHistory`, `CompositionState`, `WordBoundary`, `DragSelection` | shipped, TextArea-private shapes | reused or mirrored |
| `IClipboard.SetData` with several formats | shipped | plain + HTML + internal copy |
| `IAccessibleTextSource` → UIA text pattern / AT-SPI text | shipped | flat-text a11y seam |
| Per-span **font family** | **missing** | inline code |
| A span-aware **paragraph handle** in Core | **missing** | everything interactive |
| Hit test / selection on metric spans in `TextElement` | **broken** (plan 12 gap) | fixed by the handle |

`TextAreaElement` cannot grow into the editor: it holds one flat string in one style and
assumes every line has the same height (`firstVisible = scrollY / lh`).

## The model

Blocks, each a run of text with marks — the shape every editor in this class settles on
(Quill lines, ProseMirror's flat-list variant, Google Docs paragraphs):

```csharp
public sealed class RichDocument { ImmutableArray<RichBlock> Blocks; }   // never empty

public sealed record RichBlock
{
    BlockKind Kind;            // Paragraph, Heading, BulletItem, NumberedItem, TaskItem, Quote,
                               // CodeBlock, Rule (holds no text), Raw
    string Text;               // '\n' is a line break (Shift+Enter; a code block's lines)
    ImmutableArray<RichSpan> Spans;   // sorted, non-overlapping, never plain, merged when marks equal
    int HeadingLevel;          // 1–6 for Heading
    int Indent;                // list/quote nesting; on a paragraph, continuing a list item
    BlockAlign Align;
    bool Checked;              // TaskItem
    string? Info;              // CodeBlock language, or Raw's source format
}

public readonly record struct RichSpan(int Start, int Length, RichMarks Marks);

public readonly record struct RichMarks(
    InlineStyle Style,         // [Flags] Bold | Italic | Underline | Strike | Code
    Color? Color = null, Color? Highlight = null,
    float? Size = null, string? Family = null, string? Link = null);
```

A list item at `Indent` k draws its marker in column k and its text in column k + 1; a
paragraph at `Indent` j draws its text in column j. So Backspace at a list item's start turns
it into a paragraph one deeper and the text does not move.

- **Immutable, shared by reference.** An edit replaces a contiguous run of blocks and
  shares the rest. Undo stores (index, old blocks, new blocks, selection before/after);
  `PatchFrom` compares documents by reference; apps keep a `RichDocument` in a Signal.
- **Flat nesting.** Lists and quotes nest by `Level`, not by containment. Enter splits,
  Backspace at start outdents or unwraps, Tab/Shift+Tab indent — no tree surgery. A list
  inside a quote is not representable in v1; Markdown import flattens it (documented).
- **Positions** are `(block, offset)`. A **global offset** (blocks joined by `'\n'`) maps
  both ways through a prefix-sum index rebuilt per document; a11y, find and the plain-text
  clipboard work on it.
- **Marks are app-facing; `TextSpan` is render-facing.** The theme turns marks into spans
  (code → mono family + background, link → link color + underline), so a theme change
  never rewrites a document.
- **`Raw` blocks** keep source the editor cannot represent (a table, an image, HTML,
  `<details>`) verbatim. They render as a muted, read-only box showing the source and
  export unchanged — loading a GitLab template with a table must not destroy the table.

## Layout and paint

**One new pipeline primitive: a paragraph handle.** Core gets

```csharp
public abstract class TextParagraph
{
    public abstract IReadOnlyList<TextLineBox> Lines { get; }   // offsets absolute in the text
    public abstract Size Size { get; }
    public abstract float LineX(int line);                       // alignment offset
    public abstract float Baseline(int line);
    public abstract float CaretX(int line, int offset);
    public abstract int OffsetAtX(int line, float x);
    public abstract void SelectionRanges(int line, int from, int to, List<TextRangeBox> into);
    public abstract bool TryMoveCaretVisually(int line, int offset, bool right, out int result);
}
// IRenderContext + IMeasureContext:
TextParagraph LayoutParagraph(string text, ResolvedInheritedStyle style, float maxWidth,
    IReadOnlyList<TextSpan>? spans);
// IRenderContext:
void DrawParagraph(TextParagraph paragraph, Point origin);
```

Skia wraps the cached `TextLayout` and its `TextLine`s, so caret, click and highlight read
the same glyph positions the paint does — span-aware and direction-aware for free. The
default implementation (test doubles, recorders) builds lines from `LayoutTextLines` and
prefix widths. `TextElement` adopts it for selection and links, which closes plan 12's
hit-test gap and is the handle's first test.

**Per-block cache.** Each block keeps `(block reference, width, theme version) →
TextParagraph`. Immutability makes the key a reference compare; an edit invalidates
exactly the replaced blocks. Block tops are a prefix sum of heights; paint, hit test and
scroll binary-search it and touch only visible blocks — a 5,000-paragraph document paints
like a 50-line one.

**Block chrome** is drawn by the editor, not the pipeline: bullet / number / checkbox in
a gutter of `Level × indent`, quote bar, code-block background, heading size from the
theme, `Raw` box. Numbering is computed per run of consecutive numbered items at a level.

**Scrolling** is owned, as in `TextArea` (`IScrollbarDraggable`, `IAutoScrollable`, wheel
consume protocol, keep-caret-visible). With `AutoHeight`, the editor measures its whole
content instead and lets a surrounding `ScrollView` scroll — what a form field wants.

## Editing

The engine is a plain class, `RichEditState` (document + selection + pending marks +
history), with no element in sight, so nearly all behavior is unit-tested without layout.
Every mutation goes through one primitive, `Replace(range, RichFragment)`, as TextArea's
`Replace` does; commands are built on it:

- **Typing** takes the marks before the caret (or the *pending marks* set by Ctrl+B with
  no selection). Links do not extend past their end; code does not extend past its end.
- **Enter** splits the block (after a heading comes a paragraph; a new task is unchecked);
  on an empty list item or quote it outdents, then leaves the list. In a code block it is a
  line break, and on an empty last line it leaves the block. Shift+Enter is a line break
  anywhere but a heading. **Backspace** at a block start drops the block's format (the text
  stays put), then outdents, then joins the block before; a rule goes whole.
- **Marks:** toggle over a selection (on if any part lacks it), set color/size/family,
  set or remove a link. **Blocks:** set kind, level, alignment, task check.
- **Markdown shortcuts** while typing at a block start: `# `…`###### `, `- `/`* `, `1. `,
  `[ ] `, `> `, ` ``` `; inline `**x**`, `*x*`, `` `x` ``, `~~x~~` on the closing mark.
  Each shortcut is its own undo step, so Ctrl+Z gives back the literal characters.
- **Undo** groups as `TextUndoHistory` does: typing coalesces by word, a formatting
  command is one step, selection moves seal the group.
- **Features** (`RichTextFeatures` flags) limit what the editor offers. A Markdown
  editor turns off underline, color, highlight, size, family and alignment; shortcuts,
  the toolbar and paste all respect it, so nothing is created that cannot be saved.

Keyboard, pointer and IME follow `TextArea` (word/line units, double and triple click,
drag, preferred x on Up/Down across blocks, Page keys, composition drawn inline), with
Ctrl+click on a link raising `LinkActivated`. In `ReadOnly` mode links open on a plain
click.

**Clipboard.** Copy writes `text/plain`, `text/html` (written by us; browsers and Word read
it) and `application/x-hultui-rich` (the fragment, for lossless paste between editors).
Paste prefers the internal format, then plain text. HTML paste is not in v1: it needs an
HTML parser, and Core takes no dependencies; the Markdown package can offer it later.

**Accessibility.** Role `TextInput` (multi-line), value and `IAccessibleTextSource` over
the plain text, so UIA's text pattern and AT-SPI text work as for the terminal (TextArea
offers only a value). Formatting attributes on text ranges (UIA `FontWeight`, `IsItalic`)
are a follow-up. As built (stage 5), the drawn lines are the lines and a picture reads as
its words.

## Element surface

```csharp
var editor = RichTextEditor.New(doc)                 // RichDocument
    .OnDocumentChanged(d => doc = d)
    .Features(RichTextFeatures.Markdown)
    .Placeholder("Describe the change")
    .AutoHeight(min: 120, max: 480);
var toolbar = RichTextToolbar.New(editorRef);        // buttons reflect the caret's state
```

`RichTextEditorElement` exposes the commands (`ToggleStyle`, `SetBlockKind`, `SetLink`,
`Indent`, …), `Selection`, `CurrentMarks` / `CurrentBlock` and `SelectionChanged`, so an app
can build its own toolbar. `RichTextTheme` (heading scale, code family and background,
quote bar, link color, block spacing, list indent) sits on `Theme` with a
`RichTextStyle` override, the same layering as `TextInputStyle`.

The toolbar lives in Core, so it cannot use `HultUI.Icons` (docs/ICONS.md). It draws
text labels (**B**, *I*, U̲, H1…) by default and takes app artwork per command, like
`ContextMenuItem.Icon`. Phosphor's bundle lacks the text-formatting set
(`text-b`, `text-italic`, `list-bullets`, `list-numbers`, `quotes`, `text-h-*`,
`text-align-*`); they are added to the bundle for the Fluent page.

## Markdown package

`src/HultUI.RichText.Markdown` (Markdig, as Junction already pins 1.3.2):

- `RichMarkdown.Parse(string) → RichDocument`, `RichMarkdown.Write(RichDocument) → string`.
- GFM: emphasis, strong, strike, code, links (inline and autolinks), headings, lists
  (loose/tight kept as a document flag), task items, quotes, fenced and indented code.
- Anything else becomes a `Raw` block holding its exact source span.
- Writing is canonical (`-` bullets, `1.` numbering, fenced code, `*`/`**`), so a
  round-trip normalizes markers once and is stable after.
- Tests: parse → write → parse equals parse, for a corpus of real GitLab templates and
  Junction's existing markdown fixtures.

## Staging

1. **Pipeline prerequisites.** `TextSpan.FontFamily`; `TextParagraph` +
   `LayoutParagraph` / `DrawParagraph` (Skia, default, recorder); `TextElement` selection
   and links on it (plan 12 gap closed, with a test that fails before). **Done 2026-10-06.**
2. **Model and engine.** `RichDocument` & co., global-offset index, `RichEditState`
   with every command above, undo, Markdown shortcuts, features. Unit tests only.
   **Done 2026-10-06** (`src/HultUI.Core/RichText`, 74 tests).
3. **Element.** Layout cache, block chrome, virtualized paint, scrolling, caret,
   selection, pointer, keyboard, IME, clipboard, links, read-only, context menu, theme.
   Headless tests and goldens (light/dark, every block kind). **Done 2026-10-06**
   (`RichTextEditorElement` + `RichTextTheme` + `RichClipboard`/`RichHtml`; 22 headless tests,
   3 goldens). The window hands clipboard and IME to it through `ITextServicesClient`; drag
   selection uses `CapturePointer`. A highlight with no text colour picks dark or light text
   by its luminance. Ctrl+B/I/U/E, Ctrl+Shift+X/7/8/9, Ctrl+Alt+0–6 and Tab in lists landed
   here too; Ctrl+K raises `LinkRequested` for the app's link editor.
4. **Toolbar and shortcuts.** `RichTextToolbar`, Ctrl+B/I/U, Ctrl+K link popover,
   Ctrl+Shift+7/8/9 lists, Ctrl+Alt+0–6 block kind, Tab/Shift+Tab in lists.
   **Done 2026-10-06.** `RichTextToolbar` (FindBar-shaped: app-owned, built each frame, acts
   through a `Ref`; build it *before* `WithRef` moves the ref to the freshly built editor),
   buttons pressed per `SelectionMarks`/`SelectionIs`, filtered by `Features`, text labels
   unless `Icon` supplies artwork; a link `Popover` (address, Apply, Remove) that Ctrl+K opens
   through `OnLinkRequested`. New `Element.AccessibleToggled` (UIA Toggle / AT-SPI checked;
   the action router presses the button). Default code background, quote bar and rule are
   tints of the text colour: the palette's surface and border fills matched the Fluent field
   and drew nothing. Phosphor's text-formatting icons were not added.
5. **Accessibility.** Text source, role, announcements for formatting toggles. **Done 2026-10-07:**
   - **The text.** The blocks a paragraph each, as the plain text reads them, except that a
     picture reads as its words ("Picture" with none). A table's cells are paragraphs in reading
     order. The caret and selection map onto it.
   - **Lines are drawn lines.** `AccessibleText` takes the element's line starts. A line is then
     what is drawn and a paragraph what a newline ends; UIA got a paragraph unit and AT-SPI a
     paragraph granularity for it. Text that does not wrap (the terminal) is unchanged.
   - **Made on the UI thread.** The snapshot is built from the layout when the tree is built, and
     bridge threads get the last one built. The tree is built before the frame is drawn, so an
     editor's first snapshot has no layout; its first paint, a new width or a new theme asks for
     the tree again. Geometry is each line's box in document coordinates, plus exact character
     positions for the lines on screen when the snapshot was made (others spread their characters
     evenly). Boxes are placed through a record the paint replaces, so scrolling needs no new
     snapshot.
   - **Told.** A caret or selection move notifies the tree while a reader listens, so the bridges
     raise caret-moved and selection-changed. Formatting shortcuts are spoken ("Bold on",
     "Heading 2", "Bulleted list"). An assistive tool setting the value replaces the document as
     plain paragraphs, one undoable edit (`RichEditState.ReplaceDocument`), refused when read-only.
   - **Checked** with Windows' own UI Automation client on the Fluent sample: an Edit control with
     Value and Text patterns; lines, paragraphs and words; boxes in screen pixels matching the
     layout; a point inside line 1 finding its word; the caret. That check found the missing-layout
     snapshot above. AT-SPI is unit-tested only, as for the terminal.
   - **Cost.** With a bridge on, a keystroke in a 5,000-block document is 1.40 ms, 0.26 ms more
     than without a text source: the snapshot walks every drawn line.
   - **Not done.** A reader moving the caret or selection (UIA `Select`, AT-SPI `SetCaretOffset`)
     is refused by both bridges, as for the terminal. AT-SPI `EditableText` (set-value on Linux).
     Formatting as text attributes (UIA `FontWeight`, `IsItalic`; AT-SPI attribute runs).
6. **Markdown package.** Parse, write, `Raw`, round-trip corpus. **Done 2026-10-07**
   (`src/HultUI.RichText.Markdown`: `RichMarkdown.Parse`/`Write`/`WriteInline`, `Features`;
   53 tests). Decisions made on the way:
   - **Line breaks.** A soft break reads as a space, which is how CommonMark and GitLab
     render it. A hard break is `'\n'`, written as `\` + newline. Shift+Enter in the editor
     therefore survives the round trip as a visible break.
   - **Bare URLs stay text.** The autolink extension is off, so a pasted address round-trips
     untouched and GitLab still links it. `<https://…>` reads as a link.
   - **Raw blocks.** A raw block runs to the line before the next block, because Markdig's
     span for a table stops at its header row. Container indentation is stripped up to the
     block's column, and the writer puts it back.
   - **Emphasis is checked, not trusted.** Delimiters are placed as insertions into the
     escaped text, and each one is checked against CommonMark's flanking rules where it
     lands. A style whose delimiters can't open or close there (`a*&*`, or `e~~**A`) is
     written as `<strong>`/`<em>`/`<del>`, and the reader reads those tags back as marks.
     The writer then reads each block back, and if a parser quirk still disagrees, writes
     that block's styles as tags.
   - **Minimal escaping.** `#12`, `~label` and `@user` stay as typed. A `*`, `_` or `~` at a
     piece edge is escaped because a delimiter may sit beside it. `<` is escaped before
     anything but a space (`<a@b.c>` is an autolink). `!` is escaped before a link. A
     backtick next to a code span is written `&#96;`. A link whose text is ` `, `x` or `X`
     spells it as an entity, since `[ ]` in a list item is a task box.

   **Proven:** 3 seeds × 4,000 random documents per run, drawn from an alphabet of Markdown's
   own characters. Each round-trips exactly, except a style starting or ending with a space:
   the writer moves that space out of the style on purpose, so those are held to idempotence.
   A one-off run at 3 × 50,000 passed. **Not representable, by design:** underline, colour,
   highlight, size, face, alignment, empty paragraphs, spaces at a line's ends, a code mark
   over a line break, an ordered list's start number, loose vs tight lists.
7. **Adoption.** Fluent gallery page; Junction Create MR description on the Markdown
   profile (the posted body is the written Markdown). **Fluent page done 2026-10-06** (nav
   item "Rich text": toolbar, auto-height editor, read-only switch, word count, and the
   document as Markdown, live). **Junction done 2026-10-07:**
   - **The form.** It holds a `RichDocument`. The pre-fill from the commits (`- subject` per
     commit, `Closes #N`) is written as Markdown and read in, and GitLab is sent
     `RichMarkdown.Write`.
   - **The editor.** It offers `RichMarkdown.Features` only, grows from 96 to 320 px, and
     takes `Ui.Field`'s look. Its compact toolbar (B, I, S, code, link, H2, the three lists,
     quote, code block) uses Phosphor artwork for link, code and tasks.
   - **Keys.** The editor now leaves Ctrl+Enter and Alt+Enter to the form, which submits on
     them. Junction's typing check names the editor. Vesper, Meridian and Aperture host none
     and are unchanged.
   - **Toolbar fix.** Toolbar buttons grow past `ButtonSize` to fit their label; at a fixed
     width, "H2" wrapped in Junction's face.

**Open after stages 1–4:** ~~the accessible text source~~ and ~~an assistive tool setting the
value~~ (stage 5, done); DevTools showing the document; Up/Down through a line that mixes
directions has had no test.
8. **Measure.** Typing latency and paint on a 5,000-block document under the perf
   suite's rules (docs: perf-suite); find integration (`TextSearch` over global offsets).
   **Done 2026-10-07:**
   - **Scenarios.** Ten in the perf suite: nine `richtext/` (open, type and Enter in 5,000 blocks,
     the wheel, Ctrl+A, a resize of 1,000, a paragraph of 600 spans, a 200-cell table, an
     auto-height editor in a scroll view) and `a11y/type in rich text, bridge on`.
   - **Result.** Idle-gated, three runs a side, interleaved: 0 slower, 11 faster. The paragraph
     of 600 spans went from 19.2 to 2.7 ms, the auto-height editor from 3.3 to 0.8 ms; the rest
     is in the suite's README.
   - **Profiled, not guessed.** The span scan this plan named was third. First was a Skia call
     per character asking a face for a glyph; a face now remembers the answers (a two-bit table
     for the Basic Multilingual Plane), which also sped every `text/` scenario up by 7–27 %.
     Second was three passes over every span for every painted line. Both span lookups now
     binary-search, spans of one colour are clipped and drawn as one stretch, and blobs are
     kept by the glyphs they draw, so a paragraph laid out again keeps its unchanged lines' blobs.
   - **The editor.** After an edit, only the blocks it replaced are laid out: blocks shared by
     reference at either end keep their layouts and move, and the run is widened to whole tables.
     A random walk of 300 edits on each of eight seeds matched a fresh layout every time; one
     case it caught was an undone row, which leaves the cells kept as the same instances.
     `IRenderContext.ClipBounds` (new; Skia answers from its canvas) lets the editor draw only
     the blocks and lines the clip leaves, which an editor that grows inside a scroll view needs.
     An auto-height editor measures from that same layout at the width it was drawn. A document
     makes its plain text once, in one copy.
   - **Find.** `editor.Search` (`.HighlightSearch(search)`) takes a `FindBar`'s `TextSearch`: one
     line a block, not global offsets, because the search and its hits are line-based and a
     block is the editor's line. The editor binds it again after every edit, tints matches
     behind the text (amber; `RichTextTheme.FindHitColor`/`FindCurrentColor`), and
     `RevealSearchMatch` selects the current match, which scrolls it into view.
     `FindSelection` seeds the bar from the selection. Fluent's notes have Ctrl+F.
   - **Open.** Opening 5,000 blocks lays every block out once (15.5 ms, 18 MB). Estimated
     heights for blocks off screen would make it cost what the screen does, at the price of a
     scrollbar that settles as blocks are measured. While an assistive tool listens, each key
     still copies the document's text once (1.6 MB at 5,000 blocks); stage 5's text source is
     the fix.
9. **Pictures and tables.** Brought into scope 2026-10-07. Both take the shape GitLab's Markdown
   gives them and Junction's Markdown viewer already draws. See [Pictures and tables](#pictures-and-tables).
   **Done 2026-10-07:**
   - **Engine.** `RichTable` / `RichTableRange`, `RichEditState.Tables.cs`, and one table-aware
     `CutSelection` that every edit goes through. 36 engine tests, among them a random walk of
     1,500 edits per seed that keeps every table rectangular.
   - **Element.** A table is laid out as a unit; Up/Down move by row; a click picks the cell; a
     right-click moves the caret before the context menu opens. 13 headless tests, 2 goldens
     (`richtext-table`, `richtext-picture-selected`).
   - **Toolbar.** Table and Picture buttons. The Picture button opens the toolbar's own picture
     editor (address and description), the link editor's twin; `ImageRequested` drives the
     context menu's "Change picture…".
   - **Markdown.** Tables and pictures read and written; 9 new tests. The random round trip now
     draws tables and pictures: 3 × 4,000 per run, and a one-off 3 × 40,000 passed.
   - **Adoption.** The Fluent notes hold a table and a picture drawn in code; the resolver also
     loads file paths. Junction's description editor loads pictures through its Markdown view's
     loader (`MdPictureFor`).

   Found on the way:
   - **Markdig keeps `\|` inside a code span in a cell.** GFM, and so GitLab, unescapes it there.
     The reader unescapes it, and the writer escapes every `|` in a cell, code included.
   - **Markdig widens a table to its widest row.** GitLab drops the cells that overhang. Such a
     table stays `Raw`; the width is taken from the delimiter row, as GFM takes it.
   - **An address with `<` or `>`** was percent-encoded, which does not read back. It is now
     backslash-escaped inside `<…>`, for links too.
   - **`LinkRequested` was not copied in `PatchFrom`.** It is now, with `ImageRequested` and
     `ImageResolver`.

   **Not done:** a Table button in Junction (Phosphor's bundle has no table glyph), pasting
   picture data (needs an upload hook), resizing a picture or a column by dragging.

## Pictures and tables

**A picture is a block.** `BlockKind.Image` is void like a rule and carries
`RichImage(Source, Alt, Title, Width, Link)`. A picture alone on its line is what GitLab
descriptions and Junction's viewer hold. A picture inside a line of text would need inline boxes
in the text pipeline, so it stays out of scope. Markdown with one stays `Raw`.

- **Loading is the app's.** The editor takes `ImageResolver: Func<string, IImageSource?>`. A
  picture with no source, or one not loaded yet, draws as a placeholder line with its alt text.
  A loaded one is scaled down to fit, never up (`ImageElement.ScaledSize`), with `Width` as its
  natural width and `RichTextTheme.ImageMaxHeight` as its ceiling.
- **Arrival.** The resolver is asked again on every rebuild (`PatchFrom`). A picture whose
  answer changed re-lays out its own block and nothing else.
- **Editing.** A caret on a picture means the picture is selected: it draws a ring, not a bar.
  Typing or Enter there opens a paragraph after it. Backspace or Delete removes it. Backspace at
  the start of the block after it selects it first, since a picture is content, not a separator.
- **Commands.** `InsertImage(RichImage)` and `SetImage(block, RichImage)`. The toolbar's Picture
  button opens its own picture editor: an address and a description. An app with a picker of its
  own leaves the button out and raises its picker from `ImageRequested`, which the context menu's
  "Change picture…" also raises. Pasting image data needs an upload hook from the app and is not
  part of this stage.

**A table is a run of cell blocks.** `BlockKind.TableCell` is one block per cell, row by row, with
`Row` and `Column` from 0. A table is a maximal run of cells, and a cell at (0, 0) starts a new one.
Row 0 is the header, as GFM requires. A column's alignment is the `Align` of its cells, kept
equal. A cell holds inline text with marks and line breaks, which is what a GFM cell holds.

- **Why flat.** Positions stay (block, offset). Typing, marks, links, undo, the clipboard and
  the global-offset text work in a cell unchanged. A tree (ProseMirror's shape) would make every
  position three levels deep and every command table-aware.
- **Invariant: rectangular.** Table commands rewrite the whole run. `RichTable.Normalize` repairs
  a run that arrives ragged (part of a table pasted, a document built by hand), and `Load` applies
  it.
- **Keys.** Enter moves to the cell below. From the last row it opens a paragraph after the table,
  which is the way out of a table that ends the document. Shift+Enter is a line break in the cell.
  Tab and Shift+Tab move to the next and previous cell, and Tab in the last cell adds a row.
  Up and Down move by row in the same column. Left and Right go in reading order.
- **Deleting.** Cells never join. Backspace at a cell's start and Delete at its end do nothing.
  Backspace at the start of the block after a table moves into the last cell, and an empty block
  goes. A selection across cells clears what it covers and never removes a cell. A table goes only
  when a selection covers it and reaches past it on both sides, or through Delete table.
- **Other commands.** Paste into a cell flattens to one cell's text: blocks become line breaks,
  pictures and rules are dropped. Block formats (headings, lists, quotes, code) skip cells.
  `InsertTable(rows, columns)`, `InsertRow(below)`, `InsertColumn(after)`, `DeleteRow`,
  `DeleteColumn`, `DeleteTable`, and `SetAlign` on cells (the whole column) are on the state, the
  context menu and the toolbar's Table button.
- **Layout.** A table is laid out as a unit. Column widths follow Junction's viewer: each
  column's weight is its widest cell on one line plus padding, held between 48 and 360, and the
  columns share the width by weight. A row is as tall as its tallest cell. The header row is bold
  on a tint, with a hairline grid. A selection paints the cells it covers in reading order: it is a
  text selection, not a cell rectangle.
- **Markdown.** GFM pipe tables. The writer escapes `|` as `\|` and writes a line break as `<br>`.
  The reader takes `<br>` in a cell as a line break. A table with a cell the document cannot hold
  stays `Raw`.

**Features.** `Images` and `Tables` are new flags, both inside `Markdown`.

## Non-goals (v1)

- Inline images and inline boxes of any kind (pictures are blocks, stage 9); merged or nested
  table cells, tables without a header row, column widths that are kept.
- HTML or RTF paste, and reading either. Writing both is wanted (2026-10-07, TODO.md): a
  standalone HTML document grown from `RichHtml`, and an RTF writer, so a document can be saved
  for Word or a browser and the clipboard can offer `text/rtf`.
- Collaborative editing, comments, tracked changes.
- Spell checking (no platform seam exists yet).
- Lists inside quotes and other containment the flat model cannot hold.

## Risks

- **Styled layout cost per keystroke.** Only the edited block re-lays out, but a long
  paragraph with many spans pays `EffectiveStyleAt`'s linear scan per segment. Measured in
  stage 8: the scan was real but third in line, behind a Skia call per character to ask a
  font for a glyph and a paint pass over every span for every line. All three are fixed.
- **Ligature fallback** (`LineXAt` measures prefixes in the element style) can put a
  caret a hair off inside a styled ligature — plan 12's open note; code fonts with
  ligatures are the likely place to see it.
- **Markdown fidelity.** Canonical writing changes the user's markers once (`*` bullets
  become `-`). Acceptable for an editor; called out in the Junction change.
- **Two text controls drift.** TextArea and the editor share caret, IME and undo
  behavior by copy, not by base class. Shared pieces move into internal helpers when a
  second copy appears, not before.
