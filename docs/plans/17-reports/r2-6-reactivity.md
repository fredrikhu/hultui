# Round 2 survey: 6. Reactivity and reconcile

Release JIT, headless, counters and per-phase `GC.GetAllocatedBytesForCurrentThread`; Junction
on a 1,456-commit clone with the a11y bridge on; prototypes measured and reverted. Probe patch,
drivers and captures in the session scratchpad `probes/reactivity/r2/`. Worktree clean at 6ba7e2d.

SATISFIED: no

REVIEW: none. Round 1 changed nothing in Components, Reactivity, TreeDiffer or the generators;
the Window/StyleResolver/element edits only swapped loops. The style pass now allocates 0 B
(120 KB per 1,000-row rebuild in round 1).

Selection walk per press (a11y off): 1 rebuild, 6 patches; build 7.3 KB, reconcile 0.2 KB,
realization inside render 99.8 KB; 100 mounts, 95 unmounts, 41 ReplaceChildren; 198 resolves
(103 unchanged in the style pass, 95 first-time in render). a11y on: +288 KB.

## Candidates

- **reactivity-tween-tick-per-frame** (= reactivity-tween-rebuild-per-loop-iteration). Tick AnimationManager only on loop iterations that may render, adding up elapsed time. Application.cs:387 ticks every RunFrameCore; Run (Application.cs:415-447) makes ≥ 2 iterations per rendered frame, +1 per wake. Real Run() loop, 60 Hz, 1.2 s: no wakes 2.0 ticks and 2.0 rebuilds per frame (VideoPlayer fade, a MarkDirty spinner); a wake every ms (1 kHz mouse, mpv callbacks): fade 6.3 ticks / 4.7 rebuilds, spinner 8.9 / 8.9. Per rebuild: VideoPlayer 42.3 KB, spinner app 355 KB, Junction History 186 + 14 KB. Users: VideoPlayer bar fade (VideoPlayer.cs:195, :217, :293); ToastHost fade (ToastHost.cs:75, :100) handed MarkDirty by Aperture, Junction, Meridian (ApertureApp.cs:106, JunctionApp.cs:423, MeridianApp.cs:156) — every toast tick rebuilds the whole app; Junction's spinner (JunctionApp.Spinner.cs:51) and remote-op tween (JunctionApp.RemoteOps.cs:51); Reel (22–62 rebuilds between frames). Rebuilds per animated frame 2 → 1 (−50 %), 4.7 → 1 (−79 %), 8.9 → 1 (−89 %) with 1 kHz wakes. Perf suite can't show it (PumpFrame ticks once per frame); use the run-loop probe. Small–medium. Needs the user (timing change).
- **reconcile-blueprint-hashset** (+ array-enumerator boxing). Element.cs:1139 `new HashSet<Element>(children)` is 208 of the 272–288 B per builder `.Children(1–3)` call (incl. a 32 B boxed enumerator from UnionWith). Prototype (no set for an empty parent; `Array.IndexOf` when old × new ≤ 64) allocKB: component/edit keyed 5,889 → 5,258 (−10.7 %); unkeyed 5,760 → 5,128 (−11.0 %); reverse keyed 5,918 → 5,287 (−10.7 %); tabs switch 1,744 → 1,554 (−10.9 %); expander 68 → 60 (−11.8 %); selection walk 108 → 100 (−7.4 %); mount 300 −3.2 %. Junction: 37 calls per press, ~7.7 of 728 KB. Small.
- **reactivity-scope-threadstatic.** `[ThreadStatic]` subscriber instead of AsyncLocal (ReactiveScope.cs:9): each Effect run allocates an ExecutionContext (72 B; 160 B with another AsyncLocal live — setting CultureInfo.CurrentCulture makes one). Prototype: signal fan-out to 500 labels 94 → 16 KB/op (−83 %); mount 300 −2.2 %; other rows flat. Apps: Junction ≤ 330 B of 728 KB per press; VideoPlayer 72 B of 42.3 KB per rebuild. Small; update DECISIONS.md:97. Needs the user (tracking behaviour).
- **reconcile-restyle-on-change** (re-scoped; rank last). Junction History Down: 585 resolves per press — 215 first-time (needed), 94 changed, 99 unchanged plain, 177 unchanged ApplyTheme types. Style is 0.6–1 % of a Junction frame; 0.0026 of 0.466 ms in the walk. component/edit rows: 6,003 unchanged resolves, style ~11 % of the step. Text re-themes only because its patch copies SelectionColor (TextElement.cs:491), which ApplyTheme then overwrites (:237) — an app's SelectionColor never shows (latent bug). ResolveRecursive (StyleResolver.cs:31-80) re-resolves every flagged ancestor. ≈ −10 % on the 3 component rows only; Junction 585 → ~310. Medium–large; 31 ApplyTheme overrides to audit.

## Examined, not worth it

- reconcile-persistent-child-in-place: now 87 KB per parent rebuild for a 1,206-element tree (165 before f47751a); Reel's parent rebuilds rarely; Aperture's parent rebuild adds/removes a strip anyway; the MenuBar field pattern is only in the Fluent sample.
- reconcile-unkeyed-shrink-linear: no app or suite row shrinks an unkeyed list.
- Themed control patches always mark layout (ButtonElement has no PatchMarksOwnLayout): Junction 20.5 marks per press; realized rows are laid out again anyway; layout 3.5–8.5 % of a frame.
- Builder twins mount with MarkStyleDirty walks (6,412 down-visits per Junction press): flag writes, 0 B.
- Batching unchanged; MarkDirty stays synchronous (user's decision).
- Reconcile bytes: Junction 29 of 728 KB per press (4 %); P5a stands.
- Computed/ReactiveList notify snapshots: unused by app or framework code.
- Guideline note: Junction's spinner and remote-op tweens and ToastHost(MarkDirty) rebuild the whole app every tick — animate an element (Transform, SpinnerElement) instead.

## Needs the user

1. **reactivity-tween-tick-per-frame:** tweens would step once per rendered frame (like requestAnimationFrame) instead of on every loop wake. App authors would notice: a tween's setter and OnComplete run at most once per display interval; while the window is redrawing, a delay timer (tooltip, player auto-hide, toast hold) can finish up to one interval later (16.7 ms at 60 Hz). A window not redrawing ticks as today. OK?
2. **reactivity-scope-threadstatic:** a signal read on another thread or after an `await` inside Build or an effect would no longer subscribe that build/effect. Today the subscriber flows with the ExecutionContext — on a pool thread a data race the write guard misses. Only code breaking the documented "produce must not touch reactive state" rule would notice. OK?
