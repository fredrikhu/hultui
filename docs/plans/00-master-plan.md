# Master Plan: Framework Feedback Implementation

Execution plan for addressing feedback from the Git client sample
(`samples/HultUI.Samples.GitClient/FRAMEWORK_FEEDBACK.md`).

## Current Step

> **Phase 1, Step 1 — [Plan 01: Fluent TextElement Extensions](01-fluent-text-extensions.md)**

## Phase 1 — Trivial Fluent API Additions

No design decisions needed. Straightforward additions to `ElementExtensions.cs`.

| # | Plan | Status |
|---|------|--------|
| 1 | [Fluent TextElement extensions](01-fluent-text-extensions.md) | Pending |
| 2 | [Fluent BorderWidth](02-fluent-border-width.md) | Pending |
| 3 | [Fluent Overflow](03-fluent-overflow.md) | Pending |

Plan 03 has a naming analysis step — may need feedback on method name if `Overflow`
collides with the enum type.

## Phase 2 — Component API Improvements

Both require a design decision with user feedback before implementation.

| # | Plan | Status | Feedback needed |
|---|------|--------|-----------------|
| 4 | [MarkDirty() on Component](04-mark-dirty.md) | Pending | Method name: `MarkDirty()` vs `Rebuild()` |
| 5 | [Mount() idempotency](05-mount-idempotency.md) | Pending | Lifecycle: cache-forever vs clear-on-unmount |

## Phase 3 — Deeper Changes

Require codebase analysis before implementation. Each plan has analysis steps.

| # | Plan | Status | Analysis needed |
|---|------|--------|-----------------|
| 6 | [LocalPosition on pointer events](06-local-position.md) | Pending | Event dispatch pipeline, bubbling behavior |
| 7 | [Source generator nullable fix](07-source-generator-nullable.md) | Pending | Roslyn NRT format behavior |

## Phase 4 — Low Priority

| # | Plan | Status | Analysis needed |
|---|------|--------|-----------------|
| 8 | [ColResize/RowResize cursors](08-cursor-types.md) | Pending | Platform backend cursor mappings |

## Not Included

- **Feedback #1 (namespace collision):** `HultUI.Text` rename is a breaking change
  affecting all consumers. Separate discussion needed — not part of this plan.
- **Feedback #9 (Component.Mount() creates new host):** Covered by Plan 05.

## Process

- Execute plans in order within each phase.
- Phases gate each other (Phase 2 starts after Phase 1 completes).
- After each plan: verify build. Update status in this file.
- Plans with "ask for feedback" steps pause for user input before implementing.
