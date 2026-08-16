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
| Text segmentation | Native ICU (P/Invoke) | Skia built-in, ICU4N | Full UAX #14/#29 support (line break, grapheme) via the platform's ICU, shared loader in `IcuLibrary`. UAX #9 (BiDi) uses the same library: display reordering and interaction mapping shipped (docs/plans/14-bidi.md); auto-direction and mirroring are staged |
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

---

## Events

| Decision | Rationale |
|----------|-----------|
| Capture → Target → Bubble | Matches DOM event model. Well-understood, flexible |
| Hit testing: depth-first, reverse child order | Front-to-back testing. Respects visual stacking order |
| Focus management built in | Tab/shift-tab traversal, focus/blur events, FocusManager |

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
