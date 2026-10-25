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
| A measurement holds for a range of offered widths (2026-10-06) | A measure-cache entry keeps the widths its answer holds for. A box whose own width is set (given in pixels, forced, or from a set height by a ratio) holds at any; one sized from its content holds where its content does, mapped back through padding, min and max one float step in at each end. Text says where it holds through `IMeasureContext.MeasureText(..., out sameFromWidth)`: the pipeline answers from its single-line fast path (one line that fits is the same at any width it fits in); styled, BiDi, wrapped and truncated text, and every other context, answer NaN — that width only — which is always safe. A row holds where its children do and its items fit without growing; a column where its children do, unless it shrinks them. Percentage widths, bases, mins and maxes, wrapping, ratio boxes stretched across a column and rows aligning containers by baseline hold at their own width only. Flex plans take the same range. With it, `ComputeLayout` keeps a clean subtree on an axis whose size is set whatever it is offered there. A resize no longer measures or lays out again a fixed-width box, a label that fits, or a row item forced to its natural width: 2,000-row resize −30 %, deep-nesting edit −39 %, wrap chips −12 %, window-benchmark remove −41 %. Kept at 28 bytes an entry: the height measured at went, its exactness is a bit of a byte that was padding (at 32 bytes a 1,000-row rebuild allocated 94 KB more and ran 3–5 % slower). Left: mounting fresh icon tiles and control galleries 1–3 % slower, put down to the measuring code's shape — wrongly: see the row below |
| A leaf holds at any height when its height is set or its content ignores it (2026-10-06) | The mount regression above was real work, found by counting rather than timing: a leaf of a set height (a 20 px icon) recorded its measurement for the height it was measured at only, so its column's did too, and the column's flex plan never matched the height the layout pass offered — icons/mount 600 took no plan at all, before the width ranges or after. A leaf whose height is set, forced or made from its width by a ratio now holds at any height, as a container already did; controls whose `MeasureContent` never reads the height (checkbox, toggle, slider, progress, spinner, dropdown, stepper, radio, text field, splitter) say so with `Element.IgnoresAvailableHeight`, which promises nothing about forced heights or baselines (`MeasuresAtWidthOnly` does). Since 2026-10-10 it is true for every element and a video, the one leaf that reads the height, clears it: spacers, chevrons, sort marks and pickers kept their rows exact-height (an expander animation measured 72 % fewer containers after). A width range's ends are stepped in only where rounding needs it: always stepping cost every padless row item laid out at its natural width its plan. And a box whose old or new place is past 2^24 is laid out rather than moved by the difference: moved out to 1.7E+38 past an unbounded edge and back, its descendants came back at 0 — found by random trees of controls, older than the width ranges, which made it likelier. icons/mount −10 % (layout −40 %), controls gallery −6 % (layout −30 %) |
| A patch that changes nothing about the layout leaves it standing (2026-10-06) | `Element.PatchFrom` marked every element it patched for layout, and a rebuilt component patches every element it keeps: editing one row of a thousand laid all thousand out again (6,003 ComputeLayout calls, none kept). The layout style objects' `CopyFrom` now say whether they changed anything, and the base marks layout only then — for a kind of element that marks its own layout when the rest of what it copies changes (`PatchMarksOwnLayout`, answered for the exact type so a subclass is checked on its own): Box, Text (whose MaxLines and Overflow setters mark it when they change, since 2026-10-10; they were plain properties, checked here), Image and ScrollView; since 2026-10-11 also Canvas, Slider (whose patch marks it when ThumbRadius, the one copied value its measure reads, changes), and HultUI.Media's VideoElement and the video player's root (a player rebuild that moved nothing laid the player out again, and with a reader listening rebuilt the window's accessibility tree 100 ms later). Others keep the unconditional mark — Button's theme rewrites its padding, so its patches would differ anyway. Component edits of 1,000 rows −37 % (layout 1.2 → 0.06 ms). Random patched trees (`PatchRelayoutTests`) then found `ForcedDimMatches` keeping a box laid out unforced when forced to the size it came out at — not the same layout, since its content had been offered the room available, not the forced size; a kept layout now needs the forced sizes exactly. Left: the style pass still runs for every patched element (0.2 ms of the edit), and the rebuild itself (1.4 ms) |
| A forced width is the unforced measurement at that width (2026-10-06) | Counting text measurements per resize step found a growing label measured again at every step though it fits on one line: the measure cache keys on the exact forced width, a new one each step, while the label's answer holds for any width it fits in. For a leaf sized from its content a forced width changes nothing but the width — its content is offered what it would be offered at that width unforced — so the forced measurement is now the unforced one at that width, given that width, and that one is kept by its width range. Not for a leaf with a width of its own, a percentage min or max width, or a ratio (the counter-case has a test: a 140 px label shrunk to 100 wraps at 100). Text measurements a step: 2,000-row resize 2,000 → 0, baseline rows 500 → 0, ratio tiles and grid 600 → 0, wrapped paragraphs 300 → 150; 2,000-row resize −14 %, overfull rows −8 % |
| Aspect ratio kept, at its measured cost (2026-10-06) | Weighed against dropping it for speed. Knocked out of the current engine, flex relayout is 2–3 % faster; the +11 % its commit (8ee21ba) measured when it landed was mostly code shape that later changes absorbed, and the leaf cache above more than repaid it. `VideoPlayer` sizes video by it, and pictures keep their shape when given one side (Aperture's posters, Junction's markdown images), so it stays. An element with no ratio and no picture answers `RatioOf` from a field, before any virtual call |

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
| ReactiveScope with `[ThreadStatic]` | Thread-local dependency tracking. Safe nesting. Works with single UI thread. Was an `AsyncLocal` until plan 17 (2026-10-10): it flowed into a `Task.Run` or the rest of an `async` lambda started inside an Effect, so a read there subscribed from another thread, and every set made an ExecutionContext (72 B an effect run, 160 B with another AsyncLocal live; a signal read by 500 effects 94 → 16 KB a write). A read on another thread or after an `await` no longer subscribes |
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
| **A keyed list that comes back as it was is left in place** (2026-10-06) | Reconciling keyed children ended in `ReplaceChildren` even when every child matched in the same order. That changes nothing, but it marked the parent and every ancestor as structurally changed and stale for layout, and a structural change rebuilds the accessibility tree that frame — so any rebuild of anything holding a keyed list rebuilt the whole tree while a client listened. The list is now replaced only when the result differs from the children the parent has; a child that changed still marks its own way up. Junction keys its root, sidebar rows and overlays, so every rebuild paid it: a move of the History selection (Native AOT, UIA client listening, 3 interleaved runs a side) 2.05 → 1.39 ms a frame, accessibility 0.69 → 0.04 ms. The scenario suite and the table benchmark have no such list and are flat. Splitting Junction into components instead was measured at about 0.1 ms: the whole-app rebuild is 0.22 ms |
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
| **Left-to-right text takes the plain path unless it can need BiDi** (2026-10-06) | Any char at or above U+0590 used to send its text down the BiDi path — an em dash, an arrow, a check mark, CJK — at twice the time and memory of the plain path for the same labels with a hyphen. Now only text that can need the BiDi path takes it: a char that can be right-to-left or steers direction, or one that can join its neighbours into one user-perceived character (surrogates, marks, joiners and variation selectors, Hangul jamo, format chars, separators, unassigned), which the plain path's cuts between chars would split. Broken into lines the two paths lay the rest out the same to the bit, except that the BiDi path, adding up break segments shaped alone, broke some lines that fit (Noto Sans CJK on Fedora, letter spacing) — the plain path keeps them. Truncated, the text now truncates the way Latin text always has. `PlainPathEquivalenceTests`, `BidiRoutingTests`; `PlainPathForLtr = false` keeps the old routing as the reference. `text/create 2,000 unique labels` 8.6 → 4.9 ms, 14.9 → 7.6 MB |
| **A TextArea finds each line break from a guess at a line's length** (2026-10-06) | Wrapping a line measured the whole rest of it and then bisected all of it, measuring prefixes up to the line's length for every line it wrapped to — a 1,700-character paragraph was shaped some 150,000 characters' worth, and opening a 900-line document took 30 ms and 41 MB. The search now starts where the line should end, from the width of the text so far, and settles between a probe that fits and one that does not: about three measurements of a line's length per wrapped line (31.6 → 8.5 ms, 41 → 13.5 MB). It counts grapheme clusters, because a cut inside one measures nonsense — half of 👩‍💻 draws as a woman and a laptop, wider than the whole — so over clusters, and only there, more of a line never measures narrower; a line can no longer be broken inside a cluster either. A line that fits is measured once, whole, as before, and a line of plain characters skips the cluster walk. `TextAreaWrapTests` holds it to every whole-cluster prefix measured, with real fonts |
| **A TextArea reads a line's widths off one table of its prefixes** (2026-10-06) | Each of the three or so prefixes the break search measured was a new string, laid out whole: itemized, shaped, a single-line layout built and cached, the cache filled with strings nobody would draw. `IRenderContext.MeasureTextPrefixes` hands back the widths of every prefix of a string at once — `TextPipeline.MeasurePrefixes`, the table one-line truncation already used, itemized once with the word cache's shapes and the running sums laid out the way a measurement adds them, so each width is `MeasureText` of that prefix to the bit. The search asks for one per wrapped line, half as long again as its first probe. A prefix is a paragraph of its own, so one that stops short of the line's kana or Hangul can draw its Han in another face: those are laid out the long way. Text on the BiDi path, or with a newline in it, gets no table (null, also the default for other contexts), and its prefixes are measured one at a time as before. A table is built where the old probes found the pipeline's caches warm, though: every keystroke wraps every line again, and typing in a document of long paragraphs went 1.0 → 3.1 ms. So the TextArea remembers where each long line broke, by its text, for the same width, style, render context and UI culture, keeping only the lines there are now; an edit searches the line it changed (0.80 ms, below the 1.0 it was). (Until 2026-10-12: the area now keeps its rows by line, and the table by text went, below.) Lines that fit were not kept — measuring one whole was a hit in the pipeline's cache (until 2026-10-10: the area keeps their widths now, below). `TextPrefixWidthsTests` (every prefix of a corpus, HarfBuzz and system fonts, five styles), `PrefixWidthsTests` (faces of different widths per Han context), `TextAreaWrapTests` (a plain-path corpus, letter-spaced too; 80 edits under changing widths and styles). `text/open a long document in a TextArea` 8.3 → 4.6 ms, 13.5 → 7.1 MB; `input/type in a wrapped 900-line TextArea` 1.0 → 0.8 ms, 0.93 → 0.49 MB |
| **The text caches are bounded by bytes, not only by entries** (2026-10-09) | An application keeps one `TextPipeline` for its whole life, so what its caches hold outlives the text that filled them, and an entry count bounded little: a label's shaping keeps ~0.5 KB alive, a long line's 16 bytes a glyph more, and 16,384 shapings or 8,192 words of any length could stay. Once closed, a CSV file of 1,000-character lines without spaces in a `TextArea` left 23 MB of "words" behind; a 5,000-block document in the rich text editor 8.8 MB, mostly laid-out paragraphs. The shape, word and layout caches now also stop at 24 (8 since 2026-10-10), 4 and 4 MB of estimated bytes (16 a glyph, 2 to 4 a character, a few hundred an entry: within 6 % of what full collections measure), least recently used out first: the CSV leaves 4.2 MB, the rich text document 4.5. An entry counts for at most half a bound, so a whole document laid out in one label stays cached beside the labels drawn around it instead of pushing them out, and being pushed out, every frame. The shape cache's bound is as large as it is because a `TextArea` measures every line that fits again on each keystroke and finds it there: it holds 16,384 lines of 55 characters, the count's worth. At 8 MB, typing in a 10,000-line JSON document went from 2 to 26 ms a keystroke, and opening the suite's 19,000-line JSON response again from 7.5 to 8.7 ms. So a document of short lines still leaves its shapings (19,000 lines of JSON: 22 MB) until something else takes their place; once a `TextArea` keeps its own line widths, the bound can come down to ~8 MB. It did (2026-10-10, row above): 8 MB, where every perf-suite row finds what it found at 24, and 4 MB was too small (the 5,000-block rich text document took twice as long to open). A 19,000-line JSON document closed now leaves 4.3 MB behind (9.6 before), TODO.md 2.5 (4.0), a 20,000-line log scrolled through from top to bottom 11.3 (26.8); a JSON document scrolled through keeps its lines' shapes, under the bound, and their widths: 11.3 MB (9.8). Longer lines that fit get fewer entries than before: typing stays fast (3–7 ms a keystroke) up to ~7,000 lines of 150 characters, and takes 31–77 ms from 8,000 (16,384 before). Every perf-suite row finds what it found before in all three caches (hits and misses counted per row), Junction and Vesper Commander use under 2 % of the bounds, and no suite row is slower (the NativeAOT `grid/` rows move ±10 % with a never-run method added). The BiDi/styled memos are still bounded by entries only: 15 MB after a 5,000-block rich text document is opened twice and resized (by bytes too since 2026-10-12, row below). `CacheBoundTests`, `LruCacheTests` |
| **A TextArea keeps each line's width, and asks for a width, not a layout** (2026-10-10) | A TextArea works its line starts out again on every edit and every resize, and of a line that fits it needs one number, its width. It got it by laying the line out (`MeasureText`), so every line of a document it opened was shaped whole and kept in the shape cache, drawn or not (a 19,000-line JSON response: ~4,500 new entries, ~9 MB), and that cache was each keystroke's store of line widths, which is why it was 24 MB. Now the area keeps the width of each hard line, 4 bytes a line, and carries the widths to an edited text: the old text and the new are compared from each end, the hard lines inside the part they start with alike keep their numbers, those inside the part they end with alike move by the change in line count, and only the lines between are measured; another style's metrics, render context or UI culture forgets them all. A width it does not have it asks for with `IRenderContext.MeasureTextWidth(ReadOnlySpan<char>, style)` — by default `MeasureText(…).Width` — which the pipeline answers by adding the line's words up as the shaping does (`MeasureString`): the layout's width to the bit, nothing shaped whole, nothing kept but the number. Text on the BiDi path or with a newline in it is still laid out; since 2026-10-11 the width of BiDi text is kept as well, under its paragraph direction (the space in "a ב c" sides with the Latin in a left-to-right paragraph and with the Hebrew in a right-to-left one, and measures as its run does), because a caret or a selection on a line of emoji or marks asks for the widths of the same prefixes of a run on every frame (`TextPrefixMeasurer`, handed the run's text and a length, which a Skia context answers with `MeasureTextWidth` over a span of it: no string cut per boundary). Lines repeat — a JSON response's `"draft": false,`, most of the response to a request sent again — and finding one is cheaper than adding its words up, so the pipeline keeps widths too (4 MB, ~180 bytes and 2 a character an entry, looked up by a span of the document): every width while there is room, then only a line measured lately, because putting every line of a long document into a full cache to push another out cost the lines that never repeat more than it saved (a 900-line document of prose opened 9 % slower than with no width cache, 3 % with the rule; the 19,000-line JSON response 12 %, then 24 %, faster). `text/open highlighted JSON in a TextArea` 7.95 → 6.26 ms JIT (−21 %), 9.79 → 7.87 NativeAOT (−20 %), 6,599 → 1,903 KB (−71 %); a long document −8 %/−10 %, −26 % KB; typing in a 2,000-line TextArea −22 %/−23 %, 465 → 222 KB; undo-redo −28 %/−24 %, 477 → 233 KB; typing in a wrapped document −10 %/−5 %. Meridian, ten JSON responses of 18,884 lines in a row, some sent again: the frames that show the second to the tenth 24.9 → 7.3 MB, 131–142 → 119 ms (the first, 12.4 → 5.8 MB). Typing in a 10,000-line JSON document no longer depends on the shape cache: 1.6 ms a keystroke at any bound (it was 25 ms at 8 MB). A line is shaped when first drawn instead: a wheel step through the JSON response that draws new lines allocates 3.4 KB more. The area also draws the line strings it cut last frame while its line starts stand, and gives the caret and the selection the same strings: a repaint — a caret blink, a search tint — cut every line on screen again (~3 KB a frame over a screen of JSON) and allocates nothing now. `TextWidthTests` (a corpus of Latin, digits, tabs, letter spacing, Han with and without kana, emoji, right-to-left, a newline: bit-equal to the layout, found or measured), `TextAreaLineWidthTests` (what an edit measures; 600 random edits, every kept width its own line's, line starts a fresh area's), `CacheBoundTests`; line starts identical to the old code's for every list the perf suite and the Headless, Meridian, Vesper Commander and Junction suites computed (2.0 M lines, a scratch audit). Since 2026-10-12 the same comparison also says which lines' rows an edit keeps (row below). |
| **A TextArea edit wraps again only the lines it touched** (2026-10-12) | Every edit worked all of a document's line starts out again: a new list of every row, each line checked against its kept width, and each long line's breaks looked up by its text in a table of the last pass's — a second copy of the document's wrapped lines, made anew on every keystroke (8.3 MB held while a wrapped 20,000-line log was open; 2.4 of the 8.6 MB a keystroke allocated there). The area now keeps its list and patches it in place: the hard lines before and after the edit are the ones whose widths it keeps (the old text and the new compared from each end, above), the rows of those before it stay, those after it move by the characters the edit added or took away, and only the lines between are wrapped; what was drawn from the list is tied to a version of it, not to the list. A line's breaks depend on its text, the width, the style's metrics, the render context and the UI culture alone, which the break search now makes true: it starts every line from the same guess at a character's width, where it started from the lines above, and a longer prefix does not always measure wider (Arabic joining forms, kerning), so the old code broke an edited Arabic line at a 19-pixel width where a fresh area did not. When many rows are replaced at once — a new text whose ends differ, as a response sent again with a time at its top and bottom, a file read again — the lines that replace them are compared with the replaced lines in order, and looked up by the hash of their text after eight misses (hashing every line first cost 1.5 ms of a 6 ms pass), and the rows of one found are moved instead of searched; without that, showing the log again took 91 ms instead of 6. Typing in the log: 8,777 → 6,353 KB a keystroke (the rest is the new 6.3 MB string), 4.9 → 2.1 ms JIT, 5.0 → 2.4 NativeAOT; in a 19,000-line JSON document 981 → 724 KB, 0.92 → 0.77 ms JIT, 0.75 → 0.62 AOT; the log shown again with new ends 5.7 → 2.2 ms JIT, 4.2 → 2.3 AOT, opened 72.1 → 62.7 MB allocated, held while open 8.63 → 0.34 MB. Perf suite: typing in 2,000 lines 221.8 → 205.6 KB (−7 %, −5 % JIT / −2 % AOT on the minimum), in a wrapped 900-line document 291.5 → 268.2 (−8 %, −7 % / −3 %), undo-redo 225.1 → 207.8 (−8 %, −7 % / −4 %); a long document opened 1,439 → 1,373 KB, its time within −1…+3 % JIT and +3.5 % on NativeAOT, where `richtext/open`, which runs none of this, moved +4 % in the same runs. `TextAreaEditWrapTests` (1,500 random edits — typing, Enter, Backspace, Delete, selections typed over, pastes of several lines, undo and redo, new texts from the parent, widths, font sizes and fonts changing — over surrogates, ZWJ, flags, VS16, CJK, Hangul, Hebrew, Arabic, marks, Thai, Devanagari, soft hyphens, tabs and CRLF, each against a fresh area's list; 30,000 more in a soak), `TextAreaLineWidthTests` (an edit measures only its lines, a keystroke's bytes do not grow with the document, new ends and lines in another order are found again); line starts identical to a fresh area's for every list the perf suite and the Core, Headless, Meridian, Vesper Commander and Junction suites computed (10.7 M lines, 2,099 of the lists patched, a scratch audit). |
| **The BiDi and styled memos are bounded by bytes too** (2026-10-12) | What the BiDi and styled paths keep of a string between widths — its entry (levels, breaks, segment widths, the line of the whole text) and the lines it was cut into — was bounded by counts alone (4,096 entries, 4,096 lines, 16,384 widths), and a remembered width's or line's key held its entry, so an entry the cache had let go stayed alive as long as any of its ranges did. Paragraphs of 2,000 characters, Hebrew mixed with Latin, laid out at a few widths left 64.7 MB in the pipeline after their texts were gone; styled ones 45.2 MB; 400-character ones 32.9. Entries and lines now also count the bytes they keep (`MemoBytes`, `LineBytes`: within 3 % and 5 % of what full collections measure, for paragraphs of 400 and 2,000 characters and labels, BiDi and styled, ellipsized or not; an entry that gets the line of its whole text later is weighed again, `LruCache.Reweigh`), up to 10 and 12 MB, and a range names its entry by a number, keeping nothing of it (after the last number every entry, width and line goes and the numbers start over). The same three cases now leave 26.5, 22.1 and 26.0 MB, the memos at their bounds and the rest the other caches. The bounds sit above what real use fills, because what is kept is used again: the perf suite's 5,000-block document, opened twice and resized, fills both counts with 6.8 and 8.4 MB as weighed, and opens again as before (31.18 MB allocated, the same); an 8 MB bound on the lines already let 337 of them go. A width stays count-bounded: ~100 bytes with its key, 1.6 MB at most. Perf suite: 0 slower, 0 faster; `text/styled ellipsis 1,000 labels resize` 1,906 → 1,861 KB (−2 %, the smaller keys), every other row within its run-to-run allocation. `CacheBoundTests` (the bounds hold and are what stops; weights at least the text, levels and glyphs kept; an entry let go is collected though its lines stay; labels laid out again shape nothing; numbers that start over find nothing old), `LruCacheTests` (reweighing). |
| **A TextArea's break search reads a range of the document, in scratch the pipeline keeps** (2026-10-24) | Each wrapped line's break search asked for a table of its prefix widths (row of 2026-10-06 above), and each table was a string cut from the document, an exact copy of its segments, an array of its runs and the two objects holding them: nearly all a wrapped document's line pass allocated — 1.3 MB a step of a resize drag over the perf suite's 900-line notes file, 65.8 MB to open a 20,000-line log of 160-character lines, 69.2 MB a step to resize it. `IRenderContext.MeasureTextPrefixes(text, start, length, style)` now takes the range, and the pipeline answers with a view built in lists it keeps, good until the next call into it that measures, lays out or draws text (the default still measures a substring, so other contexts keep their answers). The range is itemized as the substring would be — a span of the text, looking ahead no further than its end (a `#` before a variation selector it does not reach stays text), its Han characters in the face its own kana or Hangul call for — so every width is the substring table's to the bit; a prefix that stops short of the range's kana is still laid out the long way, and the view survives that call into the pipeline. The next call lets go of the document. `FontItemizer` reports runs as ranges to a sink, so the durable tables (truncation, a wrap's last line) no longer cut a string per run either. Allocated: `text/resize a wrapped TextArea` 1,028 → 19 KB a step (−98 %), `text/open a long document in a TextArea` 1,373 → 372 KB (−73 %), typing in a wrapped 900-line document −5 %; the 20,000-line log opened 65.9 → 6.7 MB, resized 69.2 → 6.3 MB a step (the rest is its unique words, shaped again at each width). On the minimum the resize −4 % JIT and −5 % NativeAOT, the long document −1 % and −4 %, no row slower. `PrefixWidthsTests` (ranges against their substrings, faces per Han context, a view used after the next call throws, survives its own long-way layout, allocates nothing, lets go of its text), `TextPrefixWidthsTests` (random ranges of random documents with real fonts, fallback faces and emoji, cut before a variation selector: every prefix bit-equal to its substring laid out), `TextAreaWrapTests` (a re-wrap at a new width allocates nothing; the reference line starts as before) |
| **A TextArea takes a line of printable ASCII in a proven monospace face as fitting by its count** (2026-10-24, approved by the user 2026-10-13) | To wrap, a TextArea needs to know which hard lines fit, and it measured every line it had no width for — all of them when a document opens: a 19,000-line JSON response measured 18,887 lines (every one fitting) and kept their widths in a 4 MB cache. Junction's diff heights and Vesper Commander's comparison already answered "does it fit" by arithmetic in a monospace face, with a weaker proof. Now `IRenderContext.MonospaceAsciiAdvance(style)` (default 0) gives the advance every printable ASCII character (U+0020–U+007E) has in the style's face at its size when the pipeline has proven it with the shaper, once per face and size: the face has its own glyph for all 95 (none falls back to another face), each shaped alone is one glyph of that advance, and every ordered pair of them side by side (a de Bruijn sequence of order 2, 9,026 characters, shaped in pieces of 256 so the shaper's float pen stays exact — whole, at 13.5 px, it rounded advances by 1/256 px) is one glyph a character of that advance, to within 1/4096 of it, below any face's least kern of one unit of its em. No letter spacing. The area asks once a pass, so a face registered since, a theme or a font size change is proven again, and takes a line of those characters as fitting, unmeasured, when one character more would fit: n of them measure n times the advance, give or take float rounding, which a character's room covers. A tab, a carriage return, any other character, or a line that fits by less than a character is measured as before, and the rows are the measured path's (`FitsByCount = false`, internal, is that reference). The approved risk — a face kerning ASCII past one advance across a line would draw it clipped — is closed for pairs by the proof. JSON open 2,126 → 318 KB (−85 %) on the first open, kept after closing 4.38 → 0.17 MB (−96 %); TODO.md 1,805 → 1,282 KB, kept 2.66 → 1.52 MB. Perf suite: `text/open highlighted JSON in a TextArea` 6.23 → 0.57 ms JIT (−91 %), 8.02 → 0.73 NativeAOT, 1,902 → 270 KB; a wheel step through it draws lines whose words were not shaped at the open, 7.9 → 9.6 KB (time +2 %); no row slower. The proof costs ~0.5 ms and 219 KB once per face and size. `MonospaceAsciiTests` (a proportional face, one character of another width, a kerned pair, a pair kerned by one unit in 512, a ligature, a missing glyph, letter spacing: no advance; float noise of a millionth: proven; once per face and size; the pieces hold every pair), `MonospaceAsciiAdvanceTests` (the platform's monospace faces at 12, 13.3, 13.5, 10.67 px and bold proven, sans-serif and serif not, 2,000 random code lines each never past their count and one), `TextAreaAsciiFitsTests` (what is measured: a line with a character to spare not, exactly the width, a tab, `é`, a carriage return yes; a narrower width measures a line skipped before; rows equal to the reference at character edges), `TextAreaFitsByCountTests` (real fonts: rows at 48 widths on and a hair either side of character edges, and pinned-font frames with the caret and a selection, identical to the measured path) |

---

## Rendering

| Decision | Rationale |
|----------|-----------|
| Full redraw per frame | Simple, correct. SkiaSharp + GPU can repaint complex UI in <2ms |
| GPU-accelerated via OpenGL | SkiaSharp renders to GRContext backed by SDL3's GL context. Zero pixel copying |
| IRenderContext abstraction | Backend-swappable. Core doesn't depend on SkiaSharp |
| SKPaint pooling | Avoid per-draw-call allocation. Reuse paint objects |
| **No damage rects** (2026-08-16) | Measured, not assumed — see below |
| **A frame keeps its extra GPU submits** (2026-10-10) | Measured and declined — see "One submit per frame" below |
| **Text blobs are native handles: the renderer calls SkiaSharp's C API directly** (2026-10-11) | A run's text blob was an `SKTextBlob`, a managed wrapper SkiaSharp also registers in its handle table: 80 B per new blob, all a new blob still cost, and a scrolled list makes one for each row it shows for the first time. `NativeTextBlob` calls the four functions of libSkiaSharp's C API the wrapper calls — `sk_textblob_builder_make` (Build), `sk_canvas_draw_text_blob` (DrawText), `sk_textblob_get_bounds` (Bounds), `sk_textblob_unref` (Dispose) — and the render context keeps the handles. The same native calls, so the same pixels (headless frames, the goldens, and GL and Vulkan readbacks in a real window, byte for byte) and the same lifetimes: a blob is made with one reference, the context's, released once, when its cache empties, when the context is disposed, or by the context's finalizer if nobody disposed it (as the wrappers' finalizers did); a draw takes what it needs before the context lets go (a raster canvas draws before it returns, a recording canvas refs the blob, Skia's GPU text keeps glyphs of its own and is told when the blob dies). This ties HultUI.Rendering.Skia to SkiaSharp's C API at the pinned 3.119.2, as SkiaSharp's own bindings are; the user approved it on 2026-10-11 on condition that headless still works (it does: Headless, Rendering.Skia and the goldens on Windows, the Linux suite in WSL Fedora). The exports are looked up in the library SkiaSharp itself loaded when the first render context is made, which fails there naming any that is missing; `NativeTextBlobTests` fails if an export goes, if SkiaSharp's own binding of one changes signature, or on any SkiaSharp but 3.119.2 — check the four against the new headers before moving the pin. `scroll/jump 5,000-row list` 6.3 → 2.4 KB a step (−62 %, JIT and NativeAOT), the other scroll rows −33…−45 %, the selectable viewer wheel −33 %, the rich text wheel −67 %; time unchanged |
| **A colour stretch of a spanned line draws only the glyphs that can reach it** (2026-10-11) | A line with colour spans draws each stretch of one colour under a clip of its own, and drew every run under it whole: a line of code in six stretches handed Skia six times its glyphs, and Skia works on each glyph before the clip throws it away. Each stretch now draws a kept sub-blob (a native handle, as above) of the glyphs whose ink can reach its clip: a glyph at X inks inside its face's bounding box around X (what Skia itself culls whole blobs by; simulated bold and oblique faces report boxes that include the simulation), plus an eighth of the size (three eighths for a slanted face) and two device pixels, taken from the canvas matrix so a scaled-down or rotated line keeps its margin. So the pixels are the same by construction, and are held to it: in-process A/B tests at three scales and five transforms over eight scripts, emoji, marks and italics, byte for byte; app frames, the goldens and GL and Vulkan readbacks before and after. Colour and bitmap faces (COLR, CBDT, sbix, SVG, EBDT: Segoe UI Emoji, and Calibri and Courier New Regular, which carry bitmap strikes) draw the run whole, as does a part that is the whole run. A face whose box Skia marks as a guess — every variable font, whose box is its default instance's: Cascadia Mono and Code (`FontFamilies.Monospace` on Windows, the terminal's default; Junction and Vesper Commander use Consolas), Segoe UI Variable, Bahnschrift, Fedora's Noto Sans — drew its runs whole too until 2026-10-24; since then each run takes its extent from its own glyphs instead: one `GetGlyphWidths` at 2048 per 256 glyphs into kept buffers, the union of their bounds with the same slack, kept per run in a map made at the sub-blobs' bound (~57 KB, with the first). Glyphs per frame in Cascadia Mono (headless): a selectable viewer 34,374 → 4,268, highlighted JSON 3,872 → 1,250, a coloured terminal screen 42,445 → 3,549; frame time `terminal/coloured screen, forced frame` −60 % (JIT) / −61 % (NativeAOT), `text/selectable viewer …` −47…−49 %, the JSON open, wheel and rebuild rows −7…−16 %. A line drawn for the first time makes about 7 native blobs where it made 1 (a 20,000-line spanned document paged through: the sub-blob map starts over 68 times in 389 screens), and is still drawn faster: `text/page down through highlighted JSON` −5 % / −9 %. Pixels: in-process whole runs against sub-blobs over 160 scenes (eight faces, four scales, five transforms) byte for byte with a 512 MB glyph cache; with the default 2 MB, drawing the same scene twice already differs in 12 of them (DirectWrite remakes some glyph masks differently after the cache lets them go), so such comparisons need that A/A control; on Fedora (FreeType) 140 scenes and 138 headless frames are identical either way. A run's glyphs are found by binary search on X, or by one pass when X goes back (marks, reordered clusters). Sub-blobs are keyed by the run, not its glyphs, so a lookup hashes nothing; the map is made at its bound (~74 KB) by the first context that draws a spanned line. Glyphs handed to Skia per frame (headless): Junction's diff hover 10,650 → 2,763, Vesper's reader 11,012 → 2,932, re-coloured spans 99,167 → 8,734; frame time −15…−27 % in Junction's diff and Vesper's reader, `text/spans recolor` −65 % (JIT) / −67 % (NativeAOT), `text/spans bold` −24 % / −41 %, the rich text rows −9…−14 %; bytes per frame unchanged |
| **The accessibility bounds refresh runs whenever layout ran, even if nothing moved** (2026-10-12) | Measured and declined. Plan 17 round 3 built the skip (f5dcbad, reverted): each `Element.Layout` write compared with the box at the last look and marked its path, so the coalesced refresh could be skipped when no box moved. A shadow audit (a fresh tree diffed against the applied one at every skipped refresh, 1,155 audits over the Headless, Junction, Meridian, Aperture, Vesper and Media suites with a listening bridge forced on) found every skip empty, so it was correct — but it cost every app with or without a reader: +16 B per element (BoxElement 584 → 600 B), +0.5…+1.9 % allocation on rows that build elements (+24 KB a frame in its own 300-row rebuild-per-frame probe, more than the 11.4 KB a frame it saved), `a11y/create 1,000 rows, bridge on` +5.3 % min / +6.0 % median (JIT). It paid off only for a rebuild that moves nothing with a reader listening, and plan 17 had just removed the common one (whole-app rebuilds per animation frame: toasts, spinners, the video bar). Revisit only with a design that costs nothing per element (track moves only while a client listens, in an existing field) |
| **A scroll reaches assistive tech on the resize schedule** (2026-10-06) | A scroll moves the bounds of everything under the view and changes nothing else, so it is a layout-only change: the accessibility tree is rebuilt as the scroll starts, at most every 100 ms while it goes on, and once after it stops — the batching browsers apply to location updates, and the one a resize already had. The scroll offset reported its change as a property change instead, which rebuilt the whole tree on every frame of a wheel turn. Junction's History list (Native AOT, a UIA client listening, 3 interleaved runs a side): a frame while scrolling 1.90 → 1.20 ms down and 1.76 → 0.98 ms up, the tree rebuilt on 100 % → 18 % of those frames, p95 4.4 → 2.3 ms; the accessibility time of the whole scripted session 131 → 63 ms. The coalescing runs on the platform clock, so a headless session's virtual time drives it and tests count the rebuilds exactly |

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

### One submit per frame: measured and declined

A GL frame submits three times: the render context's `EndFrame` flushes and submits, and
`SkiaGLRenderSurface.Present` submits twice more with nothing in them. Each submit ends in a
`glGetError`, which under NVIDIA's threaded driver waits for the driver's thread. A frame drawn
through the scRGB offscreen on Vulkan makes two `vkQueueSubmit`s (the frame, then the blit into
the swapchain). Plan 17 (round 2) built one submit per frame — `EndFrame` issues the commands
without submitting, the surface submits once in `Present` and then returns the frames borrowed
from pixel sources — and measured it in real windows (counters on the resolvers handed to Skia;
captures `2026-10-10-f8e449e-onesubmit-{before,after}.json`, 2 idle-gated runs a side, alternated):

| Window | glGetError / Skia submits a frame | Frame, wall min | UI-thread cycles | Flush → Present |
|---|---|---|---|---|
| GL, Junction-like, unpaced, JIT / AOT | 3 → 1 | −0.8 % / −2.4 % | −1.3 % / −2.0 % | 0.086 → 0.057 ms; 0.026 → 0.047 |
| GL, paced at 5 ms, JIT / AOT | 3 → 1 | −2.9 % / −1.1 % | −2.9 % / −1.8 % | 0.110 → 0.070; 0.035 → 0.065 |
| Vulkan F16, Aperture-like, unpaced, JIT / AOT | 2 → 1 | −4.1 % / −3.7 % | −4.2 % / −3.3 % | 0.097 → 0.051; 0.165 → 0.166 |
| Vulkan F16, paced at 5 ms, JIT / AOT | 2 → 1 | −2.5 % / −4.5 % | −3.5 % / −2.8 % | 0.123 → 0.064; 0.162 → 0.209 |
| Vulkan 8-bit (one submit before too) | 1 → 1 | flat | flat | 0.076 → 0.047; 0.069 → 0.101 |

Pixels were identical (GL back buffer and Vulkan swapchain captures, F16 and 8-bit). **The wait
moves, it does not go:** on GL the first `glGetError` of a frame is the one that waits for the
driver's thread, and it now waits in `Present` instead of `Flush`; the two empty submits cost a
few microseconds each. No row reached the 5 % bar. The lever left is not calling `glGetError` at
all, which costs Skia its GL out-of-memory detection (a question for the user, not taken). The
DevTools "Flush" phase stays where it was: issuing and submitting the frame.

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
