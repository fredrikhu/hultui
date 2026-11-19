SATISFIED: yes. No candidate in my area clears a bar. The +1.3 % and +2 % rises are now traced to three commits and their call sites (below). Round 15's .NET commits hold in a real window at 440ba012.

Setup: worktree detached at 440ba012 (also built at 75f2288a and 99db58cf), SDK 11.0.100. I report bytes, counts and complexity only. The worktree is clean and back at 440ba012. Build servers are shut down and nothing is running.

REVIEW: no objection that clears a bar. **Where the walks' +1.3 % (expert 8) and the suite's +2 % (expert 7) come from: element size, three commits.** I measured every class's instance size (alloc delta of `GetUninitializedObject`, JIT) at 75f2288a, 99db58cf and 440ba012:
- **943610c7: +8 B on every element.** It adds `bool _accessibleEnabled` (Element.cs:520), which pushes the byte fields over an 8-byte boundary: BoxElement 584 → 592. This is the bytes per element expert 7 asked for.
- **41e2775e: +8 B on every InheritedStyle.** It adds `CursorImage? CursorImage` (InheritedStyle.cs:27): 72 → 80 B. 204 of Junction History's 374 elements have one, and 329 of Vesper's 471.
- **d43b3518: +8 B on every element.** `MeasureCache._maxContentWidth` (MeasureCache.cs:47) takes the struct from 120 to 124 B, padded to 8 inside Element: BoxElement 592 → 600, TextElement 656 → 672.
- The first two land before 043f5b38 and the third after it. That matches expert 8's "half before, half after", and 99db58cf measures exactly halfway.
- **Whole trees (headless, settled):**
  - Junction History on jrepo: 374 elements, 245,456 → 253,000 B shallow (+7.5 KB, +3.1 %, +20.2 B an element).
  - Vesper, 1,000 files in both panes: 471 elements, 314,976 → 325,128 B (+10.2 KB, +3.2 %, +21.6 B an element).
  - Heap after a full GC is 2.58–2.60 MB on both sides (elements are about 10 % of it), so the tree costs +0.3 % of the heap.
- **Walks:** walks that build or realize rows pay the +3 % on their element share. A virtual/ step's +2.1 KB is about 100 new elements at +20 B. History walks fell 1.7–2.5 % at the same time, because d43b3518 also cut measures (4.2 → 2.5 a key).
- **Smaller ones:**
  - ae3ac740 makes `KeyDownEvent` 56 → 64 B (`TargetTakesText`). That is why Aperture's key frames read 104 → 112 B in expert 8's real-window logs.
  - 7c0a3b79 adds no field: off, it costs one static read a box written, and no app listens.
  - c5bdaa3d: `Window` 912 → 1,024 B, once per window.
  - d43b3518's new layout code allocates nothing (two struct `new`s).
- **ppy.SDL3-CS 2026.1002.1 (fd0cccb7): nothing new.** I compared the 96 SDL functions HultUI calls by reflection over both packages: every signature is the same as 2026.320.0. The per-frame and per-event ones take pointers (`SDL_PollEvent(SDL_Event*)`, `SDL_GL_SwapWindow`, `SDL_GetWindowSizeInPixels`). The string-returning ones are cached or run once per action (`SDL_GetCurrentVideoDriver`, `SDL_GetDisplayName`, clipboard, error). `SDL_SetHint("1"/"0")` runs only when a 1 ms timer hold starts or ends. In expert 8's real-window AOT frame logs, Reel playback frames read a median of 0 B on the UI thread on both sides (88 and 91 frames).
- **SkiaSharp 4.156 / HarfBuzzSharp 14.4 (043f5b38): none.** Plan 18 step 7 found allocations unchanged on every row, JIT and AOT. Expert 8's sk3 → sk4 walks moved 0.0 %. `SkiaRenderContext` now draws through a pooled `SKPathBuilder` (`NativePath`) and caches dash effects. Its new closure classes are the old ones renumbered.
- **Jellyfin.Sdk 2026.10.12 on Kiota 2.1.6 (e221f105): fewer bytes.** Probe: a canned 100-item library page (150 KB JSON) through Aperture's adapter and JSON factories, from a fake HttpMessageHandler (no server, since sign-in fails today).
  - A request: 4.22–4.27 → 3.99–4.01 MB (−5.6 %). Parse alone: 3.99 → 3.79 MB (−5.1 %). Gen-0 GCs per 300 requests: 74 → 69.
- **xunit.runner.visualstudio 4 (535b0bcb): no allocation test changes.** `tests/Shared/Allocations.cs` counts `GetAllocatedBytesForCurrentThread` around synchronous work. None of the 59 files that use it blocks on a task inside a measured region; the two files that use both do so outside the measured lambdas. HeadlessSession's producers now go to the pool's global queue (`PreferFairness`), so a headless frame's bytes still leave them out.
- **Round 15 as landed: none.**
  - a9321aac and add912d2, real window, base → new: see REAL WINDOW. They hold at 440ba012.
  - f2a9c654 allocates nothing.
  - Posted-work drain (59609522, c32c4b2e), by code reading, not measured: 0 B a pass and a batch. It is a `TryDequeue` loop with a timestamp, `RefreshRate` is a stored property, the `List<Window>` loop uses a struct enumerator, and batch depth is an int.

CANDIDATES: none clears a bar (see EXAMINED for the element-packing option).

REAL WINDOW:
- **Runs and guards:**
  - One hold of `Global\HultUI-r16-desktop`, 02:40:33–02:43:23 (170.6 s, waited 74.1 s).
  - The idle gate ran first and failed after 3 min (another session's testhost). These are allocation and heap numbers only.
  - 6 launches of my AOT host: Junction base, new, new, base and Vesper new, base. All exited 0 on WM_CLOSE.
  - 0 foreground takes, 0 activations refused, 0 nvlddmkm events, 0 screen reads.
  - Monitors: the 4K (3840×2160, 120 Hz, primary, DPI 144 = 150 %) and the 1440p (2560×1440, 120 Hz) to its left. Windows were on the 4K: Junction 2160×1350 px (1440×900 logical), Vesper 1860×1230 px. System timer 1.000 ms (Steam).
- **Junction on jrepo, base → new (heap after a full GC):**
  - 60 History keys: heap 7.58/7.62 → 7.54/7.54 MB. All threads 26.4 MB on both sides (440 KB a key). UI frame bytes 29.4 → 28.6 KB a key (−2.5 %). GCs 4/3/3 on both sides.
  - 40 file keys in the 314-file commit: 6.23 MB a key on all threads on both sides (+0.2 %). UI frame bytes 781 → 792 KB a key (+1.4 %, the element share). GCs 18/8/4. Heap 28.52/28.62 → 28.54/28.57 MB.
  - Back to History plus 10 keys: heap 23.68/23.76 → 23.69/23.71 MB.
  - LOH: 1.12 MB at start, then 1.64 MB from the first walk to the end of all four launches. `RunUpTo`'s private pool keeps at most two arrays a size (about 2 MB at worst) and did not grow.
- **Vesper, 1,000 files in both panes:**
  - Down: 33.3 → 33.7 KB a frame (+1.1 %) plus 18.2 → 18.4 KB of input handling.
  - Wheel: 16.1 → 16.4 KB a frame (+1.5 %, about 12 new elements a step).
  - Heap 6.17/6.26 MB on both sides.
  - A real AOT window spends about 1.7× the bytes of the same JIT headless key (51 KB against 30 KB a key).

WAITING, RE-RANKED:
- **apps-junction-big-file-revisit (5, with 4): stronger, keep.** I ran expert 8's real-time bigrepo probe on JIT with an in-process allocation-tick listener:
  - Revisits cost 31.95 MB a key (first visits 32.02). GCs 26/9/5 per 12 keys.
  - **LOH is 2.60 MB a key, all `String`:** the file's two versions as text, about 1.3 MB each. The other 92 % is small-object churn from the colour pass that the next key cancels.
  - The 5 gen-2 GCs per 12 keys follow the LOH (31 MB per 12 keys, about one per 6 MB).
  - One finished pass over the pair, after the last key, allocated 255 MB in 3 s.
  - Keeping the two versions' text cuts about 8 % of the bytes and most of the gen-2s. Keeping the tokenizer's progress or result (expert 5) is the part that clears the churn bar (about −29 MB a revisit).
  - **Suite proposal (h): support.** Count all threads with `GC.GetTotalAllocatedBytes(precise: true)`, plus gen-0/1/2 counts and LOH bytes, on a real clock (the cancel at 400 ms needs one), with a pair of files of about 675 KB. The suite's allocKB counts only the UI thread and misses this loader work.
- **findbar-countonscreen-foreach: still a nit.** FindBar.cs:211 is untouched since round 14: a boxed enumerator, 32 B a switch.
- **fedora-local-dotnet: keep waiting.** On 2026-11-18, FedoraLinux-44's `updates` and `updates-testing` still offer only 11.0.100~rc.3.26522.148-0.13.fc44. The installed rpm is dotnet-sdk-10.0-10.0.112. Both links (`/usr/local/bin/dotnet`, `/usr/local/sbin/dotnet`) still point to `/usr/local/share/dotnet` (11.0.100). WSL was terminated after the check.

EXAMINED, NOT WORTH IT:
- **Taking the 16–24 B an element back:** pack Element's bools (`_accessibleEnabled`, `_detached`, `_mounted` and others) into one flags byte, fit `_maxContentWidth` into existing padding, and move `CursorImage` to a side table. That is −20 B an element (−3 % element bytes, about −1.3 % of walk bytes, −0.3 % of the heap). It is a medium-sized change to Element's layout, with no API change, and below every bar.
  - **For PERFORMANCE.md:** every Element field is paid on every element of every build and every realized row. A bool is free only while the byte fields have room, and a 4-byte field in an embedded struct can cost 8. Check `GetUninitializedObject` sizes before and after.
- **Kiota's model parse:** about 40 KB an item, 25× the JSON. 20.3 KB of each item is `BaseItemDto.GetFieldDeserializers()` (155 setter delegates and their dictionary, generated code). It happens once per page (Aperture's home asks for 12 + 12 + 16 a library), under one gen-0 GC a page. Only a hand-written parser for list endpoints would change it.
- **Vesper with the same folder in both panes rebuilds on each key:** 30 KB a key at 100, 1,000 and 5,000 files, so it does not grow with the folder. This is expert 8's partner-60ms case.

NEEDS THE USER: none.

FILES (in C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\b6ef64d9-4ed1-4793-9955-6a986d74de79\scratchpad\r16-dotnet\):
- `sizes\src\Program.cs`, `sizes\proj\Sizes.csproj`, `sizes\diff.py`: type sizes, tree counts and Vesper keys. Outputs: `sizes\sizes-{75f2288a,99db58cf,440ba012}.tsv`, `sizes\diff-75-440.txt`, `sizes\tree-*.txt`.
- `kiota\src\Program.cs`, `kiota\proj\KiotaProbe.csproj` (assembly named Aperture.Tests to reach the internal JSON factories), `kiota\kiota-old.txt`, `kiota\kiota-new.txt`, `kiota\kiota-new2.txt`.
- `loh\src\Program.cs` (expert 8's bisect probe plus allocation ticks), `loh\run1.txt`.
- `sdlsig.ps1` (SDL3-CS signature diff), `framebytes.py` (expert 8's frame logs, base against new).
- `host\` (round 15's host, rebased unchanged): `batch16.ps1`, `holds.log`, `gate.log`, `runs\A*.out.txt`, `runs\A*.frames.csv`, `runs\A.jsonl`. Publishes are in `aot-base\` (75f2288a) and `aot-new\` (440ba012).
- `fx\`: jrepo, bigrepo, files100, files1000, files5000.
- `skiarc.diff`, `r16-7-a11y.md`, `r16-8-apps.md` (copies read).
