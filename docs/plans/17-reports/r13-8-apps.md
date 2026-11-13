SATISFIED: no

**Method.**
- **Shared publishes:** the five apps were published on NativeAOT at 2ced4e26 into `r13-apps\aot\<App>\` and READY.txt was written first. All exited 0. The smoke test (hold h1) passed: 5 launches, first frame drawn, closed by WM_CLOSE.
- **Scripted publish:** `r13-apps\aotx\` adds three things to round 12's Script.cs:
  - resizes run on the window's own thread (`drag`/`dragrel`, 16 ms apart);
  - HultUI's pointer is placed in-process (`ptr`, `sweep`);
  - every SetWindowPos records its UI-thread and all-thread bytes.
- **Driver:** Driver.cs plus Driver13.cs record each thread's wait state and wait reason. They also take a stack sample (suspend the thread for a moment, then scan RIP and the stack against the exports of the system DLLs), and read the window's monitor, DPI and whether the OS cursor is inside it (GetCursorPos, never the screen).
- **Walks:** headless AOT walks on the same day, round 12's walk host at f9c44628 (`aot-now12`) against 2ced4e26, same walk source, sides alternated. Round 12's recorded rows can't be the base: they came from an earlier build of the walk host (`aot-now`), and Meridian rows differ by up to −97 % from a rebuild of the same commit.

REVIEW: one small objection.
- **053a35ef and b6c6eb6f** make Junction's `SummaryFor` allocate more on every call, whether the loader's key hits or not:
  - 053a35ef adds a captured variable (`shown`);
  - b6c6eb6f adds a second closure (`onLoaded`) and a dictionary read.
  - Bisect, History Down UI KB a key: 3f93d54c 28.3 → 053a35ef 28.6 → b6c6eb6f 29.1 → a6bfb1fc 29.1 → 2ced4e26 29.3. The build bucket goes 13.1 → 13.8; the last +0.2 is render in the text commits.
  - Below every bar (History +3.4 %, commit walk +0.4 %), but every Junction build pays it.
- **Did round 12 help the real apps? Yes.**
  - **Headless walks:** 67 walks, geometric mean of UI KB after/base 1.003; summed −0.2 %. Builds per key are equal everywhere but one walk.
  - **Meridian tab switch HTML↔JSON:** 171.7 → 137.9 KB (−19.7 %), builds 5 → 3.
  - **Rich text editor:** review-comment typing 3.4 KB a key (+1.5 %, about 50 B), review Enter +1.4 %.
  - **Timing walks, not regressions:** Vesper thumbs +12.5 % (builds 0.05 → 0.08) and transfer-frames +10.1 %.
  - **Real windows, commit file walk:** 1.00 frame a key with HultUI's pointer resting on the diff (round 12: 2.00). UI bytes 829–897 KB a key (round 12 survey: 977).
  - **Real windows, Vesper navigation:** 1.23–1.30 frames a navigation against round 12's 2.37 (100 ms pace). UI bytes equal: 344.5 → 343.1 KB.
  - **Real windows, Vulkan at rest:** 110–118 wakes a second (round 12: 207).

CANDIDATES:
- id: apps-vesper-nav-uia-share (owner 7, lead)
  what: in a real window, the UIA bridge takes about 38 % of a Vesper folder navigation's frame time, and a third of a drag step's. Headless runs can't show it.
  evidence:
  - Real AOT window, NavigateTo ×30 at 100 ms, pointer resting on the list. "Other" is 38.5–40.1 % of the six phases with a11y on, 1.1 % with `HULTUI_A11Y=0`.
  - UI KB a navigation 343.1 vs 328.2 (only −4 %); frames 1.30 both ways.
  - Vesper drag (60 steps, 16 ms apart): other 32–34 % vs 0.5 %, bytes identical (11.8 / 14.1 KB a step). So the cost is time in raises and provider calls, not allocation.
  - Junction and Meridian drags: other 0.1–0.5 % with a11y on.
  real app: Vesper, opening and leaving folders (Enter, Backspace): constantly.
  expected: time bar if the bridge's per-navigation work (structure events, provider calls answered on the UI thread) can be cut. Count raises and calls a navigation first, with round 12 step 3's probe.
  cost: unknown, no API impact expected.
- id: apps-junction-drag-a11y-bytes (owner 7, lead; evidence for Waiting a11y-structural-walk-during-resize)
  what: during a drag, the bridge's structural walks cost a third of Junction History's UI bytes for each step.
  evidence: History, 30 + 30 SetWindowPos steps on the UI thread, 16 ms apart, −360/−240 px and back. UI KB a step is 27.1 / 24.9 with a11y on against 16.2 / 17.3 off (−35 %). Frames 1.00 a step both ways. Meridian 5.1 / 4.1; Vesper unchanged on or off.
  real app: resizing Junction's window by dragging its edge: occasional.
  expected: churn bar (−35 % a drag step) if the structural walks' bounds also wait for the rest.
  cost: medium.
- id: apps-junction-summaryfor-closures (owner 6; the REVIEW objection)
  what: build the summary loader's closures and key only when the key changed.
  evidence: the bisect in REVIEW. `AsyncLoader.Load(object key, Func<T>, Action<T>?)` is the only overload (AsyncLoader.cs:103); `SummaryFor` has 26 call sites.
  real app: every Junction build; History Down is 29.3 KB a key.
  expected: −0.8 KB a build (−2.9 % a History key), more if the key string and the first closure go too. **Below the 10 % churn bar on its own:** bundle it with another Junction step or decline.
  cost: small if the check stays in the app; a state overload on AsyncLoader would be public API (NEEDS THE USER).

REAL WINDOW (the 4K primary, 3840×2160 at 150 % (DPI 144), 120 Hz; RTX 4080):
- **Holds:** 3 holds of 145.5 / 137.6 / 64.3 s, waits 0 / 765 / 338 s; 28 launches.
- **Safety:** 0 foreground takes (and none handed back), 0 activations refused, 0 nvlddmkm events, 0 screen reads. The OS cursor was outside every window in h2 and h3. One Jellyfin sign-in, device "aperture-r13".
- **Lead: Vulkan rest wakes by thread (with experts 3 and 4).** Reel 110–118 and Aperture 115 wakes a second, excluding the probe's own guard thread. Nothing in HultUI or .NET is among them:
  - all four waking threads are NVIDIA driver threads (nvoglv64.dll, NVIDIA's Vulkan driver), all started at nvoglv64.dll+A3D280, all waiting in WaitForSingleObjectEx called from inside nvoglv64;
  - they wake 93.6–100.6, 11–12.5, 4–6.8 and 1.0–1.5 times a second;
  - UI thread 0; .NET thread-pool threads 0–0.2; CRYPT32 0.1.
  - **The ~98/s thread does not move with:** MAILBOX 99.0, IMMEDIATE 100.6, implicit layers off (`VK_LOADER_LAYERS_DISABLE=~implicit~`, so not OBS's layer) 99.1, a11y off 98.6, SDL's timer resolution off 98.2.
  - **FIFO makes it worse:** it adds DXGI threads (nvwgf2umx) at 99.6 and 27.8 a second, 252 in total.
  - **Timer resolution:** nvoglv64.dll imports timeBeginPeriod. With SDL's hint off, the probe's 5 ms guard thread still wakes 177 times a second in Reel but 67.5 in Junction (GL), so in the Vulkan apps the driver itself holds 1 ms. A ~10 ms wait at 1 ms resolution would fit ~100 wakes a second.
  - **GL apps:** one nvoglv64 thread at 11.9–12.0 a second.
  - **What's left for expert 3:** does the ~98/s thread belong to the swapchain or to the device (device-only versus swapchain in the probe host)?
  - Once in h1, Aperture's UI thread woke 97.8 times a second at rest with 1 frame. It did not come back in h2 (0, cursor outside), probably OS input.
- **At rest with HultUI's pointer resting:** one hover frame, then 0 frames in every app. 0 frames at rest in all 13 h1 launches. Working set at +3 s: Aperture 112, Reel 107, Junction 112 MB; 0 GCs.
- **Shares, real against headless (same commit):**
  - **Commit file walk (bigrepo):**
    - a11y on: render 46–50 %, flush 19–20 %, present 4–6 %, other 21–25 %; 829–897 KB a key.
    - a11y off: render 53–63 %, flush 24–31 %, other 1.5–2 %; 554–620 KB.
    - headless: render 96 %, 492–556 KB.
    - So the bridge is still about 275 KB a key (33 %): the walk round 12 decided to keep (fd514631).
  - **History Down:** render 49 %, flush 37–40 %, present 8–10 %; headless render 99 %. 1.10 frames a key with the pointer on the list against 1.00 without: the hover tint following the scroll.
  - **Vesper Down:** render 49–51 %, flush 38–40 %, present 8–10 %; 5.0 KB a key against 3.1 headless.
  - **Aperture library Down:** 1.90 frames a key. A key that brings a new row in draws 5 frames 8 ms apart (posters landing: 34–67 KB UI and 90–338 KB all-thread each). Headless: 0.5 builds a key, 28.6 KB.
  - **Drags:** 0.97–1.03 frames a step in every app. The largest share is flush on GL (Junction 66–69 %, Meridian 80 %) and present on Vulkan (Reel 58 %, Aperture 31–71 %). UI KB a step: Junction 25–27, Vesper 12–14, Meridian 4–5, Aperture 5–7, Reel 3.2–3.4.
- **Pointer sweeps (for expert 1's hover-move-frames-nothing-shows):** 60 moves 16 ms apart; frames are 0.1–0.8 KB each.
  - Junction diff 33 frames (0.55 a move);
  - Junction History 49 (0.82);
  - Vesper list 48 (0.80);
  - Meridian sidebar, fresh profile 0;
  - Reel 7;
  - Aperture library 10.
  - Of these, only History (JunctionApp.History.cs:1097) and the Changes list use `HoverHighlight`. Whether the diff's and Vesper's frames show anything is for expert 1 to check.

WAITING, RE-RANKED:
- **Suite scenarios:**
  - (a) the idle-Vulkan all-thread wakes row: keep, low. Only as a gated UiTests guard for round 12 step 5 (116 against 212 a second); what's left is driver-owned and specific to this machine.
  - (b) frames per key under a real loop: close. DiffFrameCountTests covers it with a pointer, and the real window shows 1.00 a key.
  - (c) a shell-shaped resize: keep only as expert 7's measure for apps-junction-drag-a11y-bytes.
- **junction-changes-leads:** not perf. It still reproduces from the code: round 12's a11y commits (5d973076, 67fc0884, a6bfb1fc) touched no selection pattern.
- **junction-review-old-path:** reproduces. Every GitFileDiff is built with OldPath null (JunctionApp.Changes.cs:122, 124; GitRepository.cs:496, 511).
- **junction-leads (remaining):** reproduces. No test calls `SelectConflict` (JunctionApp.Changes.cs:1477).
- **aperture-poster-as-hero:** round 12 changed no Aperture file; the code is as before (ApertureApp.Cards.cs:538–539).
- **aperture-tmdb-size-fallback:** unchanged (RequestModels.cs:413–419).
- **junction-narrow-gitlab-glyphs:** no GitLab file changed; not exercised.
- **meridian-pane-choice-not-persisted:** reproduces (MeridianApp.cs:148; flipped at :340, never saved).
- **meridian-min-width-wraps:** 46fccd88 keeps the Windows floor at 448 px and the breakpoint at 1320, so round 10's wrap at 760×540 stands on Windows. Not re-shot (no captures).

EXAMINED, NOT WORTH IT:
- **SDL's 1 ms timer resolution at rest (GL apps):**
  - SDL3 3.5.0 holds timeBeginPeriod(1) for the whole session. `SDL_TIMER_RESOLUTION=0` provably applies (guard 178 → 67.5 a second).
  - No app thread changes: Junction at rest has 0 app wakes; its driver thread goes 12.0 → 11.9.
  - Another process on this desktop already holds 1.0 ms (NtQueryTimerResolution reads 1.0 ms with no HultUI app open), so nothing is gained here.
  - Only a quiet laptop could gain. That is a lead for expert 3 if anyone measures it there, and the frame pacing of the first frame after a rest would have to be checked.
- **The Vulkan rest wakes:** NVIDIA driver threads; see REAL WINDOW.
- **History's 1.10 frames a key with a pointer:** the hover tint really moves.
- **Aperture's 5-frame bursts:** posters landing, by design.

NEEDS THE USER:
- **Jellyfin:** one new device, "aperture-r13". Remove it with the "aperture-survey" and "aperture-r12" devices and revoke the API key when the work is done.
- **apps-junction-summaryfor-closures:** only if it takes the AsyncLoader route, a `Load` overload that takes a state argument would be public API.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\17c11f5e-9de0-4477-8f58-0d0a5e006605\scratchpad\r13-apps\`
- **Publishes:** `aot\` (shared, READY.txt) and `aotx\` (scripted); sources in `start\` and `start-x\` (`src\Script.cs`, `src\Program.cs`, `pub.ps1`).
- **Real-window probes, in `rest\`:**
  - drivers: `Driver.cs`, `Driver13.cs`, `common13.ps1`, `batch13a.ps1` (h1), `batch13b.ps1` (h2, h3);
  - analysis: `wakes13.py`, `frames13.py`;
  - data: `runs\h1..h3.jsonl`, `runs\h*-stacks.txt`, `runs\*.frames.csv`, `runs\h2-frames.txt`.
- **Lock log:** `locks.log`.
- **Walks:**
  - scripts: `allwalks.ps1`, `walks.ps1`, `walks2.ps1`, `run.ps1`, `tree.ps1`, `aotpub.ps1`, `bisect13.ps1`, `bisect13b.ps1`;
  - analysis: `mk13.py`, `mkdrift.py`, `cmp10.py`;
  - data: `runs\walks.jsonl`, `effect13.txt`, `drift.txt`, `runs\bisect.jsonl`, `runs\r12-*.frames.csv` (headless scenes).
- **Trees and hosts:** `tree-r13`, `tree-xb6c6eb6f`, `tree-xa6bfb1fc`, `tree-x3f93d54c`, `tree-x053a35ef`; walk hosts `walk\aot-*`; fixtures `fx\`.
- **State left:** the fixture server is stopped, build servers are shut down, and the worktree is clean at 2ced4e26.
