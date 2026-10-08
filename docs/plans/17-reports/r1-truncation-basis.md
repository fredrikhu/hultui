# Round 1: truncation basis, side by side

For Needs-the-user question 1. A temporary prototype (env switch, one process per variant,
reverted; worktree removed) rendered three ways to compute a one-line truncating label's
flex base size. The composite images were shown to the user on 2026-10-08 and are kept in
the session scratchpad `truncation/` (not in the repo).

- **today**: base = the label truncated at the container's available width.
- **css**: base = max-content (natural) width.
- **clamp**: base = min(natural width, available width), from the cached natural width.

## What changes

**today vs css.** 13 of 45 strip labels in 6 of 24 rows change, up to 16.7 px. Siblings of
a long label shrink harder (an author beside a long message 56 → 40 px at 400 px). The
bigger effect is the min-content floor: every truncating label wider than its room now
shrinks, the floor (its longest word) applies, and labels with long words overflow instead
of truncating. Junction Branches worktree names overlap LAST COMMIT at 1400 px; Meridian's
sidebar root path runs to the edge with no ellipsis.

**today vs clamp.** Only the ellipsis slack moves: the same 13 strip labels, largest shift
0.62 px, at most one character ("Fredrik …" → "Fredrik…"). Junction Branches at 1400 px:
9 boxes 0.9–6 px wider, 0 differing pixels. One real change: cells with **zero room**
(Junction Branches at 1000–1200 px). Today the truncated measurement still reports the
ellipsis (6.9 px), the shrink loop runs and the floor pushes the name to 117 px, drawn over
the next column; under clamp the base is 0, nothing shrinks and the name disappears
(8 labels at 1200 px, 15 at 1000 px). Both layouts are already broken there.

**Unchanged in all three:** 1,223 one-line labels on 38 app screens (Junction's other five
views at three sizes, Vesper, Aperture fixture library, Meridian, Reel) apart from the cases
above. The only app row where two truncating labels shrink is Meridian's URL bar, and each
fits alone, so all three agree.

## Numbers

3 idle-gated runs per variant, interleaved, P-core pinned. Text measurements per step /
median allocKB per op / best min ms:

| Row | today | css | clamp |
|---|---|---|---|
| text/ellipsis | 1,715 / 7,296 / 5.003 | 930 / 4,048 / 3.475 | 930 / 4,049 / 3.445 |
| text/bidi ellipsis | 2,000 / 7,137 / 15.415 | 1,000 / 5,666 / 12.344 | 1,000 / 5,666 / 12.349 |
| text/styled ellipsis | 2,000 / 6,319 / 5.816 | 1,000 / 3,830 / 3.610 | 1,000 / 3,830 / 3.671 |
| text/styled bidi ellipsis | 2,000 / 12,442 / 27.670 | 1,000 / 8,300 / 19.166 | 1,000 / 8,300 / 18.666 |
| text/multi-font ellipsis | 1,991 / 11,586 / 8.013 | 999 / 5,652 / 4.638 | 999 / 5,652 / 4.590 |

css and clamp both halve the measurements: time −20…−43 %, allocation −21…−51 %, within
noise of each other. Flat in all three: layout/shrink rows (labels inside boxes with a set
basis) and table/* rows (cell labels fit their columns). Every suite passes under all three
variants (Core 3,837, Headless 634, Skia 193, Meridian 391, Aperture 218, Vesper 811,
Junction 887); no test pins today's base. The prototype added a 12-byte slot per element
for the untruncated size.

## Recommendation (layout expert)

**clamp**: all of css's saving, while ordinary rows move only by the ellipsis slack. css
would need `MinWidth(0)` (or a zero automatic minimum for truncating labels, which CSS gets
from `overflow: hidden`) on Junction's branch-name label (JunctionApp.Branches.cs:320) and
Meridian's root path (MeridianApp.Requests.cs:97), plus `FlexShrink(0)` on secondary labels
beside long ones.

## Bugs found on the way

- **The min-content floor on a one-line truncating label** (its longest word) makes it
  overflow instead of truncating when it must shrink. Junction's Branches table overlaps
  text at ≤1200 px (a worktree chip leaves the name cell no room), and a long query in
  Meridian's URL bar pushes the Send button off-screen. CSS gives such labels an automatic
  minimum of 0 because of `overflow: hidden`.
