# Round 4 survey: 6. Reactivity and reconcile

At ba817e8; headless JIT allocation counts and call counts, no timings. Probes reverted; worktree
clean. Probe code in the session scratchpad `r4react/` (`probe/`, `tracked-ba817e8.patch` with counters,
Junction section bytes and env switches R4_PARTS (chrome split), R4_REVIEW_SHAPE, R4_TAGS_ONCE;
`RrProbe.cs`, `R4ReviewProbe.cs`, `R4ResizeProbe.cs`, `junction-walks.txt`, `review.sh`, `vesper.sh`).

SATISFIED: no

## Review

- **e0b816f (Vesper grids on resize):** right, but it rebuilds the whole app on every resize step while
  a Brief or Thumbnails view shows. Per +4 px step (1240×820, 300 files): Full 2.3 KB (0 rebuilds),
  Thumbnails 520.1 KB, Brief 749.5 KB (1 rebuild each). → vesper-grids-rebuild.
- No regressions in aad602a/91893ba, 1e3edff, 133bd8a/7bfc1e7/5e28b9a, 1d39128/8042e8e, 2894d98,
  9c43337/7cfd0c9, 1bd5e4c, 88fa622: effects disposed with their player or mount (Reel's Ended path
  disposes its own effect mid-run inside an untracked block — safe); the chapter Computed tracks
  `Chapters` (a signal), so late marks reach it; the ToastHost Ref is cleared when no toast shows;
  PointerFollow flushes on release and keys; History 1.00 rebuild per key.
- Item-row shape keys spot-checked (Junction diff, Branches/Tags, Files): every value a row builder
  reads is in the item or the shape (Branches protection through ProjectFor, in the shape). No stale-row
  bug.
- Minor: comments at JunctionApp.Files.cs:538-541 and History.cs:438-441 say "a MarkDirty rebuild is
  untracked" — stale (ComponentHost.ForceRebuild re-tracks).

## Candidates

- **apps-junction-components** (approved: Junction's chrome first). Titlebar, sidebar and toolbar as
  three persistent child components. History key, a11y off: chrome Build titlebar 25.7 KB (26
  elements), sidebar 47.0 (39), toolbar 55.4 (53) = 128 of 182 KB of Build (70 %) + ~10 KB reconcile;
  Branches 128 of 170 (75 %). Prototype (parts never rebuild during a walk; 1,562-commit clone, Down ×20,
  press + 8 frames, a11y off / bridge on): History 243.7 → 105.6 KB (−57 %) / 344.7 → 206.6 (−40 %);
  Branches 185.9 → 47.2 (−75 %) / 198.4 → 60.1 (−70 %); Tags 415.0 → 138.5 (−67 %) / 436.4 → 159.5
  (−63 %); Files 238.3 → 99.3 (−58 %) / 261.3 → 122.5 (−53 %); Commit file list 237.0 → 98.4 (−58 %) /
  255.2 → 116.4 (−54 %); commit-summary typing 206.5 → 67.6 (−67 %) / 219.9 → 80.9 (−63 %). Design:
  `ChromePart(app, Func<Element>)` components call the existing BuildTitleBar/BuildSidebar/BuildToolbar
  and read a new `Signal<int> _chromeRevision`; safe by default — `Rerender()` and a `private new void
  MarkDirty()` in JunctionApp bump it; only view-only paths opt out (`RerenderView`/`MarkViewDirty`):
  MoveCommitSelection, MoveBranchSel/SelectBranch, MoveTagSel/SelectTag, Files cursor/SelectTreeFile,
  Commit file cursor, composer OnTextChanged, search fields. Chrome inputs: View, `_compactChrome`,
  `_remoteOp`, active repo/workspace, SummaryFor, GitLab marks/todos/head pipeline, popover; no per-view
  selection. Loaders read inside a part subscribe that part; the toolbar's `_remoteOpIcon` Ref works (the
  part rebuilds on Begin/EndRemoteOp via MarkDirty); the chrome builds no Spinner(). Side effect: moving
  three hosts per app build adds Children marks (Branches 0 → 4 per key), no extra a11y rebuild today.
  Churn −40…−75 % per key on six real walks. Medium, app only.
- **apps-junction-tags-double-rebuild.** A Tags key rebuilds the whole app twice: MoveTagSel calls
  SelectTag (MarkDirty, JunctionApp.Tags.cs:423) then MarkDirty again (:441); since a27320fd, not round
  3. Prototype: 2.0 → 1.0 rebuilds per key, 415.9 → 237.8 KB/key (−43 %), bridge on 436.8 → 258.6
  (−41 %). Small (test that ScrollRangeIntoView still follows).
- **junction-review-rows-shape.** In review mode, keep the woven rows across builds and remake only
  the caret/range rows. 400-line file, one hunk of ~470 rows, caret Down ×30: 1,014.7 KB/key (bridge on
  1,095.2): weave 307.1 KB (WithReviewRows copies every row, a SHA-1 LineCode per line and a `with {
  Index }` record per row each build, Diff.cs:484-485, Review.cs:428-457); 30.3 rows remade per key,
  413.7 KB (`|| review is not null` invalidates every build, Diff.cs:537); render 431.5; chrome 138.8;
  565 mounts and 344 unmounts per key; `RowLabels(rows)` a new delegate each build drops kept labels
  (Diff.cs:552). Prototype floor (woven list kept by flattened rows, HeightKey, Composer, Range; no
  InvalidateRows on caret-only builds): 281.3 KB/key (−72 %), bridge on 361.7 (−67 %); rows made
  0.3/key; with the chrome split 131.8 KB. Expected −65…−70 % once the two caret rows are remade.
  Small–medium (caret as SelectedIndex or an item field, range in the item).
- **reel-still-previews-rebuild.** A landing still calls `InvokeOnMainThread(Rerender)`
  (ReelApp.Preview.cs:48), which rebuilds ReelApp but never the VideoPlayer that builds the preview card
  (VideoPlayer.cs:395-397): pure waste, and a still landing under a resting pointer shows only on the
  next move. Per still (1280×760): 2.0 KB (chapters panel open 66.2 KB); bridge on 5.9 / 76.1 KB (the
  moved host marks Children → a full a11y rebuild). ~120 stills per file at 1–2/s: +2–4 KB/s on Reel's
  22 KB/s quiet playback (+9–18 %), or 66–132 KB/s with the panel open. Fix: a VideoPlayer method (e.g.
  `RefreshPreview()`) acting only while a card shows, called from onFrame. 120 → 0 app rebuilds per file;
  fixes the card not updating. Small; one new public method.
- **reactivity-scrubber-preview-in-place.** Move the scrubber's preview card in place (Left, image
  source rect and time label through Refs) instead of rebuilding the VideoPlayer each frame the pointer
  moves over the scrubber (Build reads `_previewAt`, VideoPlayer.cs:395). Suite row `media/scrubber
  hover, 1 kHz mouse, real loop`: 420.7 KB/op, 1.0 player builds per drawn frame (~38 KB per frame).
  Churn ≥ −80 %; builds 1.0 → ~0 per frame. Small–medium (HultUI.Media).
- **reconcile-restyle-on-change** (SelectionColor showing approved). Mark style on patch only when the
  inherited style changed, with themed values kept out of the patched slots. History key: 152 patches,
  143 keep the same inherited style; the 9 that differ are themed flip-flops (Button 7, Kbd's Text 1,
  TextInput 1) → 9 layout marks per rebuild; 389.5 resolves (30.8 first, 186.0 same, 172.7 same-themed,
  0 with a changed result), 539.5 style down-visits, 40 B. Branches: 146 of 154 patches same, 8
  flip-flops, 187 resolves. 31 ApplyTheme overrides in 27 files; TextElement's SelectionColor override
  makes every Text "themed". NoteChildJoined stays in the three add paths (checked). Time only (0 bytes):
  resolves −92 % (~390 → ~31 per Junction key), flip-flop layout marks 9 → 0. Medium–large.
- **vesper-grids-rebuild.** Thumbnails grid on item rows, Brief's built entries kept; rebuild on resize
  only when tiles per row, rows per column or the grid width change. Down key (1240×820, 300 files):
  Full 242.7 KB; Thumbnails 583.6 KB (one-argument RenderWith, CommanderApp.Views.cs:107, 333 unmounts
  per key, render 370 KB); Brief 761.5 KB (every entry built, 675 KB build, 583 mounts per key). Resize:
  Thumbnails 520.1 and Brief 749.5 KB per +4 px step vs 2.3 in Full. Churn −55…−65 % per grid key, ≥
  −90 % per resize step. Medium (app; item-rows adoption approved one list at a time).
- **reactivity-computed-pooled-notify.** Computed notifies through a rented snapshot like Signal, not
  `new List<IReactiveNode>(_subscribers)`. Signal→Computed→Effect 64.0 B per upstream write vs
  Signal→Effect 0.0 B; REACTIVITY.md now routes fast values through Computed; Reel's panel-open report
  591 B (1e3edff) → ~527 (−11 %, borderline); 0 B per write for every Computed. Small.

## Examined, not worth it

- Persistent host moved by its parent's build: in the split prototype, moved hosts add Children marks
  (Branches 0 → 4 per key) but no extra a11y rebuild (every walk already rebuilds once per key); in Reel
  one 24-element a11y rebuild (3.9 KB) per app build. Revisit after restyle-on-change: once the
  layout-driven refresh is gone, the split would cost one full a11y rebuild per key with a reader
  (Branches 12.8 KB, Tags 20.9 KB).
- componenthost-patch-keeps-layout (owner 1): a persistent host is re-added (AddChild/ReplaceChildren),
  never patched; ComponentHost never appears in layout-from-patch in Reel or the Junction split — opting
  it in changes nothing measured.
- 1e3edff's Computed beyond its notify snapshot: the effect's version check costs 0 B per report.

## Needs the user

None.
