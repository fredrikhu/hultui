# HultUI Design Decisions

This document records all architectural and design decisions for the HultUI framework.

---

## Framework Philosophy

| Decision | Rationale |
|----------|-----------|
| No XAML | Prefer programmatic C# API. If markup is added later, it compiles to C# code |
| No full CSS cascade | Cascade has perf overhead and complexity. Use property inheritance for a small set + explicit styles |
| Performance first | Full redraw per frame is fine initially, but architecture supports dirty tracking and caching |
| HTML/CSS-inspired layout | Flexbox is well-understood, covers 95% of layouts, maps naturally to a programmatic API |
| Source generators for boilerplate | `[Reactive]` reduces ceremony, improves perf, eases learning curve |

---

## Technology Stack

| Layer | Choice | Alternatives Considered | Rationale |
|-------|--------|------------------------|-----------|
| Rendering | SkiaSharp | Silk.NET, Veldrid, MAUI Graphics | Battle-tested 2D, handles text/shaping, used by Avalonia. Can be swapped later behind abstraction |
| Text shaping | HarfBuzz (via SkiaSharp.HarfBuzz) | Manual shaping | Industry standard, handles complex scripts |
| Text segmentation | Native ICU (P/Invoke) | Skia built-in, ICU4N | Full UAX #14/#29 support (line break, grapheme) via the platform's ICU, shared loader in `IcuLibrary`. UAX #9 (BiDi) uses the same library, including first-strong paragraph direction for `TextDirection.Auto`: text is complete (docs/plans/14-bidi.md), interface mirroring is not part of it |
| Windowing/Input | SDL3 (ppy.SDL3-CS) | GLFW, platform-native | Cross-platform, provides GL context + input + clipboard + IME + drag-and-drop |
| Source generators | Roslyn incremental generators | Fody, PostSharp | No runtime dependency, standard .NET tooling |
| Test framework | xunit | NUnit, MSTest | Widely used, good async support |

---

## Architecture

### Project Structure

| Project | Purpose | External Dependencies |
|---------|---------|----------------------|
| HultUI.Core | Element tree, layout, reactivity, events, styling | **None** (zero deps) |
| HultUI.Text | Text pipeline interfaces, ICU4N segmentation, caching | ICU4N |
| HultUI.Rendering.Skia | SkiaSharp renderer, HarfBuzz shaping, font collection | SkiaSharp, SkiaSharp.HarfBuzz |
| HultUI.Platform.SDL3 | Window creation, input, GL context | ppy.SDL3-CS |
| HultUI.SourceGenerators | [Reactive] and future generators | Microsoft.CodeAnalysis.CSharp |
| HultUI | Meta-package, Application class | All above |

**Key constraint:** HultUI.Core has zero external NuGet dependencies. This makes layout, reactivity, events, and element tree testable with fast, pure unit tests.

### Dependency Direction

```
HultUI (meta-package)
  ├── HultUI.Platform.SDL3  ───► HultUI.Rendering.Skia ──► HultUI.Core
  │                                      │
  │                                      ▼
  │                                HultUI.Text ──────────► HultUI.Core
  └── HultUI.SourceGenerators (analyzer, no runtime dep)
```

---

## Layout System

| Decision | Rationale |
|----------|-----------|
| Custom C# Flexbox (not Yoga P/Invoke) | Better debugging, profiling, extensibility. P/Invoke overhead per node adds up. ~1000-1500 lines of algorithm |
| Box model (padding, border, margin) | Fundamental to all UI layout, matches CSS box model |
| Absolute positioning as escape hatch | Needed for popups, tooltips, overlays |
| Grid layout deferred to later | More complex, flexbox covers most needs initially |
| Layout is a pure function | Element tree + constraints in → LayoutResult on each node out. Fully testable without rendering |
| A leaf is measured once per width, not once per pass (2026-10-06) | Text measures at a width, so the leaf measure cache leaves the offered height out of its key unless the box has a percentage height, works a forced height out from the unforced measurement, and a leaf's `ComputeLayout` reads the same cache instead of measuring again. A 2,000-row resize went from 22,000 text measurements to 8,000 and its layout pass from 5.1 to 4.1 ms; all 71 perf-suite scenarios lay out bit-identically. Sharing the cache made a gap matter: the style pass never invalidated layout, so a font size changed by a theme or `MarkStyleDirty` kept the old size. It now marks an element layout-dirty when a property text is measured with changes |
| A measurement holds for a range of offered heights (2026-10-06) | Each measure-cache entry records the least offered height at which its answer holds (−∞ any, NaN its own only), worked out while measuring: a label any height; a percentage height, min or max exact; a definite height (given, forced, from a ratio) any; otherwise the content's floor plus padding — a row where its children hold, a column at or above its items' total when nothing grows or shrinks, a scrolling column anywhere. Grids, wrapping columns and rows aligning a container by baseline stay exact. A 2,000-row resize ran 22,001 container flex measurements, 14,000 of them repeats on the offered height alone; now 8,001: its layout pass went from 4.0 to 2.5 ms and the frame is 27 % shorter. `LayoutPathIndependenceTests` (1,000 random trees through windows that grow and shrink, against the same trees laid out fresh) is what keeps it honest. The next lever: the layout pass re-ran each container's flex phases 1–4 after the measure pass ran them at the same inputs — about a fifth of that resize's layout pass; taken in the row below |
| The layout pass takes the measure pass's flex resolution (2026-10-06) | The measure pass files each nowrap container's resolved items — Phases 1 to 3.5 and the baselines — with the content width and height they were worked out at and the heights they hold for (the range above); the layout pass takes them when its width matches and its height is in range, and sizes the lines itself, since they read the container's own size, definite only there. Filed per `PerformLayout` and dropped at its end, so an element carries only an index; wrapping containers are not filed. On a 2,000-row resize every container but the root takes one (−10 %). With it, an element's min-content width is kept in its measure cache until invalidated: it reads no constraint, and a shrinking styled label shaped its words for it on every pass (styled ellipsis resize −48 % for the two). Known miss: a row item laid out at its natural width, narrower than it was measured at, filed a plan it never took (wrap 1,000 chips: +0.03 ms of layout); taken by the widths in the row below |
| A measurement holds for a range of offered widths (2026-10-06) | A measure-cache entry keeps the widths its answer holds for. A box whose own width is set (given in pixels, forced, or from a set height by a ratio) holds at any; one sized from its content holds where its content does, mapped back through padding, min and max one float step in at each end. Text says where it holds through `IMeasureContext.MeasureText(..., out sameFromWidth)`: the pipeline answers from its single-line fast path (one line that fits is the same at any width it fits in); styled, BiDi, wrapped and truncated text, and every other context, answer NaN — that width only — which is always safe. A row holds where its children do and its items fit without growing; a column where its children do, unless it shrinks them. Percentage widths, bases, mins and maxes, wrapping, ratio boxes stretched across a column and rows aligning containers by baseline hold at their own width only. Flex plans take the same range. With it, `ComputeLayout` keeps a clean subtree on an axis whose size is set whatever it is offered there. A resize no longer measures or lays out again a fixed-width box, a label that fits, or a row item forced to its natural width: 2,000-row resize −30 %, deep-nesting edit −39 %, wrap chips −12 %, window-benchmark remove −41 %. Kept at 28 bytes an entry: the height measured at went, its exactness is a bit of a byte that was padding (at 32 bytes a 1,000-row rebuild allocated 94 KB more and ran 3–5 % slower). Left: mounting fresh icon tiles and control galleries is 1–3 % slower, and stays so with the ranges switched off — the measuring code's shape, not its work |
| Aspect ratio kept, at its measured cost (2026-10-06) | Weighed against dropping it for speed. Knocked out of the current engine, flex relayout is 2–3 % faster; the +11 % its commit (f1fa4e4) measured when it landed was mostly code shape that later changes absorbed, and the leaf cache above more than repaid it. `VideoPlayer` sizes video by it, and pictures keep their shape when given one side (Aperture's posters, Junction's markdown images), so it stays. An element with no ratio and no picture answers `RatioOf` from a field, before any virtual call |

---

## Styling

| Decision | Rationale |
|----------|-----------|
| Small set of inheritable properties | FontFamily, FontSize, FontWeight, TextColor, TextDirection, Cursor. Null = inherit from parent |
| Everything else explicit per element | Padding, margin, background, border are not inherited — avoids cascade complexity |
| Theme object for root defaults | One Theme per Application. Swappable at runtime. Provides root values for inherited properties |
| Theme extension by key, not by selector | A control shipped outside the framework needs a themable slot. `theme.Get<T>()` over a keyed store, defaults from `IThemeExtension<T>.CreateDefault(theme)`, overridden with `theme.With(...)`. One dictionary lookup at style time — no cascade, no specificity, and the theme object stays inspectable. The built-in control themes are facades over the same store, so the framework's own controls are not a privileged case |
| Style resolution is O(1) with caching | Resolved value cached on element. Invalidated only when ancestor changes |

---

## Reactivity

| Decision | Rationale |
|----------|-----------|
| Signal<T> / Computed<T> / Effect | Fine-grained reactivity (SolidJS-inspired). Most performant model — surgical updates, no diffing |
| ReactiveScope with AsyncLocal | Thread-local dependency tracking. Safe nesting. Works with single UI thread |
| Batch updates | Multiple signal changes coalesce into one effect flush. Prevents redundant rebuilds |
| [Reactive] source generator | Generates Signal<T> backing from property declaration. Reduces boilerplate |
| Direct mutation as escape hatch | Any element can be mutated directly (dirty-flag style) for perf-critical paths |

---

## Component Model

| Decision | Rationale |
|----------|-----------|
| Class-based primary | Natural C# unit, IDE-friendly, source-generator-friendly, lifecycle hooks as virtual methods |
| Function shorthand for stateless | Static methods returning Element. Concise for simple components |
| Build() wrapped in Effect | Auto-rebuilds when signals read during Build() change |
| Lightweight tree diffing | Match by type + key, patch in-place. Full vDOM diffing not needed since reactive system knows what changed |
| Discarded build elements are marked, not forwarded | Patching keeps the live element and throws the incoming one away, so an element captured during a build is a corpse one frame later. `Element.IsDetached` says so, and in a strict-lifetime build reading its `Layout` or writing its scroll offset throws. Forwarding those accesses to the live counterpart was considered and rejected: after a keyed reorder the counterpart is a *different row*, so forwarding would quietly act on the wrong element — worse than failing. `Ref<T>` remains the way to hold an element across a frame |

### The detached-element trap

Every rebuild constructs a fresh tree. `TreeDiffer` patches its values into the live tree and
discards it, so a reference app code captured during the build points at something that will
never be laid out, rendered or hit-tested again. Both halves fail silently: reads return
geometry that was never computed, writes land on state nothing consumes. The symptom is always
"this control has stopped responding", with nothing in the log and nothing to breakpoint.

Three defences, in descending order of how much they catch:

1. **`Ref<T>`** — the fix. The reconciler repoints it at the live element on every patch. Inside
   a handler, `e.CurrentTarget` is already live.
2. **The runtime guard** (`HULTUI_STRICT_LIFETIME`, on in Debug) — sound, because the reconciler
   is the one place that knows which elements it threw away. Names the element and its origin
   line, so the message points at the build site rather than the crash site.
3. **HULTUI003** — catches the one shape of the mistake that is decidable at compile time:
   comparing an event's `Target` against an element from elsewhere.

There is deliberately **no** analyzer for "element stored in a field". Deciding whether a given
element came from a build needs interprocedural escape analysis — the value is routinely a
method parameter — and the shape is legitimate elsewhere: `Window` holds the live root and the
drag source, and the Benchmark sample holds a statically built tree it never reconciles. A rule
firing on those would be noise, and warning noise is the failure these rules exist to prevent.

---

## Events

| Decision | Rationale |
|----------|-----------|
| Capture → Target → Bubble | Matches DOM event model. Well-understood, flexible |
| Hit testing: depth-first, reverse child order | Front-to-back testing. Respects visual stacking order |
| Focus management built in | Tab/shift-tab traversal, focus/blur events, FocusManager |
| **Handler storage stays `Dictionary<Type, List>`** (2026-09-20) | The bitmask/inline-array rewrite was measured at ≤5 ns per event — see "Allocation items P4/P5" under Rendering |

---

## Threading

| Decision | Rationale |
|----------|-----------|
| Single UI thread | No locks in hot path. Simplest correct model |
| Thread affinity is checked, not assumed | No locks means a background write corrupts state silently instead of failing. `UIThread.VerifyAccess` on the Signal setter, effect scheduling, and the `Element.Mark*Dirty` funnel throws at the offending call. Debug-on / Release-off by default; one thread-id comparison |
| Custom SynchronizationContext | Standard async/await automatically marshals back to UI thread |
| Same-thread rendering | Layout + paint on UI thread. Simpler than separate render thread. Sufficient with VSync |

---

## Text Rendering

| Decision | Rationale |
|----------|-----------|
| Build font fallback chain | SkiaSharp doesn't auto-fallback. Required for emoji + multi-script text |
| Three-level caching | MeasurementCache (full layout), GlyphCache (shaped runs), FontFallbackCache (codepoint→font). Text is the hot path |
| ICU4N for segmentation | Full Unicode algorithm support. Needed for correct cursor movement, selection, line breaking |
| HarfBuzz for shaping | Industry standard. Handles ligatures, kerning, complex scripts |

---

## Rendering

| Decision | Rationale |
|----------|-----------|
| Full redraw per frame | Simple, correct. SkiaSharp + GPU can repaint complex UI in <2ms |
| GPU-accelerated via OpenGL | SkiaSharp renders to GRContext backed by SDL3's GL context. Zero pixel copying |
| IRenderContext abstraction | Backend-swappable. Core doesn't depend on SkiaSharp |
| SKPaint pooling | Avoid per-draw-call allocation. Reuse paint objects |
| **No damage rects** (2026-08-16) | Measured, not assumed — see below |

### Damage rects: measured and declined

`DamageProbe` (attach `DamageProbe.OnFrame`, or read the per-frame `Damage`
annotations in an F11 trace) reports what a damage-rect renderer would have
repainted: the union of the deepest paint-dirty elements, a deliberate best case
for partial repaints. Junction hosted headlessly on this repository at 1400×900,
per rendered frame:

| Workload | Frames | Damage union p50 | Full-repaint cost p50 |
|----------|-------:|-----------------:|----------------------:|
| History hover sweep | 30 | 100% (40% of frames <10%) | 1.1 ms |
| History selection walk | 25 | 97% | — (git work dominates) |
| History wheel scroll | 25 | 64% | 1.1 ms |
| Files find-bar typing | 5 | 97% | 1.3 ms |
| Files tree cursor walk | 15 | 45% | 0.9 ms |
| View switch | 3 | 100% | 0.8 ms |
| Tooltip appears | 1 | 100% | 1.2 ms |
| Idle | 0 | — | — |

Three reasons partial repaints don't pay here:

1. **Idle is already free.** Frame-granularity dirty tracking renders nothing
   when nothing changed — the caret-blink scenario damage rects classically fix
   does not exist in this framework.
2. **The frames that do render are honestly large or conservatively large.**
   Scrolling moves 64% of the window; rebuild-driven frames (typing, selection)
   patch whole views; and the input safety net (`Window` marks the root when any
   handler ran, because handlers may mutate without marking) turns most pointer
   traffic into whole-window invalidations. Damage rects cannot shrink a frame
   the invalidation system cannot describe — tightening invalidation (the
   HULTUI002 analyzer is the migration path for silent mutations) would have to
   come first, and it, not clipping, is where the win lives.
3. **A full repaint costs ~1 ms** at 1400×900 on the CPU raster path (GPU is
   cheaper still) — ~6% of a 60 Hz budget, only while the user is actively
   interacting. The complexity price of damage rects (old-bounds tracking,
   shadow bleed, buffer age) buys back part of one millisecond.

Revisit only if a workload appears where frames are frequent, small-change, and
expensive — the probe stays in the tree to make that check cheap.

### Allocation items P4/P5: measured and declined

The external review (2026-08-12) listed four remaining allocation/locality
items after the SKPaint pooling, copy-on-write dispatch and `Signal` 0/1/many
work landed. Measured 2026-09-20 (Release, i7-14700K, headless session, mins of
five rounds; a temporary `GC.GetAllocatedBytesForCurrentThread` probe inside
`TreeDiffer` for the reconcile numbers):

| Item | What it would replace | Measured cost today | Verdict |
|------|-----------------------|---------------------|---------|
| P4 handler bitmask | one `Dictionary<Type, List>` probe per path element that has handlers | 2.6 ns miss / 3.5 ns hit; a full depth-8 `Dispatch` is 105 ns (170 ns with all 8 listening) | ≤5 ns per event. Declined |
| P4 inline handler array | the dictionary + list behind the first handler | first handler +424 B on a 576 B `BoxElement` (dictionary ~216, list ~120, closure+delegate ~88); second event type +208 B | ~250–300 B per handled element is the only real number here. Not needed by any app; revisit if element memory becomes a target |
| P5 `TreeDiffer` pooling | the two `ToArray` snapshots and the keyed `Dictionary<string, Element>` | 1,000-row keyed rebuild: 3.6 ms, 4.2 MB, of which snapshots 112 KB + keyed 118 KB = 5.4 % (2.8 % unkeyed; same ratio at 50 rows) | ~5 % of gen0 churn on rebuild frames, no measurable time. Declined |
| P5 text `LruCache` ring | the `LinkedList` relink on a cache hit | hit 57 ns vs 50 ns for the bare dictionary lookup; ~7 ns of the 70 ns warm `Layout` call | Declined |
| P5 `Element` bool packing | six `bool`s and one `bool?` | ≤8 B of 576 B; the size is inline structs (`LayoutResult`, 4-slot `MeasureCache`, `ResolvedInheritedStyle`, constraint cache) | Declined |

Rebuild bytes are dominated by constructing the new tree (~1 KB per element),
and a pointer move is dominated by hit-testing, not dispatch: the same move
costs 107 ns over a 10-row table and ~5 µs over a 1,000-row column (~5 ns per
child), with enter/leave allocating 528 B vs 1,016 B. That linear hit test is
the lever if pointer cost ever matters; none of the review's items touch it.

### Right-to-left *text* is done; right-to-left *UI* is a separate project

BiDi (docs/plans/14-bidi.md) is finished as a **text** feature: mixed-direction
lines reorder per UAX #9, brackets mirror, the paragraph direction can be
auto-detected, and the caret, click targeting, selection and arrow keys all
follow the visual order. `TextAlign.Start`/`End` follow the direction the text
resolved to.

Mirroring the **interface** — a `direction: rtl` that flips flex rows,
`JustifyContent.Start`, absolute Left/Right, grid column order, logical
padding/margin sides, and the scrollbar to the other edge — is deliberately not
part of it. `LayoutEngine` has no notion of direction at all today (the word
appears nowhere in its 1,100 lines), so this is not a switch to flip: it is
logical-vs-physical box sides across the whole layout API, ~14 explicit
right-edge placements in Core's elements, and every app's own left/right
choices. It is a capability like Grid or animations, and belongs in the roadmap
with its own design — not smuggled in behind a text change.

The line between them is where the value is: a chat message, a commit subject or
a file name in Hebrew renders and edits correctly **today**, inside an interface
that stays left-to-right. That is what an LTR application showing user content
needs. A fully mirrored interface is what an application *localized into* Hebrew
or Arabic needs, and nothing in the tree asks for that yet.

---

## Future Considerations

- **Grid layout** — Phase 2 layout addition after flexbox is stable
- **Animation system** — Frame-based update loop, easing functions, spring physics
- **Multi-window** — SDL3 supports it, architecture should accommodate
- **Hot reload** — Detect tree changes and re-render without restart
- **Accessibility** — Design for it from start (Role, AccessibleName on elements), platform bridges later
- **Compiled markup** — Optional DSL that compiles to C# Build() methods
- **Units for measurements** - points, percentage, etc. What makes sense; cm/inches do not.
- **Theme improvements** - Color.Themed(light, dark) or something like that. We need to make some design decisions here.
- **Keyboard navigation** - E.g. tab to focus next element (and possibility to decide what is next), arrows to change between radiobox selection.
- **Right-to-left interface mirroring** - Logical (start/end) box sides through the layout API, mirrored flex/grid/absolute placement, scrollbar and gutter on the other edge. Distinct from right-to-left *text*, which is done; see the note above.
