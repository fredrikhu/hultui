# Automatic Minimum Size (min-content) — Analysis

External-review item ordered after rich-text spans (docs/plans/12). The review's own
proposal is superseded by the in-repo analysis (TODO.md, carried here): approximating
min-content from the flex Phase 1 measurement was **tried and reverted**, and the
reason it failed is the design's central constraint.

## The gap

CSS flexbox gives every auto-sized flex item an *automatic minimum size*
(`min-width/height: auto`, spec §4.5): shrinking never compresses an item below its
content's min-content size. HultUI's shrink path has no such floor — a bounded
container crushes content to whatever fits. `FlexShrinkTests.
BoundedContainer_DoesNotCrushContentBelowItsMinimum` (skipped) is the acceptance
case: a text line inside a card inside a 10px-tall box must keep its line height.

## Why the obvious approximation fails (the reverted attempt)

Phase 1 of flex layout already measures every child, so using that measurement as
the floor looks free. But for a *container*, the Phase 1 hypothetical is its **full
content extent**, not its min-content size. An ancestor wrapping a scroll view got
pinned to the entire scrollable height — 14,640px tall, viewport collapsed, culling
dead, 1,007 draw calls instead of 47. Any correct design must treat these as two
different questions:

- *"How big would you like to be?"* — Phase 1, full extent.
- *"How small can you get before your content breaks?"* — min-content, where a
  scroll container's whole purpose is to answer **zero** (spec agrees: automatic
  minimum is zero when `overflow` is not `visible`).

## The design

A genuine recursive min-content measurement, separate from Phase 1:

- **Scroll containers** (`Overflow` ≠ visible on the axis): contribute **0**. This
  single rule is what the reverted attempt lacked.
- **Text**: min-content width = widest unbreakable segment (the pipeline's break
  opportunities already know them); min-content height = height when wrapped at the
  item's cross size. The measure-wrap gotcha stands: one-line labels still need
  `MaxLines(1)`, min-content does not repeal wrapping.
- **Leaves with MeasureContent**: measure at zero available main size.
- **Flex containers**: row min-width = Σ children min-widths + gaps; column
  min-width = max of children; heights transposed. `FlexShrink(0)` children
  contribute their full size (they already refuse to shrink).
- **Explicit `MinWidth`/`MinHeight`** wins over the automatic minimum, as in CSS
  (`min-*: auto` only applies when the property is auto — HultUI's
  `Dimension.None` default maps to auto).

**Mechanics**: computed lazily, only where the shrink path actually needs a floor
(free space negative and an item's shrink would take it below its floor), memoized
per element beside `MeasureCache` and invalidated by layout dirt.

## Sequencing (from TODO.md, kept deliberately)

1. **Shrink-heavy benchmark first.** The current suite barely exercises the shrink
   path, so a costly min-content pass would land unnoticed. Add a
   Samples.Benchmark scene (deep trees under bounded containers, scroll views in
   ancestors) and take baselines before any layout change.
2. **Implement + unskip** the acceptance test.
3. **The anti-regression test the last attempt earned**: an ancestor wrapping a
   scroll view keeps its viewport height (the 14,640px case, asserted via
   LayoutProbe against the broken shape first — the false-green history in
   [[hultui-layoutprobe]] applies squarely here).
4. **App sweep.** This changes real layouts: every place an app leaned on crushing
   (or worked around it with `FlexShrink(0)`) may move. All three app suites plus
   an eyeball pass gate the merge; some `FlexShrink(0)` workarounds become
   removable afterwards.

## Status (2026-08-16): shipped, default-on

Implemented per the sequencing above. The floor lives in the shrink branch of
`ResolveFlexGrowShrink` (LayoutEngine) with css-flexbox §9.7-style iterative
freezing; floors are computed lazily, so only overflowing lines pay. Explicit
mins now also floor shrinking (they previously did not). The shrink benchmark got
*faster* (0.91→0.59, 0.82→0.67, 0.42→0.25 ms p50): floored items land on the size
they measured at, so Phase 3.5's re-measure of moved items shrinks. The scroll-
zero anti-regression test was verified red against the rule's removal — with real
text content; an explicit-height empty box is correctly capped to zero by the
definite-size cap and cannot reproduce the failure. Whole solution (2,285 tests,
all three app suites) green with zero churn.

Remaining refinement (tracked): text min-content *width* measures at zero
available width (per-character wrap), a floor slightly below the spec's
widest-unbreakable-segment — conservative, never over-pins. Flex wrap containers
use the row max rule per line-less approximation.

## Open decision

Whether the automatic minimum ships **on by default** (CSS semantics, matches
developer expectations, one-time visual churn in the apps) or behind an opt-in
(`Box.MinSizeAuto`-style flag, no churn, but a permanent divergence from the spec
and from the framework's "HTML/CSS-inspired" philosophy). Recommendation: default
on, gated by the app sweep — a divergence flag would outlive its usefulness.
