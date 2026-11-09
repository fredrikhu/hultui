SATISFIED: no

Measured at ea3e57ab, detached. Headless, JIT, Release, pinned to P-cores. Each step is a key (or click) and two frames. Bytes come from `GetAllocatedBytesForCurrentThread` in round 8's buckets (`apply9.py` = `apply8.py` retargeted; all anchors still match). No timings. Prototypes were gated by environment variables in the same binary. Everything was reversed with `git apply -R final9.patch` and the probe file deleted; the worktree is clean.

REVIEW:
- **41f48bd3** (main's merge 3a15b5b1: "requests, folders and environments are renamed and deleted in the app") makes every full Meridian build dearer.
  - Each request row in the tree gained a "⋯" `RowActionsButton` and a `RowContextMenu` (MeridianApp.Requests.cs:185–190). Each folder row gained three buttons.
  - The tree part is rebuilt on every `MarkDirty`: opening a request, a send (two: MeridianApp.cs:603 and :635), a save, each toast.
  - Tree part bytes per full build, with the new row actions gated off → on: 25 requests 142.0 → 202.3 KB (+42 %); 200 requests 920.8 → 1,389.7 KB (+51 %), about +2.35 KB per request.
  - This becomes candidate apps-meridian-tree-rows-kept, which removes it and more.
- Round 8's own commits: no objection.
  - **80f31170 `Restyle`:** `VisualState` is a struct, so a restyle allocates nothing unless the element has box shadows. It cancels in-flight tweens and lays the hover state back on.
  - **4048e35b Vesper rings:** `SettleRing` runs in `BeginFit` before every pane build. `_ringMoved` holds at most the two moved cells. Rows made in a frame read the live cursor (`ThumbCursor`). Thumbnail tiles use `KeptElements` with no `Begin`, pruned when the count doubles.
  - **d366505b / 24d63eb5 Aperture rings:** the ring is an `Outline`, which hover styles do not snapshot, so no `Restyle` is needed. The focus callback is one delegate per build.
  - **7a12f5a9 Home shelves:** the kept shelf's menu captures the old `all`, but it only reads the row's place and count from it, and both are in `HomeShelfState`. `MoveHomeRow` reads `AvailableHomeRows()` fresh.
  - **2e10b549 picture addresses:** the memo is bounded at 1,024 entries.
  - **ad8e6fbd / 4960997d landing coalescing:** each flag is cleared with `Interlocked.Exchange` (a full fence) before the build reads the cache, so a later landing still posts a build.
  - **62a67257 Meridian History:** the archive part reads `_partRevision` with `Peek` and writes its own signal only while the list and the archive are hosted.
  - **e7d10c63, 4fc94836 (framework):** `TickSelectionEdgeScroll` reads the clock once a frame, allocates nothing, and asks for frames only while a drag is past the edge. PaneSplitter has no reactivity change.

CANDIDATES:
- id: apps-meridian-typing-builds-nothing
  what: A character in an Environments value cell, a header field or the body builds nothing when nothing else on screen moves, as the URL box already does (e0ace9c9).
  evidence:
  - **Environments cell** (MeridianApp.Environments.cs:341: `doc.SetValue(key, v); MarkViewDirty()`) rebuilds the whole compare table on every character, one `TextInput` per cell, all patched:

    | Table (envs × keys, secret keys) | KB a key | Elements made a key | Keyring Gets a key |
    |---|---|---|---|
    | 3 × 12, 2 secret | 312.3 | 264 | 10 |
    | 3 × 40, 4 secret | 842.9 | 676 | 20 |
    | 5 × 40, 4 secret | 1,167.2 | 916 | 36 |

    - With a reader installed the bytes are the same (312.6 / 843.4 / 1,167.7).
    - The keyring Gets come from `BuildSecretCell` → `Secrets.Resolve` (:360), on every build. On Windows each Get is a `CredReadW` with no memo (Keyring.cs:119); Linux and macOS memoize (`KeyringMemo`, SecretServiceKeyring.cs:106).
    - Prototype: build only when `doc.Edited` flips. That is the dot in the env list and the "N unsaved" footer; `Edited` only goes back to false on save. Result: 312.3 → 2.3 KB, 842.9 → 5.4, 1,167.2 → 5.4; builds 1 → 0; keyring Gets 0. With the reader: 2.8 / 5.9 / 5.9.
  - **Header value** (Requests.cs:573–578 → `DocEdited` → `MarkViewDirty`): 65.4 KB a key, 1 build, 55 elements.
  - **Body** (Requests.cs:678–683): 59.4 KB a key, 1 build.
  - Prototype for both, with the URL box's own check (`tab.Dirty == wasDirty && UrlCountsOf(tab) == _urlCountsShown`): header 65.4 → 2.2 KB, body 59.4 → 3.9; builds 1 → 0.
  expected: churn −97…−99.5 % a key in the Environments table, which grows O(envs × keys) today and is flat after. Header −97 %, body −93 %.
  cost: small, app (3 sites). No API change, same pixels. Guideline: a field that already shows what was typed builds only when something else on screen moves.

- id: apps-meridian-tree-rows-kept
  what: Hand back each unchanged request row of the tree part with `KeptElements`. Key it by path. The state is method, display name, active, dirty, the first produces entry, the uses count and the depth; the display scale should go in the state or clear the cache.
  evidence:
  - A full build (`MarkDirty`: every part):

    | Requests | KB a build | Tree part | Elements made |
    |---|---|---|---|
    | 25 | 321.1 | 202.3 | 286 |
    | 200 | 1,579.2 | 1,389.7 (88 %) | 1,399 |

  - Prototype (request rows only; folder rows still made): 25 requests 321.1 → 153.6 KB (−52 %); 200 requests 1,579.2 → 236.1 (−85 %); tree part 1,389.7 → 118.0; elements made 1,399 → 199.
  - A Meridian toast (flash to gone) at 25 requests drops 654.5 → 315.8 KB.
  - A send makes 2 full builds (start and end). At 200 requests that is about 3.2 MB a send, about 0.47 MB with rows kept (derived from the full-build numbers; a send itself was not run).
  - The row closures are safe to keep: they capture the path, and `RequestActions(path)` reads live state when the menu opens.
  expected: churn −52 % (25 requests) to −85 % (200 requests) per full build, on every send, open, save and toast. It also removes O(requests) work per full build (scaling bar). The 41f48bd3 regression goes with it.
  cost: medium, app (RequestRow, maybe folder rows). No API change, same pixels.

- id: vesper-brief-scroll-before-build (was Waiting brief-columns-lag-a-key)
  what: A Brief key that scrolls sets the scroll before its build (`ScrollBriefToCursor` in `MoveCursor` before `MarkViewDirty`), so the build makes the columns for the new scroll.
  evidence:
  - Today the build runs at the old scroll, then `OnAfterBuild` scrolls (CommanderApp.cs:842; `BriefGeometry` reads `ScrollOffsetX`, Views.cs:612). The next key sees the fit moved and builds again.
  - 400 files, 1240×820, 28 rows a column. Prototype as described above:

    | Walk | Before | After |
    |---|---|---|
    | Down ×402 | 29 builds; 14.0 KB a key | 18 builds (−38 %); 11.2 KB (−20 %) |
    | PageDown ×6 | 5 of 6 keys leave blank columns in view (8 columns), until the next key | 0 blank; 325 → 297 KB a key (−9 %) |
    | Right ×14 | flat | flat |

  - The KB figures leave out the probe's own column check, 7.8 KB on both sides.
  expected: churn −20 % on a Brief Down walk, builds −38 %. Correctness: no blank columns after PageDown.
  cost: small, app (1 line). Same pixels except the blank columns. For expert 1: a `ScrollEvent` fired on a programmatic scroll would fix the blank columns but not the extra build.

- id: apps-meridian-discovery-row-click
  what: Selecting an endpoint in Discovery hands back unchanged map rows (`KeptElements` keyed by `endpoint.Key`, state = selected and the row's inputs). It builds the view only (`MarkViewDirty`): no part shows the selection.
  evidence:
  - `EndpointRow` (MeridianApp.Discovery.cs:375–414) calls `MarkDirty()` on a click.
  - 60 endpoints: 347.1 KB a click, 327 elements made, title bar and rail parts rebuilt (33 KB).
  - 300 endpoints: 1,398.2 KB a click, 1,287 elements made.
  expected: removes O(endpoints) per click (scaling bar). Not prototyped; by the tree prototype's ratio, ≥ −80 % a click.
  cost: small–medium, app. A click, not a key: rank low.

- id: apps-toast-gone-view-only
  what: When a toast goes away (`IsShowing` false at the callback), build the view only. The toast appearing keeps the full build, because callers rely on it (Discovery.cs:283: "builds the window, the tree too").
  evidence: `new ToastHost(MarkDirty)` (ApertureApp.cs:106, MeridianApp.cs:177), so both builds of a toast rebuild every part.
  - Aperture Home: 2 app builds and 4 part builds, 128.3 KB from flash to gone; rail 60.2 + title bar 33.3 = 73 % of it.
  - Meridian, 25 requests: 654.5 KB; parts 490.4 (tree 404.5).
  - Idle without a toast: 0 B, 0 frames.
  expected: about −36 % a toast in Aperture and −37 % in Meridian.
  cost: small, app (a lambda per app). A one-off event: rank last.

WAITING, RE-RANKED:
- brief-columns-lag-a-key: worth it, now candidate vesper-brief-scroll-before-build: builds −38 % on a Down walk, −20 % bytes, 8 blank columns after PageDown gone.
- aperture-leads (remaining):
  - Toasts are now measured: 2 builds and 128.3 KB from flash to gone, 73 % of it rebuilt parts → apps-toast-gone-view-only (low).
  - Grid Down's 29.07 KB a key is the row of cards scrolling in, which has to be made. Close it.
  - Discover Down walking a shelf's own pages is a UX note, not perf.

EXAMINED, NOT WORTH IT:
- Meridian prompt dialogs (new request, folder or collection; rename; secret values; Discover URL): 0.59 KB a character, 0 builds. `OnTextChanged` only writes `_dialogValue`, and the check runs on OK.
- Web preview (180d2579): `NavigateToString` runs once per response (reference check, WebPreview.cs:95). The Request tab (967ce1ac) is built inside the response part, only while it is the tab shown (code read).
- Windows keyring memo on its own: the cell no-build fix leaves 0 keyring Gets while typing. The rest happen once per Environments build (10–36 Gets), and their cost is time I can't measure here, not bytes. Fold it into the first candidate if the build agent sees the time.

NEEDS THE USER:
- none.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\c484f5ac-8f22-48dd-b669-13c24b1e7889\scratchpad\r9-reactivity\`
- Probe: `apply9.py`, `R6Probe.cs`.
- Driver: `drv\Program.cs`, `drv\R9Drv.csproj`. Modes: menv, mfields, mdialog, mdisc, mtree, mtoast, vbriefwalk, atoast.
- `final9.patch`: the probe and the prototypes, gated by R9_ENVFIX (env cell, header, body), R9_TREEKEEP, R9_NOROWACTIONS, R9_BRIEFFIX.
- Outputs: `run-menv.txt`, `run-menv-fix.txt`, `run-menv-a11y.txt`, `run-menv-a11y-fix.txt`, `run-mfields-dialog-disc.txt`, `run-mfields-fix.txt`, `run-mdisc.txt`, `run-mtree.txt`, `run-mtree-noactions.txt`, `run-mtree-keep.txt`, `run-mtoast.txt`, `run-vbrief-atoast.txt`.
- Review diffs: `show-4048e35b.diff`, `show-24d63eb5.diff`, `show-d366505b-cursor.diff`.
