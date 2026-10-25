# Round 1 survey: 4. .NET runtime

Allocation probes under JIT and an AOT-published headless probe; 600 headless window
open/close cycles and 2,000-cycle overlay loops for leaks (worktree
agent-ab604730a092dd98b at b9f80d0, probes reverted).

SATISFIED: no
REVIEW: none (round 1)

## Candidates

- **runtime-aot-trace-type-name.** Build the trace scope's arguments only while the tracer records. `element.GetType().Name` runs per rendered, laid-out and measured element every frame; under NativeAOT each call allocates a string.
  - Evidence: ElementRenderer.cs:52-54, LayoutEngine.cs:53-55, :592-594; `tracer is null ? default : tracer.Begin(...)` checks IsRecording after building arguments. AOT: 48 B per rendered element; 400 tiles 11.45 of 20.69 KB/frame; Junction History steady frame 16.35 of 41.03 KB/frame (348 elements); History selection move layout 26.41 → 1.30 KB/op. JIT caches Type.Name → invisible to the perf suite.
  - Expected: Junction AOT frame −40 % bytes; AOT gen0 GCs on tiles 24 per 20k frames → 0 (with text-draw-enumerators).
  - Cost: small.
- **runtime-text-draw-enumerators** (= paint-text-zero-alloc's enumerator half). 72 B per drawn label per frame, JIT and AOT. Tiles 9.22 → 0.71 KB/frame; Junction History 10 of 24.6 KB/frame. allocKB (with frame-floor): paint/tiles plain 5.9 → 0; dense text 18.2 → 0; input/type 44.8 → 1.8; deep nesting 51.3 → 1.8; anim/hover 36.5 → 2.1; input/hover 6.8 → 0.5; scroll/wheel 5,000 9.7 → 3.3; component/counter 10.2 → 3.6; theme switch 28 → 10.5. 45 rows drop ≥10 %.
- **runtime-frame-floor.** Remove the steady-frame and idle-loop allocations: SlidingWindowMax LinkedList (8 nodes/frame, HUD off; SlidingWindowMax.cs:21,42, PerformanceCounters.cs:128-135, ~0.45 KB/frame); method-group delegate to PumpEvents per loop pass (Application.cs:371, 64 B, idle too); interface foreach over Children in dirty-flag walks (Window.cs:2265, :2331, LayoutEngine.cs:1771, StyleResolver.cs:67; 40 B per dirty container; ~28 KB per Junction History move, ~22 KB per Vesper cursor move); FindCommonAncestor HashSet (PointerState.cs:71, ~0.2 KB per hover change).
  - Evidence (with text-draw-enumerators, temporarily applied): tiles steady 0.71 → 0.00 KB; idle pass 0.06 → 0.00; 60 looping tweens 0.8 → 0; 1,000-row hover 1.34 → 0.69 KB/move (rest = event objects, boxed DirtyFlags, a closure).
  - Expected: steady frames at the 0.1 KB target.
  - Cost: small.
- **runtime-shaper-allocs** (owner: text pipeline; = text-shaper-reuse). 930 B per shaped run: 563 inside SKShaper.Shape, ~188 SKPaint/SKFont wrappers, 174 our conversion (SkiaTextShaper.cs:20-21, :37). text/create 2,000: 1,816 of 7,312 KB/op, 1,476 removable; TextInput keystroke 3.1 of 7.6 KB, 2.45 removable. Each SKPaint/SKFont registers in SkiaSharp's global HandleDictionary. Minimal (drop SKPaint, cache SKFont) ~−5 %; shaping through a HarfBuzz buffer directly ~−20 %.
- **runtime-text-services-per-window.** Each window/WinForms control builds its own SkiaTextServices, never disposed; its SKShaper cache (HarfBuzz font + SKStreamAsset per typeface) leaks on close; caches duplicated per window.
  - Evidence: SkiaGLRenderSurface.cs:36, SkiaRasterRenderSurface.cs:35, SkiaVulkanRenderSurface.cs:126; SkiaTextServices not IDisposable (:16); SkiaTextShaper.cs:10 cache never disposed. 600 headless open/close cycles: HandleDictionary +2 SKStreamAssetImplementation per cycle (48 → 652 over 300); private bytes +29 KB per cycle; managed +0.5 KB per cycle. Per-window caches up to 16,384 + 8,192 + 4,096 entries (TextPipeline.cs:21, 32, 1348). Hits WinForms controls, DevTools window, multi-window apps.
  - Expected: leak closed; sharing per Application stops duplication.
  - Cost: small (dispose with surface) or medium (shared per Application).
- **runtime-junction-graph-allocs** (app; = junction-lane-palette). `LanePalette` returns a new Color[6] per read (JunctionApp.History.cs:21-29), read 4× per LaneColor (:1287-1288); `foreach` over `IReadOnlyList<GraphEdge> ?? []` boxes (:1263, 1266, 1278, 1280). 13.9 KB per History frame; 94 % of the 14.76 KB AOT frame left after the framework fixes. Guideline: an expression-bodied property returning a collection expression allocates on every read.
- **a11y-uia-apply-and-retention** (owner: a11y; code reading only) = a11y-uia-wrapper-rebase + a11y-build-diff-garbage.

## Examined, not worth it

- Overlay mount/unmount leaks: none (2,000 cycles each; heap plateaus).
- Replacing 300 handled rows × 300: −7 KB; wheel × 2,000 plateau 40 KB; Junction view switches plateau at 37.5 MB. Long History walk retention not measured.
- Icon raster caches: discrete key set, invalidated on theme change.
- Junction highlight prefetch: 10.3–11.5 MB per keypress on workers, ≥75 % inside TextMateSharp/Onigwrap; app design.
- Vesper KeyChord enumerator: 0.8 % of a keypress.
- Big per-interaction bytes are rebuild and VirtualList realization: Vesper cursor move 720 KB (466 in ScrollView.UpdateContentSize across both panes); Junction History move 822 KB (build 286, drain 262, UpdateContentSize 219). Boxed Children enumerators 3–3.4 %.
- AOT vs JIT after the trace guard: same bytes.
- UiaClientsAreListening P/Invoke once per frame.
- No LINQ/closures in the frame path apart from the PumpEvents delegate.

## Needs the user (resolved by the orchestrator)

- **Idle wake-ups** (same as GL/Vulkan's): Run() waits 16 ms when nothing is dirty (Application.cs:456) → 62.5 RunFrameCore passes per second forever. Deadline wait needs explicit wakes for: AT attach, AT action-router queue, trailing a11y refresh (Window.cs:732-745). **Orchestrator: yes, idle bar added.**
- **AOT allocation capture:** shipped apps allocate 2.2× more per frame on AOT (tiles 20.7 vs 9.2 KB, Junction 41.0 vs 24.6). **Orchestrator: yes** — the build phase checks AOT allocKB when a change targets per-frame allocation in apps (brief rule added).
- Housekeeping: two stray probe edits had landed in distracted-mendel-5071ba and were reverted; verified clean (SkiaTextShaper.cs matches blob 7ebc35c).
