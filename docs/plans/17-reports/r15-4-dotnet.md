SATISFIED: no. Round 14's per-file patch load leaves two retained-memory costs in Junction, both measured with a prototype (candidates 1 and 2). A third candidate is a measurement of a known lead: the History colour prefetch is about 70 % of a History walk's all-thread bytes.

Setup: worktree detached at 5a212f3e, SDK 11.0.100. My own NativeAOT probe hosts (round 14's r14b2 host plus new steps), GL, the apps' GC settings. Only bytes, counts and complexity are reported. The worktree is clean (the prototype edits were undone by reverse edit). Build servers are shut down, nothing is running.

REVIEW:
- **8b994a41 (objection → candidate 1).** `_commitPaneShown` (apps/Junction/JunctionApp.Commit.cs:362, set at :390) holds the last Commit-tab detail's whole `DiffByPath`. It is replaced only by the next Commit-tab build, so after a walk through a big commit's files, History keeps that commit's parsed diffs alive.
- **c7dd2365 (objection → candidate 2).** `GitProcess.RunUpTo` rents its buffer from `ArrayPool<char>.Shared`, doubling from 16K to 256K chars. Every thread-pool thread that read a long patch keeps one array of each size in its thread-local slots: ~992 KB, 512 KB of it on the LOH. They go only when the thread retires (~20 s idle) or after two gen-2 trims ≥30 s apart, and Junction now has almost no gen-2 collections. (The commit is right that a stopped read keeps only its part; this is the pool's own retention.)
- **5d9a5c71 (word cache 32,768 / 16 MB): no objection.** Word cache at rest in real windows:
  - Vesper 194 words / 0.07 MB, Meridian 88 / 0.02, Aperture 130 / 0.04.
  - Junction after 240 History keys and a 73-file Commit walk: 7,104 / 2.50 MB, below the old 8,192 bound.
  - Only the 154k-character ja+zh file uses the new room: 18,426 / 10.25 MB.
  - Latin worst case: 32,768 × ~308 B weighed ≈ 10 MB (it was 2.5 MB at 8,192).
- **21cf206c, a4bad904, 05f19d27, e0263b8f, 3a859d8e: none.**
  - One `ITimer` per watcher, rooted only while armed.
  - `IndexPrint` reads through one pooled 64 KB byte[].
  - `_folders` ≤ 8,192, `_ask` ≤ 256, `_waiting` ≤ 4,096, all cleared when full.
  - One StringBuilder per check-ignore question.
- **b62e22cf: none.** No allocation in the diff. Real-window frames: Aperture wheel median 608 B, Aperture keys 104–184 B, Vesper cursor keys 520 B, Meridian keys 104 B; Junction History key frames ~32 KB (the view build).
- **00f17182: none.** 40 palette toggles: heap 27.61 → 27.63 MB (+20 KB), 31.5 KB of UI-thread input bytes a toggle, 218 of 259 fade frames at 0 B. `_a11yOverlaysClosed` is cleared every pass (Window.cs:2108).
- **The rest read: none.** 1b019826's `Task.Delay` per waiting navigation is one timer and closure per navigation, against 264 KB of frame bytes a navigation.

CANDIDATES:
- id: dotnet-junction-commit-pane-keeps-detail (owners 4/6)
  what: Drop `_commitPaneShown` when a new detail lands (in `PickInitialFile`), so the left commit's parsed diffs can be collected. It is only ever used for the same detail (`ReferenceEquals(Owner, detail.DiffByPath)`), so nothing a user sees changes.
  evidence:
  - B3, a 60-key walk in 643eb07b (314 files, 7.18 M chars), then History, a small commit and 10 Downs: heap after a full GC 40.01 MB. It falls to 27.56 MB only when the Commit tab is shown again (−12.45 MB).
  - Prototype behind `R15_PROTO` in the same binary, alternated ×2, 40-key walk: the kept detail is 6.0 / 6.0 MB in base and 0 in proto. In History after leaving the Commit tab, together with candidate 2: 32.08 / 32.10 → 22.33 / 22.40 MB (−30 %).
  real app: Junction, reading a round's plan merge (3–8 M characters) file by file, then going back to History. The memory stays until the Commit tab is opened again. This user does this every round.
  expected: retained memory −6 to −12 MB (−19 to −31 % of the heap; bar 5 %). A leak-like hold of one commit, closed.
  cost: small, app only, a one-line reset. No API or pixel impact.
- id: dotnet-junction-patch-buffer-shared-pool (owner 4)
  what: Rent `RunUpTo`'s buffers from a private `ArrayPool<char>.Create(262_144, 2)` (or one process-wide spare), not `ArrayPool<char>.Shared`, whose per-thread slots keep ~1 MB on every pool thread that read a long patch.
  evidence:
  - poolprobe, 120 commits of jrepo: retained after a full GC with 1 / 4 / 8 threads 1.03 / 3.95 / 7.84 MB (LOH 0.5 / 2.0 / 4.0 MB).
  - With 8 threads, 20 s later (the threads retired) 1.03 MB remains; at 40 s, 0.06 MB.
  - Real window, same prototype run as above: after a 60-key History walk 25.82 / 26.50 → 19.14 / 23.55 MB, LOH 3.07 → 1.57 MB in both runs. At the end 26.04 / 26.07 → 23.07 / 23.14 MB (−2.9 MB, −11 %).
  - No churn added: History walk 188.6 / 235.9 → 173.7 / 229.6 MB on all threads, Commit walk 237.4 → 243.2 / 237.4 MB, the same GC counts.
  real app: Junction History browsing at a reading pace. Each resting key loads a detail, and 15 of jrepo's first 120 commits are longer than 262,144 characters.
  expected: retained memory −3.0 to −6.7 MB while browsing (−11 to −25 %; bar 5 %). No LOH garbage left behind when threads retire (it was freed only by a gen 2).
  cost: small, app only (GitProcess.cs:281–282). No API or pixel impact.
- id: junction-history-highlight-prefetch (owners 8/5, with 4; data for Waiting text-junction-first-visit-tokenize)
  what: Each History rest tokenizes the opening file's two versions for a Commit tab that is not on screen (`PrefetchHighlight`, JunctionApp.Commit.cs:514–531, `rest: true`).
  evidence: real window, jrepo, from HEAD:
  - First walk of 120 Downs 250 ms apart: 3.13 MB a key on all threads. A second walk over the same 120 commits, where the highlight cache hits: 0.90 MB a key (−71 %).
  - Git runs 10.2 → 8.6 a key; app GCs 23/6/1 → 6/2/0.
  - B3 and D, 60 Downs: 3.1–3.9 MB a key.
  - Junction repo switch to the bigrepo/bigrepo2 fixtures: 24.2 MB a switch on all threads (round 14: 25.8). The opening file's two 8,000-line C# versions are tokenized.
  - The cache itself is cheap to keep: `_highlights` held 2.1 MB after 60 rests, 8.85 MB after 60 Commit-tab file visits (≤128 entries, cleared all at once).
  real app: Junction History at a reading pace, minutes at a time, with the Commit tab never opened.
  expected: memory churn −70 % of a History walk's all-thread bytes (bar 10 %), if the prefetch waits longer or is skipped. Alternatively a cheaper tokenizer (expert 5).
  cost: small to medium, app only. It changes when colours appear: round 14 measured ~350 ms of uncoloured diff on the tab switch without the prefetch. See NEEDS THE USER.

REAL WINDOW (NativeAOT, GL, guarded; Junction 2160×1350 px = 1440×900 logical):
- **Runs and guards:**
  - 4 holds of `Global\HultUI-r15-desktop`: A 98.7 s (waited 274.3 s), B 100.8 s (142.0 s), C 73.8 s (0 s), D 143.6 s (0 s). The idle gate ran before each hold.
  - 11 launches, all exit 0 on WM_CLOSE. 0 foreground takes, 0 activations refused, 0 nvlddmkm events. Input was in process only.
  - Monitors: the 4K 3840×2160 at 150 %, primary, 120 Hz (window DPI 144), and the 1440p 2560×1440 at 120 Hz to its left. System timer 1.000 ms at every hold start.
- **What headless runs do not show:**
  - Junction's private bytes went 130 → 307 MB over a 100 s session while the full-GC heap went 6.4 → 40.7 MB. GC commit was +53 MB, plus +21.9 MB in a 1 GB reservation. NVIDIA's write-combined regions (`PAGE_READWRITE|PAGE_WRITECOMBINE`, 32 MB reserves) grew +18.3 MB (28 MB committed, 11.5 resident). About +60 MB went to 8–16 MB native read-write reservations, likely Skia's or the driver's allocator.
  - **Arabic README at 960 logical: the text caches sit at their bounds** after the drag: memo 3,114 entries / 10.00 MB, width 14,774 / 4.00 MB, layout 1,670 / 4.00 MB. The drag costs 2.0 MB a step on the UI thread (median 1.48 MB a frame), against 136 KB a step for the 154k-character CJK file. This is a lead for expert 5: a cache cliff like round 14's word cache.

WAITING, RE-RANKED:
- **text-bidi-real-window-private (5/3/4): close, not a leak and not ours.**
  - The commit that is never resident is NVIDIA's write-combined memory. After the Arabic walk one region holds 22.5 MB committed but only 2.7 MB resident, another 15.6 / 8.5 MB.
  - They grew +9.0 MB in the drag and gave back 10.9 MB 12 s later. The same regions appear for the CJK file (22.5 / 2.7 MB) and in Junction's History and Commit walk.
  - The rest of the growth is the GC's own commit (22.1 → 54.6 MB, live heap 9.8 → 30.1 MB): the text caches at their bounds.
- **junction-ctrl-d-kept-patch (6/4): still so.** `CurrentDiffTarget` returns `CommitPane`'s kept path and diff while a first visit waits (~33–47 ms in a big commit). Small, correctness, not perf.
- **findbar-countonscreen-foreach: still a nit.** FindBar.cs:212 is unchanged since round 14. 32 B a switch.
- **textarea-chunked-document: keep parked.** No real app types into more than 256k characters. The largest real text (the 154k-character file) is read-only in Files.
- **fedora-local-dotnet: keep waiting.**
  - FedoraLinux-44 `updates` and `updates-testing` still have only 11.0.100~rc.3.26522.148-0.13.fc44.
  - The installed rpm is dotnet-sdk-10.0-10.0.112.
  - Both links (`/usr/local/bin/dotnet` and `/usr/local/sbin/dotnet`) still point at `/usr/local/share/dotnet` (11.0.100). WSL was terminated after the check.

EXAMINED, NOT WORTH IT:
- **Palette open/close retention:** +20 KB over 40 toggles.
- **Junction's other caches after long walks:**
  - A second History walk over the same commits adds 0.47 MB.
  - About 15 MB of the first walk's +20 MB is neither the highlight cache, the text caches nor the pool. It stays flat on revisits: one-time state, probably the tokenizer's grammars (round 11's syntax registry).
- **Folding GetCommit, GetCommitFiles and GetCommitDiff into one git run:** −2 of ~10 runs a key, ~56 KB of 3.1 MB a key (−2 %). Below the bar.
- **Meridian's Ctrl+Tab with nothing to switch:** 1 frame of 104 B a key. Not perf in my area.

NEEDS THE USER:
- **junction-history-highlight-prefetch:** should History stop tokenizing the commit's opening file before the Commit tab is opened? The other options are a longer rest (for example ~600 ms) or only once the Commit tab has been used in the session. Each saves ~2.2 MB a resting key on all threads (−70 %) but brings back up to ~350 ms of uncoloured diff on the first switch to the Commit tab. Before/after could be shown as a frame capture of the tab switch at 50 ms and 400 ms.

FILES (in C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\8e8ba33b-db33-43fa-885a-cb6b82b7253f\scratchpad\r15-dotnet\):
- host\src\Program.cs, host\src\Script.cs: the probe host, with new steps wc, vm, jfile, jpal, jclearhl, drag, text, wait and key modifiers.
- host\batch15.ps1 (batches A–D), host\Driver.cs, host\Monitors.cs, host\Start.props, host\pub.ps1, host\sum15.py, host\holds.log.
- host\runs\*.out.txt, *.frames.csv, A.jsonl–D.jsonl.
- proto-d.diff: the R15_PROTO prototypes, not in the worktree.
- poolprobe\Program.cs, poolprobe\PoolProbe.csproj, poolprobe\wait8.txt.
- fx\: copies of jrepo, bigrepo, bigrepo2, realar and realbig.
- *.diff: the round 14 commits read; gate.log.
