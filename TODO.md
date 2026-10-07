# HultUI — Implementation TODO

## Just Completed
- **Rich text editor, stage 5: for a screen reader** (docs/plans/16-rich-text-editor.md) — the editor
  offers `IAccessibleTextSource`: blocks as paragraphs, drawn lines as lines (`AccessibleText` now
  takes `lineStarts`; UIA gained a paragraph unit, AT-SPI a paragraph granularity), a picture as its
  words, cells in reading order; caret and selection; character boxes from the layout that follow
  scrolling. Made on the UI thread when the tree is built; the first paint, a new width or theme asks
  for a new one. Caret moves notify, formatting shortcuts are announced, an assistive tool's
  set-value replaces the document as one undoable edit. Checked with Windows' UI Automation client
  on the Fluent sample. Tests: 6 headless, `AccessibleText`, UIA and AT-SPI units. Bridge-on typing
  at 5,000 blocks 1.40 ms (+0.26).
- **Rich text editor, stage 8: measured, faster, and find** (docs/plans/16-rich-text-editor.md) —
  ten perf-suite scenarios (`richtext/`, `a11y/type in rich text`); idle-gated A/B 0 slower, 11
  faster (captures 2026-10-07 in the suite). A paragraph of 600 spans 19.2 → 2.7 ms per keystroke,
  an auto-height editor 3.3 → 0.8 ms, every `text/` scenario 7–27 % faster. Fixes: a face remembers
  which characters it has (Skia was asked per character per itemize), binary-searched span lookups
  in layout and paint, one clip per colour, blobs kept by glyphs, `IRenderContext.ClipBounds` culling,
  incremental editor layout (checked against a fresh layout over 8 × 300 random edits), auto-height
  measured from that layout. Find: `editor.Search` / `.HighlightSearch(search)`, `RevealSearchMatch`,
  `FindSelection`; Ctrl+F in Fluent's notes. Tests: 4 find, 2 layout-equivalence seeds; 2 goldens
  moved one pixel (an italic overhang no longer cut at a same-colour span edge).
- **Rich text editor, stage 9: pictures and tables** (docs/plans/16-rich-text-editor.md) — a picture
  is a void block (`BlockKind.Image`, `RichImage(Source, Alt, Title, Width, Link)`) whose pixels
  come from the app (`.Images(resolve)`, asked again every rebuild); a table is a run of
  `BlockKind.TableCell` blocks with `Row`/`Column` (`RichTable`, `RichTableRange`), so positions,
  marks, undo and the clipboard work in cells unchanged. Cells never join; a selection across
  them empties them; Enter goes down and out of the last row, Tab goes cell to cell and adds a
  row; Up/Down move by row; context menu rows/columns/alignment/delete; right-click moves the
  caret first. Toolbar Table and Picture buttons (built-in picture editor). Markdown: GFM tables
  (`<br>` line breaks, `\|` everywhere incl. code), pictures as `![…](…)` or `<img width>`; Markdig
  quirks handled (wide rows → raw; `\|` in cell code). Fluent notes hold a table and a drawn
  picture; Junction's description loads pictures through its Markdown loader. Tests: 36 engine,
  13 headless + 2 goldens, 9 Markdown (random round trip now with tables and pictures).
- **Junction: the merge request description is rich text** — the Create MR card's description is a
  `RichTextEditor` on `RichMarkdown.Features` with a compact toolbar (B, I, S, code, link, H2, lists,
  tasks, quote, code block); the commit pre-fill is read from Markdown and GitLab is sent
  `RichMarkdown.Write`. The editor leaves Ctrl+Enter/Alt+Enter to the form (the card submits on
  Ctrl+Enter); toolbar buttons grow to fit their labels. Tests: 2 Junction (typed with Markdown
  shortcuts → the exact Markdown posted; only Markdown's formats offered), 1 headless.
- **Rich text editor, stage 6: Markdown** (`src/HultUI.RichText.Markdown`, Markdig 1.3.2 as Junction) —
  `RichMarkdown.Parse` keeps what the document cannot hold (tables, HTML and comments, pictures,
  quotes holding lists, link definitions) as `Raw` blocks written back exactly; soft breaks read as
  spaces, hard breaks as `'\n'` (written `\`); bare URLs stay text. `RichMarkdown.Write` is canonical
  (`-`, `1.` counted, fenced code, `**`/`*`/`~~`), escapes only what would read as markup, places
  emphasis delimiters as insertions and writes a style as `<strong>`/`<em>`/`<del>` where CommonMark's
  flanking rules would not let a delimiter open or close (the reader reads those tags back), then reads
  each block back and falls back to tags if a parser quirk still disagrees. 53 tests, including 3 × 4,000
  random documents in Markdown's own characters that must round-trip exactly (3 × 50,000 passed once).
  Fluent's rich text page shows the document as Markdown, live.
- **Rich text editor, stages 3–4: the element and its toolbar** — `RichTextEditorElement`
  (`RichTextEditor.New(doc)`, `.OnDocumentChanged`, `.Features`, `.ReadOnly`, `.AutoHeight(min, max)`,
  `.OnLinkActivated`, `.OnLinkRequested`; `CaretBounds(position)` for anchoring): blocks laid out once
  per width as `TextParagraph`s and reused by block reference, only visible blocks drawn, own
  scrolling (scrollbar, wheel, autoscroll), list markers (three bullet shapes by depth, numbers
  right-aligned, task boxes that toggle on click), quote bars, code backgrounds, rules, alignment;
  caret with wrap affinity, preferred x for Up/Down across blocks, Home/End per visual line, Page
  keys, visual stepping on mixed-direction lines; click/double/triple/drag selection through
  `CapturePointer`; IME preedit; clipboard as the editor's own JSON + HTML + text; links (plain
  click read-only, Ctrl+click editable); context menu; controlled value (a document handed in
  that is neither the last handed in nor the edited one replaces it). `RichTextTheme` on `Theme`;
  the field look is `TextInputTheme`/`TextInputStyle`. The window hands clipboard and IME over
  through the internal `ITextServicesClient`. `RichTextToolbar` + link popover;
  `Element.AccessibleToggled`. Fluent sample "Rich text" page. Tests: 22 headless, 3 goldens.
- **Rich text editor, stage 2: model and engine** (`src/HultUI.Core/RichText`) — `RichDocument`
  (immutable blocks, never empty, global offsets = blocks joined by `\n`, `Slice` → `RichFragment`),
  `RichBlock` (kind, text, normalized spans, heading level, indent, align, checked, info; value
  equality), `RichMarks` (styles + colour, highlight, size, family, link), `RichTextFeatures`
  (`Markdown` = no underline/colour/highlight/size/family/alignment; `Restrict` for paste).
  `RichEditState`: one `Commit` primitive replacing a run of blocks; typing marks (links and code do
  not grow past their end; pending marks until the caret moves), Enter/Shift+Enter/Backspace/Delete
  rules per block kind, paste joining first/last blocks, toggle/set marks, links, block kinds
  (paragraphs ⇄ one code block), indent limited to one past the block before, rules, alignment,
  tasks; Markdown shortcuts (`# `…, `- `, `1. `, `[ ] `, `> `, `**b**`, `*i*`, `` `c` ``, `~~s~~`,
  ```` ```lang ````/`---` + Enter), each its own undo step. `RichUndoHistory` groups like
  `TextUndoHistory` with block-range steps checked by reference. 74 tests.
- **Rich text editor, stage 1: paragraph handle** (docs/plans/16-rich-text-editor.md) — `TextSpan`
  gains `FontFamily` (metric-affecting: inline code). `TextParagraph` (Core,
  `IRenderContext.LayoutParagraph` / `DrawParagraph`) is one paragraph laid out with its spans,
  line limit and overflow and kept: lines, alignment offset, baselines, caret x, offset at a
  point, selection boxes (merged over a line's break), visual caret moves, wrap affinity
  (`LineOf(offset, upstream)`), an empty line for empty text and after a final newline. The
  pipeline backs it with the cached `TextLayout` (`PipelineTextParagraph`, glyph-exact, BiDi
  aware); other contexts get `TextParagraph.FromLines` (lines + one-line caret methods); the
  recorder logs `DrawParagraph` with every span field. `TextElement` hit-tests and paints
  selection through it — plan 12's gap (a bold or larger run ahead of a link shifted every click
  after it; centred lines hit-tested as if left-aligned) is closed, and clicks land on the
  nearest caret position instead of rounding up. `LayoutProbe.DrawnLines` replaces reflection on
  `TextElement._lines` (Junction's DiffWrapTests used it). Tests: 11 paragraph, 5 headless link
  (all 5 fail on the old code); `GroupSelectionDragTests.ShiftDown…` clicks two characters in
  rather than 1px, which only rounded up to inside row 3.
- **Golden images** — `HeadlessOptions.Golden()` + `GoldenImage.Match(window, "name")`: a frame
  compared against `Golden/<name>.png` beside the test, the same PNG bytes on Windows and Linux.
  Two halves: **pinned fonts** (`SkiaFontCollection.Pinned` — every family resolves to DejaVu
  Sans in four styles or DejaVu Sans Mono, embedded in HultUI.Headless; no OS fallback; slant
  decides before weight) and **text as outlines** (`SkiaRenderContext.TextAsOutlines` draws each
  shaped run's glyph paths through Skia's own rasteriser — DirectWrite and FreeType shade the
  same file thousands of pixels apart, hinted or not; the paths match byte for byte).
  `HULTUI_UPDATE_GOLDENS=1` writes baselines (only past tolerance, so no churn); a mismatch
  writes actual/expected/diff PNGs to `golden-failures/`, which CI uploads. Default tolerance
  ±2 per channel, 0 pixels — headroom for another CPU's SIMD rounding; this machine and WSL
  Fedora agree exactly. `ControlGoldenTests`: controls light/dark, focus ring, text styles,
  menu bar + expander + table. `SkiaFontCollection.Register` also lets an app ship a font.
- **Sticky positioning** — `PositionType.Sticky` + `.Sticky(top, bottom, left, right)`: in the flow,
  held against an edge of the nearest scroll view while it is scrolled past, never out of its
  parent's content box (or, as the scroll view's own child, out of the scrolled content).
  `Element.StickyShift` is worked out from the scroll offset when drawn (renderer translate),
  hit (HitTester shifts point and clip), read (a11y builder threads it as negative scroll) and
  measured (`ScreenBorderBox`, so popovers, tooltips, the focus ring and headless clicks follow);
  layout never moves it. `ZOrderHelper` draws a sticky child after its siblings at its ZIndex — and
  is stable now: it promised insertion order for equal z-indexes but used `List.Sort`, which only
  looked stable while insertion-sorting 16 or fewer. `ScrollRangeIntoView` scrolls a revealed row
  clear of a header held at the top. Adopted: Junction's change list (each section a column; Staged
  / Changes / Conflicts / Hidden headers with Unstage all / Stage all stay in reach) and the Fluent
  gallery's section titles. Survey: every pinned header in the apps is built outside its scroll
  view; nothing tracked the scroll offset; VirtualList rows are not sections (a stuck group header
  in a virtual list is a different feature — Junction's hunk headers, blame runs). Tests: 7
  headless, 2 z-order, 1 Junction; controls (10): never shifting, drawn/hit/read in place, drawn
  under its siblings, not held in its parent, unstable z-order, flat headers bounded by the
  viewport, reveal landing under the header, Junction headers not sticky — each fails its test.
- **Aspect ratio** — `BoxStyle.AspectRatio` + `.AspectRatio(w, h)` / `.AspectRatio(ratio)`: the
  side left auto follows the known one (given, stretched, flexed, or a grid cell's), within its
  min/max; both known → ignored; neither → height from the content's width. Width wins: rows and
  grid cells do not stretch a ratio box down (grid: CSS's normal-is-start); in a column a ratio
  box of auto width and height takes the column's room in both passes — Phase 1 measures it at the
  stretched width, or nested columns of no width saw a zero-height box — unless its height is
  given. A shrinking column floors it at the height its width makes. One `ResolveBox`/`RatioOf`
  pair serves ComputeLayout, MeasureSize, MeasureLeafSize and the baseline mirror. Pictures keep
  their own shape (`Element.NaturalAspectRatio`, ImageElement's crop or intrinsic size) when given
  exactly one side — never when stretched or given both, so existing letterboxed images do not
  change. Adopted: HelloWorld's image (`TODO: This must be able to be done nicer`, a guessed 164 for
  a 180-wide sidebar) and the video scrub preview's height. Survey: Aperture's ratios are fixed
  constant pairs; its fluid poster grid is held back by VirtualList row heights, not by layout;
  Junction's image diff keeps its equal 300px panels on purpose. Tests: 11 layout; controls (9):
  no transfer, column measured without its width, rows/grids stretching ratio boxes down, no
  shrink floor, no content-width fallback (all three copies), pictures ignoring their shape,
  given heights stretched across, pictures keeping shape with neither side — each fails its test.
- **Table / column model** — `TableView<T>` + `TableColumn<T>` + `TableState` + `TableTheme`
  (`src/HultUI.Core/Builders`): a model like `TreeView<T>`, header and rows on one set of grid
  tracks (fixed px or fill fr, never auto). Sort from the header (press, Enter/Space), stable,
  memoized on list identity, `Group` pins rows regardless of direction, `SetSort` for app-owned
  sorts; resize by a grip (end edge before the fill column, start edge after it — the fixed
  columns after a fill are pinned to the end), double-click resets; right press on the header
  hides/shows columns; `State`/`Restore`/`StateChanged` (once per settle); `Body(items)` a
  configured virtual list with `RowLabel` names. New a11y role `ColumnHeader` (UIA HeaderItem
  50035, AT-SPI 57), invokable. Fluent sample: a sortable, resizable "Downloads" table. Survey: 4
  real tables (Vesper pane + Sync, Junction Branches + Tags) all share one template string; not
  converted — Junction's are tree-shaped with custom styled cells, Vesper's owns Tab and per-tab
  sort; candidates for follow-up. Fixed in passing: AT-SPI `RoleProgressBar` was 46 (ROOT_PANE),
  pinned by its own test — progress bars were read as "root pane"; now 42. Vesper's file rows had
  no accessible label (read as the FileEntry record's ToString()); now "name, size, modified
  …[, marked]". Traps: an absolute child does not stretch between top and bottom (height 0); its
  right is measured from the content box; a parent with OverflowX hidden clips hit testing to its
  content box — the grip in the padding was unreachable; a later sibling wins a press on a shared
  edge; `Array.Sort` insertion-sorts ≤16 items, so stability needs a longer test list. Tests: 9
  model, 6 headless, 1 Fluent, 1 Vesper, AT-SPI role rows; controls (10) each fail their test.
- **Popover** — `src/HultUI.Core/Builders/Popover.cs` (+ `PopoverTheme`): a non-modal card of any
  content against an anchor. Placement (Below/Above/Right/Left × Start/Center/End) flips when it
  does not fit and the other side has more room, slides to stay in the window, lands on whole
  pixels, and is redone every frame so it follows a scrolling anchor; closes when a rebuild drops
  the anchor. Takes the keyboard (first control, else the card), Tab stays in, Esc closes and the
  window hands the keyboard back; `ArrowKeys` for row lists. Card is a dialog, anchor reports
  expanded. Host: `IOverlayHost.ShowAnchoredOverlay(content, anchor, place)` — placed every frame
  in `RenderOverlays`; a press on the anchor of a light-dismiss overlay closes it and is swallowed
  (press and release), so a toggle button does not reopen it (Dropdown and DatePicker each had a
  flag for this); a press inside a lower overlay closes the anchored overlays above it (a popover
  opened from a dialog closed only on Esc before). New `Element.WindowBorderBox`/`WindowContentBox`
  (the overlay's place added): dropdowns, date pickers and tooltips placed their popups from
  `ScreenBorderBox`, which inside an overlay is overlay-relative — the space pointer events use,
  so it stays — and opened off by the popover's offset; the focus ring, headless `Click` and the
  driver's `@` clicks were off the same way. (First tried folding the origin into
  `ScreenBorderBox`: the WebView's overlay test caught pointer mapping counting it twice.) Vesper's recent-folders popover is now
  one (rows focusable, ↑ ↓ Enter work as its footer always claimed, Esc, Alt+←/→ kept, a press on
  the cluster only closes it). Fluent sample: a WinUI flyout under "Empty bin". Survey: Junction's
  and Meridian's popovers are anchored item menus, a menu-button job, not converted. Tests: 10
  headless, 5 placement, 3 Vesper, 1 Fluent; controls (9): anchor press passing through, placed
  once, dialog press keeping it, window box ignoring the overlay, dropdown placing from the
  overlay-relative box, keyboard not taken, no flip, staying open without its anchor, Vesper rows
  unfocusable — each fails its test.
- **Expander** — `src/HultUI.Core/Elements/ExpanderElement.cs` (+ `Builders/Expander.cs`,
  `ExpanderTheme`): a header (chevron, title or any element, actions that do not toggle) over a
  body that slides open with the chevron turning in step. Keeps its own state across rebuilds, or
  the app's with `Expanded(bool)` (Tabs' controlled/uncontrolled rule); a closed body leaves the
  tree but an element body is kept and patched, a function body is built only while open; a
  closing body hands the keyboard to the header. Owns its children (`ManagesOwnChildren`), patching
  the app's parts into live ones. Default look a disclosure (leading chevron, hover fill); the
  Fluent sample's card is a theme (trailing chevron, shared border, rule between header and
  body) and replaces its hand-rolled one; the accessibility sample's "Details" tree item is one
  too (its UIA ExpandCollapse UiTest passes on a real window). Six hand-rolled expanders surveyed;
  Junction's are list rows with precomputed heights or mixed-content toggles and stay. Also fixed:
  expanded state was never announced — no UIA ExpandCollapseState change event, and AT-SPI's
  expandable/expanded states were defined but never sent, so Orca read every expander, tree
  folder, dropdown and menu-bar title as a plain button. New `AccessibilityProperties.IsExpanded`,
  differ compares `Expanded`, `AtSpiMarshal.StateBits` testable. Trap: hover/press restore the look
  they captured when they began, corner radius included — a card closed while hovered kept square
  bottom corners once the pointer left; the header re-snapshots whenever its shape changes. Tests:
  10 headless, 1 differ, 2 AT-SPI state; controls (10): differ blind, AT-SPI bits dropped, action
  toggles, NumLock as a modifier, rebuild overriding own state, focus left in a closing body,
  title read twice, body never sliding, no re-snapshot, bare disclosure squaring its corners —
  each fails its test.
- **MenuBar** — `src/HultUI.Core/Builders/MenuBar.cs` (+ `MenuBarItem`, `MenuBarElement`,
  `MenuTitleElement`, `MenuBarTheme`): titles that open the existing context menus as
  dropdowns. Pointer: press to open/close, hover switches while one is open. Keyboard the
  Windows way: F10 and lone (left) Alt take the titles, Alt+letter (`&File`) opens a menu, ← →
  cross menus (the window hands them over through the new `IOverlayHost.OpenMenu` /
  `MenuRequest.Navigate` when the menu has no submenu to close or open), Esc steps back
  (`MenuCloseReason.Escape`) and then lets go, focus comes back; mnemonics underline while Alt is
  held. New a11y roles `MenuBar`/`MenuItem` mapped in UIA and AT-SPI (34/35), titles report
  expanded. In the Fluent sample's title bar (File, View with live checks, Help). Traps hit: a
  component must look its elements up through its host — references kept from `Build` are the
  throwaway copies a rebuild patches from (no overlay host, no layout); a label inside a
  `WindowDrag()` bar is a drag point unless it is hit-test invisible; with nothing focused keys
  land on the root host, so global key handlers belong on `Mount()`, not inside the tree. Also
  fixed: the Fluent sample's context-menu hover was the window-blended fill, invisible on the
  flyout (#2D2D2D on #2C2C2C). Tests: 7 headless + 1 Fluent; controls: no ← → hand-over, Esc taken
  as a dismissal, AltGr counted as Alt — each fails its test; AT-SPI role constants test.
- **One command palette** — `CommandPalette` + `PaletteCommand` + `FuzzyMatch`
  (`src/HultUI.Core/Builders`), themed by `CommandPaletteTheme`, replace Junction's, Vesper's and
  Meridian's own; `Kbd` + `KbdTheme` replace four apps' key chips (Aperture's was dead code). Each
  app had something the others lacked: Meridian ranked fuzzy matches and named its dialog,
  Junction scrolled to the chosen row and showed a second line, Vesper read its keys from the
  keymap. Each also had a gap: Meridian's selection walked out of view past its 60-row cap,
  Vesper's list had no scroll view at all, Junction and Vesper searched by substring and
  announced nothing to a screen reader. The framework one is a modal `Dialog` (new
  `DialogOptions.TopOffset`) with a component that rebuilds itself; the list is taken once on
  open; matches rank titles before groups, details and keywords; the chosen row stays in view
  (fixed row heights, so the band is known before layout); a long list shows 100 and counts the
  rest; Esc, a click outside or the opening key close it and `Closed` fires at once. Each app's
  look moved into its theme. Two fixes on the way: the dialog was announced twice (the
  `Dialog.Show` panel and the card inside it), and a full-height card for a one-row list. Found
  and filed separately: `JustifyContent(Center)` on an auto-height column inside a fixed-height
  row measures it half a row too tall and drops its child to the floor. Tests: 11 headless
  (two controls: no reveal, a growing list), Meridian's palette tests on the framework filter,
  Vesper's harness on `PaletteCommand`, Junction's toggle tests; driver shots of all three.
- **Shortcuts in the framework** — `KeyChord` and `ShortcutMap` (`src/HultUI.Core/Events`, docs
  in FOCUS.md). Junction's display-only `Shortcuts` table and Vesper's `Keybindings` keymap both
  became `ShortcutMap`s that dispatch and display read; Meridian, Reel, Aperture and the
  framework's `VideoPlayer` got one each. What the duplication hid, all now fixed: per-view keys
  matched the bare key and ignored modifiers — Ctrl+C on a selected branch checked it out,
  Ctrl+L pulled, Ctrl+S/Ctrl+W flipped the diff, Ctrl+D dropped a rebase commit or marked a
  to-do done, a focused video player took Ctrl+F; Vesper could rebind a command onto a key it
  kept for itself (Ctrl+C: the command never fired, or replaced copying) or onto ↑/Enter;
  Meridian advertised Ctrl+I and Ctrl+N with no handler and Ctrl+R that worked on one view;
  Junction's zoom read the Swedish + key as the US minus and zoomed out; hints were typed by hand
  in four notations ("Ctrl P", "Ctrl+P", "⌥↑↓", "Ctrl ↵") and went stale on a rebind. Now: exact
  modifiers (Super = Ctrl), letters by the layout's label, other keys by position, character
  chords for symbols; one notation (`Ctrl+Shift+P`, bare letters lower case) that reads back
  Vesper's saved spellings; rebinding that steals from rebindable shortcuts and refuses fixed
  ones, with the capture saying whose key it is. Tests: 52 Core, 3 Vesper, 2 Junction, 3 Meridian,
  1 Media, plus updated ones. Controls: modifier-blind letters fail the Junction chord test;
  positional zoom fails the Swedish + test; earlier ones in the Core suite. Left: the three
  command palettes and four `Kbd` chips are still per app; positional punctuation is labelled with
  its US symbol (`` ` `` is § on a Swedish keyboard); FindBar, the terminal and the web view keep
  their own chord checks.
- **Single instance in the framework** — `HultUI.SingleInstance` (+ `SecondLaunch`) replaces
  Reel's and Vesper's identical `SingleInstance.cs` and their named-mutex code. The copies' real
  faults, not just their duplication: they listened only after the window was built, and a
  launch that waited 500 ms gave up — so most of a multi-file "Open" landed nowhere; they exited
  whether or not the hand-off worked; a relative path (`vesper .`, `reel film.mkv` from a
  terminal) resolved against the running copy's folder; `Raise` was refused by Windows' foreground
  lock (Reel never raised, so a relaunch with no file did nothing visible); a pipe name taken
  elsewhere made the accept loop spin; and the pipe was machine-wide. Now: a lock file
  (`FileShare.None`, flock on Linux) per user (+ session on Windows), the pipe listening from
  `Claim` with launches kept until `Listen`, many senders at once, `CurrentUserOnly` both ends,
  an acknowledgement so `Forward` can return false and the launch opens its own window, the
  sender's working directory carried with its args, and `AllowSetForegroundWindow` for the
  running copy. Also fixed: `Window.Raise` restored unconditionally, which un-maximizes — every
  hand-off shrank a maximized Vesper. Linux: a listener per name shares one socket there, closed
  with the last of them, so the next listener is made before a connection is handed off (a burst
  lost launches on Linux until then). Apps keep their Windows-only gating and their options.
  Tests: 9 headless (Windows, and Linux under WSL Fedora ×3), 1 real-window (`Raise`); a
  cross-process probe: six launches during the first's start-up, all delivered in 23 ms each.
  Controls: no early queue fails the before-Listen test; a shared lock fails 6 of 9; unconditional
  restore fails the `Raise` test. Not verified: the foreground hand-off itself, which needs a
  launch the person started (a tool-started process has no foreground right to pass on).
- **Window placement** — `Window.Placement` (a `WindowPlacement` record: restored bounds plus
  maximized) to save on close and give back on start; Junction, Vesper (in its session file),
  Reel and Aperture now open where they were closed. Meridian persists nothing and is unchanged.
  Windows reads and moves the HWND's outer rectangle (`WindowsPlacement.cs`), because SDL's
  client-rect path is a caption height off under the custom frame; `GetWindowPlacement`'s
  workspace coordinates are converted by the window's monitor, as Chromium does. Elsewhere
  SDL3Window notes the restored bounds as the window moves, since SDL cannot report them for a
  maximized window. `WindowPlacement.FitTo` brings back bounds a changed desktop stranded.
  Three traps found on real windows: a move onto a monitor at another scale lands at the wrong
  size (SDL answers `WM_DPICHANGED` by keeping the old pixel size) — a second `SetWindowPos`
  fixes it; a process's first `ShowWindow` takes its command from `STARTUPINFO`, so the cloaked
  maximize is retried once; and a cloaked window gets `EXPOSED`, whose live-resize frame cleared
  the dirty flags so the loop never drew the frame that reveals it — live frames now wait until
  the window is shown. Tests: 13 Core (`FitTo` cases on this desk's two-monitor layout, JSON),
  3 headless, 4 real-window UiTests (both frames, every monitor, maximized-before-first-frame,
  restored bounds while maximized/minimized/full screen, missing monitor), app round trips in
  all four. Controls: no second `SetWindowPos` fails 3 of 4 UiTests; live frames before show
  fails the reveal test (once its first frame stopped being forced — a forced frame hid it);
  "always in reach" fails 7 of 13 Core; no restore in Vesper fails its test. Linux (SDL path)
  builds but was not run on a desktop: Wayland ignores the position by design.
- **App storage in the framework** — `HultUI.Core.Storage`: `AppFolders.Config/Data(app)` and
  `JsonFile<T>`, replacing four apps' own copies of "where do my files go" and "read and write
  the JSON". The copies had drifted where it mattered. Junction and Aperture wrote beside the file
  and swapped it in, keeping a `.bak`; Reel swapped with no backup; Vesper wrote its settings,
  session and bookmarks straight over the live files, so a crash mid-write could lose all three.
  The fresh-Linux-account fix (special folders that answer "" until they exist, which put
  settings in the working directory) lived only in Vesper and in the CEF profile path, as two
  copies. Now every file is written beside, flushed to disk, swapped in with the previous kept
  as `.bak`, and read back from the backup when the live file is unreadable — or missing, which
  is the moment between the swap's two renames. Neither ever throws; `Save` says whether it
  worked. Folders are never relative. `XDG_CONFIG_HOME` still counts on Windows, as it always
  did; it is how the tests move Junction's config. `XDG_DATA_HOME` only counts off Windows,
  as Vesper always had it. The WebView2 and CEF profiles use the same folders, unchanged in
  practice. Every path the apps used is the same as before. `VESPER_CONFIG_DIR` stays Vesper's.
  13 Core tests (four faults put back — no backup, no fallback, relative folders accepted, a
  failed save's staging file left — each caught), and the apps' settings suites unchanged:
  Junction's already pinned the backup, the corrupt-file fallback and the staging file.
  Window size and position followed separately (see Window placement).
- **One find bar** — `FindBar` (`src/HultUI.Core/Builders/FindBar.cs`) and its `FindBarTheme`
  replace four copies of the same bar around the same `TextSearch`: Junction's (diff and Files),
  Vesper's (viewer and comparison), Meridian's (response body) and the terminal sample's. The
  copies had drifted. Meridian's had no match-case or regex chips, no close button, and left the
  keyboard on the unmounted field when Escape closed it; only Junction and Meridian took `n`/`p`
  from the body; the terminal sample's had no seeding from the selection. The shared bar has
  all of it: Ctrl+F (seeded from the selection, starting at it; on an open bar, re-selecting
  the query), Escape, Enter/Shift+Enter, Alt+C/Alt+R, `n`/`p` with `LetterKeys` (not in an
  editable field), the keyboard handed back to `Body` on close, and the reveal held until the
  body is in the tree and run after layout (`RevealAfterLayout`, off for Vesper, whose
  `ScrollRangeIntoView` defers itself and lands on the same frame). Each app keeps its look
  through `Theme.FindBar` plus an `Icon` factory for its glyphs; the terminal sample shows the
  `FindBar(search, onChanged)` form over a search the terminal owns. It needed two seams on
  `IOverlayHost` with defaults, `RequestPostFrameCallback` and `IsInTree`, which `Window`
  already had.
  Two traps worth knowing. The host has to be found through the body's *ancestors*: a body
  mounted in this build has no `OverlayHost` until the style pass, which runs after
  `OnAfterBuild`, and Junction's reveal-delivery tests caught a reveal held for ever that way.
  And `RevealCurrent()` must not rebuild: it spent a reveal on the outgoing body.
  Found and fixed in `TextSearch` on the way: a scan that stopped at `MaxHits` returned before
  choosing its first match, so in every app a capped search read "0 of 20,000+" and tinted and
  scrolled to nothing until the first Enter.
  8 headless tests (five faults put back, each caught — the `n`/`p` one only after the test
  stopped sending text with no key down, which is not a keystroke), 5 Core status tests, and the
  apps' existing find suites, moved off reflection onto the public type.
- **`AlignItems.Baseline` does what it says** — it was in the enum, the builders and LAYOUT.md
  ("align children along their text baseline"), and the engine fell through to FlexStart for it.
  Now a row lines its baseline-aligned items up on their first baselines (CSS Flexbox §8.3/§9.4):
  the item reaching furthest above its baseline goes flush with the line's top, the rest are
  lowered to meet it, and the line grows when that pushes something past its bottom — text
  sitting on an icon's bottom edge hangs its descenders below it.
  Where a baseline is: `Element.MeasureBaseline(borderBox, ctx)`, answered by the ten built-in
  elements that draw text — text, field, area, checkbox/radio/toggle labels, dropdown, number,
  date and time fields — each from the same font and offsets its `Render` uses, through a new
  `IMeasureContext.MeasureFirstBaseline` (exact from the pipeline's first line in Skia, estimated
  from `FontMetrics` elsewhere). A flex container takes its first line's shared baseline or its
  first item's; anything else, a grid included, gets one synthesized at its border-box bottom.
  The trap was the measure pass: Phase 1 sizes are write-free and separate from layout, so a row
  that baseline alignment makes taller has to be measured taller too, or the parent reserves
  less than the row takes. So the baselines are computed measure-only (`LayoutEngine.Baseline.cs`),
  in both passes, before line cross sizes — and only for rows that ask. Column containers keep
  FlexStart, as CSS does. **Benchmarked**, because the first cut ran a pass over every row's items
  whether or not anything asked: three alternating A/B rounds showed append-1,000 about 3% slower,
  consistently (best-of 8.07–8.14 → 8.28–8.60 ms). The check now rides in `MeasureFlexItems`, the
  loop that already visits every item, and the pass runs only when it found something; the re-run
  put append-1,000 at 7.70–7.78 against main's 7.90–8.52 and create-10,000 level.
  8 Core layout tests (a font whose numbers check by hand) and 1 headless pixel test with real
  fonts: flat-bottomed capitals at 30px, 12px and in a 44px field end on one pixel row (±1),
  and with the alignment off the 12px word ends 19 rows higher. Four faults put back — no
  baselines, none in the measure pass, no line growth, no container baselines — each caught;
  the measure-pass one by exactly the predicted symptom, a parent sized 40 for a row laid out at 43.
- **A dialog holds the keyboard** — the overlay dialog could only be closed with the pointer,
  Tab walked straight out of it into the page behind (traversal always ran over the main tree,
  and an element not in it reads as "start from the top"), and with nothing focused the page's
  own shortcuts fired underneath it, because keys go to the focus or else the root.
  Overlays can now be **modal** (`ShowOverlay(..., modal: true)`; `Dialog.Show` always is).
  While one is open, keys go to it whenever the focus is not in it or above it, and Tab walks
  only its controls, always wrapping. Any overlay holding the focus is a Tab scope too, so Tab
  in a popover cycles there instead of jumping to the page. The dialog takes the keyboard when
  it opens (its first control, else the scrim, which is focusable but not a Tab stop), and
  Escape closes it with the default result (`DismissOnEscape`, on by default). The handler sits
  on the scrim, so whatever is inside sees Escape first and can keep it: a dropdown closing its
  list, or the web view's and SSH sample's panels, which answer on Escape themselves. The panel
  gets the `Dialog` role unless the content chose one, named by the title for Confirm/Alert.
  Focus going back to what had it on close was already there and still holds.
  9 headless tests, 2 Core. Four faults put back — no Escape handler, Tab over the main tree,
  keys following a focus behind the dialog, no focus on open — each caught (7, 1 and 3 tests;
  the first two were run together).
- **The wheel is shared out between nested scrollers** — every scroll view stopped the wheel
  event whether or not it moved, so a list at the end of its range kept the wheel from the page
  around it and a horizontal strip inside a vertical page ate the vertical wheel. Stopping
  turned out to cost more than nesting. In this framework `StopPropagation` also ends the
  element's *remaining* handlers, and owned handlers run first, so an app's own `ScrollEvent`
  handler on a scroll view never ran. That killed three app features nobody had noticed:
  Junction's and Vesper's sideways pan on a trackpad swipe or Shift+wheel (the diff and the
  viewer), and Vesper's brief view re-windowing its columns after a sideways scroll.
  Now `ScrollEvent` carries the sharing out. A scroller asks `IsFor(this)`, moves by
  `RemainingDeltaX/Y`, and reports with `Consume(this, movedX, movedY)`; an axis it could not
  move on is left for the next one out. The event bubbles all the way and nobody stops it. Four
  rules ride on that:
  - **Latching.** A gesture stays with the scroller its first event moved while events keep
    coming under 300 ms apart and the pointer stays over it (`WheelGesture`, kept by the
    window). Without it, handing on at the end of a range would make a flick down a list carry
    on with the page, and a list arriving under the pointer would take over a page scroll.
  - **Shift turns a vertical wheel sideways** (`ScrollDeltaX/Y`), as in a browser. It had to,
    or the pan handlers that can now run would have scrolled the diff diagonally.
  - **A wheel over an overlay is the overlay's.** The wheel path never hit-tested overlays, so
    a wheel over a dialog or a popup scrolled the page behind it.
  - **NumericUpDown and TimePicker** respect a latched gesture, so a settings page scrolled past
    a number field no longer changes it, and no longer step *down* on a sideways swipe, whose
    zero DeltaY read as "down".
  `ScrollSidewaysWith` now carries the wheel's sideways motion as well as the autoscroll's, so the
  three app lines that hung `HandleScroll` on a list are gone; `HandleScroll` follows the protocol
  for a pan whose content is not a scroll view's. Terminal and WebView keep the whole wheel as
  before, through `Consume` rather than a stop. LAYOUT.md "Nesting scroll views" has the rules
  and the three-step pattern for app code.
  11 headless tests, 9 Core, 1 in Junction (swipe and Shift+wheel pan the diff), and the old
  `ScrollEvent_StopsPropagation`, which pinned the bug, rewritten as two: the parent sees the
  motion already used, and sees it unused when the view could not move. Five faults
  were put back: the old handler (8 of 11 fail), latching off (4 plus 1 unit), and overlays,
  Shift and the zero-DeltaY step each caught by their own tests. The Junction test reports
  exactly the symptom with the old handler back.
  Follow-up, decided and done: **a horizontal view turns the vertical wheel sideways**
  (`VerticalWheelScrollsSideways`, on from `ScrollView.Horizontal()`), as Windows' list view
  does — Vesper's brief view did nothing on the wheel at all. It defers to the page: only when no
  scroll view around it can still move that way does it take the wheel, so the strip-in-a-page
  case above still scrolls the page. 3 headless tests (alone; in a page at its end; a vertical
  view never turns it) and 1 in Vesper, which wheels a 400-file brief view to its last column and
  asserts that column was built. Three faults put back — mapping off, no deference, every view
  mapping — each caught by its own test.
- **Undo and redo in text fields** — `TextInputElement` and `TextAreaElement` handled
  Ctrl+A/C/X/V and nothing else, so every field in every app lost an edit for good. Ctrl+Z,
  Ctrl+Y and Ctrl+Shift+Z now work, along with `Undo()` / `Redo()` / `CanUndo` / `CanRedo` and
  Undo/Redo at the top of the right-click menu. Both fields share `TextUndoHistory`, which stores
  each edit as one replace (start, removed, inserted) rather than a snapshot of the value, and
  groups edits the way word processors do: typing a word at a time, a run of Backspaces or Deletes
  as one step, paste/cut/selection-delete/IME commit each on their own, any caret move ending the
  run. Every edit in both fields now goes through a single `Replace`, which is what makes the
  history complete. If anything bypasses it, the next undo finds text the step does not describe
  and drops the history rather than splicing into the wrong place.
  Two decisions worth knowing. **A different value set from code clears the history**, as a
  browser does: its offsets describe text that is gone. The rebuild every keystroke causes in a
  controlled field patches in the *same* value and keeps it — the headless tests type a frame
  per key to prove that, since one `TypeText` call with no pump between characters would not.
  **Ctrl+letter chords now match the letter on the key** (`KeyDownEvent.LayoutKey`, as FOCUS.md
  told apps to), not its position. Matched by position, Ctrl+Z on a German keyboard would have
  redone. The older chords had the same bug where their keys move — Ctrl+A on French AZERTY, all
  four of Ctrl+A/C/X/V on Dvorak — and were moved over too.
  30 Core tests, 4 headless, and 1 in Meridian proving its URL and body editors round-trip their
  value exactly through the document (a document that normalised it would silently kill undo
  there). Five faults were put back and every one was caught.
- **Middle-click autoscroll pans an unwrapped diff sideways** — reported from Junction: the
  scrollbar under an unwrapped diff worked and the gesture that means the same thing did not.
  The diff's sideways position lives in `HorizontalPan` (a margin inside a clipping cell)
  because `VirtualListElement` pins its content width to the viewport, so the list's own
  `ScrollOffsetX` can never leave zero — and that offset is what the autoscroll drove. The
  wheel never had this problem because it arrives as an event the app can route; the autoscroll
  is driven straight from the frame loop and never reaches app code. So a scroll view can now be
  told who really scrolls it sideways (`ISidewaysScroller` / `SidewaysScroller` /
  `ScrollSidewaysWith`), `HorizontalPan` implements it, and `ScrollBy` sends the horizontal half
  there — `CanAutoScroll` asks it too, so a short, wide list still claims the press. Junction's
  diff and both of Vesper's panned lists (diff and viewer) are wired. The retired Junction test
  that pinned "sideways does nothing, and that is not an oversight" is the positive case now.
  4 framework tests, 2 in Junction, 1 in Vesper — each verified by disabling the delegate path.
- **Two things wrong with the accent swatches, one of them mine** — both reported by eye and both
  confirmed by measuring the rendered frame, which is the only place either showed.
  The selected swatch was not ringed, it was *shrunk*. Its ring was a 3px border in `p.Card`, and
  a border draws inside the box — so against a card background it was invisible, and the only
  visible effect of selecting a swatch was its colour going from 38px to 34px. Now an `Outline`
  in the accent colour, held 2px off the square: both states measure 38px and the ring is the
  thing the old comment said it wanted, "the accent showing around it".
  The second was a regression from moving the border inside the box a commit earlier. With the
  stroke's outer edge landing exactly on the fill's antialiased arc, two antialiased curves share
  one boundary and blend instead of one covering the other — so the fill showed through the
  border at the corners while the straight runs stayed clean. Measured on the blue swatch: the
  border pixel read #3A5662 at the corner against #252525 along the edge. The fill is now pulled
  back under the border by half its width, which puts its soft edge in the middle of the band;
  half rather than the whole width, so the fill still runs under most of the border, which is
  what a translucent border composites against. Back to #2E373B, and the test asserts the pixels
  outside the box outline carry no fill at all — red fill, blue border, white ground, so any blue
  channel under 255 out there is fill that escaped.
  Neither was caught by the snapping rig, which is the point: it asserts single-colour edges, and
  both of these are about two colours meeting. Four faults, all four caught.
- **And the fills** — the follow-up the border pass left open, and the call sites decided the
  shape of it rather than theory. `DrawRect` turns out to be carets, selection bands and dividers,
  all hard-edged; those snap. `DrawLine` turns out to be almost entirely *diagonals* — the
  checkmark in a checkbox, the two legs of a chevron — and snapping their endpoints kinks the
  glyph instead of sharpening it, so only axis-aligned lines snap and a diagonal is explicitly
  left alone. Backgrounds snap too, including gradients, so a gradient-filled box and a solid one
  beside it do not sit half a pixel apart. Shadows, text and images are untouched: a blurred edge
  has nothing to gain, and text stays subpixel-positioned because that is what keeps it smooth.
  The clamp is on "was asked for at all", not on "is small". Rounding both edges of a 0.3-DIP rect
  to the same pixel erases it, and a vanished caret is worse than a soft one — but the same clamp
  on a genuinely empty rect would smear a one-pixel band, and an unselected range is a zero-width
  rect drawn every frame a text field has focus.
  A fill's size comes from its snapped edges, so it is position-dependent: a 1-DIP rule at 1.5x is
  one device pixel at y=10.4 and two at y=10.0. That is the rule working — deriving a
  position-independent size is exactly what opens seams between things that should tile — and the
  test asserting a single number for 1.5x was that mistake, caught by failing against correct
  code. A line is different and keeps a position-independent weight: it has a width rather than
  two edges, so there is nothing to derive one from.
  12 more tests, 31 in the rig now. Five faults put back, four caught; the fifth — backgrounds not
  snapped — was not, because the seam in that test sat at x=20, which is already a whole pixel at
  both 1.25x and 1.5x. Moved it off the grid.
  Measured after: the sample's caret is two solid pixels and its focus underline two solid accent
  pixels, both previously soft. The benchmark's render pass moved 0.3→0.2ms and 0.6→0.7ms, which
  is under its noise floor — layout, untouched, moved 87.1→89.1 in the same runs.
- **A border lands on whole device pixels** — the crispness half of the WinUI comparison, and it
  turned out not to be a fractional-scale problem at all. Measured first: a 1-DIP border on a box
  edge came out `128 128` at 100% — two half-covered pixels — `192 192` at 150%, `32 255 32` at
  125%. Nothing was ever crisp, at any scale, because the stroke was centred on the box edge; it
  straddled it and spilled half its width outside the border box. WinUI paints two fully-covered
  pixels at 150% and nothing either side.
  The rule is CSS's and the order is the whole trick: snap the *edges* onto the device grid, then
  take the width from the snapped edges — never round position and size independently. Two boxes
  sharing a coordinate snap it to the same pixel, so a gap cannot open between things that were
  flush; rounding them apart is the classic way to get a one-pixel seam. Because the edges land
  first, the width comes out the same on all four sides, which keeps the uniform fast path.
  Arcs are deliberately left antialiased — snapping a curve steps it, and the Gallery antialiases
  its corners too (straight run two solid pixels, arc through #1F1F1F #1E1E1E #232323). This first
  pass snapped borders only; fills followed (below).
  The test rig came first and it had to, because every other pixel test in the project runs at
  scale 1 where the interesting half is invisible: 19 tests at 1.0, 1.25, 1.5 and 2.0, of which 13
  failed against the old renderer. Four faults were put back; three were caught and the fourth —
  dropping the "a hairline never rounds away to nothing" clamp — was not, because no case had a
  width under half a pixel. Added one.
  The stroke moving from centred to inset also moved the `Outline` ring by half its width, which
  the outline's own pixel tests caught. Its geometry is simpler for it: inflate to the ring's
  outer edge and let the band fall inward.
  Verified end to end: the sample's card border went from `#262626 / #232323 / #2C2C2C` — partial,
  solid, partial — to one solid pixel at 100%, and to exactly two at 150%, matching the Gallery.
  The app driver takes `--scale` now, so that check is repeatable rather than a one-off.
- **An outline, so a selected thing does not resize** — `Outline(width, color, offset)` on any
  box: a ring drawn outside the border box that the layout engine never reads. CSS's `outline`,
  and it exists for the reason CSS has one. A state that adds a border makes an element bigger,
  and an element that sizes to its content then moves, along with everything packed beside it.
  Junction's diff toolbar did exactly that — measured, the Split button slid 2px left the moment
  Wrap was turned on, and Unified moved with it without changing state at all.
  The tempting diagnosis is `content-box` sizing and it is wrong. HultUI is already border-box
  (`finalW = resolvedWidth ?? content + padding + border`, so a declared width *is* the border
  box) — but that only decides how a *declared* size is read, and these buttons declare none.
  `content + padding + border` is what an auto-sized box is under either model; there is nothing
  for a box model to reinterpret. Which is also why `ViewTab` needed the transparent-border
  trick even here, and has a comment saying so: the big tabs never moved because someone
  remembered. An outline cannot be forgotten wrong.
  Geometry is CSS's: offset 0 puts the ring immediately outside the border edge, negative pulls
  it in, so `offset: -width` lands exactly where a border was — which is what the diff *rows*
  needed, since rows touch and a ring outside would be painted over by the next one.
  Eleven tests. Four faults were put back to check them and two were not caught the first time:
  the layout-neutrality test used declared widths, which is precisely the case where border-box
  hides the problem, and the corner test asserted pixels that were reasoned out rather than
  measured — it failed against correct code. Both rewritten: content-sized cells with a border
  as the control, and a corner sampled off the real diagonal (grown 14–21, un-grown 12–19, both
  ends asserted). A fifth fault — the border put back in Junction — is caught by three tests
  that pin the toolbar's boxes across a toggle.
  Eight call sites converted across Junction, Vesper and the Fluent sample. Three more matched
  the pattern and were left alone after checking: the colour swatch, the Fluent accent swatch and
  the Fluent search box all declare their size, so border-box already holds them still, and the
  last two spend the border inward on purpose.
- **The pane's outline is darker than the backdrop, not lighter** — reported by eye from a
  side-by-side, and the cause is that `CardStrokeColorDefault` is not a colour: it is black at
  10% (6% in light), and a WinUI border paints its background *inside* the stroke, so the stroke
  composites over whatever is behind the element. One token, two values. Measured on the
  Gallery: a card on the #272727 pane is outlined #232323, and the pane itself on the #202020
  backdrop is outlined #1D1D1D — below the backdrop on both sides of it. The sample used the
  card's value everywhere, which put a line *lighter* than the backdrop down the side of the
  window; that reads as a seam rather than an edge, and it is the first thing that separates in
  a side-by-side. New `LayerStroke`, used by the content frame and nothing else.
  A border here paints over its own element's fill, the way CSS does with `border-box`, so the
  compositing is done up front rather than by the renderer. The tests derive both values from
  the alpha and the surface rather than comparing to a hex, so retuning `Layer` or `Backdrop`
  without recomputing its stroke fails instead of drifting. The alphas are measured too, not
  recalled: 35/39 and 29/32 in dark bracket `#19000000`, and because light Mica lets the
  wallpaper through, the light theme was measured as ratios against its own neighbours —
  236/251 and 232/247, both `#0F000000`. That also caught the light card stroke, which had been
  derived over the backdrop rather than over the pane: #E5E5E5 where the rule says #EAEAEA.
- **The border stopped being drawn at the corner** — reported by eye ("it looks like the border
  just kind of stops"), and a regression in the per-side border work below: clipping each side to
  its own 1px band is right for the straight run and wrong for the arc, which bulges out of both
  of the bands that meet at it. A single-sided border never showed it, which is why four pixel
  tests missed it; the sample's content frame has all four sides and an 8px radius, so it showed
  it four times. Each side now also strokes its half of every corner it touches, split on the
  diagonal the way CSS splits a mitre, so two adjacent sides join and one side alone still stops
  halfway round. The 5th renderer test is the one that matters: it asserts a top-and-left border
  paints the arc between them, and it is the only one of the five that fails when the corner
  halves are removed.
- **The backdrop, measured rather than assumed** — the sample put a vertical gradient behind the
  title bar and nav pane on the theory that Mica fades. It does not: the whole top and left of
  the Gallery is a uniform `#202020`, cursor parked outside the window so nothing is a hover
  artefact. One flat colour now, and the two nav-item states go with it — hover `#2D2D2D`,
  selected-and-hovered `#292929`. That last pair is backwards from the intuition and was wrong
  here for the same reason: `SubtleFillColorTertiary` is *closer to the ground* than Secondary,
  so a second state makes an item quieter, not louder. The sample had it at `#323232`, brighter
  than hover. Asserted in both themes, because "quieter" means darker on one and lighter on the
  other.
- **A per-side border follows the corner radius** — it used to paint each edge as a plain
  rectangle, which is exact on a square box and wrong on a rounded one: the bar runs corner to
  corner and sticks out past the arc in two square tabs. Every single-sided border on a rounded
  control had them — the sample's search box on all four corners, the buttons' two-tone halves,
  the Effects sample's border cards. The rounded path now strokes the real outline clipped to one
  edge's band at a time, so each side follows its own curve and a corner is drawn by whichever of
  its two sides are present; the square path is untouched, because that is what every divider in
  the shipped apps draws and moving it half a pixel would show up everywhere at once. This also
  retires the workaround `TextFieldUnderline` was written around. 4 renderer pixel tests — and
  the second one took two attempts: "the corner stays clear" alone would pass for a bar that had
  merely been shortened, so it now asserts that the run *narrows* through the band (13–46 at the
  top, 18–41 at the bottom), which is what tells a curve from a rectangle.
- **A text box is not a button with a caret** — `TextInputTheme.UnderlineColor` /
  `FocusUnderlineColor` / `UnderlineThickness` / `FocusUnderlineThickness` / `FocusBackground`,
  read by both `TextInputElement` and `TextAreaElement`. Measured, because a text box turned out
  to be the one control whose border is *not* the two-tone every other control has: at rest a
  dark WinUI text box is `#303030` on three sides and `#9A9A9A` along the bottom —
  `ControlStrongFillColorDefault`, a different token entirely, and the loudest thing about the
  control. On focus the bottom becomes a 2px accent, the other three sides do not move, and the
  fill goes *darker* to `ControlFillColorInputActive`. Lighting the whole ring is what the sample
  did and what the single `FocusBorderColor` invites; all three assertions in the sample test are
  therefore negative ones. Drawn as a clipped stroke rather than a single-sided border, because
  the renderer draws those as plain rectangles and a bottom-only border on a rounded field pokes
  out past both corners. Null underline draws nothing, so no existing app moves. 6 headless tests
  + 1 in the sample.
- **What the Design tab said we had wrong** — the type ramp is four columns, not three. The
  Gallery publishes each style as size, weight, *optical size* and *line height*; the sample had
  taken the first two. Caption belongs on **Segoe UI Variable Small**, not Text, and every style
  carries a published line height (12/16, 14/20, 20/28, 28/36) that the sample was leaving to the
  font's own metrics — visible the moment a description wraps, which in a page of settings cards
  is most of them. Also from Design: a card's outline *recedes* — `CardStrokeColorDefault` is
  black at 10% in dark mode, so it is darker than both the card and the page; the sample had it
  lighter, which makes a page of cards look like a page of buttons. And the Spacing page's 4px
  grid caught two off-grid values (a 28px page padding and a 1px label margin).
  Checked and deliberately *not* changed: the Geometry page lists 8px `OverlayCornerRadius` for
  "app windows, flyouts, cards and dialogs" and 4px `ControlCornerRadius` for "in-page elements
  such as controls and list backplates". A real Settings card measures 4px, so the doc's "card"
  is a top-level container and a settings row is a list backplate. The sample follows the app it
  imitates rather than the sentence.
- **The InfoBar's severity mark is a filled badge** — a 16px disc in the severity colour with the
  symbol knocked out of it in `TextOnAccent`, black on dark and white on light. The sample had
  the outlined ⓘ-style glyphs, which is the obvious mistake and survived until the two were put
  side by side. The tell is in the font: the Segoe glyphs WinUI uses here (`F13C`–`F13F`) are the
  bare symbols — an exclamation mark, a cross, a tick, a letter i — with no ring or disc of their
  own, so the disc has to be drawn behind them. Sized by measurement too: those glyphs sit well
  inside their em box, so the first attempt at "10px" came out about five pixels tall and read as
  a smudge; 16 matches. Without the icon font there is nothing to knock out (Phosphor has no bare
  "i"), so the fallback keeps the outlined icon and the test asserts that branch too. Found while
  writing it: the InfoBar's accessible name was `"{title}. {message}"` and every title already
  ends in a full stop, so a screen reader read "Saved.. Your changes were written to disk."
- **The two-ring focus visual** — `FocusVisualTheme` (outer/inner colour and thickness, margin,
  `KeyboardOnly`) and `Element.ShowsFocusVisual`, drawn by the window around whatever holds
  focus. Two rings rather than one because a single ring can only contrast with one background:
  white vanishes on a light card, dark vanishes on a dark button, and an indicator that
  disappears on some controls is worse than none because you stop looking for it. The defaults
  derive from the palette and get the relationship right in both — `TextColor` outside,
  `Background` inside. `KeyboardOnly` is on by default and is the difference between a focus ring
  and an irritation: a click focuses a control too, and a ring that lights on every click reads
  as something stuck. The window drives it because the visual sits *outside* the element's
  bounds — drawn from inside the subtree it would be clipped by any scroll view and painted over
  by later siblings — and because only the window sees whether focus arrived from Tab or from a
  press. Opt-in per element for the same reason it is drawn outside: it needs clearance the app
  knows about, and a control already showing focus its own way would say it twice.
  The sample's composed controls take it, and had to become controls first: a box is neither
  focusable nor listening to the keyboard, so `F.Focusable` gives them Tab, the ring, and
  Space/Enter. That is a real accessibility gap closed, not decoration — the whole gallery was
  mouse-only, which looks identical in a screenshot, which is why it has a test.
  7 headless tests + 1 in the sample, each verified by putting the fault back.
- **One scrollbar, drawn once** — `Scrollbar`: the colours, the widths, the hover state, the
  thumb-length formula, the track-click centring, the drag maths and the painting, in one place.
  There were two copies. `ScrollViewElement` scrolls child elements and `TextAreaElement` scrolls
  its own wrapped rows, so they share no base class below `BoxElement`, and each had grown its
  own version of the same arithmetic — they stayed in step through the rounded-corner change by
  luck and a grep. The extraction was the price of asking for expand-on-hover in the text area:
  the alternative was a third copy of it. Both elements keep their public `Scrollbar*` properties
  as facades, so the two apps that set `ScrollbarWidth` per element are untouched, and the scroll
  view now holds one bar per axis — pointing at the vertical bar no longer expands the horizontal
  one with it. `IScrollbarDraggable.SetScrollbarHover` takes a position rather than a bool for
  the same reason. Meridian's body editor and Junction's diffs got the expansion without a line
  of app code. 2 more headless tests; the existing 8 carry the rest, and both of the assertions
  that read a width from the draw log needed a leading space to be worth anything — `12x150`
  contains `2x150`, so "the bar is 2px" was passing while it was 12.
- **The scrollbar expands under the pointer** — the behaviour half of the Gallery comparison.
  `ScrollbarExpandOnHover` (on by default), `ScrollbarExpandedWidth` (12), `ScrollbarTrackHoverColor`
  and `ScrollbarExpandDuration`: the track widens from the resting width to the expanded one and
  the thumb to half of it — a 12px bar around a 6px thumb, the Windows proportion — over 120ms.
  A view that says nothing about hover keeps its resting look exactly and only gains a wider
  track when reached for. The Fluent sample takes the real thing: a 2px hairline with no track,
  widening to 12px with one.
  The load-bearing part is that the region counting as *on the bar* is the expanded width even at
  rest. Without it a 2px bar is a 2px target, nothing would ever expand it, and the hairline
  would be decoration rather than a control — it is asserted in both suites for that reason.
  Hover has to be delivered by the window (`IScrollbarDraggable.SetScrollbarHover`, default
  no-op) rather than by a pointer handler: the bar is painted *over* its own content, so the
  element under the pointer there is whatever the bar covers, and a handler on the scroll view
  would only fire where the bar happened to sit over empty space. The window already decides
  this by geometry one line earlier, to pick the cursor. It also owns the leaving case — a view
  the pointer has left is no longer on the path being walked, so it would never hear about it.
  6 headless tests + 1 in the sample, each verified by putting the fault back. `TextAreaElement`
  keeps its plain bar; its scrollbar is a second copy of this code and deserves extracting before
  it gets a third feature.
- **Three things the WinUI Gallery said we had wrong** — the sample was built from the published
  colour table; this was the first time it was held up against the real thing, running on the
  same machine. Captured with `PrintWindow(PW_RENDERFULLCONTENT)` and driven through UIA, so no
  screen grabs and no synthesized input, and every finding below is a pixel measurement rather
  than an impression.
  - **The scrollbar thumb is a pill.** Ours drew `DrawRect` — square ends, plus an always-on
    track. WinUI's resting bar is a 2px rounded thumb with *no* track, in
    `ControlStrongFillColorDefault`, that widens under the pointer. `ScrollbarRadius` on
    `ScrollViewElement` and `TextAreaElement` now defaults to half the width, so every app gets
    the pill; set 0 for the old square ends. The expand-on-hover behaviour is not implemented,
    which is why the sample keeps a grabbable 6px rather than a 2px target.
  - **The two-tone control border ran the wrong way in dark mode.** Measured: a dark Gallery
    button is fill `#2D2D2D`, top `#353535`, bottom `#303030` — the *top* is the highlight. The
    rule is that the bottom edge always recedes, which in light means darker and in dark means
    the top is brighter; the sample had the bottom heavier in both. Right for light, backwards
    for dark, and invisible without the two windows side by side.
  - **The off toggle's knob was too dim and its track too empty.** WinUI uses
    `TextFillColorSecondary` (78.6% white) for the knob — brighter than the
    `ControlStrongFillColorDefault` ring around it — and a faint `ControlAltFillColorSecondary`
    in the track rather than nothing.
  Confirmed *correct* while measuring, which is worth as much: the stroked chevron, black text
  and a black knob on a dark-mode accent, the slider's accent-inside-a-ring thumb, 32px controls,
  36px nav items and the 3x16 selection pill. 4 headless tests, 2 in the sample, each verified by
  putting the fault back.
- **A text input can hand its box to the caller** — `TextInputElement.DrawsChrome`, set through
  the `Chromeless()` builder: the theme's background, border, corner radius and padding are left
  alone so the surrounding control owns them. There was no way to get one before, and the way it
  failed was the bad kind — `ApplyTheme` runs on every style pass, so a background set on the
  instance survived until the next pass and then reverted, which reads as the setter not working
  rather than as the theme winning a race. The text colours (placeholder, caret, selection) still
  come from the theme, because those are not chrome. Written for the Fluent sample's search
  field, which needs a magnifier inside the box, but it is the same shape any cell editor or
  composed combo box needs. The sample also shows what it costs: the focus ring now belongs to
  the wrapper, and an element only gets focus styles for its own focus, so focus has to be
  carried in app state and focusing the field rebuilds the pane — which is fine, and is pinned by
  a test that types into it and asserts the input was *patched* rather than replaced. 4 headless
  tests + 2 in the sample. The `PatchFrom` one is worth a look if you write another: asserting a
  chromeless input stays chromeless passes with the copy deleted, because the live element was
  already chromeless — only a rebuild that changes its mind has anything to say.
- **A scroll view that had not been painted could not be scrolled** — its extent was computed
  by the *renderer*, and the renderer culls: anything outside a clipping ancestor is skipped
  entirely, so such a view reported a content size of zero. That zero is not inert, because the
  offset setters clamp against it — setting `ScrollOffsetY` on one silently did nothing, which
  is the worse of the two ways to be wrong. Wheeling was never the broken path (the wheel only
  arrives once the pane is on screen, and being on screen is what made it measurable); what
  broke is everything that scrolls a view *before* it is painted — restoring a saved offset,
  revealing a search hit, scrolling an item into view on the frame the pane is built. A vertical
  scroll view nested inside another vertical one hit it every time, because whatever sits above
  it in the outer view is usually tall enough to push it outside the outer's clip on the first
  frame; it arrived permanently unable to scroll, which is how this surfaced. The extent is now
  computed by the layout pass, which is the pass that actually knows where the children ended
  up. `UpdateScrollExtent` (pure: measure and clamp) split from `UpdateContentSize` (the render
  hook), because `VirtualListElement` also realizes and lays out its visible rows there and that
  cannot run from inside the layout pass it would re-enter. 5 headless tests, 3 of which fail
  with the layout-pass call removed — and the two that do not are labelled as the coverage they
  are. Still open, and a different complaint: an inner view swallows the wheel once it has
  nothing left to give instead of handing the rest to the page.
- **The WinUI look, as a sample** — `samples/HultUI.Samples.Fluent`: a Windows 11
  Settings-shaped app — borderless window, caption bar drawn as elements, NavigationView with
  the 3x16 accent selection pill, a content frame with one rounded corner, settings cards,
  toggle switches, InfoBars, an expander, a controls gallery, light/dark and six Windows accent
  presets. No framework feature was added for the shape of it; the whole design language turned
  out to be arithmetic (32px controls, 4px radii, 68px rows) plus the two details that actually
  carry the resemblance. Those are worth naming because both are easy to lose: WinUI's control
  border is a *vertical gradient*, darker along the bottom, which is what makes a Fluent button
  look like it rests on the page — a box takes one border colour, so a button is two nested
  boxes, outer drawing the bottom edge and inner the other three. And in dark mode the accent
  is a pale blue with *black* text on it; getting that inversion backwards is the fastest way to
  look like something else. Colours carry Microsoft's own names (`CardBackgroundFillColorDefault`,
  `ControlStrokeColorSecondary`) so the sample can be checked against the published table.
  Icons are Segoe Fluent Icons where the font is installed and Phosphor where it is not —
  probed by file, because a missing family does not fail at the font stack, it silently draws
  blank boxes. Mica is the one thing faked, and the About page says so. 9 tests, each verified
  by putting the fault back; drivable as `--app fluent [--dark false] [--accent 0-5]`.
- **The slider is themable** — `SliderTheme` (`TrackColor` / `FillColor` / `ThumbColor` /
  `ThumbBorderColor`, all nullable and all falling back to what the control already painted).
  It was the last interactive control reading `theme.Primary` and `theme.Border` straight,
  so an app whose card borders are a hairline and whose slider track is a mid grey had to pick
  which one to get right. Sizes stay per-instance on purpose:
  `ApplyTheme` runs on every style pass, so a themed size would overwrite whatever the caller
  set, and being a size it would need a layout invalidation with it. 3 headless tests, one of
  which is the one that matters — an unthemed slider paints exactly what it did before.
- **A dropdown's list is a stroked chevron away from Fluent** — `DropdownTheme.ChevronShape`
  (`Triangle` | `Stroke`) and `ChevronStrokeWidth`. The drop mark was the glyph `▾` typed from
  the control's own font; every current design system draws a thin V instead, and a glyph is
  the wrong mechanism for it — the shape then depends on what the UI font's designer thought a
  small solid triangle looks like, and on the font having one at all. `Stroke` is two
  `DrawLine` calls at 0.62em. The default stays `Triangle`, because changing it would repaint
  every dropdown and date picker in every app that never asked; opt in per theme. The
  painting moved to a shared `ChevronPainter` — the dropdown and the date picker had a copy
  each of the same constant and the same six lines, which is how two controls that are supposed
  to look identical stop looking identical. 3 headless tests.
- **Nothing knew where an element actually was** — `Element.ScreenBorderBox` /
  `ScreenContentBox` / `AncestorScrollOffset`. A scroll view does not move its children: their
  boxes are computed once and stay where the content puts them, and scrolling is a translate
  applied at paint time and mirrored in the hit tester. So everything that positions itself
  *against* an element needed to subtract the ancestors' scroll — and four places had their own
  copy of that walk while three more had simply forgotten it. The three that had forgotten:
  a dropdown and a date picker opened their popup wherever the control *would* be if nothing
  had scrolled (on a long page, most of a screen away from the control it belongs to — this is
  the one that was reported), a tooltip appeared beside the wrong row, and
  `HeadlessWindow.Click(element)` fired into whatever else was at that point. That last one is
  the worst shape a defect can have: a click that hits nothing is not an error, so the test
  around it passed with its handler never having run. `DoubleClick`/`RightClick`/
  `HeadlessElement.Click` all shared `CenterOf`, so all three were wrong the same way. The
  screenshot driver had it too, and gained a `wheel:` step to reach anything below the fold.
  7 headless tests, each verified by putting the offset back. Related, found while writing
  them and *not* fixed: a vertical scroll view nested directly inside another vertical one
  measures its content as zero and cannot scroll at all.
- **Autoscroll reaches text areas** — the gesture matched on `ScrollViewElement`, which is
  what carries a *list*. A wall of text is a `TextAreaElement`: it scrolls its own wrapped
  rows rather than child elements, and is a `BoxElement`. So every list in every app scrolled
  under a held middle button and the pane those lists exist to open sat still — Meridian's
  response body most visibly, Junction's Files body beside it. A reader has no way to tell the
  two kinds of scrolling thing apart, so the window now looks for `IAutoScrollable`
  (`MiddleClickAutoScroll` / `CanAutoScroll` / `ScrollOffsetY` / `ScrollBy`) and both answer to
  it; implement it on anything else that scrolls content it owns. `TextAreaElement.ScrollOffsetY`
  became public and clamping on the way (it was internal, and three test files reflected at it).
  The trap to remember is the shape, not the fix: a capability found by class name silently
  excludes everything that does the same job differently. 8 framework tests, 2 in Meridian,
  2 in Junction — each verified by putting the class-name match back.
- **Middle-click autoscroll** — holding the middle button in any `ScrollViewElement` (and so
  any `VirtualList`) anchors an origin and turns the pointer's distance from it into a
  *speed*, the way Windows does: the view keeps moving while the button is held even though
  no further input arrives, faster the further out, and it stops when the pointer comes back
  inside the dead zone or the button is released. A ring is drawn at the anchor so the zone
  you have to return to is the circle you can see. On by default (`MiddleClickAutoScroll`),
  because in a list the middle button otherwise does nothing at all; a view with nothing to
  scroll never claims the press, so the button keeps whatever meaning the app gave it (Vesper
  closes tabs with it). A gesture that scrolled swallows its own click, a press that never
  left the dead zone still clicks, and the release is dispatched either way so nothing is
  left holding a pressed `:active` style. The scroll runs from the frame loop rather than
  from pointer moves — `NeedsRender` keeps frames coming while the button is down — and the
  captured view is re-resolved if a rebuild reconciles it away. 11 headless tests.
- **Date and time picker polish** — the calendar's labels are centred in their cells.
  `IRenderContext.DrawText` lays lines out from the top of the rect it is given
  (`TextAlign` is horizontal only), so every label in a cell taller than its own line sat
  high in it by the leftover height. The time field also stopped borrowing the dropdown's
  horizontal padding (`TimeFieldTheme.PaddingX`): a dropdown's padding sits beside a sentence
  of arbitrary length, and around a fixed handful of digits it read as a field several
  characters wider than it had content for. Its segments are now centred in the field too —
  measure reserved padding either side that the render never applied, so the digits sat flush
  against the left border with all the slack on the right.
- **Date and time pickers** — `CalendarElement` (a month grid: header, weekday headings, six-by-seven days, drawn rather than composed — forty-two boxes rebuilt to move a hover highlight is forty-two allocations, and flex is the wrong tool for a grid of equal cells), `DatePickerElement` (a field that opens the calendar in an overlay and keeps focus itself, forwarding the arrows in, per the W3C combobox pattern), and `TimePickerElement` (a segmented HH:mm[:ss][ AM/PM] spinner: Left/Right pick a segment, Up/Down step it, digits type into it, the wheel nudges it, A/P set the half of the day). `Today` and `Culture` are properties rather than reads of the ambient clock and locale, so a rendered picker can be compared against a baseline that does not expire and does not depend on the developer's country. Themed by `CalendarTheme` and `TimeFieldTheme`; both fields borrow `DropdownTheme` for their box so a form does not end up with three differently-shaped controls in a row. Builders: `MonthCalendar` (not `Calendar` — `System.Globalization.Calendar` owns that name in exactly the files that format dates), `DatePicker`, `TimePicker`. 48 headless tests; `samples/HultUI.Samples.DateTime`.
- **Repeating tweens** — `TweenBuilder.Repeat(n)` / `.Loop()` / `.Yoyo()`. The cycle and its phase are derived from elapsed time rather than accumulated per cycle, so a repeat holds its period: a restart that dropped the frame's overshoot would quantise every cycle up to a whole number of frames, and a spinner built that way visibly slows down and drifts. A finite repeat ends on its end value; a loop never renders the terminal frame at all. Junction's Fetch/Pull/Push buttons are the first consumer.
- **Headless mode** (`HultUI.Headless`) — apps run with no window, GL context, or display. `IPlatformWindow`/`IWindowChrome`/`IWindowingPlatform` in `HultUI.Core.Platform` decouple `Window`/`Application` from SDL3 (which now implements them); `InputEvent` moved to `HultUI.Core.Events`. `SkiaRasterRenderSurface` renders into a CPU `SKSurface` reusing the same render/measure contexts as the GL path, with `Snapshot`/`ReadPixels`. `HeadlessSession` drives a manual frame pump on a virtual clock, so animations and tooltip delays advance deterministically; `PointerState` takes a `TimeProvider` so double-click timing does too. Input injects through the same funnel as real SDL events (`Click`/`TypeText`/`Drag`/`Wheel`/`PressKey`); queries run against fresh accessibility-tree snapshots (`GetByText`/`FindByRole`/`FindByAutomationId`), so a test can only find what assistive tech can. 42 tests, green on Windows and on a display-less Linux runner. Docs: `docs/HEADLESS.md`.
- **Display-list snapshot testing** — `RecordingRenderContext` decorates the real render context and captures every drawing call as readable text, so a regression diff says what moved instead of handing you two images, and baselines delta-compress in git. `FixedMetricsMeasureContext` derives text geometry from font size alone for baselines that hold across operating systems. Rendering still happens, so pixel capture works alongside it.
- **Cross-platform CI** — `test-linux` job runs `HultUI.CrossPlatform.slnf` (the solution minus the Windows-only `HultUI.UiTests`) on `ubuntu-latest`, proving the framework is display-free. The FlaUI UI tests stay excluded from CI; the headless suite is what gates interaction coverage there now.
- **Flex shrink / scroll-view collapse** — a scroll container's indefinite main axis now propagates into nested flex containers, so content overflows and scrolls instead of collapsing to the viewport (`FlexShrink(0)` is no longer needed as a workaround; existing ones are redundant). Percentages against an indefinite size fall back to auto, per CSS. Aligning the flex Phase 1 and Phase 6 constraints also fixed a cache mismatch that laid subtrees out twice — the benchmark's "remove row" went 10.2ms → 4.3ms.

- **Linux AT-SPI2 per-object D-Bus export** — `LinuxAccessibilityBridge` now connects to the a11y bus (`org.a11y.Bus.GetAddress`), registers via `org.a11y.atspi.Socket.Embed`, and exports the whole accessible tree through a single `IPathMethodHandler` over `/org/a11y/atspi/accessible/*` plus a `Cache.GetItems` bulk-fetch. Implements `Accessible`, `Component`, `Application`, `Action`, `Properties`, `Peer`, and `Introspectable`; emits `children-changed`, `state-changed` (focused / checked / enabled), and `property-change` (name / description / value) signals driven by the render-side `TreeDiff`. The served tree is an immutable snapshot swapped on each rebuild, so the bus-thread handlers read it lock-free. Verified on a live GNOME a11y stack with `pyatspi` (Orca's client): full tree / name / role / extents / interfaces round-trip and change signals delivered. 24 unit tests pin the ABI role numbers — the scaffold's guesses were wrong (push button 25→43, check box 45→7, text 40→61, …), exactly the class of bug on-desktop verification exists to catch. Also emits `Cache.AddAccessible`/`RemoveAccessible` (verified: 5-node burst at startup, well-formed on the wire) to keep client caches warm, and seeds a minimal application root *before* `Embed` so the registry's immediate post-Embed probe resolves instead of answering `UnknownObject`. Harness: `samples/AtSpiVerify` drives it headlessly (no display) and `atspi_inspect.py` / `atspi_events.py` read it back.
- **Accessibility bridge scaffolding** — Shared core in `HultUI.Core/Accessibility/`: `AccessibilityTreeDiffer` + `TreeDiff`, extended `AccessibilityNode` (IsFocusable, IsFocused, IsEnabled, IsToggled, Value, Parent), `IElementPropertyObserver` hook on `Element`. Checkbox/Radio/TextInput/TextArea fire notifications on value change. `Window` rebuilds + diffs the tree only when `DirtyFlags.Children` is set and a bridge is active. 16 new differ/builder tests (37 accessibility tests total, 383 core tests pass).
- **Windows UIA bridge** (`HultUI.Platform.Accessibility.Windows`) — AOT-compatible via `[GeneratedComInterface]` + `StrategyBasedComWrappers`. `IRawElementProviderSimple/Fragment/FragmentRoot` implemented. Patterns: Invoke, Toggle, Value, SelectionItem. `WindowsAccessibilityBridge` owns wrapper cache keyed by `ElementId`, raises UIA structure/property/focus events. `IUiaHostBridge` collaboration surface with SDL3 host adapter that installs a `WM_GETOBJECT` message hook and marshals UIA worker-thread actions through a per-frame queue.
- **Linux AT-SPI2 bridge scaffold** (`HultUI.Platform.Accessibility.Linux`) — Project structure, role mapping, session-bus connection flow via `Tmds.DBus.Protocol 0.92`. D-Bus per-object export is stubbed pending on-desktop verification; `IsActive` stays false until that lands.
- **Platform factory** — `AccessibilityBridgeFactory.Create(window)` dispatches by OS. `HultUI.csproj` references both platform bridges unconditionally; each is `SupportedOSPlatform`-gated so only the right one activates.

- **IME composition** — `SDL_EVENT_TEXT_EDITING` plumbed through `EventTranslator → TextEditingEvent`. `TextInputElement` and `TextAreaElement` render the preedit inline at the caret with a thin underline across the whole preedit and a thick underline on the active IME segment. `SDL_SetTextInputArea` is called each frame on the focused input so OS candidate windows snap to the caret; `SDL_ClearComposition` fires on blur. Custom candidate-list rendering is deliberately skipped — the OS-native window is used instead. New `ITextInputHost` abstraction in `HultUI.Core.Platform`; `SDL3TextInputHost` is the implementation. 9 unit tests covering the state machine.

## Up Next (in order)
- **Rich text editor** — docs/plans/16-rich-text-editor.md; all nine stages done. Candidates for more
  adoption: Junction's review notes and replies. Left from stage 5: a reader moving the caret or
  selection (UIA `Select`, AT-SPI `SetCaretOffset`/`SetSelection` — both bridges refuse it for every
  element), AT-SPI `EditableText` (set-value on Linux), formatting as text attributes. Left from stage 9:
  a Table button in Junction (needs Phosphor's `table` glyph in the bundle), pasting picture data
  (an upload hook), dragging to resize pictures and columns. Left from stage 8: opening a long
  document lays every block out (5,000 blocks: 15.5 ms) — estimated heights off screen would make
  it cost a screenful.
- **Rich text editor: export as HTML and RTF** (plan 16; export only, no import) — a whole
  `RichDocument` written out as a file another program opens. HTML: grow `RichHtml.Write` (today
  the clipboard's fragment writer) into a standalone document — `<!doctype>`, charset, the theme's
  heading sizes and code face as a small stylesheet, nested lists, tables with `<thead>`,
  `<img>` with width and alt, links. RTF: a new writer — font and colour tables from the marks,
  `\b \i \ul \strike`, `\fs` for size, `\cf`/`\highlight` for colours, `\par` blocks with list
  numbering (`\pn` or `\ls`), `\trowd` tables, pictures embedded as `\pict` PNG when the app's
  resolver can give the bytes, else their alt text. Both sit in Core (no dependency needed) or a
  `HultUI.RichText.Export` package; the clipboard can then offer `text/rtf` too, so Word and
  WordPad paste formatting. Tests: golden output strings, and an HTML parse check.
- Windows UIA bridge on-device verification — run a sample, open Accessibility Insights / `Inspect.exe`, confirm tree, name, role, bounding-rect, and pattern actions round-trip.
- **AT-SPI `Action.DoAction`** — an AT can now activate controls. The neutral
  invoke/toggle/select logic was lifted out of the Windows-only `Sdl3UiaHostBridge`
  into a shared `AccessibilityActionRouter` (`src/HultUI/`, implementing
  `HultUI.Core.Accessibility.IAccessibilityActionHost`); both the Windows UIA host
  and the Linux AT-SPI bridge route through it, and `Window` drains its UI-thread
  action queue each frame on every platform. Verified live with pyatspi:
  `do_action(0)` on the button dispatches a real click (counter 0→3 over three
  calls) and on the checkbox toggles it. The Windows UIA path was refactored to
  run through the router and **re-verified on Windows (2026-07-19): the FlaUI
  suite `tests/HultUI.UiTests` is 14/14 green** — invoke/toggle/select/set-value
  all still round-trip through the shared router.
- AT-SPI refinements (remaining polish): (1) a `state-changed:defunct` still shows up in a *mid-stream* event monitor on first contact with each node — but that's a client cold-start artifact: the monitor connects after our startup `Cache.AddAccessible` burst and only subscribes to `object:` events, so it never warms its cache. A client (Orca) running from app launch receives the cache signals. Not fixable app-side for a cold listener; our app provably emits only the intended signals. (2) `Component` extents are now device-pixel-correct (bounds × display scale, via `IAccessibilityGeometryHost` — verified: at 2× the button reads `[24,84 752x76]` vs `[12,42 376x38]` at 1×). The remaining gap is *absolute screen* coordinates: under Wayland a client has no reliable window screen origin, so SCREEN and WINDOW coordType both resolve to window-relative pixels; Wayland is the only target, so this is not planned. (3) `TreeDiff.Reordered` (same children, new order) needs **no** signal — every query is served from the freshly rebuilt snapshot, so `GetChildren`/`GetChildAtIndex` already return the new order. Verified: a persistent pyatspi client observes each reorder with the reorder signal entirely disabled. A high-level AT that caches child order and only refreshes on events (Orca) would want an `object:children-changed` nudge; add it when an Orca test shows it's needed and confirms the form (`samples/AtSpiVerify/atspi_reorder.py` reproduces reorders). (4) end-to-end Orca *speech* pass (largely covered by the pyatspi verification, since Orca reads through the same API; the gap is Orca-specific event handling and actually listening).

## Deferred
- **The card fill is probably too dark, but the measurements disagree** — on one capture of the
  Gallery's All page the pane is #272727 and a tile is #343434, while ours is #2D2D2D; real
  Settings gives a clean 5.1% white for a card over its page (#000000 -> #0D0D0D, exactly
  `#0DFFFFFF`), which over our #272727 layer would be #323232. Both references say our card is
  too dark. But the Gallery's own Color page shows its swatch cards at #2B2B2B on a #282727
  page, a delta of 3 rather than 11, and that fits neither — so something about what
  `PrintWindow` returns for a Mica window, or about which brush those swatches actually paint,
  is not understood yet. Changing `Card` also forces `CardHover`, and the Gallery labels the
  hover brush `CardBackgroundTertiary` while showing a warning on it. Not worth guessing at:
  what would settle it is a hover measurement on a Settings card, which needs the real pointer.
- **Automatic minimum size for flex items** (CSS `min-height:auto`) — content can still be crushed below its own size inside a bounded container. Needs a real recursive min-content pass in which scroll containers contribute zero; approximating it from the flex Phase 1 measurement was tried and reverted, because for a container that measurement is its full content extent, so an ancestor wrapping a scroll view gets pinned to the whole scrollable height (14,640px tall, 1007 draw calls instead of 47). `FlexShrinkTests` carries a skipped case. The shrink-heavy benchmark this needed exists since 2026-10-05: the scenario suite's `layout/shrink …` rows (`samples/HultUI.Samples.PerfSuite -- --filter layout/shrink`) — capture them before and after.
- **Render-log element attribution** — recorded ops carry no element identity, so "what did this element draw?" means matching on coordinates. `ElementRenderer` has a single per-element call site to hook.
- UIA `ITextProvider` (word-by-word reader navigation) — currently exposed via `IValueProvider`. Would also let selectable labels (`TextElement.Selectable`) report their selected sub-range to assistive tech via `GetSelection`/`RangeFromPoint`; today mouse/keyboard selection works but a screen reader reads the label as a whole.
- AT-SPI `Text` / `EditableText` interfaces.
- macOS `NSAccessibility` bridge.
- LiveRegion / alert announcements.
- Accessibility for overlays, tooltips, context menus.

## Traps & decisions (durable)

Engineering notes that aren't obvious from the code, carried over from the AT-SPI
and benchmark work.

- **Do not change the `FlexShrink = 1` default.** Fixed-height items shrinking in a
  bounded, overflowing column is correct CSS; the real bug was narrower (a scroll
  container's indefinite main axis not propagating into nested flex containers), and
  it is fixed. (The `min-height:auto` clamp is a separate, still-deferred item above.)
- **A/B every layout-engine change with the benchmark** — `--auto` and
  `--auto --virtual`, in one session, with `SDL_WINDOW_ACTIVATE_WHEN_SHOWN=0` so the
  window doesn't steal focus. Unit tests passed straight through a viewport-collapsing
  regression that only the benchmark caught. Compare within a single session only;
  cross-session numbers are noise. For the breadth, also the scenario suite
  (`samples/HultUI.Samples.PerfSuite -- --filter layout,grid,text --json …` on both
  sides, then `--compare`): it covers grid, wrap, baseline, shrink, text and resize
  paths the table never takes.
- **Snapshot baselines in raw string literals carry the source file's line endings**
  (CRLF on Windows, LF on Linux under autocrlf), so a baseline can pass one CI leg and
  fail the other. Normalise with `ReplaceLineEndings("\n")`.
- **`FlexGrow(1)` alone does not mean "take the leftover space".** The default `auto` basis
  sizes the item to its own content first and grows from there, so an item whose content is
  wide — a text line holding one unbreakable token, a base64 blob or a long hash — takes that
  width and overflows its row. `FlexBasis(Px(0))` is what says "start from nothing and take
  what is left"; the app already uses it where a column must not be pushed out. Wrapped text
  makes it look like a wrapping bug, but the text had wrapped correctly at the width the
  label had wrongly taken.
- **Measure text at the width the label actually gets, padding included.** A wrapped row's
  height comes from measuring its line at the code column's width; the label's own horizontal
  padding is part of that column and not room the text can use. Junction subtracted the
  number/sign columns and not the label's 14px, so every line was wrapped 14px too wide and
  any line that band was the difference for came out a visual line short and drew over the row
  below. It reads as an intermittent fault — it only shows when a line's length lands in the
  band. Measuring *narrower* than the truth is the safe direction: a row a line too tall is a
  small gap, a row a line too short is overlapping text. The row builder and the measurement
  must share named constants, since this is exactly what drifts.
- **A resize relayouts but does not rebuild.** An app that skips the rebuild on an ordinary
  resize (both apps do, to keep a drag off the build path) is fine for every fixed-height
  row and wrong for any height *derived from the width* in the build — a wrapped diff row,
  say. Layout then stacks rows at the new width using heights measured at the old one, and
  the overflow draws over the row below. It only showed on a window resize: dragging the
  pane's own splitter is app state and rebuilds anyway. Junction's diff and Vesper's diff
  *and* viewer all had it; Vesper's auto grid tracks did not save it, because the virtualized
  body forces the measured height regardless of what the track would have grown to.
- **`IsDetached` does not mean "no longer in the tree".** `TreeDiffer` marks the *incoming*
  element of a patch, the one thrown away — so a patched element is still the right object and
  correctly unflagged. An element that a rebuild *replaces* is dropped from the tree without
  being marked at all, and looks perfectly alive. Anything holding an element across frames
  has to ask `Window.IsInTree` as well; the middle-click autoscroll went on scrolling a
  replaced list nobody could see. And when the live one cannot be found yet — a fresh element
  has no layout until the pass after the tick that looks for it — keep the old reference and
  ask again next frame rather than giving up, or one rebuild ends the gesture for good.
- **A click belongs to the primary button.** `ClickEvent` used to be synthesized for whatever
  button was pressed, and `OnClick` takes no button — so every handler in every app fired on a
  middle click too. It only mattered once middle-drag became the way to scroll a list: a press
  that never left the autoscroll's dead zone staged the diff line under it in Junction. Clicks
  are now the primary button's alone, which is what the DOM does (`auxclick` for the rest) and
  what half this framework's own controls were already doing by hand with
  `if (evt.Button != 0) return`. A handler that really wants the middle or right button reads
  it off PointerDown, where the button is.
- **A control that owns its own wrapping is immune to all of this — but test that it does.**
  Meridian shows response bodies through `TextAreaElement`, which wraps during layout and
  caches its line starts against the width it wrapped at, so a resize re-wraps with nothing
  for the app to do. The measured-height fault only exists where an app measures wrapped rows
  itself and hands them to a virtualized list; reach for the control before building that
  machinery. What "it wraps itself" does not buy you is the width being right: adding 14px to
  the wrap width inside the control left every suite in the repository green, all four apps
  and the framework, because nobody had asserted that a wrapped line fits the box it is in.
  `TextAreaWrapWidthTests` does now.
- **Correcting a width takes two halves, and each is useless alone.** The column width is
  read from a laid-out box, so a build started before layout sees the width from a frame ago
  — one rebuild on resize measures at the *old* width and fixes nothing. The post-frame
  settle that re-reads the width is what corrects it. But that settle is registered from a
  build, so with no rebuild on resize it is never scheduled and never runs. Disable either
  half and the fault comes back; both were verified that way in Vesper.
- **A row grows to fit its text; the list still stacks it at the height it was given.** So
  "is this row tall enough" has to be asked of the label's own line boxes, not of its
  laid-out box — the box reads back as the row's either way, and a check against it passes
  straight through the bug. `LayoutProbe.DrawnText(label).Height` is the clamped one; summing
  the element's `_lines` is not. Two tests written the first way were green against provably
  broken code.
- **Keys among siblings used to be all-or-nothing** — fixed 2026-09-02, and worth knowing
  because the workaround is still visible in the apps. A single keyed child made the
  reconciler match its whole child list by key, so every unkeyed sibling was *replaced*
  rather than patched and lost focus, caret and scroll: one key on Junction's commit
  composer left the summary field discarding everything typed after the first character,
  and the History include bar cost its `/` filter the same way. `ReconcileKeyed` now matches
  the unkeyed children by position among themselves, the way `ReconcileIndexed` does, so
  keys can be added one at a time again. The keys added to those two bars to work around it
  are harmless and still correct; nothing needs unpicking.
- **An event runs the handlers that were live when it was dispatched.** `Dispatch` pins
  each element on the path (`Element.PinHandlers`) before the walk starts, because the path
  is *elements* and reconciliation keeps an element while changing what it is: a handler
  that marks dirty can have an ancestor still to be bubbled through patched into something
  else entirely, and reading its handler list at invoke time then ran the new build's
  handlers. That is how one click both added a row and deleted it — Meridian's "add
  assertion" starter ran `AddAssert`, the rebuild patched the button's parent into the new
  row's ✕, and the same click bubbled into `RemoveAssert`. A corollary worth knowing: a
  handler attached to an ancestor mid-dispatch does not run for the event that attached it.
  Found 2026-09-02 by the differ change above, which is what turned a latent bug into two
  failing app tests — Meridian's assertion starters and Junction's repo drag-reorder. It was
  reachable before that in any all-unkeyed list, just harder to hit. Measured on both: event
  dispatch is at parity (the path and the pinned lists are pooled, which pays for itself —
  the old path `List<Element>` grew 4→8→16→32 on a deep tree), and a keyed reconcile of 500
  rows costs ~3% more.
- **Inside `HultUI.*`, bare `Text` binds to the `HultUI.Text` namespace**, not the
  builder — alias `using Text = HultUI.Core.Builders.Text;` inside the namespace.
- **`grep "warning CS"` does not match `warning CA1416`** — platform-analyzer warnings
  hide from that filter, so a clean-looking `warning CS` grep can still be masking
  CA1416. The Linux a11y test project carries a scoped `<NoWarn>CA1416</NoWarn>`
  because its pure role-mapping tests call `[SupportedOSPlatform("linux")]` helpers on
  every CI leg (Windows included).
- **AT-SPI wire-level gotchas:** `Tmds.DBus.Protocol`'s `MessageWriter`/`Reader` are
  ref structs — they can't be a `using` variable and passed by `ref` at once (dispose
  by hand). Never guess AT-SPI role numbers — introspect a live GTK app with `busctl`.
  The registry probes the app root *during* the `Embed` round-trip on the receive
  thread, so seed the root into the snapshot **before** `Embed` or that probe answers
  `UnknownObject` (visible only on the raw bus, not through pyatspi). Verify with two
  lenses: pyatspi for tree/semantics, `busctl monitor` for wire-format and timing —
  each catches bugs the other misses.
- **Re-verifying the AT-SPI bridge** needs a running `at-spi2-registryd`.
  `samples/AtSpiVerify` drives the bridge headlessly
  (`HeadlessWindow.InstallAccessibilityBridge` / `InstallActionRouter`), and
  `atspi_inspect.py` / `atspi_action.py` / `atspi_reorder.py` read it back with pyatspi.
