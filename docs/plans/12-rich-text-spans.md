# Rich Text Spans — Design

External-review item 11's first project (docs/plans/10-capability-roadmap.md context;
review section 2). Display-only, per the review's own framing: "editing rich text is a
separate, much larger project — display-only spans cover 90% of demand."

## What exists, what's missing

`TextSpan(Start, Length, Color, Background)` already recolors ranges — but only at
**draw time**: `IRenderContext.DrawText(..., spans)` slices the already-shaped runs
and repaints them. Layout never sees spans, so nothing a span carries may affect
metrics. That is exactly why the current spans stop at color.

The gap is **metric-affecting** styles: a bold word inside a sentence, an italic
range, a different size — where the styled range shapes differently, is wider, and
must wrap as part of one paragraph. Apps fake it today with per-token `TextElement`s
in a `Flex.Row` (Junction's intraline diff), which breaks line-wrapping across style
boundaries and pushes selection onto `TextSelectionGroup`.

## The model: extend TextSpan, not a new document type

The review suggests an `AttributedText` type. We already have the span idiom in four
call sites and one struct; a parallel model would leave two ways to say "this range
is special". Instead `TextSpan` grows optional style fields:

```csharp
public readonly record struct TextSpan(
    int Start, int Length,
    Color Color, Color Background = default,
    FontWeight? Weight = null,        // metric-affecting from here down
    FontSlant? Slant = null,
    float? FontSize = null,
    TextDecoration Decoration = TextDecoration.None);   // underline / strikethrough
```

- Null/None fields inherit the element's style — a color-only span behaves exactly
  as today, byte-for-byte.
- `Decoration` is paint-only (a line at the run's baseline/中height) but rides along
  because links and search-hit styling want it in the same range description.
- Spans stay sorted and non-overlapping (the existing contract; `TextElement.Spans`
  already documents it).

A span with any metric-affecting field set makes the whole layout span-dependent.
Callers that only recolor (all of today's) never pay for what they don't use.

### One type vs two — measured

Whether the extra fields tax the existing color-only path was measured before
deciding (2026-08-16, scratchpad SpanBench mirroring the three hot uses):

| | Today (16 B) | Widened (36 B) |
|---|---|---|
| Draw-slicer walk (per frame) | 2.5 ns/span | 2.4 ns/span — **unchanged** |
| List build (per rebuild) | 2.5 ns/span | 9.5 ns/span |
| SpansEqual walk (per patch) | 10.7 ns/span | 12.3 ns/span |

The per-frame read path doesn't touch the new fields and pays nothing. Build and
equality get slower per span, but at worst-case volume (a fully decorated
4,096-span file body) the delta is ~30 µs per **rebuild** — and the largest span
producers are memoized on content (`FileBodyTextAndSpans`), so they don't rebuild
per frame at all. A separate rich-span type would buy back microseconds at the
price of two range models in every API. **One type.** If a future trace ever shows
span-list building, the struct can pack its nullable fields behind the same API
(sentinel-based, ~24 B) without callers changing.

## Pipeline changes (HultUI.Text)

The pipeline's structure survives; the itemization boundary moves.

1. **Style segmentation before font itemization.** Today:
   `FontItemizer.Itemize(text, family, weight, slant)` → font runs. With spans:
   split the string at metric-span boundaries first; itemize each segment with its
   *effective* (weight, slant); shape at its *effective* size. A run is now
   (style-range ∩ font-fallback-range) — the same shape the itemizer already
   produces for fallback, so `ShapedRun` needs only the effective size it already
   carries plus the span it came from.
2. **One line-breaking pass.** `LineBreaker.FindBreakOpportunities` is
   style-independent (UAX #14 over the string) — unchanged. `BreakIntoLines`
   measures segments; segment measurement becomes span-aware
   (`MeasureString(text, style, spans, from..to)`); break decisions then wrap
   styled paragraphs correctly for free.
3. **Line metrics.** `ShapeLineText` already takes max ascent/descent across runs
   (fallback fonts differ), so a larger-size run raising its line's height needs no
   new mechanism.
4. **Caches.** The word-shape cache keys on (segment, font, size) and is
   style-correct as-is — a bold segment resolves a different `IResolvedFont`. The
   string-level `ShapeKey`/`MeasurementKey` must include a span digest for
   span-carrying text; plain text keeps today's keys and hit rates. Digest =
   spans' content hash; the diff view re-creates span lists per frame, so the
   digest must be value-based, not reference-based (the existing `SpansEqual`
   value-compare in `TextElement` is the precedent).
5. **Ellipsis/clip paths** call `MeasureString`/`FitPrefix` — they get the
   span-aware overloads and otherwise stay as they are.

## Element surface

No new element. `TextElement` (and `TextAreaElement`'s span path) keep their `Spans`
property; the elements pass spans down to `Layout` when any span is
metric-affecting, and continue passing them only to `DrawText` when not. Paint picks
per-run color/background/decoration from the span the run was cut from instead of
re-slicing at draw time (the draw-time slicer remains for the plain-color path so
existing behavior is untouched).

`RichTextElement` from the review is not needed for stage 1–2; if a paragraph
element with links/inline images materializes later it composes on this pipeline.

## Staging

- **Stage 1 — decorations (small).** Underline/strikethrough on `TextSpan`,
  paint-only, drawn in the span slicer. Ships alone; zero layout risk. Unlocks
  links-look and richer search-hit styling immediately.
- **Stage 2 — metric-affecting runs (the project).** Pipeline changes above +
  `TextElement` wiring + tests. Success criterion: a bold word inside a wrapping
  paragraph wraps identically to a browser rendering the same text, and the
  existing render-log baselines for span-free text are byte-identical.
- **Stage 3 — adoption.** Junction intraline diff moves from per-token elements to
  one spanned line per row (fewer elements per row on the hottest list in the app);
  markdown-ish commit-body rendering becomes possible.

## Non-goals

- Editing rich text (review's own carve-out).
- Inline images / arbitrary inline boxes — different problem (inline layout
  objects, not text runs).
- BiDi — next project in the sequence; the run model here is BiDi's prerequisite
  (visual reordering operates on exactly these runs), which is why spans come first.

## Risks

- **Cache pressure**: span-carrying keys are bigger and hash more; mitigated by
  keeping plain text on today's keys and by the word cache's indifference.
- **Wrap-path perf**: `BreakIntoLines` re-shapes per segment already (a known
  inefficiency); span awareness must not multiply it. The text-perf probe
  methodology (docs + JUNCTION_BENCH) gates the stage-2 merge.
- **Selection mapping**: `TextLine.Start/Length` mapping survives unchanged, but
  offset→x math inside a line must walk runs of varying size — `TextAreaElement`'s
  caret/selection code is the reviewer for that change.
