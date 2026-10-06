# Rich Text Editor — Design

Plan 12 shipped display-only rich spans and named editing "a separate, much larger
project". This is that project. Scope decided 2026-10-06:

- **An editor.** `ReadOnly` mode is the viewer; there is no separate viewer control.
- **v1 formats:** inline styles (bold, italic, underline, strikethrough, inline code,
  color, highlight, size, font family), links, and block formats (paragraph, headings
  1–6, bullet / numbered / task lists with nesting, quote, code block, alignment).
  Inline images and tables are out of v1.
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
the global-offset plain text, so UIA's text pattern and AT-SPI text work as in TextArea.
Formatting attributes on text ranges (UIA `FontWeight`, `IsItalic`) are a follow-up.

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
5. **Accessibility.** Text source, role, announcements for formatting toggles.
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
   item "Rich text": toolbar, auto-height editor, read-only switch, word count). Junction
   waits on stage 6; its own "is the user typing" checks (`JunctionApp.cs`, and Vesper,
   Meridian, Aperture likewise) list `TextInputElement or TextAreaElement` and must learn the
   editor, or app shortcuts fire while typing in it.

**Open after stages 1–4:** the accessible text source (UIA text pattern / AT-SPI Text over
the plain text, so readers move by word and line — today a reader gets the value only); an
assistive tool setting the value; DevTools showing the document; Up/Down through a line
that mixes directions has had no test.
8. **Measure.** Typing latency and paint on a 5,000-block document under the perf
   suite's rules (docs: perf-suite); find integration (`TextSearch` over global offsets).

## Non-goals (v1)

- Inline images and inline boxes of any kind; tables.
- HTML or RTF paste; RTF anything.
- Collaborative editing, comments, tracked changes.
- Spell checking (no platform seam exists yet).
- Lists inside quotes and other containment the flat model cannot hold.

## Risks

- **Styled layout cost per keystroke.** Only the edited block re-lays out, but a long
  paragraph with many spans pays `EffectiveStyleAt`'s linear scan per segment. Measure in
  stage 8; a sorted-span cursor is the known fix.
- **Ligature fallback** (`LineXAt` measures prefixes in the element style) can put a
  caret a hair off inside a styled ligature — plan 12's open note; code fonts with
  ligatures are the likely place to see it.
- **Markdown fidelity.** Canonical writing changes the user's markers once (`*` bullets
  become `-`). Acceptable for an editor; called out in the Junction change.
- **Two text controls drift.** TextArea and the editor share caret, IME and undo
  behavior by copy, not by base class. Shared pieces move into internal helpers when a
  second copy appears, not before.
