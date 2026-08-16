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
- **Stage 3 — polish.** ✅ Done, except for the last item, which turned out not
  to belong here at all.
  - **First-strong auto direction.** `TextDirection.Auto` resolves the paragraph
    per UAX #9 P2/P3 — ICU's `UBIDI_DEFAULT_LTR` does it during the analysis
    that was already happening, so it costs nothing extra, and
    `TextLayout.IsRtlParagraph` reports what it decided. A managed first-strong
    scan covers the no-ICU fallback. Text with no RTL in it never reaches any of
    this: the classifier still answers first and the plain path is untouched.
  - **Bracket mirroring (UAX #9 §3.1.3).** Already correct, and verified rather
    than assumed: HarfBuzz mirrors during normalization, but *only* because
    stage 1 set the buffer's direction explicitly instead of guessing.
    `RtlGlyphOrderTests` pins it so the free lunch cannot be silently lost.
  - **Direction-aware alignment.** `TextAlign` gained `Start` (the new default)
    and `End`, which resolve against the direction the text actually resolved
    to; `Left`/`Right` stay absolute. Nothing changes for LTR text, where Start
    *is* Left.
  - **Visual caret movement.** Left/Right now step by what is next on screen on
    a line that mixes directions. `TextLine.TryMoveCaretVisually` declines on
    lines with no RTL run, so LTR editing keeps the callers' own codepoint
    stepping — surrogate pairs and clusters included — bit for bit.
  - **Ellipsis** (2026-08-16, with the styled-text twin). BiDi text used to clip
    under `TextOverflow.Ellipsis` — the text simply stopped, with nothing to say
    it had been cut. It now ellipsizes, and the mark goes where *reading* ends:
    at the left edge in a right-to-left paragraph, with everything already
    placed shifted right to make room. The prefix kept is logical, because
    truncation removes the end of the text, not the right of the line.
  - **RTL scrollbar/gutter layout — not this project.** Mirroring the interface
    is logical-vs-physical box sides across the whole layout API, not a text
    concern; the verdict and its reasoning are in docs/DECISIONS.md, and it is
    listed as its own future capability.

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
