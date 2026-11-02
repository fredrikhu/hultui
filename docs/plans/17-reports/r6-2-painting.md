# Round 6 survey: 2. SkiaSharp: painting

I measured at ef4288ad, which has the code of f31194ab. Everything ran headless on JIT and I counted calls and bytes only, no timings. The round 5 render-pass probe was extended and run over all 151 suite rows, then compared with round 5's output. The new counters cover:
- draws by kind, clips, layers, and elements rendered or culled;
- blob and sub-blob makes and map sweeps, and glyph-ink reads;
- glyph arrays composed during the render pass, and where they were composed.

Two prototypes, each switched by an environment variable, ran as A/Bs in one build. The A/A between builds was identical.
- **`PROTO_TEXTKEY`**: `1` adds the text-keyed lookup, `2` also builds a miss's blob from the word segments. `PROTO_VERIFY=1` composes both runs of every text match and compares them.
- **`PROTO_WHEEL`**: a scroll that cannot move the viewport changes nothing.

The `R6Probe` tool adds three modes: `wheel` and `stream` for the terminal, and `pixels` for frame hashes.

All probe edits were reverse-applied and the worktree is clean at ef4288ad. Probes live in the scratchpad folder `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\855f4ddd-feda-4f0a-a595-a83e07af08f8\scratchpad\r6-painting\`:
- `probe-r6-final.patch` holds everything, including both prototypes; it needs `PaintProbe.cs` copied into `src/HultUI.Core/Rendering`;
- `apply_probe6*.py` and `apply_proto_*.py`;
- `probe\` (R6Probe);
- `tab.py` and `ab.py`;
- the outputs `suite-probe-jit2.txt`, `proto-{off,on,verify}.txt`, `proto2-{off,on,verify}.txt` and `probe\pix-*.txt`.

SATISFIED: no

## Review

No objections.

- **dff306df and 09abf712 (blob working set).** No suite row sweeps either map: 0 sweeps and 0 entries looked at over 151 rows. Blob makes per step are unchanged elsewhere.
- **e5ff3fea (glyph ink bounds).** Steady rows read 0 glyph inks per step; JSON wheel reads 0.2. The union is taken per spanned run per frame: 41–81 runs a frame on spanned rows, 62 on the dense terminal. That is the accepted trade, and it stays flat.
- **91d48601 (glyphs on first read).** The glyph-array bytes moved into the render pass:
  - scroll/jump: render 2.4 → 10.5 KB;
  - viewer wheel: 2.6 → 10.7 KB;
  - richtext/wheel: 1.2 → 8.5 KB;
  - gallery mount: 5.0 → 28.1 KB;
  - resize rows: 0 → 4–73 KB.

  Totals fell as recorded when it landed. This is not an objection, but on resize rows most of those bytes are composed only to look up a blob that already exists. That is candidate 1.
- **0d3bc10a, 6895ac2d, 827f6ad5.** The list-drawn highlight is one rounded rect per selected row. `TextCaretXs` allocates nothing on the paint path. The context menu's overlay bytes per open fell: dropdown 2.41 → 0 KB, menu bar 0.3 → 0 KB.
- **Unchanged since round 5:**
  - Steady and hover rows allocate 0 B in render, including viewer hover, terminal forced frames, dense text and tiles.
  - Layers come only from opacity: toast 18 per 20 frames, bar fade 28, dialog 1, palette 0.6, tiles 72.
  - The blur and gradient caches miss 0 times after the first frame, and no row makes a dash effect.

## Candidates

- **id: paint-blob-key-by-words-text**
  - **what:** Runs whose glyphs depend only on face, size and text get a flag. These are the runs `ShapeByWords` makes: single words from the word cache, and runs of several words composed later.
    - The run-blob map hashes every run by (face, size, text).
    - Two flagged runs with equal text count as equal without reading their glyphs. Every other pair needs equal text and equal glyphs.
    - On a miss, a flagged run builds its blob straight from its word segments, through a kept scratch span, and never makes its own glyph array.
  - **evidence:**
    - `SameGlyphs` (SkiaRenderContext.cs:226-247) hashes and compares `run.Glyphs`. Since 91d48601 that read composes a lazy run's array (TextPipeline.cs:2581) on every lookup of a new run.
    - Glyphs composed per step only to find a blob that exists:

      | Row | Glyphs composed for a hit |
      |---|---|
      | text/wrap | 3,885 (30 runs) of 4,599 composed in render (render 72.7 KB) |
      | layout/shrink 300 overfull | 1,271 |
      | text/ellipsis | 1,908 |
      | multi-font ellipsis | 1,812 |
      | richtext/resize | 839 |

    - **`PROTO_TEXTKEY=1`**, the hit half only:
      - text/wrap 397 → 336 KB/op (−15.4 %);
      - shrink 300 overfull −6.6 %, ellipsis −4.9 %;
      - glyph comparisons on hits fall to 0 on 20 rows (styled wrap 2,711 → 0, 600-span paragraph 2,544 → 0);
      - steady frames hash text strings instead of glyphs (dense text 4,496 glyphs a frame, hover transitions 3,500).
    - **`PROTO_TEXTKEY=2`**, both halves (allocKB per op, or render KB where the row is small):

      | Row | Before → after |
      |---|---|
      | text/wrap | 397 → 325 (−18.1 %); render 72.7 → 0 |
      | text/open a long document in a TextArea | 369 → 308 (−16.5 %) |
      | text/resize a wrapped TextArea | render 16.1 → 9.9 (about −34 % of 18) |
      | shrink 300 overfull | −7.2 % |
      | text/ellipsis | −7.2 % |
      | scroll/jump 5,000 rows | 10 → 3 (render 10.47 → 3.04, −70 %) |
      | scroll wheel rows | render 2.6 → 1.4 |
      | overlay/popover open | 33 → 29 (−12 %) |
      | richtext/wheel | render 8.5 → 6.2 |
      | virtual/list 100k jump | −4.4 % |
      | TableView wheel | −4.7 % |
      | a11y/create 1,000 | render 10.5 → 3.0 |
      | gallery mount | render 28.1 → 14.1 |

    - What still composes is readers that need glyphs: carets, selection, and spanned lines (sub-blobs need the glyph range).
    - Blob makes, sub-blob makes, runs drawn and glyphs drawn are identical on all 151 rows.
    - `PROTO_VERIFY` found 0 mismatches over every text match in the suite.
    - Pixels: 39 frames of wrapped and ellipsized labels in Segoe UI, Consolas and sans-serif, at 13 widths, with a 512 MB glyph cache. The A/A run was identical and so was the A/B.
    - `ShapedRun` stays 56 B: the flag fits its padding, and adding the field moved no row's allocKB by 0.5 % or more.
  - **expected:**
    - **Memory churn bar:** text/wrap −18 %, long TextArea open −16.5 %, scroll/jump −70 %, popover open −12 %.
    - This covers any window resize over wrapped or ellipsized labels (Aperture overviews, Vesper names in Detail) and every scroll that reveals plain text.
    - It probably saves time too, because a hit no longer does a word-cache lookup per segment or a two-field hash per glyph. The build phase has to time it.
  - **cost:** small to medium; no API change, no pixel change.
    - A flag set at TextPipeline.cs:2513, :2531 and :2566 (not when `composeNow`). The flag and the compose-into-span method need `InternalsVisibleTo` for HultUI.Rendering.Skia from Text, or internal accessors.
    - `SameGlyphs` gets a new hash and equality.
    - `GlyphsDrawn` must come from the glyph count. Today SkiaRenderContext.cs:2012 and :2020 read `run.Glyphs.Length`.
    - `ComposeDigits` must take a span; the prototype allocated a temporary array for digit words.
    - The scratch buffer must be made lazily. The prototype's eager 4 KB per context showed as window/open +3.1 % in the A/A.
    - This is not either earlier decline: the identity-first side map (+38 % bytes, round 3) and the cached hash (+8 B a run, round 4). There is no side map and no growth, and the new evidence is the lazy arrays.
    - Owner 2, with 5 for the pipeline half.
    - Tests:
      - a pending run drawn after an equal one composes nothing;
      - a miss composes nothing;
      - runs with equal text but different glyphs (letter spacing, RTL) stay apart;
      - pixels on the text/, richtext/ and terminal/ rows, with an A/A.

- **id: terminal-noop-scrollback-wheel** (Waiting, 2/Terminal; keep, now with numbers)
  - **what:** A wheel notch that cannot move the scrollback changes nothing.
  - **evidence:**
    - `TerminalController.ScrollViewport` (TerminalController.cs:358-366) always raises `ContentChanged`, and `XtermEmulator.ScrollViewport` (:137-142) always bumps the generation.
    - `R6Probe wheel` at 1200×900: a notch down at the bottom, or up at the top, draws 1.00 frame. That frame is the whole terminal: 5,440–5,682 glyphs and 836–882 draws with 12 listings of scrollback.
    - With no scrollback at all, every notch is a no-op and still draws a frame of 1,821 glyphs and 282 draws.
    - It also schedules a search rebind when a query is open (TerminalElement.Search.cs:61), and a screen-changed notice when a reader listens.
    - `PROTO_WHEEL` compares `Buffer.YDisp`, bumps the generation only on a move, and the controller compares generations. No-op notches go from 1.00 to 0.00 frames; notches that move are unchanged (1.00 frame, 9.5 KB).
  - **expected:** One full terminal frame per no-op notch → 0, as round 4's events-noop-input-frames did for the framework. Low priority: only the terminal sample uses HultUI.Terminal.
  - **cost:** small; no API or pixel change.

- **id: terminal-row-strings-by-content** (new, 2/Terminal, low)
  - **what:** Keep the last frame's row strings by content, not only by screen row (`_rowStrings[row]`, TerminalElement.Render.cs:377-384). Output that scrolls, or a scrollback notch, moves every row, so every row makes a new string even though its text was on screen one frame earlier.
  - **evidence:** `R6Probe stream` writes a line a frame at the bottom of a full 1200×900 screen:
    - render allocates 10,276 B per line, all in `TerminalElement`;
    - 55 rows draw and 1.0 blob is made per line, so the layout cache and blob maps already serve the moved rows; only the strings are new;
    - a scrollback notch that moves allocates 9,392 B in render.
  - **expected:** About 10 KB → about 0.2 KB per frame of streaming output, and 9.4 → about 0.5 KB per notch that moves. Low priority: no app uses the terminal.
  - **cost:** small; no API or pixel change.

## Examined, not worth it

- **controls-menu-max-height-scroll** (Waiting, 1/2): still open. ContextMenuElement has no maximum height or scrolling. It is reachability, not performance; keep it with expert 1.
- **Composes on spanned lines:** `PrefixMeasurer` (SkiaRenderContext.cs:1827-1834) reads `Glyphs.Length` of every run on a spanned line. On the 600-span paragraph that composes 4,922 glyphs per keystroke (about 13 % of 592 KB).
  - `DrawRunPart` needs those glyphs anyway, for `GlyphRange.Reaching` and the sub-blobs, so the glyph count alone would save no bytes.
  - The real cost is re-laying out unchanged lines per keystroke, which is text pipeline work.
  - The row's 223.6 new sub-blobs per keystroke (keyed by run identity) cost native work only, on one row.
- **Render-time cull visits:** a non-virtual list tests every child's box per frame (scroll/wheel 5,000 rows 4,973; layout/resize 2,000 1,974; grid 1,566). This is expert 1's layout-ordered-children (Waiting). There is no new app case: Junction's Changes list stays a scroll view (virtuallist-sticky-headers).
- **Rounded clips:** only from the indeterminate ProgressBar (ProgressBarElement.cs:176) and rounded overflow-hidden boxes: 24 a frame on spinners + progress, 10 on the expander. The cost is on the GPU and cannot be counted headless; Skia applies an anti-aliased rounded clip analytically.
- **Render-pass bytes owned by other experts:**
  - VirtualList realizing rows at render: list 100k jump 176 KB, diff rows made in Build 254 KB, selection walk over 1,000 rows 83 KB.
  - TextArea and rich text layout at render: long document open 308 KB after candidate 1, richtext open 4.5 MB.
- **Image decode:** `SkiaImageSource.FromFile` and `FromBytes` decode whole images, but every app that shows large pictures already bounds them (Vesper `ImageDecode.Bounded`, Aperture `PosterCache` via the codec, Junction markdown resized to `MdPictureMaxPixels` off the UI thread).
- **Video pixel path:** 248 B per frame, unchanged since round 4.

## Needs the user

None.
