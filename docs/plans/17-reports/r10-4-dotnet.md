# Round 10 survey, expert 4: .NET runtime

SATISFIED: no

The worktree is clean at 3462f18e. I added probe tests to Meridian.Tests, Aperture.Tests and VesperCommander.Tests and
a temporary suite scenario; all of them are removed, and copies of the probes are in the scratchpad. All numbers are
headless JIT at 1440×900 unless a line says AOT. Heap numbers come from a full GC and `GC.GetTotalMemory(true)`. No
timings are reported.

REVIEW:
- **Suite bytes, full suite at 3462f18e, JIT and AOT.**
  - Against round 9's survey captures (ea3e57ab, 214 shared rows): JIT 0 rows rose and 19 fell; AOT 0 rose and 18
    fell. Every fall is an a11y row from step 5 (5c9d6b8b), for example "type in TextInput" UIA and AT-SPI 1.3 → 1.0
    KB, and "context menu walk" AT-SPI 0.4 → 0.1.
  - Against round 9's last captures (bf74fbca after, 221 rows): 0 rows moved, JIT or AOT.
  - JIT and AOT agree within 1 KB on 209 of 221 rows. Only "overlay/dropdown open 200 items" differs by more than 5 %
    (38.4 against 35.2 KB).
- **Steady frames and idle.** Unchanged on both runtimes.
  - 0.0 KB: 60 looping tweens, spinners + progress, hover across 1,000 rows, both terminal frames, progress ticks with
    UIA.
  - `loop/idle window, 5 s`: 0.2 KB and 0.2 passes/s, as in round 9. `loop/minimized`: 0.8 KB, 0.2 passes/s.
- **Objection, c940c643 (Meridian tree rows kept): the request tree's kept rows keep the whole Requests view alive
  behind every other view.**
  - `BeginTreeRows` runs only inside `TreePart`, which only the Requests shell builds (MeridianApp.Requests.cs:33,
    Chrome.cs:76). Another view's build never calls it, and nothing forgets `_requestRows`.
  - Each entry's `Live` ref holds a removed row. Through its Parent links, that row holds the old shell: tree, editor
    and response pane.
  - A breadth-first search over the app's and the session's fields found exactly those entries as the holders.
  - Before c940c643, with no request open, nothing held the old view.
  - The rows can't be handed back on return anyway, because removed rows are not hosted, so letting them go costs
    nothing. Numbers are in the first candidate.
- **ff6125a6 (hover re-hit-test): holds up.**
  - Per frame it is one flag read; it walks to the top only after a change of children.
  - Probe: wheel 3 notches over a 100k-row VirtualList with the pointer resting on it: 27.5 KB a step, the same as with
    no pointer.
  - Retained memory and idle are as landed. Note: `_buttonsDown` is reset only by a button release or the modal-loop
    cancel (Window.cs:3138/3325). No missing release was seen, because SDL captures the mouse during a press.
- **5c9d6b8b (lent `TreeDiff`): holds up.** The diff is emptied in the callers' `finally` blocks, and `List.Clear`
  nulls the node references. Only the borrowing bridges get it. The lists keep the room of the largest refresh, which
  is bounded.
- **fa2477cf (Windows keyring memo): holds up.**
  - A recalled answer allocates 0 B: the key is the three-part tuple with a comparer, behind a `System.Threading.Lock`.
  - Entries are bounded by the secrets; failed calls are not kept.
  - Leftover: `Secrets.Resolve` still builds the `{env}__{key}` variable name on each resolve without an own value
    (Keyring.cs:98). See EXAMINED.
- **cb1df8c0..d1274992 (Meridian discovery and the `SaveMap` writer): holds up.**
  - The queue keeps only the latest map per file (MeridianApp.Discovery.cs:143–176).
  - `Task.Run` starts a writer only when none is running, and the writer exits when the queue is empty. No polling and
    no idle wake.
  - Save and drain take the same lock, so no save is lost.
  - Maps are copied, never changed (`Record`, ApiMap.cs:63/84), so the writer never reads a map that is changing.
    `Dispose` waits at most 10 s.
  - `PathMatches` is a `ref struct` walk: 0 B.
- **74cf844c (preview dispose timer): holds up.** One `ITimer` reused through `Change`, with a static callback and
  state. It wakes once, at the minute. `_previewPages` is pruned to hosted pages at each build (at most 2), and it is
  disposed with the preview.
- **d00f7f65, then ae32c9d8 and bf74fbca (syntax colours): nothing unbounded remains.**
  - The per-scope-list cache is gone.
  - What is left: `Palette._byId` (bounded by the theme's colour table), `LanguageByRoot` (PowerShell and Less), and
    one entry per distinct extension or fence name, as before. All are cleared on `SetTheme`.
  - All tokenizing runs under `Gate`, so `_byId` is never written from two threads.
- **The other src commits: no per-frame allocation added.**
  - f5609009: 24 → 0 B.
  - b4f182b5's clip read, c4a459ef's static lambdas, f9a7cc74's type check: 0 B.
  - 58aeb965: one static flag read a frame. A sink leaves the static release list on its next draw or on dispose
    (GpuFrameSink.cs:119/394).
  - A scan of the round's src diff found no new LINQ, closure or boxing on a frame path. bf74fbca's LINQ runs once per
    registry.

CANDIDATES:
- id: dotnet-kept-elements-hold-weakly (objection to c940c643)
  what: `KeptElements` holds its live elements weakly, so it never keeps a view that left the window. The window
  already holds what is shown.
  evidence:
  - Meridian, no request open, Requests → History, then `ForgetTreeRows()`:
    - 200 requests: 1,350 KB of 5,770 KB freed (−23.4 %).
    - 25 requests: 226 KB of 4,379 KB freed (−5.2 %).
  - With a 239 KB JSON response open, 200 requests: −1,468 KB in Environments, −1,491 KB in History (−18 %), once the
    app refs in the next candidate are also cleared.
  - The weak entry is internal: an `IRef` over `WeakReference<Element>`, which PatchFrom retargets as it does a `Ref`
    (Element.cs:1736). It needs an internal `WithRef(IRef)`.
  - `WeakReference<Element>` is 24 B, the same as `Ref<Element>`, plus one weak GC handle per key.
  - `TryKeep` and `Live` need hosted elements, which are strongly reachable, so their behaviour is unchanged.
  expected:
  - **Retained-memory bar:** −23 % at 200 requests, −5.2 % at 25.
  - It also removes one of the several holders in Aperture and Vesper (next candidate) and in any future `KeptElements`
    user.
  - App-only alternative: Meridian forgets its tree rows in a build that makes no tree, like Discovery's `EndMapBuild`
    (MeridianApp.Discovery.cs:452).
  cost: small, Core internal, no API or pixel change.
- id: apps-refs-hold-last-view (guideline-worthy app misuse)
  what: An app's `Ref` fields and scratch lists keep the last view of a kind alive behind the next view. Each app
  should empty them in builds that don't show that view, as `_sendElapsed.Current = null` already does (Chrome.cs:97).
  evidence (all holders found by the field search; % is of the heap while the other view is shown):
  - **Meridian:** `_respTabRef` (Response.cs:694, from 65929882 in round 9), `_responseBodyRef` (Response.cs:25) and
    `_urlRef`. They hold the Requests view when a response is open: +118…+141 KB on top of the first candidate.
  - **Aperture Home** (7a12f5a9, round 8; round 9's survey missed it):
    - Holders: `_homeShelfCards` (ApertureApp.Home.cs:101/223), a scratch list reused from shelf to shelf and never
      emptied, plus `_homeShelfRows` and `_homeCards`.
    - Home → Settings: 5 libraries × 20 films −473 KB (−11.1 %); 8 × 40 −824 KB (−17.0 %).
    - Clearing `KeptElements` alone frees 0, because the list holds the build's twin cards, whose refs reach the live
      cards.
  - **Vesper Brief → Detail:** held by `_cursorRowL` and `_briefScrollL` (from 62d41df7) plus `_briefCellsL`. −419 KB
    (400 files −6.4 %, 3,000 files −5.7 %); it is bounded by the 140 cells on screen.
  - **Aperture Library:** `_gridList` (since f7b3f0a6), 600 films, −267 KB (−5.9 %). But Library.cs:45 reads it for the
    next build's viewport height, so keep that height as a float instead of the element.
  expected: **Retained-memory bar:** Aperture Home −11…−17 %, Vesper −6 %, Aperture grid −6 %, Meridian with a response
  open about −2 % more. The guideline: a `Ref` or a list into a view keeps the whole view through Parent links.
  cost: small, app only, 1–3 lines per holder, no pixel change.

WAITING, RE-RANKED:
- **findbar-countonscreen-foreach:** still a nit. FindBar.cs was not touched in round 9 and still uses `foreach` over
  `bar.Children` (FindBar.cs:209). It costs 32 B on AOT, 0.4 % of an 8 KB Enter. Fix it when FindBar is next touched.
- **textarea-chunked-document:** still parked.
  - Suite `input/type in 2,000-line TextArea` is 205.4 KB a key, JIT = AOT, unchanged since round 9.
  - Round 9's TextArea change (step 6, break by count) only speeds up opening read-only files.
  - No app edits a document above the user's 256k-character condition.

EXAMINED, NOT WORTH IT:
- **The variable-name string in `Secrets.Resolve`:** at most 40 B × 36 resolves, about 1.4 KB of the 47.1 KB
  response-pane drag frame (3 %). It goes away with Waiting apps-meridian-response-drag-build.
- **Hover settle under a wheel:** 27.5 KB a step with and without a resting pointer, so 0 B added.
- **The lent `TreeDiff`'s kept capacity:** bounded, and it holds no node after `Clear`.
- **The `SaveMap` writer's int-to-string per endpoint:** off the UI thread, once per send.

NEEDS THE USER:
- **Optional, framework:** should the public `Ref<T>` let go of its element when the element leaves the window? It
  would fix every app's holders in the second candidate at once. But it changes what `Current` returns to code that
  reads a removed element (Aperture's Library.cs:45 does). [My recommendation: no. Fix the apps and write the
  guideline.]

FILES (all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\3484c586-c631-4af0-957d-95e7a03cac41\scratchpad\r10-dotnet\`):
- suite-3462f18e-jit.json, suite-3462f18e-aot.json: the captures (suite-jit.ps1; suitepub.ps1 with sa\SuiteAot.csproj,
  published to C:\Users\fredr\AppData\Local\Temp\r10dotaot).
- cmp-jit-vs-r9survey.txt, cmp-aot-vs-r9survey.txt, cmp-jit-vs-bf74.txt, cmp-aot-vs-bf74.txt, steady.txt, jitaot.txt:
  the comparisons (bytesdiff.py, steady.py, jitaot.py, loopfields.py, rows.py).
- ZzR10MeridianProbe.cs + meridian-probe.txt, ZzR10ApertureProbe.cs + aperture-probe.txt, ZzR10VesperProbe.cs +
  vesper-probe.txt: the retention probes (field search included).
- wheel-hover-jit.json: the hover-under-wheel probe row.
- d-hover.diff, d-savemap.diff, d-preview.diff, commits.txt: the review diffs.
