# Round 8 survey, expert 4: .NET runtime

SATISFIED: no

I left the worktree clean at 27020cdb. Every probe edit in src, tests and samples was reversed: `git apply -R` for the src patches, and the probe test file was deleted.

REVIEW: none. Round 7's build and main's app-support merge f64e4674 hold up, on JIT and on NativeAOT.

- **Suite bytes.** I ran full captures of 27020cdb on JIT and on AOT and compared them with step 11's captures (31e1aaa7, the last captures before the merge). On both builds, 0 of 213 rows moved by more than 0.2 KB and 2 % in either direction. JIT and AOT agree within 1 KB on every row.
- **Round 7 step 1 holds.**
  - Both bridge maps still keep one lock (LinuxAccessibilityBridge.cs:54, WindowsAccessibilityBridge.cs:39).
  - `Shortcut.Matches` still uses an index loop (ShortcutMap.cs:210).
  - Every overlay removal goes through `RemoveOverlayAt` → `AccessibleOwnerSlots.ReleaseTree` (Window.cs:3477-3487). That includes app-support's new `CloseOverlays` (e60fd2ee) and `DismissPopups` (86da0e3a).
- **`KeptElements<TState>` (bdd8cd1c).**
  - `TryKeep` allocates 0 B.
  - `Made` costs 88 B per element (the `List<IRef>` plus `IRef[4]` that `WithRef` makes), plus a 24 B `Ref<Element>` the first time a key is seen.
  - It holds at most max(64, 2 × live) entries. Each entry holds its element, including a detached one until the next prune or remake. Aperture Home has 35 entries. This is bounded, not a leak.
- **FindBar `Moved` (b6119ae9).** An Enter builds nothing. `CountOnScreen` loops `foreach` over `bar.Children`, which costs 32 B on AOT if that is an interface list: 0.4 % of an 8 KB Enter. A nit only; fix it whenever FindBar is next touched.
- **In-place a11y refresh (86f9c609, 77ce4633).**
  - A structural refresh's own bytes (the `TreeDiff` and its list copies, stand-in nodes, new child arrays) are at most ~2.6 KB per Junction wheel notch.
  - The walk over the unchanged tree costs 6.9–7.7 KB, and that is joined names (candidate below).
- **App-support's frame-robustness and SaveFrame commits add 0 B per frame.**
  - `DrawingFrame` is a struct used with `using`, so it is not boxed.
  - `Canvas.RestoreToCount(1)` is a P/Invoke.
  - `_frameSaves.Count` is a field check.
  - `FrameRequests` runs only when its environment variable is set.
- **3bfbb84f and 8251f028 (UIA element gone, lock privacy).**
  - `Drop` now calls `UiaDisconnectProvider` for each dropped element that has a native object. Counted over the suite's 22 UIA rows: 0–5 calls per session (at most 0.2 per apply), 10–20 µs each. That timing is indicative only; the desk was loaded.
  - `ElementRef` now takes `_nativeLock` on every lookup: uncontended, 0 B.
  - `Detach` also lets go of the old tree a client still held, which is a retained-memory gain.
- **b5972213 (headless sessions side by side).**
  - The `AsyncLocal<Application>` is set once per application on the UI thread. It adds no bytes per work item or `await`.
  - Only in test processes: a thread started under a test session that is later disposed keeps that application reachable through its ExecutionContext.
- **4da0d543 (monitors).**
  - `PointerEvent.LocalPosition` now walks the ancestors on every read: O(depth), 0 B without transforms, and one List per read when a transform is on the way.
  - Each Application subscribes to its own platform's `MonitorsChanged`. There is one platform per application, so no leak.
- **CommandPalette.** The row key `$"{Group}|{Title}|{Index}"` makes ~60–100 B per row made, about 2 % of a palette key.

CANDIDATES:
- id: dotnet-a11y-walk-name-compare (resolves Waiting a11y-walk-unchanged-bytes; shared with expert 7)
  what: In the in-place walk, `Compare` (AccessibilityTreeBuilder.Refresh.cs:658) builds every button's name-from-content string only to compare it with the node's name. It calls `ResolveName` → `GetContentText` (AccessibilityTreeBuilder.cs:821-844), which makes a `StringBuilder` plus a string for each button with two or more texts. The fix compares those texts against `node.Name` span by span and builds nothing.
  evidence:
  - **Prototype.** `NameIs` plus `ContentTextMatches`, 49 lines, behind an environment switch. Patch: `proto-namecmp.patch`.
  - **Probe.** Round 7 step 7's Junction probe, Changes view at 300 files, Junction.Tests, JIT, 1400×900. Two alternated runs per side gave identical numbers.
  - **Walk alone on the unchanged tree:** 6.86–7.67 → 0.02–0.84 KB.
  - **Wheel at 120 ms a notch:** recording bridge 22.8 → 16.2 KB (−29 %), UIA 25.4 → 18.8 (−26 %).
  - **Caret Down at 120 ms:** recording bridge 91.8 → 81.4 KB (−11 %), UIA 94.2 → 83.8 (−11 %).
  - **Bridge off:** unchanged (13.6–16.6 and 78.1).
  - **Same work both sides:** 60 in-place refreshes and 0 trees, the same structural diffs (+599 −604 and +308 −306), and the walk matched every time.
  - **Suite.** Of 66 a11y rows, only `type in a rich text editor inside a VirtualList row` moves: 3.4 → 2.5 KB (−26 %) with the bridge on and with UIA. The rest stay within 0.3 KB.
  - **AOT.** Strings and StringBuilders are heap objects on AOT as well, and the suite reads JIT = AOT to 0.3 KB on every a11y row.
  expected: Churn bar: −26…−29 % per wheel notch and −11 % per caret key on Junction Changes whenever a reader or any UIA client listens. It removes nearly all of the 6.9–8.2 KB the Waiting item names. Any app whose rows are buttons with several texts gains the same way.
  cost: small. Core only, no API change. No pixel change and no reader change, because it compares the same strings.

WAITING, RE-RANKED:
- runtime-element-layout-state phase 2: not worth it, and lower than in round 7. The walks behind round 7's −2…−9 % now make far fewer element copies:
  - Meridian URL typing on Headers, Body and Auth builds nothing (66.6 → 4.0 KB a key).
  - Aperture Home's cursor key makes 0 builds.
  - A Vesper Brief mark makes 1–2 cells, not 140.

  At ~70 B a copy it stays under both bars.
- junction-leads, big files' gen 2: not worth it. 0–1 gen 2 per 100 keys, and the collections are background ones.
  - A file loads off the UI thread. Its bytes (1.5 MB) and its UTF-16 text (3 MB) both land on the large object heap and are needed.
  - Only a CRLF file adds a third copy, from `Replace("\r\n", "\n")` (JunctionApp.Files.cs:630). It could be split without that copy if it ever matters.
- textarea-chunked-document: still parked. The user's 256k-character condition is unmet and there is no new evidence (Meridian's 89k body typing is ~238–241 KB a key).

EXAMINED, NOT WORTH IT:
- `UiaDisconnectProvider` per drop: at most 0.2 calls per apply in the suite, 10–20 µs each.
- `KeptElements` retention: bounded at max(64, 2 × live) entries; 35 in Aperture Home.
- `LocalPosition` walking ancestors: O(depth), 0 B. App trees are 2–3 deep at hit-test points (round 6).
- The `AsyncLocal` ambient application: 0 B per work item; one application per process in apps.
- Steady-state rows still allocate nothing:
  - 0.0 KB: `anim/60 looping tweens`, `spinners + progress`, `input/hover across 1,000 rows`, both terminal forced-frame rows.
  - `loop/idle window, 5 s`: 0.2 KB per 5 s.
- A kept `TreeDiff` for structural refreshes: ~0.5 KB per refresh.

NEEDS THE USER:
- None.

FILES (all in `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\64e7e0e2-bc1c-4069-9d42-9311fee04c9e\scratchpad\r8-dotnet\`):
- `suite-27020cdb-jit.json`, `suite-27020cdb-aot.json`: the captures.
- `bytesdiff.py`: row bytes against the step 11 captures.
- `jitaot.txt`, `steady.py`: JIT against AOT, and the steady-state rows.
- `sa\SuiteAot.csproj`, `suitepub.ps1`: the AOT publish of the suite's sources. It builds into the worktree's ignored bin/obj `aot8` folders, because the scratchpad path is too long for link.exe.
- `probe-disconnect.patch`, `probe-uia.log`: the drop and disconnect counts.
- `proto-namecmp.patch`, `ZzR8NameProbe.cs`, `nameprobe.ps1`, `nameprobe\junction.txt`: the candidate's prototype, the Junction probe, its runner and its results.
- `nc-A.json`, `nc-B.json`: the suite's a11y rows with the switch off and on.
- `d-*.diff`, `r7-src.diff`, `srcfiles.txt`: the review diffs.
