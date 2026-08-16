# BiDi (UAX #9) — Design

The last of item 11's three text projects (after rich-text spans and min-content),
deliberately sequenced last: visual reordering operates on runs, and the run
machinery (segmentation before font itemization, per-run properties, max-metrics
lines) was built by spans stage 2. ICU4N is already referenced by HultUI.Text and
carries the UAX #9 implementation (`ICU4N.Text.Bidi`); DECISIONS.md has noted the
"available but unused" state since the pipeline was LTR-only.

## What is wrong today

- The shaper guesses one direction per buffer (`GuessSegmentProperties`), so
  purely-RTL strings largely shape (HarfBuzz reverses the glyphs), but any mixed
  line — Arabic with numbers, an RTL name in an LTR sentence — comes out in the
  wrong visual order, and short neutral-heavy segments can guess wrong outright.
- Runs are laid out left-to-right in logical order unconditionally; there is no
  reordering step, no paragraph direction, and caret/selection/hit-testing assume
  logical == visual.

## The design

**Segmentation order** (per line): direction runs (UAX #9) → style runs (spans) →
font runs (fallback). Each shaped run carries its embedding level's direction and
is shaped with that direction set *explicitly* on the HarfBuzz buffer — never
guessed (`ITextShaper.Shape` gains an `rtl` flag; the buffer guesses script, then
the direction is overridden).

**One line-breaking pass, logical order.** UAX #14 break opportunities and greedy
width accumulation stay in logical order (widths are direction-independent).
After a line's runs are shaped, `Bidi.ReorderVisually` over the runs' embedding
levels assigns `OffsetX` in *visual* order while `TextLine.Start/Length` stay
logical — the same split the platform text stacks use.

**Paragraph direction** comes from `style.TextDirection` (already inherited,
already themed); `Rtl` sets the base embedding level to 1, which also flips what
"neutral" runs attach to. Auto-detection from first-strong character is a later
refinement, not stage 1.

**Word cache**: RTL runs bypass it in stage 1 (shape the whole segment). Arabic
joining does not cross spaces, so the cache is likely sound for RTL too — but
"likely" is proven, not assumed (the WordShapeCacheTests precedent), and until
then correctness beats reuse. LTR text keeps every cache and fast path untouched.

## Stages

- **Stage 1 — display-only mixed-direction lines.** Direction segmentation +
  explicit-direction shaping + per-line visual reordering. A read-only label or
  TextArea shows mixed Arabic/Hebrew/Latin correctly. Caret, selection and
  hit-testing remain logical==visual (wrong inside RTL runs) — documented.
- **Stage 2 — interaction mapping.** ✅ Done. `TextLine` owns the offset↔x
  mapping — `CaretXAt`, `OffsetAtX`, `VisualRanges` — and every consumer goes
  through it: the renderer's span backgrounds, colors and decorations, and
  `IRenderContext.TextCaretX` / `TextOffsetAtX` / `TextSelectionRanges`, which
  TextInput and TextArea use for the caret, click targeting, selection
  highlights and IME underlines. Inside an RTL run a logical prefix is a visual
  *suffix*, read straight off the glyph array (the shaper returns RTL glyphs in
  visual order — `RtlGlyphOrderTests` pins that); runs where shaping merged
  clusters keep today's measure-the-prefix fallback. A logical range crossing a
  direction boundary now paints as two rects instead of one that covered
  unselected text. Two things came along: hit-testing picks the *nearest*
  boundary rather than always rounding up (clicking the left half of the first
  character used to put the caret after it), and paragraph levels are resolved
  once per paragraph and sliced per line rather than re-resolved per line and
  per break candidate — UAX #9 resolution is paragraph-scoped, so the old way
  was also wrong for neutrals next to a cut.
- **Stage 3 — polish.** First-strong auto direction, bracket mirroring
  (UAX #9 §3.1.3), RTL-aware default alignment, RTL scrollbar/gutter layout.
  **Visual caret movement** belongs here too: Left/Right still move logically,
  which is GTK's and Firefox's default but not Chrome's. The primitives stage 2
  built are what an implementation would sit on; the open question is the
  boundary convention, not the geometry.

## Non-goals

Vertical text; Ruby; per-span direction overrides (isolates beyond what UAX #9
resolves from the text itself).

## Risks

- **Perf**: Bidi analysis per line allocates; gate: run it only when the text
  contains any char ≥ U+0590 (the cheap classifier browsers use) or the style
  says Rtl — pure-ASCII/Latin text must not pay anything.
- **Caches**: shaped-run and layout caches key on text+style; direction derives
  from both, so keys need no change. The word-cache bypass for RTL is the only
  hit-rate cost, and only for RTL text.
- **Fallback interaction**: font itemization must not split inside a joining
  sequence when fallback switches mid-word; direction segmentation first keeps
  each directional run's fallback decisions internal to it.
