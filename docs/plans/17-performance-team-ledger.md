# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work.

- **Round:** 1
- **Phase:** survey
- **Review commits since:** fc0f4f7 (the branch point; nothing to review in round 1)

## Verdicts

| Expert | Round | Satisfied | Review objections |
|---|---|---|---|
| 1. Layout | — | — | — |
| 2. SkiaSharp: painting | — | — | — |
| 3. GL and Vulkan | — | — | — |
| 4. .NET runtime | — | — | — |
| 5. Text pipeline | — | — | — |
| 6. Reactivity and reconcile | — | — | — |
| 7. Accessibility bridges | — | — | — |
| 8. App workloads | — | — | — |

## Queued candidates

None yet.

## Landed

| Round | Commit | Candidate | Before → after |
|---|---|---|---|

## Examined, not worth it

None yet.

## Needs the user

None yet.

## Open

None yet.

## Log

- 2026-10-07: brief, ledger and `scripts/perf/idle-gate.ps1` written; round 1 survey started.
- 2026-10-07: user removed the round cap and allowed more roles. Split into eight: text
  pipeline out of SkiaSharp, reactivity and reconcile out of layout, accessibility
  bridges out of .NET, and a new app-workloads profiler. Experts 5–8 joined the round 1
  survey late; 1, 2 and 4 were told their narrower scope mid-survey.
