SATISFIED: no

The worktree is clean at ea3e57ab. The probe tests I added to tests/Meridian.Tests and tests/Aperture.Tests were deleted, and a copy of the Meridian probe is in the scratchpad. All numbers are from JIT, headless, at 1440×900, unless a line says otherwise. Timings are not reported, apart from one per-call cost that is marked as indicative.

REVIEW:
- **Suite bytes: no regression.** I ran full captures of ea3e57ab on JIT and on AOT and compared their allocKB rows with round 8's last full captures (JIT: 48ed3a35-richtext-blocks-after; AOT: 61a82eba-name-without-string-aot-after). Threshold: more than 0.2 KB and more than 2 %.
  - JIT: 0 of 214 rows moved.
  - AOT: 0 rows rose. 5 fell: step 3's rich text rows (enter −48 %, open −31 %, resize −29 %) and two rows of 0.2–0.3 KB.
  - JIT and AOT agree within 1 KB on 202 of 214 rows, and within 4 % on all of them.
  - Steady rows are unchanged: 0.0 KB for tweens, spinners, hover across 1,000 rows and the terminal frames; idle 0.2 KB per 5 s.
- **Objection, f276bb38 + 417ef713 + 967ce1ac (main's merge): the Windows keyring is asked once per secret on every build.** Each ask is a CredReadW call to another process. 417ef713 gave the Linux and macOS stores a memo (`KeyringMemo`) because "the table asks at every build", but Windows got none. f276bb38 made `Secrets.Resolve` ask up to twice per secret (the environment's own value, then the shared one). 967ce1ac's Request tab resolves every environment × every secret on each response-pane build (`SecretValuesInUse`). Numbers are in the first candidate.
- **Objection, 12d0392e: a large spec is read as a string.** `OpenApi.FullText` reads the spill file with `File.ReadAllText` (2 B a character), and `JsonDocument.Parse(string)` then rents a UTF-8 copy of 3 B a character, rounded up to a power of two. That rented array stays in `ArrayPool.Shared` afterwards. Numbers are in the third candidate.
- **The other commits I was asked about hold up:**
  - **80f31170 `Restyle`.** It keeps no delegate. Vesper passes static cached lambdas (CommanderApp.Views.cs:442-443). The base look is a `VisualState?` struct inside `InteractionStyles`, so it adds no heap object. `Capture` copies a list only for a box that has shadows.
  - **Vesper `_ringMoved`.** It holds at most the two cells a walk is between, and each pane build clears it.
  - **`KeptElements` (Vesper, Aperture).** It is still bounded at max(64, 2 × live) entries per instance. Aperture's `_homeCards` and `_discoverCards` keep one instance per row key ever shown, and remove none. Those keys come from the server's shelves, so this is bounded.
  - **2e10b549 artwork memo.** At most 1,024 entries, cleared on a new data generation. The `Uri` it keeps is the poster cache's key string, so the memo itself holds little.
  - **ad8e6fbd, 4960997d `Interlocked` flags.** 0 B. They also make one delegate per post where every landing made one before.
  - **f35c3df9 `ItemIsEnabled`.** Every implementer is a class (`ContextMenuElement`, `VirtualListElement<T>`), so there is no boxing. It adds one interface call per item in loops that were already O(items).
  - **61a82eba `NameIs`.** It compares spans and allocates 0 B.
  - **bc007ae8, a10f21b1, 7aaae4c8 (rich text).** The paragraph shares the layout's exact `TextLine[]`, and the boxes are made lazily and cached with `??=`. No new retention.
  - **62a67257 History `Part`.** The part's closure is made once. A key makes 0 closures.
  - **e7d10c63 (selection in scrolled views).** The per-frame tick reads the clock (0 B) and returns at once when no selection drag is held. Each pointer move does O(depth) work. `_selectionScroll` is cleared on release.
  - **4fc94836 `PaneSplitter`.** It adds a field write per build and no allocation.

CANDIDATES:
- id: dotnet-meridian-windows-keyring-memo
  what: Give `WindowsCredentialKeyring` the same `KeyringMemo` the Linux and macOS stores have, so a build that resolves secrets makes no CredReadW calls after the first build.
  evidence:
  - **Env table keystroke.** Typing in a plain cell of the Environments table asks the keyring 22 times a key with 4 secrets × 3 environments, and 66 times with 12 × 3. This is a counting keyring inside the real app.
  - **Response pane drag.** Dragging the pane with the Request tab shown asks 59.4 times a frame (12 × 3). With the Body tab shown it asks 0 times.
  - **Requests view.** A full build asks 6 times (4 secrets) or 18 times (12 secrets), for the URL chips.
  - **Cost of one call.** A Windows miss is 168 B (the target string) and about 25 µs (indicative only; the desk was shared). A hit adds the 96 B box from `Marshal.PtrToStructure<Credential>` plus the value string.
  - Code: Keyring.cs:119-133; MeridianApp.Environments.cs:360; MeridianApp.Response.cs:599/680; MeridianApp.cs:554.
  expected:
  - **Scaling bar.** It removes O(environments × secrets) cross-process calls per keystroke (22–66 → 0) and per drag frame (59 → 0). That is about 1.5 ms of call time a frame at 25 µs a call, to be measured in the build step.
  - **Bytes.** It saves 4–20 KB a key, which alone is under the 10 % bar.
  cost: small, app only, no pixel change. One behaviour change: a value written outside the app (by the CLI or Credential Manager) is seen only after a restart, which is the rule Linux and macOS already follow.
- id: dotnet-hover-holds-removed-view
  what: The window's two hover references keep an element alive after a build removed it: `Window._lastHoveredElement` (Window.cs:27/2168) and `PointerState._hoveredElement` (PointerState.cs:16). Through its Parent and Children links, that element keeps the whole old view alive until the pointer next moves or leaves the window.
  evidence:
  - **Scenario.** Meridian with an imported spec. I clicked a map row, then pressed 1 for the Requests view with the pointer at rest.
  - **Holders.** A field scan plus a breadth-first search of the object graph found these two references and nothing else. All probe steps ran in NoInlining helper methods, so the probe itself held nothing.
  - **Bytes freed by clearing them:**
    - 50 endpoints: 471 KB of a 4.9 MB heap (−9.6 %).
    - 500 endpoints: 4.0 MB of 12.3 MB (−32 %).
    - 2,000 endpoints: 15.5 MB of 38.3 MB (−40 %).
  - **Aperture.** A grid card click that opens the item showed no difference, because the app keeps the grid itself. The retention matters only where nothing else keeps the old view.
  expected: Retained-memory bar: −10…−40 % of the managed heap in the Meridian case, while the pointer rests. The old tree also no longer survives into gen 2.
  cost: small, Core and Window.
  - **Pixel-free version.** After a frame's reconcile, if the hovered element is no longer hosted, fire PointerLeave on its removed part of the chain right away and point hover at its nearest hosted ancestor. Today that leave fires on the next pointer move instead, so only its timing changes.
  - **Version with a pixel change.** Re-hit-test at the last pointer position, as the drop fix does at drop time. The new element under a still pointer would then show its hover style without a move; that needs the user (see below).
  - Shared with expert 6.
- id: dotnet-meridian-large-spec (app; shared with expert 8)
  what: Three O(n) costs that appear once a large spec is mapped, which 12d0392e makes possible through discovery.
  evidence (synthetic OpenAPI 3 specs: 50 operations = 42 K characters, 500 = 426 K, 2,000 = 1.7 M):
  - **(a) A click on a map row is about 4.9 KB per endpoint.** It rebuilds the whole window (`MarkDirty`, MeridianApp.Discovery.cs:412), and every endpoint row is made again with its closure, two `Endpoint.Key` strings and an interpolated name. Per click: 351 KB at 50, 2,592 KB at 500, 9,917 KB at 2,000.
  - **(b) The parse leaves a large pooled array behind.** `OpenApi.Parse` of a string leaves 2.0 MB (500 operations) or 8.0 MB (2,000) in `ArrayPool.Shared`. That is exactly the power-of-two bucket of 3 × the character count.
  - **(c) Every send to a tracked base URL is O(n).** `ApiMap.Record` copies the list and splits both paths for every endpoint: 97 KB at 500, 396 KB at 2,000 (unknown path 234 / 934 KB). `SaveMap` then rewrites the whole cache file on the UI thread: 272 / 1,106 KB.
  - **For scale.** The map itself keeps about 1.4 KB per operation. The parse allocates about 11.7 KB per operation, transiently.
  expected:
  - **(a)** Churn bar: about −90 % per click at 500 endpoints or more, by rebuilding only the two rows a click changes or using a VirtualList.
  - **(b)** Retained bar: 8 MB of a 38 MB heap at 2,000 operations, by reading the spill file as bytes and calling `JsonDocument.Parse(ReadOnlyMemory<byte>)`. That has no 3× transcode and no 2 B-a-character string.
  - **(c)** Per send, below the bars on its own: fold it in only if (a) is built.
  cost: small to medium, app only, no pixel change.
- id: dotnet-meridian-env-cell-typing-builds-view (shared with experts 6 and 8)
  what: A keystroke in an Environments value cell builds the whole Environments view (`OnTextChanged` → `MarkViewDirty`, MeridianApp.Environments.cs:341; present since ff6262ff).
  evidence:
  - 228 KB a key with 4 secrets × 3 environments, 462 KB with 12 × 3; one view build a key.
  - Each secret cell adds about 9.75 KB a key. Main's merge grew these cells (menus, value source, "use everywhere").
  expected: Churn bar: above −90 % a key if the cell's text stays in its TextInput and only the unsaved marker is updated (a part, or a ref). This also removes the keyring calls of the first candidate from typing.
  cost: medium, app only.

WAITING, RE-RANKED:
- findbar-countonscreen-foreach: still a nit. FindBar was not touched in round 8 (FindBar.cs:209 still uses `foreach` over `bar.Children`). It costs 32 B on AOT, 0.4 % of an 8 KB Enter. Fix it when FindBar is next touched.
- textarea-chunked-document: still parked. There is no new TextArea evidence; the 12d0392e spec is parsed, not shown in a TextArea. Meridian's 89k-character body typing is still about 238–241 KB a key, and the user's 256k-character condition is unmet.

EXAMINED, NOT WORTH IT:
- Dialog open, type and close cycles (new-request dialog, 200 cycles): retained 0 B a cycle. Typing in the dialog: 0.22 KB a key, 0 builds.
- `TickSelectionEdgeScroll` per frame: 0 B and one clock read. It requests frames only while a drag is held past the edge and the view can still scroll.
- `KeyringMemo` on Linux and macOS: one entry per (collection, environment, key) for the life of the process. Bounded by the secrets that exist.
- Aperture's per-row `KeptElements` dictionaries: one per shelf key the server has, each with at most max(64, 2 × live) entries.
- Request tab `PrettyJson(sent.Body)` on each response-pane build: only the sent body, which is small. The keyring calls, not this, are the cost there.

NEEDS THE USER:
- dotnet-hover-holds-removed-view: should a rebuild under a still pointer re-hit-test hover? That would show the new element's hover style without a move, as browsers do. Or should it only release the removed elements, with no pixel change (my default)?
- dotnet-meridian-windows-keyring-memo: is it acceptable that, on Windows, a secret changed outside the app (by the CLI or Credential Manager) is seen only after a restart, as on Linux and macOS today?

FILES (all in C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\c484f5ac-8f22-48dd-b669-13c24b1e7889\scratchpad\r9-dotnet\):
- suite-ea3e57ab-jit.json, suite-ea3e57ab-aot.json: the captures.
- suite-jit.ps1, suitepub.ps1, sa\SuiteAot.csproj: the JIT run and the AOT publish. The AOT build goes into the worktree's ignored bin\aot9 and obj\aot9.
- bytesdiff.py, cmp-jit.txt, cmp-aot.txt, compare-jit.txt, compare-aot.txt: the byte comparisons against round 8's captures.
- jitaot.py, jitaot.txt, steady.py, steady.txt: JIT against AOT, and the steady rows.
- ZzR9DotnetProbe.cs, meridian-probe.txt: the Meridian probe (keyring counts, the Windows call cost, the large spec, the hover holders, the pane drag, the dialog cycles) and its results, runs 1–9.
- aperture-hover-probe.txt: the Aperture click result. The probe source was not kept.
- d-restyle.diff, d-artworkurl.diff, d-history.diff, d-selscroll.diff, d-splitter.diff, commits.txt: the review diffs.
