SATISFIED: no

The worktree is detached at dee48fbb (SDK 11.0.100) and clean: the probe tests are removed and their copies are in the scratchpad. No timings are reported as results. The AOT sample shares below are 4 kHz leaf samples from an out-of-process sampler, one run per side (about 8–9k busy samples each). They are shares of busy time, not times.

REVIEW:
- **Upgrade (207fbe25 + c99e23bf), NativeAOT: one codegen regression explains the "runtime's own share" of dotnet11-aot-textarea-long-document.** It became the first candidate below.
  - On ILCompiler 11.0.0, `Dictionary<,>.AlternateLookup<TAlt>.FindValue` no longer devirtualizes `IAlternateEqualityComparer`. ILCompiler 10.0.15 did.
  - Both of HultUI's alternate lookups now make 2 `RhpInterfaceDispatch` calls per lookup, where ILC 10 inlined the comparer. These are the word shape cache (`WordShapeKeyComparer`, MeasurementCache.cs:50–72) and the width cache (`WidthKeyComparer`).
  - In the map file, `WordShapeKeyComparer.Equals_0` (117 B) and `GetHashCode_0` (73 B) exist only on 11; on 10 they were inlined. FindValue is 396 → 362 B.
  - Probe `altprobe\` reproduces it outside HultUI. All three instantiations dispatch on 11: a `__Canon` value, an `int` value, and a non-ref-struct alternate. None do on 10. So it is generic to ILC 11, not to our types.
- **The ledger's split wording is off.** The B→C half (SDK 11 + net10 → SDK 11 + net11, +6 % long document, +4 % resize) is where ILCompiler 11 is. The A→B half (SDK 10.0.300 / ILC 10.0.8 → SDK 11 / ILC 10.0.15, both net10) is Roslyn plus an ILC 10 servicing bump. Its one mover is `FontItemizer.Extends`, 1.6 % → 3.9 % of busy samples, the rest flat (sampled from `%TEMP%\hultui-ab\ca31555d-sdk10.0.300` and `ca31555d`).
- **c99e23bf holds up.** No other `HashCode.Add/Combine` in src takes a nullable non-int struct.
- **JIT app walks show no .NET 11 regression.**
  - Setup: same source (dee48fbb + round 7's probe), SDK 11, net10.0 against net11.0, `DOTNET_TC_CallCountingDelayMs=0` on both sides so the measured keys run tier-1 code. 11 app specs, 56 walks.
  - 0 walks rose. 4 fell, all Vesper: thumbs 5.4 → 4.7 KB a key, transfer 20.1 → 18.1, detail 4.5 → 4.4, sync 0.4 → 0.3. Geometric mean of KB ratios 0.983.
  - GC counts are equal. Meridian's 89k body typing is 182.1 KB a key on both runtimes.
- **The round 10 build (b2fdcf52, 3194c830 …):** no objection. The holders that step left are measured below.

CANDIDATES:
- id: dotnet-aot11-alternate-lookup-dispatch
  what: Give the word-shape and width caches a lookup that ILCompiler 11 can devirtualize, instead of `Dictionary.AlternateLookup` through `IAlternateEqualityComparer`.
  evidence:
  - AOT sample shares of lookup + Marvin + `Equals_0`, ILC 10.0.15 → ILC 11.0.0:
    - `text/open a long document in a TextArea`: 12.2 % → 20.0 % of busy samples (FindValue 9.1 → 12.6, Marvin.ComputeHash32 3.1 → 5.3, Equals_0 0 → 2.1).
    - `text/resize a wrapped TextArea`: 12.1 % → 18.9 %.
  - Disassembly: dumpbin on both exes, `disasm-findvalue-*.txt` and `disasm-widthkey-*.txt`.
  - JIT is unaffected (0 rows slower in the upgrade A/B). JIT already beats AOT on these rows: 2.79 against 3.98 ms min on .NET 11.
  - Code: MeasurementCache.cs:191 (`_map.GetAlternateLookup<TAlternate>()`). It is the only framework user; Meridian's ApiMap.cs:152 is off the hot path.
  expected:
  - Time bar on AOT: the +6 % long-document and +4 % resize runtime share should come back if the cluster returns to ILC 10's ~12 %. Every text-shaping AOT row gains a little.
  - A specialized table could also drop the `LinkedList` LRU nodes, which are 2–3 % of samples.
  cost: medium (an LruCache with a struct comparer and an open-addressing table, so constrained calls inline on JIT and AOT). Internal only, no API or pixel change. Alternative: wait for an upstream fix (Needs the user).
- id: apps-refs-hold-last-view-rest
  what: Each app empties the remaining refs into a view in builds that don't show it, as round 10 step 2 did for the others.
  evidence (headless JIT, full GC; % of the heap while the other view is shown):
  - **Aperture `_cursorCard`** (ApertureApp.Cursor.cs:40/212):
    - After 5 cursor moves, Library 600 → Settings: −265 KB (−6.0 %). After 3 moves, Home 5×20 → Settings: −264 KB (−6.6 %).
    - The old view dies. With no cursor move the ref is null and nothing is held.
  - **Meridian History:** `_historyListRef`, plus two holders the ledger did not list, found by a path search: `_historyFieldRef` and `_historyArchiveRef`.
    - Emptying `_historyListRef` alone frees 0.
    - Emptying all three: History → Requests −289 KB (−6.1 %) at 200 sends, −238 KB (−4.7 %) at 2,000; History → Discovery −289 KB (−5.3 %).
  - **Vesper `_thumbVListL/R`**, Thumbnails → Detail: −274 KB (−4.3 %) at 400 files, −265 KB (−3.7 %) at 3,000.
    - The next build reads the list's height, so keep it as a float, as Aperture's `_gridViewportHeight` does.
  expected: Retained-memory bar: Aperture −6…−7 %, Meridian −5…−6 %; Vesper −4 % is under the bar but uses the same fix. Bounded (one view behind), not a growing leak.
  cost: small, app only, 1–3 lines per holder, no pixel change.

WAITING, RE-RANKED:
- **meridian-stalled-timeout-early:** cause found; it is not a .NET 11 timer change.
  - The start point is not it: HttpEngine.cs:131 starts the Stopwatch before the CancellationTokenSource at :132, which can only add time.
  - The timer really fires early. xUnit's own time for the failing test was 388 ms, below the 400 ms timeout.
  - Mechanism: TimerQueue measures from the interrupt clock, `QueryUnbiasedInterruptTime` truncated to ms. It steps at the clock interrupt: 15.625 ms on a quiet runner, 0.5–16 ms on this desktop.
    - A timer's start tick lags its real start by up to one step.
    - When another timer wakes the timer thread, as the many in a full run do, it can fire up to one step + 1 ms before a QPC Stopwatch says it is due.
  - .NET 10 had the same clock. PR #122706 (.NET 11) moved `Environment.TickCount64` onto it and pointed TimerQueue at that, with no change for the queue.
  - Probe: 0 of 270 timeouts early by > 1 ms on either runtime (idle, 24 or 56 busy threads, 1 or 7 ms tickers). The desktop's clock was often at 1 ms; on 1 ms ticks early fires were ≤ 1 ms, as in the 399 ms failure.
  - It failed on .NET 10 too (memory ci-red-findings: 2026-10-05, CI 2026-10-06 at 402 ms).
  - **Test fix:** HttpEngineTests.cs:207 → `Assert.True(failure.Elapsed >= TimeSpan.FromMilliseconds(400) - TimeSpan.FromMilliseconds(17))`, with a comment: one 15.625 ms interrupt step plus the millisecond the queue truncates to. The Kind and "connected" assertions still prove the timeout path. Linux's coarse clock (~4 ms) fits in the same slack.
- **dotnet11-aot-textarea-long-document:** still worth it, through the candidate above (the B→C half). The A→B half is `FontItemizer.Extends` (inlining-sensitive: 1.6 % on ILC 10.0.8, 3.9 % on 10.0.15, 1.65 % on 11.0.0) and is for expert 5.
- **apps-refs-hold-last-view (unmeasured holders):** now measured; it is the second candidate. Meridian needs two more refs than listed.
- **findbar-countonscreen-foreach:** still a nit. FindBar.cs:209 is unchanged: 32 B an Enter on AOT, 0.4 %.
- **textarea-chunked-document:** still parked.
  - Suite `input/type in 2,000-line TextArea` is 205.4 KB a key on JIT and AOT, .NET 10 = .NET 11.
  - Meridian's 89k body is 182.1 KB a key on JIT 10 = 11, which is the new string.
  - No app reaches the user's 256k-character condition.

EXAMINED, NOT WORTH IT:
- **The .NET 11 JIT boxing patterns** (probe `boxprobe\`, 8 elements per call, after tier-up).
  - JIT 11 boxes 24 B per value for a nullable non-int struct: `HashCode.Add(W?/float?)` in a loop (192/96 B), `HashCode.Combine(W?, float?)` in a loop (288), a generic `v is null` / `v?.GetHashCode()` on `T?` in a loop (192), and a `Dictionary<(int, W?)>` lookup in a loop (192).
  - These do not box on JIT 11: single calls, `int?`, `EqualityComparer<T?>`, record structs with nullable fields, `HashSet<W?>`, enum dictionaries, `HasFlag`, constrained struct-interface calls, and one-off `(float, Color?, float)` dictionary lookups.
  - The src sweep found none left on a hot path:
    - The `HashCode` uses take plain values.
    - `Signal<TimeSpan?/float?/long?>` goes through `IEqualityComparer`.
    - The Junction/Vesper `(…, Color?, …)` style caches are single lookups.
    - No generic code null-tests a `T` value.
  - The walks above confirm it. AOT 11 boxes only the generic `IsNull<T?>` loop, which src does not use. `enum.Equals(object)` boxes on both runtimes. Interpolating a `float?` fell from 56 to 32 B on 11.
- **Round 10's `held` local in a retention probe** kept the element alive (the NoInlining trap again). The numbers above come from helpers.

NEEDS THE USER:
- **Optional:** report ILCompiler 11's lost devirtualization of `IAlternateEqualityComparer` in `Dictionary.AlternateLookup.FindValue` to dotnet/runtime. That is a public post; `altprobe\` is a 40-line repro. Or rely on the workaround candidate. [Recommendation: build the workaround; report it if the user wants.]

FILES (all in C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\ec6d577c-f9ba-4479-acbd-908e67c0ea0a\scratchpad\r11-dotnet\):
- **Trees and JIT walks:** tree.ps1, buildwalk.ps1, walkjit.ps1 (trees tree-net100/tree-net110, hosts walk\bin-net100/bin-net110); runs\walks-jit.jsonl and runs\*.out.txt; cmpwalks.py.
- **AOT suites and the sampler:** aotmap.ps1 and pubonly.ps1 (aot-net100 from clean-net100, aot-net110 from the worktree, ILC map files under each obj\); extsampler\ (the out-of-process sampler; the in-process sampler\ deadlocks against the GC, so it is not used); sample.ps1; samples\longdocx-*, resizex-*, abdoc-sdk10/sdk11-*; cmpsamples.py.
- **Code size and disassembly:** mapsizes.py; symaddr\ and disasm.ps1; disasm-findvalue-net100/net110.txt and disasm-widthkey-net100/net110.txt.
- **The alternate-lookup repro:** altprobe\ and altprobe-disasm.txt.
- **Boxing probes:** boxprobe\ with boxprobe-jit.txt; boxprobeaot\ with boxprobe-aot.txt.
- **Timer probe:** timerprobe\ and timerprobe.txt.
- **Retention probes:** ZzR11Probe-Aperture.cs, ZzR11Probe-VesperCommander.cs, ZzR11Probe-Meridian.cs; holders-aperture.txt, holders-vesper.txt, holders-meridian.txt.
- rows.py: capture row extracts.
