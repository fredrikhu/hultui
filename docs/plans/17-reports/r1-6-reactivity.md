# Round 1 survey: 6. Reactivity and reconcile

Counters and per-phase `GC.GetAllocatedBytesForCurrentThread`, Release JIT, headless with
the a11y bridge installed; Junction runs on a 1,380-commit clone of this repo. Probes left
in worktree agent-ad87c15ea59b1f41d (a hook blocked reverting them after the restart).

SATISFIED: no
REVIEW: none (round 1)

## Candidates

- **reconcile-restyle-on-change** (= layout-style-pass-after-patch; owned here, the change is in patch and mount). A patch marks style dirty only when the element's inherited style changed (compare before `PatchFrom` and after `RefreshInteractionAfterPatch`) or its type's `ApplyTheme` reads patched state. Mount marks only itself and its ancestors (a new element already starts `DirtyFlags.All`).
  - Evidence: `PatchFrom` (Element.cs:1276) and `Mount` (Element.cs:1044) always `MarkStyleDirty`; `MarkStyleDirtyDown` (:1006) walks the whole subtree. Unchanged rebuild of 1,000 rows (6,003 elements): 44,004 down-visits, then 6,003 resolves and 120 KB; 0 layout flags, 0 a11y rebuilds — style is the only pass that redoes the whole tree. Junction History Down: 6,272 down-visits, 587 resolves per press (~385 elements, 2 rebuilds). Persistent child in a Box: 1,207 resolves per parent rebuild. 93 % of Junction patches are Box/Text/Image; none reads patched state in `ApplyTheme` (Text only sets `SelectionColor`, TextElement.cs:234).
  - Expected: style 0.22 of 1.72 ms min on component/edit keyed (13 %) → ≥5 % on component edit keyed/unkeyed/reverse; Junction 587 → ~0 resolves per press; O(N·depth) → O(N).
  - Cost: medium; 27 `ApplyTheme` overrides keep today's marking until audited.
- **reactivity-scope-threadstatic.** Current subscriber in a `[ThreadStatic]` field instead of `AsyncLocal` (ReactiveScope.cs:9); pooled notify snapshot for `Computed` and `ReactiveList` (Computed.cs:94, ReactiveList.cs:107) as `Signal` has.
  - Evidence: 72 B per Effect run (new ExecutionContext; 160 B with another AsyncLocal live); two sets per Computed recompute; write through unchanged Computed 224 B, changing 448 B. component/signal fan-out 500 labels: 121.9 KB/step, 36 KB (30 %) the AsyncLocal.
  - Expected: ~−30 % allocKB on fan-out; Computed writes 224 → ~0 B.
  - Cost: small. Behaviour: a Task/timer started inside Build or an effect no longer carries the subscriber onto a pool thread (today a data race the write guard misses). Update DECISIONS.md:97.
- **reconcile-persistent-child-in-place.** Keep a field-held child `ComponentHost` in place when its parent rebuilds.
  - Evidence: builder `AddChild` moves the live host into the new tree (`DetachSilently`, Element.cs:1177), dirtying layout+children to root; keyed reconcile puts it back with `ReplaceChildren`, so a7fcca0's skip never applies. 200-row child (1,206 elements): per parent rebuild 2 detaches, 1 keyed replace, full a11y rebuild 165 KB; wrapped in a Box +1,207 resolves. Users: Aperture player (ApertureApp.Player.cs:338-340, also overwrites the host key with "picture"), Reel (ReelApp.cs:341), MenuBar's documented pattern (MenuBar.cs:57).
  - Expected: removes an O(N) a11y rebuild (and O(child) restyle when wrapped) per parent rebuild.
  - Cost: medium.
- **reconcile-blueprint-hashset** (build-path half of layout-tree-walk-allocs). `ReplaceChildren` allocates `new HashSet<Element>(children)` (Element.cs:1139) even with no existing children — every `.Children(...)` in a Build. 1,000-row rebuild: 3,002 calls, ≈550 KB of 6,248 KB/op. Junction only 36 per press (mostly `AddChild`, 456). ≈ −9 % allocKB on component edit/reverse. Small.
- **reconcile-unkeyed-shrink-linear** (confirms layout's analysis; low priority). Remove the unmatched middle by index (TreeDiffer.cs:302-304 → `List.Remove`, Element.cs:1114). n → n−k costs ~k·n − k²/2 comparisons. No app hits it. Small.

## Examined, not worth it

- Signal writes in one handler: 3 writes → 1 rebuild; write + `MarkDirty` → 1.
- Effects on unchanged inputs: 0 body runs; equal writes don't notify.
- Generated `[Reactive]` code: no boxing; read 0 B.
- Duplicate dependencies: `TrackRead` doesn't dedupe (ReactiveScope.cs:53); microseconds in apps.
- Reconcile allocations: Junction 28 of 372 KB build per press (7 %); 1,000 rows 240/350 KB of 5.58 MB. P5a declined; handler-copy churn and the second wrapper closure per `On` (88 B, Element.cs:788) stay under 10 %.
- New tree ~930 B per element: element size (P5c) and the rebuild model.
- Junction's second rebuild per History key: highlighter `MarkDirty` on completion (JunctionApp.Highlight.cs:103-108), lands in a later frame.
- Leads: a11y — one label change in 6,007 elements rebuilds the whole a11y tree, 812 KB. Layout — Junction History re-realizes every visible row per move: 523 mounts, 217 unmounts per press (supports layout-virtuallist-keep-rows).

## Needs the user

- **Should `MarkDirty()` join an open batch?** Today it rebuilds synchronously (REACTIVITY.md). 3 `MarkDirty` in one handler = 3 rebuilds (12.7 vs 4.2 KB); `MarkDirty` + signal write = 2. Junction (242 call sites) and Vesper (218) are MarkDirty-driven. Joining the dispatch batch (and wrapping the main-thread queue drain in one) caps it at 1 rebuild; a handler reading the rebuilt element right after `MarkDirty` would see the old tree until `FlushNow()`. Changes documented semantics. Vesper and rich text not counted.
