SATISFIED: no

Setup: my worktree at f9c44628, SDK 11.0.100. I applied round 11's `final11.patch` and added counters of my own: elements patched and made, Part builds by name with their bytes, a real-window event log, Vesper's nudge outcome, and env-gated A/B switches (`R12_NOKEPT`, `R12_NOREFS`, `R12_TABREV`, `R12_WATCHVIEW`, `R12_WTEQ`, `R12_SUMEQ`). The headless walks ran on JIT (round 11's walk host plus `R12.cs`). The real windows ran on a NativeAOT host with all five apps. I report counts and bytes only, no timings. Afterwards I reversed everything with `git apply -R final12.patch`: the worktree is clean at f9c44628, the build server is shut down and the fixture server is stopped.

REVIEW: none.
- **6fb8ff07 (kept bodies), builds a switch:** confirmed. With the kept bodies, a tab switch (HTML and JSON tabs) is 4 Part builds plus 1 MeridianApp build, the parts being TitleBar, Rail, Tree and Response once each. With `R12_NOKEPT` it is 6 Part plus 1, because the response part builds 3 times.
  - Per switch, all threads: 28,570 → 228 KB.
  - Frames: 2 → 1. GCs: 35/9/2 → 0/0/0.
  - One dropped signal write per switch (`ResponseDirty` while the search rebinds). This is the documented drop and is harmless with the find bar closed.
  - The TitleBar and Rail builds are older than this commit: see the first candidate.
- **1c7dc920 (map list selection):** a click is 1.00 Part build (the map detail), 0 rows made, 15.5 KB.
- **bf9aa781 and 1d9eaf92 (dropped-rebuild rule, Vesper's one restore try):** no build added.
  - The thumbnail open walk is 0.05 builds a step.
  - Junction's Changes walk still drops 0.95 forced rebuilds a key, which is the documented, deliberate drop, and makes 1.00 build a key.
- **0ed79e8a (find bar count without a build):** Changes caret 1.00 build a key, 305.7 KB.
- **635d6c53..e280bd82 (refs emptied in builds):** no extra build.
  - Meridian History ↔ Requests, `R12_NOREFS` on and off, gives identical counts: 2 top builds and 2 parts each way, 213 and 323 elements made, 279.3 KB.
  - `Ref<T>` is a plain field, not a signal, so emptying one in a build or in OnAfterBuild can never trigger a build (Aperture, Vesper).

CANDIDATES:
- id: reactivity-meridian-tab-switch-chrome
  what: Meridian's request-tab click (Requests.cs:387) and opening an already-open request (MeridianApp.cs:535) call `MarkDirty`, which bumps `_partRevision`. So the title bar and the rail rebuild too, though neither shows anything of the active tab: the title bar shows the workspace name and the environment chip, the rail shows the view. A tab revision read only by the Tree and Response parts would avoid that.
  evidence: headless walk `meridian11 tabswitch-<a>-<b>`, 20 alternating clicks, per click. Prototype `R12_TABREV`: a `_tabRevision` signal read by the Tree and Response parts, plus `base.MarkDirty()`.
  - Base: TitleBar 16.8 KB and Rail 16.5 KB a switch, which is 33.3 of 227.8 KB (14.6 %).
  - Prototype, HTML↔JSON: 227.8 → 192.7 KB a click (−15.4 %). XML↔JSON: 219.5 → 184.4 KB (−16.0 %).
  - Top builds 5 → 3; elements patched 146 → 112, made 149 → 115.
  - Images: the first and the settled frame after a switch differ from base only in the rows a base-against-base run also differs in (workspace name from a random folder name, y15–23; a response-time digit, y54–60).
  real app: Meridian, switching between request tabs (frequent, like browser tabs): −35 KB and −2 builds a switch. A send also calls `MarkDirty` at its start and end (MeridianApp.cs:670), so it likely pays the same 33 KB twice; not measured.
  expected: clears the churn bar (≥10 %) at −15…−16 % a switch.
  cost: small, app only. No API change; pixels unchanged as far as the A/A comparison shows.

- id: reactivity-junction-watch-chrome
  what: a repo watcher event (an editor saving a file in the repo) builds Junction's chrome two or three times for nothing. Three causes:
  - `OnExternalChange` (JunctionApp.cs:713) calls `Rerender`, which bumps `_chromeRevision`, only to start the new generation's summary and worktree loads. Those loaders stay the same objects per path (`KeyedAsyncLoader.For(path)`), so the parts are already subscribed. The comment's "NEW AsyncLoader instances" reasoning does not apply to the chrome.
  - Each worktree reload lands a new list, which compares by reference, so the sidebar always rebuilds.
  - `RepoSummary` is a record struct carrying `Branches` and `Remotes` lists, which also compare by reference, so an unchanged repo still rebuilds all three chrome parts and the view.
  evidence: never seen by any headless walk before. Real window: per event 10 top builds (3 JunctionApp + 7 parts), 300 patched, 533 made, 4.8 MB on all threads (AOT). Reproduced headless with identical counts (`r12 junction-watch`, file created and deleted at the repo root, 1.5 s apart). Per event:

  | | KB on the UI thread | builds |
  |---|---|---|
  | Base | 581.4 (all threads 4,433, 14 git runs) | 10 (Sidebar 3× = 129 KB, Toolbar 2× = 101, TitleBar 2× = 48) |
  | `R12_WATCHVIEW` (start the loads, `RerenderView`) | 457.6 (−21 %) | 7 |
  | + `R12_WTEQ` (an equal worktree list hands back the old one) | 412.2 (−29 %) | 6 |
  | + `R12_SUMEQ` (summary compared by content) | 415.9 (the summary really changed) | 6 |
  | README's write time touched, content the same: base | 578.2 | 10 |
  | Same, all three prototypes | 275.2 (−52 %) | 2, no part |
  | Changes view (Digit4): base | 496.9 | 11 |
  | Changes view: all three | 315.7 (−36 %) | 7 |

  The final frame after the walk is byte-identical in two base runs and two prototype runs (PNG SHA256 `382B69ED…`).
  real app: Junction open beside an editor. Every save (debounced 300 ms), every build-output write and every checkout pays this, usually with Junction in the background. −166…−303 KB and −4…−8 builds an event.
  expected: churn bar, −29 % (content changes) to −52 % (unchanged repo) an event. Also scaling: the sidebar builds per workspace repo row.
  cost: small to medium, app only (three spots). Check the GitLab loaders the chrome reads: if they key on `DataGen`, they must also start from `OnExternalChange`. `ChromePartTests` covers the chrome's inputs.
  Lead for expert 8: 14 git runs (251 K characters) an event in History and 19 (391 K) in Changes. A working-tree-only event still re-reads the log (×2) and the selected commit's diff-tree (×3), plus `remote` ×2 and `for-each-ref` ×2.

REAL WINDOW:
- **Setup:** 1440p at scale 1.0, 200.01 Hz (as the app reports them). One hold of 132.5 s, after waiting 756.5 s for the lock. Five launches (Junction, Vesper, Meridian, Aperture, Reel), each 23–35 s.
  - 0 foreground takes: driver result "ok" ×5, the in-process guard never fired, the CBT hook refused 0 activations, 0 `nvlddmkm` events.
  - GL renderer for all five (Aperture and Reel normally use Vulkan), so no Vulkan or F16 swapchain was resized.
  - Not measurable: a real DPI change (needs a system setting) and real activation (SDL checks `GetForegroundWindow`). So I pushed SDL's focus and display events in-process, and posted `WM_DISPLAYCHANGE`.
- **The one build a real window triggered that the headless walk never saw:** Junction's watcher, 10 builds an event (see the second candidate). Vesper has no folder watcher: 0 builds for a create, an append and a delete.
- **Rest:** 0 builds and 0 frames over 3 s, and again over the final 2 s, in all five apps.
- **Resize (72 steps of `SetWindowPos` with `SWP_NOACTIVATE`, ±240 px wide, ±120 px high):** the same builds as headless at the same breakpoints.

  | App | Real window | Headless |
  |---|---|---|
  | Junction | 16 (4 app + 12 parts) | 16 |
  | Meridian | 8 | 8 |
  | Aperture | 2 | 2 |
  | Reel | 0 | 0 |
  | Vesper | 2 app + 22 parts (2 path-trail breakpoints crossed) | 4 + 44 (4 crossed) |

  `Resized` fires 108 times for 72 steps (72 RESIZED, 72 PIXEL_SIZE_CHANGED, 36 EXPOSED), against 72 headless, and the real window draws 2.1–2.2 frames a step against 1. These are cheap handler calls, no builds; the frames are for experts 1 and 3.
- **Pushed events:** focus gained and lost ×3: `Activated` raised 3 times, 0 builds. Display changed, display scale, content scale, and the posted `WM_DISPLAYCHANGE`: 0 builds, 0 frames.
- **Minimize and restore (`SW_SHOWMINNOACTIVE` / `SW_SHOWNOACTIVATE`):** 0 builds, 2–3 frames.
- **The user's pointer:** a mouse enter at show and at restore (1 input event each), 0 builds.
- **Vesper navigation, keys pushed into SDL's queue:** 3.0 builds a key (121 builds for 40 keys at 33 ms: the key, the listing landing, the Foot part) against headless 4.0. The difference is the stale nudge: see WAITING.

WAITING, RE-RANKED:
- **vesper-folder-nudge-stale: not worth it, close.**
  - Real window: 52 nudges fired over 50 keys. Only 1 found a load in flight (stale): 1 of 40 keys at 33 ms key repeat, 0 of 10 at 300 ms. That is about 0.025 view builds a key (about 0.8 of 282 KB, 0.3 %).
  - Headless counts 59 of 60 as stale: the posted nudge runs in the pump right after the next key has started its load. For the guidelines: headless walks overcount timer-posted "if still loading" rebuilds.
- **aperture-cursor-guard-textarea:** code unchanged (ApertureApp.cs:554 still tests only `TextInputElement`). Not perf, 0 B. Leave it for the next Aperture keyboard change.

EXAMINED, NOT WORTH IT:
- **Resize rebuilds at breakpoints** (Junction app + 3 parts, Vesper app + 11 parts, Meridian app + 3 parts): 2–4 crossings per 48 width steps, and each crossing changes what is shown. Height steps: 0 builds.
- **Vesper's resize rebuilding all its parts at a path-trail breakpoint:** the parts' bytes are inside the app build (about 160 elements a crossing); rare.
- **Junction start-up:** 19 builds in the 2 s after the first frame (loaders landing). The headless start-up shows the same; nothing real-window specific.
- **Meridian dropped `ResponseDirty` write (1.0 a tab switch):** documented and harmless with the bar closed. With the bar open, 0ed79e8a's version check fixes the count.

NEEDS THE USER: none. Optional framework alternative to the Junction app fix: an `AsyncLoader` constructor taking an `IEqualityComparer<T>`, so an equal reload does not notify. That is new public API, and not needed for either candidate.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\0bab04ae-e7a9-44cb-b4da-96d849a107f6\scratchpad\r12-reactivity\`
- `final12.patch`: every probe edit and prototype, round 11's patch included. Apply it from the worktree with the full path.
- `walk\src\` (round 11's host, plus `R11.cs` from r11b6 and the new `R12.cs`: resize, viewcycle, folder-inout, junction-watch), `walk\after\WalkHost.csproj`, `batch.ps1`, `run.ps1`, `srv.py` with `www\` (fixtures served on port 8779), `fx\` (copied fixtures, port rewritten to 8779).
- Outputs: `runs\after-jit-r1…r8-*.out.txt`, `runs\walks-r*.jsonl`; images `png\` (`base`, `base2`, `tabrev`, `wbase1/2`, `wproto`, `wproto3`).
- Real window: `rw\src\Program.cs`, `rw\proj\RwHost.csproj`, `rw\pub.ps1`, `rw\rwbatch.ps1` (lock, guard, driver), `rw\summ.py`, `rw\runs\h1-*.log` and `h1-*.summ.txt`, `rw\holds.log`, `rw\rwbatch.log`.
