# Round 7 survey, expert 4: .NET runtime

SATISFIED: no

Worktree left clean at 57e6547c; every src probe reversed with `git apply -R probe-all.patch`.

## REVIEW

One objection, to 4325bac6 (AT-SPI objects kept by element); the same flaw was already in the UIA bridge. Both bridges
keep their objects in a default `ConcurrentDictionary`, whose lock array grows with the table: 28 locks → 1,792 on this
28-thread desk (1,024–2,047 on other processor counts). `Count` takes every one of those locks. `Update` reads
`_objects.Count` twice per AT-SPI apply (LinuxAccessibilityBridge.cs, the two `found != _objects.Count` checks), and
`Rebase(...) != _wrappers.Count` reads it once per UIA apply. → candidate a11y-bridge-map-one-lock. No other commit
regressed in this area:

- **Steady rows, JIT and NativeAOT:** the AOT and JIT suite captures of the final src
  (`2026-11-02-799bcb3-atspi-kept-*after`) agree within 1 KB/op on all 190 rows. Against round 6's start
  (`d8b342a-linestrings-before`, JIT and AOT) no row under 5 KB rose by more than 0.05 KB. The Tab-through-fields UIA
  row reads 0.3 or 0.4 KB from one capture to the next on the same tree.
- **afa7116d/61b538c3 (lazy line runs):** `_runs` only ever changes from source to runs, so a reader on another thread
  can't see it go back; every reader is on the UI thread (a11y geometry is snapshotted there: TextArea `xs` arrays,
  RichTextEditor `LineGeometry`). The `LineStyle` cache is capped at 64 entries.
- **985adef7 (`TokenizeDiff` worker):** one cancellation source per ask, disposed when the result lands; one closure and
  one state machine per ask, not per key.
- **323a3073, 5765d27b, 92084157, a4f798dd..d546eb6e:** no per-key closures or leaks; nits under Examined.

## CANDIDATES

- id: dotnet-shortcut-chord-enumerator (resolves vesper-key-rest-bytes)
  what: `Shortcut.Matches` (src/HultUI.Core/Events/ShortcutMap.cs:207) loops `foreach` over
  `IReadOnlyList<KeyChord> Keys`; on an array behind an interface that makes a 32 B enumerator per call. An index loop,
  or keeping `Keys` as a `KeyChord[]`, removes it.
  evidence: Vesper calls `Matches` 168 times per key (counted): three `Find(e, Group)` scans over ~50 shortcuts each,
  plus ~10 `_keys.Matches(...)` checks. Walk host, prototype index loop behind an env switch, AOT: Detail 26.4 → 21.1
  KB/key (−20 %), Brief 34.1 → 28.9 (−15 %). Bytes in key dispatch outside builds 6.05 → 0.80 KB/key = 168 × 32 B —
  the 5.3–6.2 KB "rest" experts 6 and 8 reported. JIT as built: Detail 26.5 → 21.7 (−18 %). Junction History 16
  matches per key (31.9 → 31.5 KB), Meridian History 9 (68.2 → 67.9); Junction summary typing none. Trap: only adding a
  counter to `Matches` made the JIT stack-allocate the enumerator — measure this fix on AOT.
  expected: churn bar, −15…−20 % per Vesper key on every walk (~5.25 KB each).
  cost: small, Core only, no API change. `RichTextToolbar.StateOf` has the same pattern (one enumerator per key); fold
  it in.
- id: a11y-bridge-map-one-lock
  what: create the bridges' maps with `new ConcurrentDictionary<,>(concurrencyLevel: 1, capacity)`:
  `LinuxAccessibilityBridge._objects`, `WindowsAccessibilityBridge._wrappers` and `_nativeByElement`. The UI thread is
  the one writer (native objects are written under `_nativeLock`); reads stay lock-free. `Count` then takes 1 lock
  instead of up to 2,047.
  evidence: probe `cdcount`: a default map has 224 locks at 100 entries and 1,792 from 600 entries; `Count` costs 17 µs
  against 13 ns with one lock. Timed inside the suite process (JIT), by bridge: AT-SPI at 3,000 objects spends 36–56 µs
  per apply in `Count`; UIA at 3,036 wrappers 18–28 µs; with one lock 0.1–0.2 µs. Kept-tab switch, AT-SPI, a11y
  phase: 0.215 and 0.223 → 0.190 and 0.189 ms (−13 %); min 0.613/0.624 → 0.610/0.600 (two alternated pairs on a loaded
  desk where the A/A control moved ±10 %: indicative only). Each grown map also keeps ~64 KB (1,792 lock objects plus
  two arrays).
  expected: time bar on `a11y/switch between 3 kept tabs of 300 rows, AT-SPI` — 34–56 µs of the 632 µs AOT min
  (−5…−9 %); verify on AOT in the build. Scaling bar: every structural apply loses 1,024–2,047 lock acquisitions
  however small the change.
  cost: small; constructor arguments only.
- id: a11y-overlay-owner-slot-leak
  what: with any bridge active, each context menu, submenu or Dropdown popup that opens takes an
  `AccessibleOwnerSlots` slot and never gives it back.
  why: `ContextMenuElement` is the overlay's root and publishes item nodes, so the tree builder calls
  `SlotOf(list.Id)`; a slot is released only in `Element.Unmount`, and `Window.RemoveOverlayAt` never unmounts the
  overlay's content.
  evidence: retain probe, 1,000 rows, menus with constant items, slot map counted by reflection: 50 → 250 slots over 200
  cycles with the AT-SPI bridge, the same with the headless recording bridge. Heap after full collections: +56, +95,
  then +198 B per cycle at 200→800→1,400→2,000, rising as the two dictionaries resize. `dumpheap` diff cycle 400 →
  2,000: `Dictionary<ulong,uint>.Entry[]` +84.8 KB, `Dictionary<uint,ulong>.Entry[]` +84.8 KB, `Int32[]` +28.3 KB,
  nothing else. No bridge: 0 slots, 0 B/cycle. A popover or a dialog with a bridge: 0 slots, 0 B/cycle.
  expected: retained bar (closes a leak that grows with every menu opened while a reader runs).
  cost: small. Release the owner slots of the overlay content's subtree in `RemoveOverlayAt`, or have overlay content
  mount and unmount properly.

## WAITING, RE-RANKED

- atspi-kept-tab-retire: fold into a11y-bridge-map-one-lock. The switch's retires all come from the diff path, 819 per
  apply with no sweep; removing the second map operation saves 819 lock-free lookups, ~1 % of the step at most. The
  regression is better explained by the two `Count` calls, ~3,600 lock acquisitions per apply.
- vesper-key-rest-bytes: traced; it is dotnet-shortcut-chord-enumerator.
- runtime-element-layout-state, phase 2: not worth it alone; keep it low. Cold fields ~72 B per `Element`
  (`_overlayHost`, `_pendingAnnouncements`, `TooltipText`, `ContextMenuItems`, `AccessibilityObserver`,
  `OverlayOrigin`, `_refs`, `_automationId`, `_accessibleDescription`); `TextElement`'s selection state ~64 B more;
  `FocusManager` must stay inline (`StyleResolver` sets it on every styled element). Copies per key now (made minus
  laid out, JIT): Meridian URL 54 (73 KB/key), Meridian History 48 (69 KB), Aperture Right 42 (48.6 KB), Aperture Down
  45 (82 KB), Vesper Brief 18 (34 KB), Vesper Detail 9 (26.5 KB), Junction History 6 (33 KB), Junction summary typing 8
  (15.7 KB). At 64–128 B a copy that is −2…−9 % a key; only Aperture Right might reach −11 %, if most copies are text.
- Retained memory after round 6 (step 15's dialog +470 B a cycle — whose?): the text pipeline's bounded caches, not a
  leak. The dialog's label names the cycle ("Fetching {i} of 100"), so every open caches a new string: +454…+675 B per
  cycle with that label, with or without the bridge; a constant label 0 B per cycle over 1,400 cycles, with or without.
  Caps: 8 MB shape, 4 MB each word, layout and width, 10 + 12 MB memo.

## EXAMINED, NOT WORTH IT

- **985adef7:** a cancelled prefetch's `Task.Delay(rest, token)` throws once per cancelled ask on the worker;
  `ConfigureAwaitOptions.SuppressThrowing` would avoid it, but that is under 0.1 % of a History key's all-thread bytes
  (1.6 MB).
- **92084157:** `IsStale`'s `foreach` over `IReadOnlyList<RichTextCommand?>` makes 32 B per key on AOT, 1.5 % of the
  2.1 KB typing key. Fix it with the shortcut change.
- **AtSpiServedItems:** `_made.Keys` and `_named.Keys` take all locks and copy the keys, but only when a served list
  shrinks.
- **Kept-tab AT-SPI object pooling:** no app uses kept tabs (0 `keepAlive: true` in apps).
- **Popover, dialog and menu open/close without a reader:** flat over 2,000 cycles.

## NEEDS THE USER

None.

## FILES

Scratchpad `r7-dotnet\` of session 855f4ddd: map locks and A/B `cdcount\`, `ab-cd1.ps1`, `ab\`, `abother.py`,
`cdlog-A.txt`, `cdlog-B.txt`; retain `retain\Program.cs`, `retain\run.ps1`, `retain\run2.ps1`, `retain\dumps.ps1`,
`retain\retain.log`, `retain\heapstat-menu-atspi-*.txt`, `heapdiff.py`; walk host `walk\src\Program.cs` (dispatch
bytes, builds inside dispatch, shortcut matches; NativeAOT build `walk\aot-w7`), `walkrun.ps1`, `walkpub.ps1`,
`elems.ps1`, `elems.log`, `shortcut-ab.ps1`, `shortcut-ab.log`, `shortcut-aot.ps1`, `shortcut-aot.log`,
`shortcut-aot2.ps1`, `shortcut-aot2.log`; census `sizes\`, `sizes.txt`; capture checks `jitaot.py`, `jitaot.txt`,
`smallrows.py`, `rowtrail.py`, `rows.py`; probe patch `probe-all.patch`.
