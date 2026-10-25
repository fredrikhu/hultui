# Round 5 survey: 2. SkiaSharp painting

At 969f3308. I measured two ways, both headless, counting calls and bytes only. First, a render-pass probe on all 140 suite rows (JIT), counting text draws, glyphs, blob and sub-blob makes, map restarts, run-extent reads, layers, effect-cache misses and render bytes by element type. Second, scratchpad probes (R5Probe modes `thrash`, `tight`, `faces`, `rich`, `varab`) with two env-gated prototypes:
- `PROTO_TIGHT`: trusted faces use per-run extents.
- `PROTO_SUBGEN`: the blob maps keep what the last two frames drew.

The probe edits are reverse-applied and the worktree is clean at 969f3308. Probe code is in the session scratchpad `r5paint/`: `probe-r5.patch`, `PaintProbe.cs`, `probe/` (R5Probe), `cmp_probe.py`, `suite-probe-jit.txt`, `thrash*.txt`, `tight-*.txt`, `faces.txt`, `suite-tight-*.txt`, `suite-subgen2-*.txt`.

SATISFIED: no

## Review

No objections.

- **Render-pass bytes against round 4's probe (122 shared rows).** Every steady or hover row that draws text allocates 0 B. The changes all trace to a known commit:
  - Text open/typing rows fell with step 8: long document 1,373 → 372 KB, JSON open 1,902 → 269 KB, wrapped typing 26.9 → 12.4 KB, undo-redo 8.6 → 5.2 KB.
  - 0ea667cf's heights array, as its commit says: dropdown 1.61 → 2.41 KB and menu bar 0.2 → 0.3 KB per open.
  - `input/type beside a 150-line diff, rows kept` rose 0 → 0.73 KB in render. This is the TextInput's 744 B per keystroke moving from layout into render, since step 12 keeps layout across the patch. The row's total stays at 19 KB/op in both rounds.
- **b52ca178 (variable-font run extents).** Glyphs per frame match the round 4 prediction:
  - selectable viewer hover 3,702 (77 whole-run + 3,625 sub-blob glyphs)
  - viewer wheel 3,784
  - JSON wheel 1,255
  - terminal 3,549

  Steady frames allocate 0 B. Run extents are read only for new runs: JSON page down 27.8 per step, viewer wheel 6.8.

  One observation, not an objection: all four apps draw code in **Consolas** on Windows (`apps/*/Fonts.cs:12-14`). Consolas is a trusted face, so b52ca178 changes nothing in Junction, Vesper, Meridian or Aperture diffs on Windows. It helps HultUI.Terminal (Cascadia by default), `FontFamilies.Monospace` users and Linux variable faces. Expert 8 should not look for an app diff gain on Windows. Its extra first-draw blobs also move Cascadia onto the path with the restart cliff in the candidate below.
- **1207c8b3 (terminal row strings).** The terminal row allocates 0 B per forced frame. `_codePointTexts` (TerminalElement.Render.cs:73, :387) keeps one string per distinct code point drawn on its own until the font changes, about 50 B each. That matches the glyph tables beside it and is bounded by what has been on screen.
- **b0a62174 (rich text ViewFrame).** Rich text rows unchanged (0.06 KB).
- **0ea667cf (menu cull).** Dropdown text items per frame 278 → 104; off-window lines 173 → 0. Items are read-only, so the kept heights cannot go stale.
- **0b992365 (hero scrim).** Gradient and blur caches miss 0 times after the first frame on all 140 rows.
- **f9980647 and 1b11aeda (video frame, kept F16 offscreen), paint side.**
  - A renderer-owned frame costs one dictionary `Remove` and no allocation.
  - The kept F16 offscreen clips its canvas to the frame, so `ClipBounds` (menu cull, paragraph cull) still sees the window.
  - Pixel-path video frames are unchanged at about 250 B per frame on media rows (examined in round 4).
- **SaveLayers** come only from opacity: toast 18 per 20 frames, bar fade 28 per 29, palette 0.6 per op.

## Candidates

- **id: paint-blob-maps-frame-working-set**
  - **what:** When the run-blob map or the sub-blob map fills, release only the blobs not drawn in this frame or the last one, instead of clearing all 2,048 entries.
    - A frame stamp goes in the map's value.
    - The sweep runs with hysteresis: the next sweep waits until the map has grown by its bound again.
    - A hard cap on glyphs held falls back to today's full clear.
  - **evidence:**
    - SkiaRenderContext.cs:1094-1095 (sub-blobs) and :1831-1832 (run blobs) clear the whole map at `RunBlobCacheMaxSize` 2,048 (:64). A screen that needs more than 2,048 then remakes every blob on every frame.
    - Steady forced frames (R5Probe `thrash`), native blob builds per frame:

      | Screen | Needed per frame | Builds per steady frame today | Prototype |
      |---|---|---|---|
      | Terminal, htop-like (a colour every 2–4 cells), unique rows, 1200×900 | 2,312 sub-blobs | 2,338 (1.2 restarts/frame) | 0 |
      | same, 1920×1080 | 6,315 | 6,315 (3.1 restarts) | 0 |
      | same, 2560×1400 | 8,273 | 8,273 | 0 |
      | same, 3840×2100 | 12,520 | 12,520 (6.1 restarts) | 0 |
      | Terminal, Chinese text (each wide character is its own run; >2,048 different ideographs on screen), 1920×1080 | 7,088 run draws | 4,096 run blobs (2 restarts/frame) | 0 |
      | same, 3840×2100 | 14,054 | 8,294 | 0 |
      | Terminal, `ls --color`, unique rows, 3840×2100 | 1,924 | 0 (just under the bound) | 0 |
      | Viewer, Cascadia, a colour every 4–9 chars, 3840×2100 | 1,619 | 0 | 0 |

    - Each build copies glyphs through `SKTextBlobBuilder` with a native malloc and free (10,417–47,810 glyphs per frame on the dense terminal). On a GPU surface a remade blob also has a new ID, so Ganesh regenerates its cached text vertex data for those glyphs every frame. Headless runs cannot show that part.
    - Managed bytes are 0 either way.
    - With the prototype, every text/, terminal/ and richtext/ suite row keeps the same misses, restarts, glyphs and allocKB; no suite row reaches 2,048.
    - The cliff has existed for trusted faces since 8ddc3eb (round 3). b52ca178 put Cascadia (the terminal's default) and Segoe UI Variable on the same path.
  - **expected:** Clears the Scaling bar: per steady frame, O(blobs on screen) native builds → 0 once a screen needs more than 2,048. That happens on a dense-colour terminal at 1200×900, a Chinese terminal at 1080p, and big or 4K screens. Time likely ≥5 % on such a screen. That needs a suite row from expert 8: `terminal/dense colour screen, unique rows, forced frame` at about 1600×1000 (~3,000 sub-blobs per frame).
  - **cost:** Small. No API or pixel change (the same blobs are drawn).
    - **Retained memory:** up to two frames' blobs under the cap. On the dense 4K terminal that is about 12.5k sub-blobs, roughly 1.6 MB native at an estimated ~130 B each. The managed map grows past 2,048 entries (~40 B each) only on such screens.
    - **Trap:** a sweep on every miss past the bound is O(n²) while a big screen first fills, hence the hysteresis.
    - **Tests:**
      - a 3,000-sub-blob screen makes 0 blobs on its second forced frame;
      - a CJK screen with more than 2,048 runs makes 0 run blobs on its second frame;
      - page-down keeps the map under its cap;
      - blobs made equal blobs released after dispose.

## Examined, not worth it

- **paint-subblob-trusted-face-bounds** (Waiting item; I recommend moving it to Examined).
  - Trusted face boxes are wide (with slack): Segoe UI −0.70…2.12 em, Arial −0.79…2.13, Times New Roman −0.69…2.17, Tahoma −0.72…1.98, Consolas −0.56…0.80. A code run's own extent is −0.02…0.92 em in Segoe UI and 0…0.55 em in Consolas.
  - Prototype `PROTO_TIGHT`, glyphs per frame on a dense viewer:
    - Consolas (the apps' code face) 6,137 → 5,492 (−10.5 %)
    - Segoe UI 9,023 → 6,344 (−30 %)
    - Arial 9,229 → 6,363 (−31 %)
  - Suite glyphs: spans recolor 7,708 → 6,214 (−19 %), spans bold 5,525 → 4,927 (−11 %), rich text rows −1.7 % (−7.8 % on the 600-span paragraph). Draw calls are unchanged.
  - Cost: a `GetGlyphWidths` call per new run in a trusted face. The 600-span paragraph goes from 81 to 515 calls per keystroke (0.4 map restarts per keystroke); styled resize rows add 73–117 calls per step.
  - By round 3's ratio (−74 % glyphs gave −15/−17 % on Junction diff hover), Consolas's −10.5 % is about −2 %. No app draws dense colour in Segoe UI or Arial. Below the bar.
  - A cheaper variant (one per-face extent over the ASCII glyphs plus a bitset check per run, no native call) gives the same small win.
- **Bitmap-strike faces draw whole runs in every stretch.**
  - Courier New Regular (EBDT) on the dense viewer: 62,385 glyphs per frame and 744 whole-run draws, against 5,492 glyphs in Consolas (11×). Calibri behaves the same.
  - Covering them needs bounds at the drawn device size (per-ppem strike knowledge) and a new pixel A/B on both DirectWrite and FreeType.
  - No app uses these faces for coloured text. Courier New appears only in RichRtf's RTF export mapping and at the end of `FontFamilies.Monospace`'s fallback list, after Cascadia and Consolas. The round 4 guideline note stands.
- **paint-drawspannedline-overlap.** No new `TextSpan` or `Spans` producer in the round 4 commits under `apps/` or `src/`. It is a latent contract, not a performance item. I recommend closing it from Waiting.
- **meridian-response-frame-flush, paint side** (stays a lead for expert 3).
  - The paint volume of that frame is small. The suite's equivalent (`text/open highlighted JSON`, 19,000 lines, TextArea culls to visible lines at TextAreaElement.cs:1837-1853) draws 53 lines, 162 stretches and 1,168 glyphs, with 20 new sub-blobs, 5 run-extent reads and no layers.
  - The text is ASCII in Consolas 12 px, so there are few new glyph masks. That is far too little drawing for an 86–152 ms flush.
  - Two cheap tests would tell the causes apart:
    1. Send the same response twice in one session. If the second frame flushes fast, the cost is first-use GPU work (program compiles, atlas growth).
    2. Record the GC pause time and the gen-2 count across the Flush phase. The formatter's thread-pool work allocates while the UI thread is inside the native flush, and a blocking GC holds the UI thread's return from native code, which would show up as flush time.
- **Blob maps below the bound.** The 4K viewer and JSON stay under 2,048 (1,619 and 740 per frame). JSON page down makes 111.8 sub-blobs and 27.8 run-extent reads per step, which are unavoidable first draws.
- **Run-extent map restart** (SkiaRenderContext.cs:1163-1164). It would need more than 2,048 distinct spanned runs per frame in a variable face; the dense 4K terminal draws 131 runs. No case found.
- **TextInput's 744 B per keystroke in render.** It shapes the new text for the caret and its width, which every keystroke needs.

## Needs the user

None.
