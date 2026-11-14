SATISFIED: no

**Method.** Shared publishes: the five apps on NativeAOT at 5a212f3e in `r15-apps\aot\`. All exited 0, READY.txt was written first, and the smoke test passed in hold h1. Scripted real-window hosts were built from round 14's start-x code plus new steps (click, wheel, move, text, burst, barvisit, jsplit, jwrap, reelseek, and gc/allMB on every MARK): new = `aotx\` (5a212f3e), base = `aotx-base\` (e432c224, a clean archive). Each scene ran base and new, alternated. Headless: the 67 walks, base = round 14's `walk\aot-r14` (e6f7b5aa, which is e432c224 plus docs only), new = `walk\aot-r15`. The idle gate failed at 22 % CPU; I report only bytes and counts.

REVIEW: one objection, plus a check that round 14 helped (it did, a lot).
- **Objection (text, not bisected): Chinese drag up +25 %.** Junction Files view, real zh README (75k characters), 1,440 px window (960 logical), 1 physical px drag steps: 41.4 / 41.2 → 51.7 / 51.7 KB a step. Reproduced in 2 holds (h2, h4), frames 0.98 both sides. Over the same tree:
  - ja is 52.8 → 47.2 (−11 %), the 154k ja+zh file is 5,215 → 67.6 KB a step (−98.7 %, 1.77 → 1.05 frames), and the resize to 1,440 is −20…−24 %.
  - Headless at whole layout widths (scale 1.5, 960 → 933) does not reproduce it (zh 38.6 → 36.9). It needs the real window's fractional widths (0.667 logical a step).
  - The suspects are round 14's only text commits: 2c809bc1, c06aa102, 5d9a5c71.
- **Round 14 helped the real apps.** All on the 4K, 150 %, 120 Hz, base → new; process counts are explained under REAL WINDOW.
  - **History at a reading pace** (jrepo, 20 Downs 400 ms apart; the first commit is a 680 KB capture commit):
    - all threads 10.3 → 1.28 MB a key (−88 %);
    - GCs 15/11/4 → 2/1/0;
    - processes 10.0 → 7.1 a key (166 vs 202 counting the read-ahead tail);
    - UI 40.0 → 37.3 KB, frames 1.05 → 1.00.
  - **bigrepo History, Down/Up between its two big commits** (3.9 MB patch): 105 → 14.3 MB a key on all threads (−86 %), GCs 36/32/6 → 4/0/0.
  - **bigrepo Commit view files:** first visits 27.6 → 11.9 MB a key (−57 %); revisits 25.5 → 23.0 (−10 %; see the second candidate).
  - **Wrapped diff** (a long-line ledger .md):
    - width drag 1.90 → 0.95 frames and 671.5 → 330.7 KB a step (−51 %);
    - height-only drag 235.3 → 2.1 KB a step;
    - splitter drag 1.78 → 0.94 frames and 506 → 300 KB a step (−41 %).
  - **Overlays** (00f17182):
    - Junction palette close 102 → 1.0–4.6 KB (−97…−99 %), open 314–361 → 213–262 KB;
    - History context menu close 100–103 → 0.7–3.1 KB; reopen on the same row 112.8 → 13.2 KB.
  - **Scrollbar visits** (545d5d89): Vesper 30 → 18.4 frames a visit. Junction new is a clean 9-frame tween each way (base frames were irregular).
  - **Wheel under a resting pointer** (Junction History): 1.10 → 1.00 frames a notch, 12.2 / 8.4 KB a notch on both sides.
  - **Vesper navigation:** 1.60 → 1.05 frames a navigation, 439 → 314 KB (−28 %).
  - **Changes view:**
    - a save 5 → 4 frames, 901 → 824 KB;
    - a terminal `git add` 4 → 2 frames, 396 → 224 KB;
    - `git restore --staged` is not heard on base (0 frames, 1 process) and heard on new (2 frames, 24 processes);
    - entering Changes 2 → 1 frames, 16 → 0 processes;
    - a build's 300 outputs under the ignored obj\ draw 0 frames on both sides, 1.74 → 0.70 MB.
  - **Headless:** 67 walks, geomean 1.001, sum −0.3 %. Vesper folder-inout −11.6 % (builds 4 → 1 a key). thumbs-60ms +12.3 % is a timing walk (+0.7 KB). Every Junction walk is identical: headless hides all of the wins above.

CANDIDATES:
- id: text-junction-zh-fractional-drag (owner 5)
  what: on real Chinese text a 1 physical px drag at 150 % now allocates 25 % more a step, while ja and the 154k file improved.
  evidence: above; 2 launches a side, exactly repeated (51.7 / 51.7 vs 41.4 / 41.2 KB); headless at whole widths −4 %.
  real app: Junction's Files view (also Vesper's reader) on a CJK document: every resize or snap step at fractional logical widths.
  expected: churn bar (+10.5 KB a step back). First bisect over 2c809bc1, c06aa102 and 5d9a5c71 with a fractional-width probe.
  cost: small to medium, no API.
- id: reactivity-junction-save-git-set (owner 6, lead)
  what: a save over Changes runs 11 git commands in two loaders, `status --porcelain=v1 -uall` twice.
  evidence (`GIT_TRACE`, hold h4):
    - commands a save: status, diff --cached --numstat -M, worktree list, diff --cached --name-status -M, diff --numstat -M, diff --name-status -M, status again, diff --cached, diff, ls-files -v, log -1;
    - run one after another, ~250 ms of git wall time;
    - a steady save: 2 frames, 220 KB UI, 3.3 MB all threads, 22 processes. The first save of a file costs 4 frames, 841 KB and 17.4 MB, most of it the markdown grammar's first load.
  real app: every save of a file while Junction shows Changes: many times an hour for a user who keeps it open beside the editor.
  expected: 11 → ~7 commands (−36 % git runs a save). The summary's name-status and numstat can come from one status plus the patches. Shorter time to an updated Changes view. Scaling: `-uall` walks the whole working tree twice a save.
  cost: medium, no API.
- id: apps-junction-big-file-revisit (owner 5 with 4; folds into text-junction-first-visit-tokenize)
  what: on large files (bigrepo, ~675 KB each, 590 changed lines) the colour read and tokenize is cancelled by the next key at 400 ms, so nothing is kept and each revisit pays it all again.
  evidence:
    - Commit view walk: 12 first visits 11.9 MB a key, 12 revisits 23.0 MB a key on all threads (base 27.6 / 25.5);
    - 2 git `show` reads (4 processes) a key either way;
    - GCs 19/8/4 per 12 revisits;
    - frames 1.00–1.17 a key: colours landed for at most 2 of 24 visits.
  real app: reading through a big commit's large files (generated code, lock files) in Junction's Commit view: occasional.
  expected: churn bar on all threads. Keep the two blob reads, or the tokenizer's progress, for the next visit (the 128-entry cache holds only finished highlights).
  cost: medium, no API.
- id: dotnet-junction-git-second-process (owner 4, unconfirmed; check first)
  what: each git command adds 2 processes to the app's job.
  evidence:
    - 22 processes for 11 traced commands, with no `run_command` child in the trace;
    - 4 processes a Commit-view key for 2 `show` reads;
    - GitProcess.cs:89–95 starts git with `CreateNoWindow = true` from a GUI process, so the second process is most likely conhost.exe for the hidden console.
  real app: every git run in Junction: 3.5–5 commands a History key, 11 a save, 25–28 at start.
  expected: one process creation fewer per git run (time bar on git-heavy paths), if a console-less start (DETACHED_PROCESS) is confirmed to drop it.
  cost: medium (P/Invoke start); none if the count is not conhost.
- id: gl-reel-paused-driver-threads (owner 3, with mpv-cuda-context-at-rest)
  what: Reel paused after a clip rests at 125 wakes a second, all of them driver threads.
  evidence: after bands.y4m played, 6 s paused: nvoglv64 threads at 98.1, 10.0, 4.0 and 1.0 a second, nvcuda64 at 10.7, UI thread 0. Base the same (125.4). A GL app rests at ~10.
  real app: Reel or Aperture paused on a video: often, for minutes.
  expected: idle bar (125 → ~15 a second) if the interop's hidden GL context and mpv's CUDA context were released or idled while paused.
  cost: medium to large, no API.

REAL WINDOW (the 4K, DISPLAY2, primary, DPI 144 = 150 %, 120 Hz; OS cursor outside every window; machine timer 1 ms, held by another process):
- **Safety:** 4 holds of 129.8, 166.4, 113.8 and 136.5 s; waits 0, 182.7, 516.8 and 142.9 s. 33 launches. 0 foreground takes, 0 nvlddmkm events, 0 screen reads. Two Jellyfin sign-ins as the existing device aperture-r13.
- **Process counts:** the job's process count is 2 per git command. Earlier rounds' "git runs" read from job counts are processes.
- **What headless did not show:** all of round 14's Junction wins are on other threads or in real-window frames (the 67 walks moved 0.0 %).
- **Terminal commit over History** (apps-junction-ref-move-a11y): 1,457 KB with a11y on vs 696 off (−52 %). The frame with the new commit is 1,016 vs 258 KB, and the next frame has 15.1 vs 0 ms of "other". Base is the same (1,468 KB, 13.0 ms).
- **Vesper reader** (TODO.md): unchanged by round 14.
  - Opening costs 1.70 MB and 2 frames; a wheel notch 24.5 KB (15.8 wrapped); a PageDown 337 KB (262 wrapped).
  - The wrapped drag is 279.1 KB a step, identical to the headless figure.
- **Reel:** playback 33 frames a second at ~3 KB UI a second; seeks cost nothing measurable.
- **Aperture library:** keys 0.2–0.3 KB, 1 frame each. Digit2 999 → 880 KB. DXGI local 80–85 MB on both sides.
- **Probe fault (mine):** the new Meridian launch died with 0xC0000409. An IOException in my Script.cs, where the UI thread's and the script thread's appends to START_OUT raced. It is not the app.
- **Meridian typing:** Meridian's start screen has no text field, so the typing scene measured nothing (base drew 1 frame in 17 s). Meridian typing rests on the headless walks: 0 change.
- **Rich text editor:** reachable in the apps only through GitLab flows; not measured in a real window.

WAITING, RE-RANKED:
- **Suite (d):** keep, and make it sharper. Use fractional widths (1 physical px at 150 %): that is where the zh +25 % shows. Add realbig (154k characters, 5,215 → 67.6 KB a step) for the past-the-cache case.
- **Suite (e):** keep. A commit over History is −52 % UI bytes with a11y off.
- **Suite (f):** keep, low. Not re-run with size changes.
- **apps-junction-ref-move-a11y:** stands, unchanged by round 14 (numbers above). Still waits with question 50.
- **apps-vesper-wrapped-drag-bytes:** counted, still worth it.
  - Per step: all 37 realized rows re-made, 3 builds (CommanderApp + 2 Parts), 162 measures, 381 layout visits.
  - 279.1 KB a step: build 71.0, render 202.9 (the new rows' text re-shaped at paint).
  - Keeping the rows and re-measuring clears the churn bar. Occasional action. Owner 1 with 5.
- **mpv-cuda-context-at-rest:** reproduces (nvcuda64 10.7 a second on a raw y4m clip). Folded into gl-reel-paused-driver-threads.
- **text-junction-first-visit-tokenize:** stands. New big-file evidence in apps-junction-big-file-revisit.
- **Non-perf app items:** round 14 changed no file in apps/Aperture, apps/Meridian or Junction's GitLab code; each is a code read, not re-run.
  - aperture-poster-as-hero: the primary-image fallback is still there (ApertureApp.Cards.cs:538–539).
  - aperture-tmdb-size-fallback: RequestModels.cs:409–419 unchanged.
  - junction-narrow-gitlab-glyphs: no GitLab change.
  - meridian-pane-choice-not-persisted: still flipped at MeridianApp.cs:340 and never saved.
  - meridian-min-width-wraps: MinWindowWidth is still 760 (MeridianApp.cs:146).

EXAMINED, NOT WORTH IT:
- **Junction palette open:** a second frame 1 ms after the first, 0 KB (once 7–9 frames of 0 KB): a frame that is owed, no allocation.
- **Vesper reader, "other" share:** 0.01 → 0.10 ms a scroll frame and 0.14 → 0.59 a PageDown, with render down by as much and the frame total equal (likely b62e22cf's hover re-hit).
- **Junction Commit view file switch:** 900–970 KB UI on both sides; these are new rows (headless bigfiles 581 KB).
- **Junction during a build:** 300 outputs under an ignored folder draw 0 frames on both sides (0.70 MB new).
- **Read-ahead tail after a History walk:** 24 processes and 6.6 MB landing during the next 1.5 s; new is still below base in total.

NEEDS THE USER:
- **apps-junction-ref-move-a11y:** unchanged. Decide it with question 50's live check (Narrator, NVDA).
- **Jellyfin:** remove the devices aperture-r12, aperture-r13 (signed in twice more today) and aperture-survey, and revoke the API key, when the work is done.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\8e8ba33b-db33-43fa-885a-cb6b82b7253f\scratchpad\r15-apps\`
- **Publishes:**
  - `aot\` (shared, READY.txt), `aotx\`, `aotx-base\`;
  - sources `start\`, `start-x\src\Script.cs` (round 15 steps);
  - `build-all.ps1`; trees `tree-base` (e432c224), `tree-r15`, `tree-b13`.
- **Real windows, in `rest\`:**
  - scripts `common15.ps1`, `batch15a.ps1`–`batch15d.ps1`; summary `sum15.py`;
  - data `runs\h1..h4.jsonl`, `runs\*.frames.csv`, `runs\*.out.txt`, `runs\h4-jsave-trace.gittrace.txt`;
  - logs `runs-h1.log`–`runs-h4.log`;
  - lock log `locks.log`.
- **Headless:**
  - `walk\src` (+ R15.cs, new filter-only walks files-cjk-drag-zh/ja/big and viewer-wrap-drag-16ms/page); hosts `walk\aot-r15`, `walk\aot-b13`; `mkwalk.ps1`;
  - runners `run.ps1`, `walks.ps1`, `walks2.ps1`, `mk15.py`, `cmp10.py`; results `effect15.txt`, `runs\walks.jsonl`, `runs\walksx.jsonl`.
- **Fixtures:** `fx\` (a copy of round 14's, plus `realbig`), `mkfx15.ps1`.
- **State:** the worktree is clean at 5a212f3e, build servers are shut down, and none of my processes are running. The VesperHost still running is expert 7's (r15-a11y).
