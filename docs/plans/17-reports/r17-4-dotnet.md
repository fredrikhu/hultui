SATISFIED: no

Set-up: worktree detached at 0df5e3ea, SDK 11.0.100. I report bytes, counts and complexity only. The worktree is clean again: the prototype was reversed by a reverse patch and its line endings set back to LF. Build servers are shut down and nothing is left running.

REVIEW: one objection (80e94eeb, folded into candidate 2) and one measurement objection (main's new suite row). The rest is clean:
- **80e94eeb (Junction keeps cancelled big files' texts, 16 MB LRU).**
  - The bound is right for paging back: 12 keys over bigrepo kept 5 entries, 13.6 MB.
  - Leaving the Commit view does not release them, by design. After History, the heap after a full GC is 22.9–24.1 MB with 13.55 MB kept. In the real window it is 24.6–25.6 MB.
  - A repo switch keeps them too. That is fine: they can be hit again on return.
  - **Objection:** Changes-mode keys carry the data generation (JunctionApp.Highlight.cs:285). Any working-tree event moves the generation (OnWorkingTreeChange, JunctionApp.cs:887): a save of another file, a new untracked file. After one such event all 5 kept entries (13.7 MB) can never be hit again, yet stay until evicted or a scheme change. → candidate 2.
- **f76cb440 (TokenizeDiff progress in a ConditionalWeakTable): no objection.**
  - The `DiffWalk` value holds both texts (primary-constructor captures), the per-line result arrays (8 B a line a side), the spans of the shown lines walked so far, and the tokenizer states.
  - A value that holds its own key does not keep it alive (ephemeron).
  - Junction is the only caller. Everything goes when Junction's LRU drops the texts, or on `SetTheme`.
- **41ecec65 (`IEagerItemsLimit`, `PeekItemLabel`): no objection.** The limit is read once per bridge install. `PeekItemLabel` costs 0 B when a label is kept and one string otherwise, and runs only with a client asking.
- **0d96e805 / b02b46d6 (UIA gate): 0 B a frame with no client asking.**
  - `IsActive` stops at `s_openGates == 0` before the `UiaClientsAreListening` P/Invoke.
  - The property observer and `Announce` return first, so no list grows.
  - The gate count is decremented on dispose (WindowsAccessibilityBridge.cs:1460).
- **89506c22 (`ViewerLoadGate`): no objection.** +80 B an open is 0.008 % of a viewer cycle (real window: 1.29 MB UI a Shift+F3/Esc cycle).
- **38ec9773 / b8a3dcf5 (VirtualList cursor and columns): no regression.**
  - Element sizes are unchanged at 0df5e3ea against 440ba012: BoxElement 600, TextElement 672, ButtonElement 640, InheritedStyle 80. ScrollViewElement goes 688 → 696 and Window 1,024 → 1,032, both one per scroll view or window.
  - `Row` keeps its size (a bool became `ListItemState`, two bools).
  - No app uses the cursor, the columns or `ItemStateTemplate`.
  - Suite, JIT, `--filter virtual/`: 24 of 24 existing rows are within 5 % of 80e94eeb's capture.
  - The new columns rows cost 753 KB a wheel step and 1,137 KB a jump. That is about 5–7 KB a cell scrolled in, the same as list rows, so not O(n).
  - **Measurement objection:** `virtual/columns cursor walk 100k item rows` reports "25/25 steps rendered nothing", 0 rows made, 0.2 KB. The row measures nothing (seen in a filtered run). For main or expert 8.
- **d3e40ecb (Roslyn pinned to 4.14.0): build only.** `4.*` already resolved to 4.14.0: it is the only version in the NuGet cache, and obj/project.assets.json names it. Generated code is unchanged.
- **Other commits in the range:**
  - ec57c4ee: an int depth, 0 B.
  - 40cadb57: `WrapRange` goes 16 → 24 B a kept range, in a bounded LRU.
  - 4917f40d: flag tests, 0 B.
  - 96d00ab3: tweens end in 0.12 s and retain nothing (cycles below).

CANDIDATES:
- id: syntax-diff-skip-shared-stretches (my round-16 lead, prototyped)
  what: In `TokenizeDiff`, the old version's walk takes a line's end state and spans from the new version's walk when the line has the same text and starts from an equal tokenizer state, instead of tokenizing it again.
  evidence:
  - How it works (`r17-dotnet\skip\skip-prototype.diff`, about 90 lines in SyntaxHighlighter.cs):
    - stage 1 records each new-version line's end state (8 B a line) and whether it can be reused (1 B);
    - in stage 2, before tokenizing old line i, it looks for a new line j with the same text whose start state `Equals` the current one: the expected line first, then a hash index of line texts, at most 16 tries;
    - it then takes j's end state, and j's spans where the old side shows i.
  - Output: bit-for-bit equal spans on 9 real pairs, also when cancelled every 150 ms and resumed.
  - One pass, no-budget clock, lines walked and UI-thread bytes:

    | file | lines walked | bytes |
    |---|---|---|
    | bigrepo file00 | 13,626 → 7,453 (−45 %) | 285.1 → 156.9 MB |
    | Window.cs @41ecec65 (8 hunks) | 3,156 → 1,668 | 14.9 → 8.0 MB |
    | VirtualListElement.cs @b8a3dcf5 (20 hunks) | 3,654 → 2,064 | 19.3 → 11.3 MB |
    | TextPipeline.cs @40cadb57 | 1,161 → 614 | 6.4 → 3.4 MB |
    | ledger archive @0df5e3ea | 7,681 → 4,240 | 18.1 → 10.7 MB |
    | ledger @0df5e3ea | 2,150 → 1,269 | — |
    | ELEMENTS.md | 2,273 → 1,892 (−17 %) | — |

    Single-hunk diffs gain nothing (Window.cs @38ec9773: 5 lines skipped).
  - Junction headless (JIT, real clock, bigrepo; 1 base and 2 prototype runs plus an earlier base run):
    - a stop on an unvisited big file: 295.0 / 295.0 → 172.7 / 175.0 MB all threads, 13,623 → 7,453 lines, GCs 18/0/0 → 10/0/0 and 10/2/0;
    - the first file now gets its colours within the Commit open's 2 s (the base does not finish in that time);
    - keys at 400 ms are unchanged (28–31 MB a key: the walk runs until the next key either way).
  - Real window (AOT): the Commit open is 229.0 / 258.5 → 169.0 / 169.0 MB. The whole scene (open, 24 keys, 4 s tail) is 1,093.5 / 1,241.9 → 966.0 / 1,006.0 MB. UI bytes per key are unchanged.
  real app: Junction Commit and Changes views on any multi-hunk diff of a file over ~1,000 lines. In this repo that is Window.cs, TextPipeline.cs, VirtualListElement.cs, the ledger and archive, touched in most rounds. Colours come after about 45 % fewer lines walked, once per file opened.
  expected: churn bar, −41…−45 % of a big file's colour pass (real-window scene −12…−19 %). Time: −45 % of the lines walked before colours land.
  cost: medium-small, no API, no pixel change (identical spans). Extra memory is 9 B a line kept with a cancelled walk (add it to Junction's `Bytes`), plus about 24 B a line for the index while stage 2 runs.
- id: apps-junction-changes-recolour-same-texts
  what: Junction keys a working-tree diff's colours by data generation, so any working-tree event re-reads and re-colours the diff on screen from line 1 although its texts did not change. Its colours go off until that walk lands.
  evidence:
  - Headless bigrepo, Changes view, an untracked file written:
    - base: 323.6 MB and 13,623 lines; prototype: 196.9 / 198.5 MB and 7,453 lines;
    - the diff on screen draws without colours from 484 ms to 3,224 ms (base), and 477 → 1,976 ms with the prototype.
  - At this repo's sizes one re-pass costs 27 MB (Window.cs, 5,574 lines) or 6.4 MB (TextPipeline.cs).
  - The kept-text entries become unreachable (REVIEW).
  real app: the user saves in an editor (any file), or creates an untracked file, while Junction's Changes view shows a modified file. Every such event costs one full pass and a colour blink. A big file blinks for 1.5–2.7 s; for a small file it blinks only until its short walk lands (not measured).
  expected: churn bar, −98…−99 % of the event's colour bytes. What is left is two reads: about 2.6 MB of large-object-heap strings and two git processes. No uncoloured frames.
  cost: small–medium, Junction only, no API. Keep a 64-bit hash of each text with Working/Staged highlights (by repo|mode|path). When the hashes match on the new generation's read, land the old colours under the new key. Carry or drop kept texts across generations the same way.

REAL WINDOW:
- **Hold and guards:**
  - One hold of `Global\HultUI-r17-desktop`, 13:01:59–13:04:55 (175.9 s), waited 128.8 s. The idle gate passed just before it (3 % CPU for 20 s).
  - 6 launches of my NativeAOT hosts (0df5e3ea plus the prototype, toggled by `HULTUI_PROBE_SKIP`), all exit 0 on WM_CLOSE.
  - 0 foreground takes, 0 nvlddmkm events, 0 screen reads. Timer resolution 1.000 ms (Steam).
  - Monitors: the 4K (3840×2160 at 120 Hz, primary) and the 1440p (2560×1440 at 120 Hz) to its left. Windows were on the 4K at DPI 144 (150 %), 2160×1350 px, GL.
- **What headless did not show:** with the prototype the first file's colours land inside the Commit open. Its colour frame then makes the rows again (UI 1,167.5 → 1,840.9 KB), which is Waiting junction-colour-landing-remakes-rows, now inside the open.
- **Leaks: none.**
  - Vesper Shift+F3/Esc on ELEMENTS.md, 40 times: heap 4.19 → 9.84 MB over the first 20 (text caches filling), then 9.81 MB; private bytes 145.6 → 144.4 MB.
  - Junction repo switch jrepo ↔ bigrepo, 20 times: heap 5.64 → 6.75 → 6.56 MB.
  - The host's palette step (`jpal`) did not run (a script parse issue), so palettes are covered headlessly only.
- **Headless cycles (JIT, heap after a full GC at 0/10/20/40/60 cycles):** flat or falling in all five scenes.
  - Junction palette: 2.49 → 2.34 MB (140 KB UI a cycle).
  - Junction History ↔ Commit: 2.97 → 2.83 MB (702 KB).
  - Junction repo switch: 3.64 → 3.50 MB (300 KB).
  - Vesper viewer: 6.67 → 6.69 MB (1,066 KB).
  - Vesper palette: 1.08 → 1.05 MB (302 KB).
- The headless real-clock Junction runs ran with the idle gate failed (another session's testhost). Bytes and line counts are unaffected; per-key MB are noisy.

WAITING, RE-RANKED:
- findbar-countonscreen-foreach: still a nit. FindBar.cs:212 still loops `foreach` over `bar.Children`; 4917f40d and 38ec9773 touched FindBar without changing it. Fix when FindBar is next touched.
- textarea-chunked-document: stays parked. No TextArea or Meridian change in the range (only 4917f40d's key test), and the user's 256k condition is unmet.
- fedora-local-dotnet: keep waiting, but closer. On 2026-11-19, `updates-testing` has the final 11.0.100-1.fc44 while `updates` still has only rc.3. The installed rpm is dotnet-sdk-10.0-10.0.112, and both links still point to /usr/local/share/dotnet (11.0.100). The user could take it from updates-testing now. WSL was terminated after the check.

EXAMINED, NOT WORTH IT:
- The prototype on held or paced keys: at a 400 ms pace the worker walks until the next key either way, so it is 28–31 MB a key on both sides. The win is per finished pass only.
- Vesper's viewer re-reads and re-colours on every open: 12 MB on workers per open of a 166 KB markdown file. A cache would help only when reopening the same file.
- `ViewerLoadGate`'s `Task.Run` of an async lambda: 80 B an open.
- VirtualList columns: about 5–7 KB a cell scrolled in, like list rows; no O(n).

NEEDS THE USER: none. One optional call for the orchestrator: whether the kept texts should also expire after an idle period. A user who browsed big files once holds up to 16 MB for the session (13.6 of a 24.6 MB heap here).

FILES (in C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\35903ed7-ff10-416d-84e9-b6c09675ab6b\scratchpad\r17-dotnet\):
- `skip\skip-prototype.diff`: the prototype, at 0df5e3ea. It has CRLF line endings; apply with `git apply --ignore-whitespace`.
- `skip\src\Program.cs`, `skip\proj\SkipProbe.csproj`, `skip\cases.txt`, `skip\run1.txt`: pair equality, lines walked and bytes.
- `jprobe\src\Program.cs`, `jprobe\proj\JProbe.csproj`, `jprobe\run.ps1`, `jprobe\runs.txt`, `jprobe\runs-base-ungated.txt`, `jprobe\pilot.txt`, `jprobe\gate.log`: Junction bigrepo, Commit and Changes views, the generation bump and retention.
- `cycles\src\Program.cs`, `cycles\proj\Cycles.csproj`, `cycles\cycles1.txt`: headless open/close cycles.
- `sizes\` (round 16's probe, re-pointed): `sizes-0df5e3ea.tsv` against `sizes-440ba012.tsv`.
- `suite\allocdiff.py`, `suite\virtual-0df5e3ea.json`, `suite\virtual.out`: the virtual/ rows.
- `host\` (round 16's host, copied): `batch17.ps1`, `holds.log`, `gate.log`, `runs\A*.out.txt`, `runs\A.jsonl`, `runs\A-driver.log`. The AOT publishes are in `aot\Junction` and `aot\Vesper`.
- `fx\` (jrepo-rw, bigrepo-rw, viewer), `fedora-check.sh`.
