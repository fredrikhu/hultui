# HultUI.Terminal — Design

A terminal emulator as an element, shipped as packages an application opts into, the way WebView
is: an application that never shows a terminal carries no emulator, no pseudoconsole and no
natives.

Written 2026-10-03 from the spike `spikes/XtermNet` (its README has the answers, `results/` the raw
runs, `patches/` the engine fixes), which ran on Windows only. Every number here comes from that
spike unless it says otherwise.
Where a claim still needs confirming it says **verify**. Three decisions were put to the owner and
are marked **DECIDED** (2026-10-03).

## What an application gets

```csharp
var shell = new TerminalController(
    new PtyConnection(PtyLaunch.DefaultShell(workingDirectory: repoRoot)),
    new TerminalOptions { Scrollback = 5000 });

Box.New().Children(
    TabStrip(shell),                         // shell.Title, shell.State, shell.WorkingDirectory are signals
    TerminalElement.New(shell).FlexGrow(1));

shell.Exited += code => ...;
shell.Send("git status\r");                  // through the writer queue; any thread
```

- **The connection is what the bytes travel over.**
  - `PtyConnection` (in `HultUI.Terminal.Pty`) runs a local process under a pseudoconsole: ConPTY on Windows, forkpty on Linux and macOS.
  - `StreamConnection` (in the core package) wraps any pair of streams: a serial port, an SSH shell channel, a test script.
- **The controller owns the emulator, the connection, and the two threads between them.**
- **The element draws the controller and turns input into bytes.**

The element is `TerminalElement.New`, not a `Terminal.New` builder: nothing in the package is
named `Terminal`, because a class named after its namespace (`HultUI.Terminal.Terminal`) is
unreachable by its short name, the `Text` trap WebView also avoided.

**Out of scope for the first version:**

- Tabs and splits (that is the app's layout).
- Sixel and Kitty images (the engine decodes them; drawing them is a later step).
- Ligatures.
- Shell integration UI beyond what the engine already reports.
- SSH (a later `HultUI.Terminal.Ssh`).

## Problems found, and what solves each

The spike's findings, each with its solution and where the solution lives. "Measured" means the
spike ran the fix; "proposed" means it is design only.

| # | Problem | Solution | Lives in | State |
|---|---|---|---|---|
| 1 | The cursor's own line is cut, not reflowed, on a narrowing resize, and ConPTY does not repaint it: a narrowed prompt stays cut. | `TerminalOptions.ReflowCursorLine` in the engine, on in HultUI | engine patch 0004 | measured: pwsh's prompt stays whole and typing continues it |
| 2 | A width resize costs O(scrollback × columns): 25 ms at 10 000 × 200. | Capacity-keeping line resize (3.6 ms), plus a resize policy for big buffers | engine patch 0001; element | measured; policy proposed |
| 3 | Scrollback is stored full width, about 25 B per cell: 47 MiB at 10 000 × 200. | Default 5 000 lines; a byte budget converted to lines at each width; both live signals | controller | built in step 2 |
| 4 | Porta.Pty's default Windows quoting wraps every argument: `cmd "/c" "exit 3"` exits 1. | MSVCRT quoting, passed verbatim | `HultUI.Terminal.Pty` | measured |
| 5 | A paste written by the reader thread deadlocks for good at 256 KiB; ClosePseudoConsole then hangs too. | A writer thread with a queue; a fixed teardown order | controller | measured; built in step 2 |
| 6 | A reader that wakes the UI while holding the engine lock is the THREADING.md live-resize deadlock. | Wake only after releasing the lock | controller | built in step 2 |
| 7 | The engine has no damage tracking. | Copy the viewport out under the lock every frame and draw all of it. No row cache: step 2 measured drawing fast enough without one | element | built in step 2; the copy costs 12.6 µs |
| 8 | SGR 53/55 (overline) never reach a cell. | Two switch cases | engine patch 0002 | measured |
| 9 | Alt+Enter and Alt+Backspace drop the Alt; Ctrl+/ sends `/`; Ctrl+Backspace equals Backspace. | Fixed in the legacy encoder | engine patch 0003 | measured: 49/49 |
| 10 | HultUI's key event is a scancode position: Ctrl+letter is wrong on AZERTY/QWERTZ; there are no keypad codes; AltGr looks like Ctrl+Alt. | Seams K1–K4 | framework | proposed |
| 11 | A bare ESC through the out-of-band ConPTY races: the first key after a resize was lost in 2–9 runs of 10. | Send ESC (and the other ambiguous keys) as win32-input-mode events when ConPTY asks for them | element, Windows | measured for ESC: 10/10 |
| 12 | Engine facts a renderer must know: default colours are palette 256/257, the deferred wrap is X == Cols, and a width-2 cell can end a row. | Renderer rules | element | built in step 2 |
| 13 | HultUI has no positioned-glyph-run or offscreen-surface API for elements. | One `DrawText` per row, with a span per style. Seams T2/T3 were for the case where that was too slow, and it is not | element | measured in step 2: about 7 ms a frame for a 207 × 58 full-screen redraw |
| 14 | HultUI has no monospace default font and no cell-metrics API. | Seam T1 | framework | proposed |
| 15 | HultUI has no UIA TextPattern, no AT-SPI Text, and no announcements. | Seams A1/A2; until then, role and value | framework | proposed |
| 16 | Porta.Pty puts arm64's `OpenConsole.exe` (1.1 MB) in a win-x64 publish. | A build target that keeps only the publishing RID's copy | `HultUI.Terminal.Pty` | proposed |
| 17 | A row shrink with the cursor near the top leaves the cut rows below the screen, so the scrollback keeps that many lines fewer, for good. A terminal created at 80 × 24 and sized to its pane before its first screenful is that case. | Drop the blank rows below the cursor, as xterm.js does | engine patch 0005 | measured: found in step 2; the new upstream test fails without it |
| 18 | A symbol the monospace font lacks (Consolas lacks some arrows and maths operators) is drawn from a fallback font whose advance pushes the rest of its row off its columns. | Only code points every default terminal font has go into a row's text; the rest are drawn one at a time at their own columns. Seam T1 would let the element ask the font instead | element | measured: found in step 2 by the sample |

**DECIDED (engine patches): kept as HultUI's patches, not offered upstream** (2026-10-03). XTerm.NET is
therefore built from source: upstream at a pinned commit with patches 0001–0005 applied, never the
NuGet package. All 2 240 of XTerm.NET's tests pass with the patches, 19 of them new. How the
patched source is kept, built and updated is in *The patched engine* below. The alternative
weighed was offering them upstream and building on the stock package meanwhile, with problem 1
visible on Windows until a release carried `ReflowCursorLine`.

## Packages

```
external/
  XTerm.NET/                    upstream's src/XTerm.NET and src/XTerm.NET.Tests at the pinned
                                commit, patches applied, plus its LICENSE; generated, committed
  XTerm.NET.patches/            0001–0005: everything HultUI's engine differs from upstream by
  XTerm.NET.version             upstream URL and the pinned commit
src/
  HultUI.Terminal.XTermNet/     builds external/XTerm.NET as HultUI.Terminal.XTermNet.dll
  HultUI.Terminal/              controller, element, renderer, key mapping, emulator seam,
                                the XTerm.NET adapter, StreamConnection, fake connection — no natives
  HultUI.Terminal.Pty/          PtyConnection: Porta.Pty (ConPTY + OpenConsole on Windows,
                                the forkpty shim elsewhere), quoting, teardown
tests/
  HultUI.Terminal.XTermNet.Tests/  XTerm.NET's own suite, from external/ (2 240 tests, ~3 s)
  HultUI.Terminal.Tests/        headless: scripted bytes through the real emulator
  HultUI.Terminal.Pty.Tests/    real shells; skipped where the shell is missing
samples/
  HultUI.Samples.Terminal/      an in-process demo shell now; a real shell, a find bar and an
                                overlay over the grid with the steps that bring them
scripts/
  vendor-xterm-net.ps1          rebuilds external/XTerm.NET from the version file and the patches
```

**DECIDED (packages): split** (2026-10-03). It differs from WebView's split on purpose:

- **Emulator.** WebView keeps every engine out of its core package because each one is native,
  platform-specific and large. XTerm.NET is 302 KB of managed code with no natives and works
  everywhere, so it comes with `HultUI.Terminal`: `HultUI.Terminal.XTermNet` is a package of its
  own only because it is a project of its own, and `src/Directory.Build.props` turns a project
  reference into an exact-version package dependency. An app references `HultUI.Terminal` and
  gets both.
- **Pseudoconsole.** This is what carries natives: `conpty.dll` + `OpenConsole.exe` (2.4 MB
  across x64 and arm64) and the Unix shims. It is the opt-in piece. An app whose terminal talks
  SSH or a serial port never loads ConPTY. One package would have been simpler, and every app
  with a terminal would have carried those 2.4 MB whether it ran a local process or not.
- **libghostty-vt.** It would come later as `HultUI.Terminal.Ghostty` plus `.Natives`, behind
  the same emulator seam, once it tags a stable release (its header still says the API "is not
  yet stable and is definitely going to change").

`HultUI.Core` grants `InternalsVisibleTo HultUI.Terminal` for `OnOwn`, `PatchFrom`,
`WantsTextInput` and `MeasureContent`, as it does for WebView and Media. Everything the package
uses on `Window` and `Application` is public already.

Licences: XTerm.NET, Porta.Pty and `Microsoft.Windows.Console.ConPTY` are all MIT. XTerm.NET's
LICENSE stays in `external/XTerm.NET/`, and the packages' third-party notices name all three.

### The patched engine

- **One source of truth.** `external/XTerm.NET/` is never edited by hand: every difference from
  upstream is a patch in `external/XTerm.NET.patches/`. So the tree is always reproducible from
  the pinned commit plus the patches. `vendor-xterm-net.ps1 -Verify` rebuilds it in a temporary
  folder and diffs it against what is committed; CI runs that, so a hand edit fails the build
  rather than vanishing at the next update.
- **Committed, not fetched.** The patched tree is in the repository, so a build needs no network,
  no git and no `git am`. Only the script touches upstream.
- **Its own assembly name.** The project builds `HultUI.Terminal.XTermNet.dll`, never
  `XTerm.NET.dll`, so an app that also references the stock package does not get two assemblies of
  one name. The namespaces stay `XTerm.*`: renaming them would make every patch and every update
  conflict. An app referencing both and naming an `XTerm` type directly gets CS0433 and needs an
  `extern alias`. `HultUI.Terminal`'s own API exposes no `XTerm` type, so only code reaching past
  the emulator seam can meet that.
- **Build settings.** At the pinned commit upstream has no package dependencies. The project is
  `IsAotCompatible`, as the spike's publish showed it can be, and builds with no warnings. The
  repository's `src/` props apply to it, except the XML documentation file: upstream never builds
  one, and its comments produce 51 doc-comment warnings, so it is off for this project. Every
  such setting lives in the csproj, never in a vendored file. The package carries upstream's
  licence as `licenses/XTerm.NET/LICENSE`.
- **Updating upstream.** `vendor-xterm-net.ps1 -Commit <sha>`:
  1. Clones upstream and checks out the commit.
  2. Applies the patches with `git am --3way`. It stops on a conflict for a person to resolve,
     and the resolved series is exported back into `XTerm.NET.patches/`.
  3. Runs XTerm.NET's test suite.
  4. Only on a pass, replaces `external/XTerm.NET/` and the version file.

  An update is a deliberate act, not a dependency bump: it is reviewed as the diff of the
  vendored tree.
- **XTerm.NET's own tests run in CI** as `tests/HultUI.Terminal.XTermNet.Tests`, compiled from
  the vendored test sources. Both use xunit v2, as the repository does, and the suite takes about
  3 s. A patch that breaks upstream's expectations fails the build, not a user.
- **Porta.Pty stays a NuGet package.** Its one problem (quoting, problem 4) is solved in
  `HultUI.Terminal.Pty` without patching it.
- **Where the patches start.** The spike's `spikes/XtermNet/patches/` is the measured original;
  step 1 moves the series to `external/XTerm.NET.patches/`, where it is maintained from then on.

## The seams inside the package

```csharp
namespace HultUI.Terminal;

public interface ITerminalConnection : IDisposable   // the bytes; threads are the controller's
{
    Stream Output { get; }                // read by the controller's reader thread, blocking
    Stream Input { get; }                 // written by its writer thread only
    TerminalConnectionTraits Traits { get; } // ReflowsItself, WantsWin32Input (both ConPTY)
    void Resize(int columns, int rows);   // UI thread, with the emulator's resize
    void Terminate();                     // end the program; its output then ends
    event Action<int?>? Exited;           // exit code when there is one
}

internal interface ITerminalEmulator : IDisposable   // NOT thread-safe: the controller's lock guards it
{
    void Feed(ReadOnlySpan<byte> output); // raises Event synchronously, inside the lock
    void Resize(int columns, int rows);
    void CopySnapshot(TerminalSnapshot into); // the viewport's cells, cursor, modes, scroll position
    string? EncodeKey(in TerminalKey key);    // legacy, DECCKM, kitty: engine state decides
    string? EncodeMouse(in TerminalMouse mouse);  // null unless the program tracks the mouse
    string? EncodeFocus(bool focused);            // null unless it asked for focus events
    void Paste(string text);                  // bracketed when asked, ESC stripped; raises Reply
    void SetScrollback(int lines);            // the normal screen's, also while the alternate is up
    void ScrollViewport(int lines);
    void ScrollToBottom();
    string GetText(TerminalPosition start, TerminalPosition end);  // wrapped rows joined
    (TerminalPosition Start, TerminalPosition End) WordAt(TerminalPosition at);
    (TerminalPosition Start, TerminalPosition End) LineAt(TerminalPosition at);
    event Action<TerminalEvent>? Event;       // Reply, Title, Bell, Directory, ClipboardWrite,
                                              // ScreenSwitched
}
```

- **The snapshot.** `TerminalSnapshot` is engine-neutral: per cell a code point or a grapheme
  cluster, width, resolved foreground and background (bold-as-bright and inverse already applied),
  flags, underline style and colour; plus the cursor, the modes the element needs (mouse
  tracking, focus events, bracketed paste, alternate screen, synchronized output, win32-input), the
  scroll position, and the lines dropped off the top so far. A selection is held in absolute lines
  (dropped + row), so it stays on its text while output scrolls. Copying 200 × 60 out of
  XTerm.NET under the lock took 12.6 µs, and the renderer works from the copy with the lock
  released.
- **The XTerm.NET adapter** maps the encoding the spike pinned down: palette mode 0 with 256/257
  as the defaults, RGB mode 1, and X == Cols clamped to the last column with `PendingWrap` set.
- **The emulator seam is internal.** Step 2 built it against XTerm.NET alone. It becomes public
  only when it has been checked against libghostty-vt's render-state API; until then nothing
  outside the package can implement it, so changing it breaks nobody. `ITerminalConnection` is
  public: an application's own transport is the point of it.
- **Pipes.** `TerminalPipe` is the in-memory stream an in-process program and the tests use. It is
  unbounded by default; given a capacity, a write waits for the reader, as a real pipe makes a
  program wait. The sample's demo shell uses 64 KiB, so its `seq` runs at the terminal's pace
  instead of queueing its whole output.

## Threading

The shape is the spike's `Session`, which measured every rule below.

- **The lock.** One lock guards the emulator. XTerm.NET is not thread-safe; its own `BufferSearch`
  says so.
- **The reader.** A dedicated thread does blocking reads of up to 256 KiB and calls
  `Feed` under the lock.
  - **Why dedicated.** The Avalonia host measured a pooled `ReadAsync` at 7.5 s time-to-first-output
    under load against 137 ms, and losing output.
  - **How long it holds the lock.** During a 64 MiB flood: 0.18–0.56 ms per 62 KB read at p50, at
    most 4.4 ms. So the UI thread never waits longer than that to copy the viewport out.
- **Waking the UI.** After `Feed`, the reader **releases the lock, then** sets a dirty flag and
  wakes the UI, coalesced with an `Interlocked` flag, through the thread pool → `InvokeOnMainThread` →
  `MarkPaintDirty`.
  - The order matters. During a live resize the frame runs inside SDL's event watcher, and taking
    the emulator lock makes the reader "a thread the frame waits for"; THREADING.md says such a
    thread must not post. Posting after the release is safe; posting under the lock deadlocks.
- **The writer.** A dedicated thread drains a queue. Everything sent to the connection goes
  through it: typed keys, pastes, and the emulator's replies (DA, DSR, kitty), which `Feed`
  raises on the reader thread.
  - **Measured.** A 1 MiB paste written inline on the reader thread deadlocked permanently at
    256 KiB (64 KiB got through), and Kill did not free it. Written on the UI thread, it blocked
    the UI 0.9 s. Through the queue, `Send` returned in 0.1 ms and the child had it all in 1.24 s.
- **Teardown,** in this order:
  1. Stop accepting writes and let the writer finish.
  2. Kill the child.
  3. Keep reading until EOF.
  4. Close the connection (ClosePseudoConsole).
  5. Dispose the emulator under the lock.

  In any other order ClosePseudoConsole can wait forever on output nobody reads (measured: it
  hung for 10 s with the reader stuck).
- **Exit.** Exit is reported once, after the output has ended, with the connection's exit code.
  The Avalonia host found EOF can beat the reap and read a false 0, so at EOF the controller waits
  up to 500 ms for the code rather than report a wrong one; a connection that never reports one
  (a plain `StreamConnection`) ends with none.
- **Synchronized output (mode 2026).** While it is set, the element keeps drawing its last
  complete snapshot, for at most 150 ms (terminals use 100–200 ms). This stops editors tearing
  mid-redraw. The limit's clock is a tween, so it runs on the frame loop's time (virtual under a
  headless session) and wakes a frame when it runs out; past it the live screen shows until the
  program closes the update. Built in step 2.

## Rendering

The element draws the snapshot copied at the start of its `Render`. All of it uses public
`IRenderContext` calls. This is what step 2 built; what it left for later says so.

1. **Metrics.**
   - **Cell width** is the font's own advance, measured over ten `M`s so a fractional advance is
     kept, and **not** rounded. A row's glyphs step by that advance (item 3), so a rounded cell
     would drift from the text by up to half a pixel a column. Background and selection edges are
     snapped to device pixels instead, so neighbouring fills meet without a seam.
   - **Cell height** is the font's line height, rounded up to whole device pixels, so rows land
     on pixel edges.
   - **Grid size.** Columns and rows are the element's content box divided by the cell, truncated.
     A change is applied after the frame, never inside it: it reaches the program, and ConPTY
     answers a resize synchronously.
   - **Not yet: window queries.** `CSI 14 t` and `CSI 16 t` should be answered from the grid, not
     the element: the strip below the last row belongs to no row.
2. **Backgrounds.** One `DrawRect` per run of equal background along a row, with default-background
   runs skipped.
3. **Text.** One `DrawText` per **row**, with a `TextSpan` per change of style: colour (dim as
   half alpha), weight, slant, underline, strikethrough. One call per row rather than per run,
   because then every glyph shares the row's baseline and steps by the font's advance, which is
   the cell, with no position worked out per run.
   - A cell the primary font may not have is drawn on its own at its column, clipped to it, with
     a space in its place in the row: a wide character, a grapheme cluster, and anything outside
     the ranges every default terminal font covers (Latin, IPA, Greek, Cyrillic, and the common
     dashes, quotes, bullet and ellipsis). Problem 18 is why the ranges are that narrow.
   - Such a glyph sits on its own font's baseline, which can be up to about a pixel off the row's.
     Seam T2 would fix that; nothing else needs it.
   - Right-to-left letters fall outside the ranges, so they are drawn one cell at a time and never
     reordered, which is what a terminal must do.
4. **Box drawing and blocks** (U+2500–259F) are drawn as geometry with `DrawRect`, not from the
   font, so lines meet across cells: light, heavy and double lines with device-pixel thickness,
   eighths, shades and quadrants. This is what Windows Terminal, kitty and Ghostty do. **Not yet:**
   rounded corners (drawn square), dashed and diagonal lines (left to the font), the powerline
   triangles.
5. **Decorations the spans do not cover.** Overline is a `DrawRect` one layout unit thick along the
   top of the cell. **Not yet:** curly, dotted, dashed and double underlines (drawn single), underline
   colour (drawn in the text colour), blink (drawn steady).
6. **The cursor.** A block (with the character under it in the background colour), bar or
   underline, as the program asks (DECSCUSR). When the element is unfocused, a hollow block
   whatever the shape. **Not yet:** blinking.
7. **Overlays** within the element: the selection, and the IME preedit at the cursor. **Not yet:**
   search hits (`TextSearch.Decorate`, see below) and a hovered hyperlink's underline.
8. **Clipping.** The element clips to its content box; a special cell to its own cells. With
   `ReflowCursorLine` on, the engine no longer leaves a width-2 cell in the last column (the stock
   engine's cut did), but a renderer that trusts `Width` there would draw past the grid if it ever
   did.

**Cost.**

- **Reading.** Copying 200 × 60 out of the engine costs 12.6 µs; walking it into styled runs,
  21 µs.
- **Drawing, measured in step 2** on a real window (Windows 11, RTX 4080, OpenGL, 1.5 × scale, the
  window 1500 × 960, so a 207 × 58 grid), with `HultUI.Samples.Terminal --bench`. Render is
  recording the draw calls; Flush is Skia rasterising them. Both are the frame's whole cost, the
  header included.

  | Workload | Frames/s | Render, median / p95 | Flush, median / p95 | Output taken |
  |---|---|---|---|---|
  | Full-screen redraw: every cell changes every frame, a new colour every 8 columns (about 1 560 runs a frame), paced at 120 a second | 120 | 2.71 / 3.32 ms | 4.26 / 5.34 ms | 3.2 MB/s |
  | Scrolling flood: `seq` as fast as the terminal reads it | 120 | 1.38 / 4.36 ms | 0.28 / 0.46 ms | 67.3 MB/s |

  - **So no seam is needed.** The heaviest frame a terminal draws costs about 7 ms, inside a
    120 Hz frame, and a flood leaves the UI drawing at the display's rate while the reader takes
    67 MB/s. T2 and T3 stay unbuilt.
  - **CPU.** The process used 1.1 cores in the redraw and 1.5 in the flood, but the demo program
    runs in it too: building and writing 67 MB/s of output is part of that.

**Frame pacing.** A flood marks the element dirty at most once per frame (coalesced in the reader).
The render loop already paces to the display.

## Input

**Keys.** `KeyDownEvent` → `TerminalKey` → `emulator.EncodeKey` → the writer queue. This is the
spike's `TerminalKeys` mapping, and it matched xterm's encodings on 45 of 49 keys, 49 of 49 with
patch 0003:

- **Printables.** Printable keys without Ctrl or Alt send nothing on KeyDown; their text comes
  from `TextInputEvent`, as UTF-8. That way layouts, dead keys and IMEs work, and nothing is typed
  twice.
- **Everything else.** Ctrl and Alt combinations, navigation keys, F-keys and Escape are encoded
  from the key.
- **Kitty.** The kitty keyboard protocol is used when the application pushes it; the engine
  tracks the flags.
- **Tab** reaches KeyDown before focus traversal (FOCUS.md); the element stops it.
- **Shortcuts.** Ctrl+Shift+C and Ctrl+Insert (copy), Ctrl+Shift+V and Shift+Insert (paste), and
  Shift+PageUp/PageDown (scroll the scrollback) are the element's own; Ctrl+Shift+F comes with
  search. Any other key with Ctrl, Alt or Super held, and F1–F12, gets the WebView treatment:
  offered to the ancestors first; a handler that stops one keeps it from the terminal. Built in
  step 2.

**Escape and win32-input-mode on Windows (problem 11).** The out-of-band ConPTY asks for
win32-input-mode (`CSI ? 9001 h`) at startup.

- **The race.** A bare ESC byte is ambiguous to ConPTY: it may start a sequence. Through Git's
  vim, the first key after a resize was lost in 2 to 9 runs of 10. Sent as a win32-input-mode key
  press, ESC was clean 10 of 10.
- **The plan.** While the connection says `WantsWin32Input` and the emulator's mode is on, send
  as win32-input-mode events the keys whose VT encodings are ambiguous or lossy: Escape, Alt+key,
  Ctrl+Space, Ctrl+Backspace. Keep text as UTF-8.
- **What it needs.** A virtual-key code and a scan code. The scan code is already on
  `KeyDownEvent` (as a USB HID usage). On Windows the element maps it to a set-1 scan code and
  then through `MapVirtualKeyW(MAPVK_VSC_TO_VK_EX)`.
- **Verify** against Windows Terminal (which sends every key this way) with the same vim, before
  widening it to all keys.

**The framework's key model (problem 10).**

- **Today.** `KeyCode` is translated from the SDL scancode, and nothing reads SDL's layout keycode.
- **Why that breaks Ctrl+letter.** The element sends what the key's US position types. On QWERTZ,
  Ctrl+Z (suspend, undo) arrives as Ctrl+Y. AZERTY and Dvorak are worse.
- **Why it breaks AltGr.** Left and right Alt are one modifier, so AltGr (which Windows reports as
  Ctrl+Alt) cannot be told from Ctrl+Alt. On a Swedish keyboard `@ $ { [ ] } \ | ~` are all AltGr.
- **The interim rule,** until K2 lands: when a `TextInputEvent` follows a Ctrl+Alt key, the text
  wins and the key sends nothing. Seams K1–K4 below.
  - **How step 2 built it.** A Ctrl+Alt key that would type a character is held back and flushed
    by work posted to the UI thread. A frame dispatches all of its input events before it runs
    posted work, so the text a key typed (if any) arrives first and drops the held key. A
    Ctrl+Alt key that types nothing is sent one pass of the loop late.
  - The test fails without the hold: AltGr+2 sent the chord's bytes and then `@`.

**Mouse.**

- **When the application tracks the mouse** (`?1000`, `?1002`, `?1003`, with SGR encoding),
  presses, drags, releases and the wheel go to it. Shift held keeps the selection for the user,
  as in every terminal.
- **Otherwise** the pointer selects:
  - drag selects a range;
  - a double click selects a word;
  - a triple click selects a line;
  - **not yet:** Alt+drag selects a block.

  A selection is held in absolute lines, so it stays on its text while output scrolls; typing
  and switching screens drop it.
- **The wheel otherwise.** It scrolls the scrollback, three lines a notch, adding up a precise
  wheel's fractions. On the alternate screen it sends arrow keys. XTerm.NET does not track
  alternate scroll (`?1007`), so the element always does this there, as most terminals do by
  default.
- **Not yet: links.** Ctrl+click opens an OSC 8 link through an app callback; the default does
  nothing.

**Clipboard.**

- **Copy** puts the selection's logical text on `Window.Clipboard`, with wrapped rows joined and
  trailing blanks dropped. `TerminalElement.Copy()` and `SelectedText` give an app the same.
- **Paste** goes through the engine, which brackets it when asked and strips ESC (measured), and
  then through the writer queue.
- **OSC 52 writes** raise `TerminalController.ClipboardWriteRequested`, which an app can cancel;
  otherwise the element puts the text on its window's clipboard. OSC 52 reads are off, as the
  engine defaults them.
- **No PRIMARY selection.** HultUI has none, and Linux middle-click paste waits for one.

**IME.** `WantsTextInput` is true. While focused, the element calls `TextInputHost.SetInputArea`
with the cursor cell's rectangle every render (as WebView does) and draws the `TextEditingEvent`
preedit at the cursor. Built in step 2.

**Focus.** On focus and blur the element sends `EncodeFocus` when the application asked for focus
events (`?1004`), and draws a hollow cursor while unfocused. Built in step 2.

## Resize, scrollback, memory

**Resize policy (problem 2).**

- **Height changes are applied at once:** about 0.02 ms at any size.
- **Width changes are applied at once while the buffer is small.**
  - **Measured with patch 0001:** a one-column change costs 0.25 ms at 1 000 × 120, 2.3 ms at
    10 000 × 120, and 3.6 ms at 10 000 × 200 (the stock engine took 25 ms there).
  - **Above about 20 000 lines** of effective scrollback (31 ms at 100 000 × 200) a width change
    waits 100 ms after the last one, as VS Code's terminal does for large buffers. Meanwhile the
    old grid is drawn clipped or padded.
  - That is a hold on the reflow, not on frames: every frame of the drag still renders. The
    resize-stretch lesson was that frames must not stop inside the modal loop.
- **The emulator and the connection resize together, always.** ConPTY reflows its own buffer and
  the emulator has to agree. That agreement is exactly what made `XYZ` land after the command in
  the patched run.

**Scrollback (problem 3).**

**DECIDED (scrollback): 5 000 lines by default, and configurable** (2026-10-03). That is about
24 MiB per terminal at 200 columns and 10 MiB at 80. The alternatives weighed were 10 000 (Windows
Terminal's 9 001 is close to it; 47 MiB) and 1 000 (VS Code's default; under 5 MiB, but short for
a build log).

Configurable means both at construction and while the terminal runs:

- **The options.**
  - `TerminalOptions.Scrollback`: lines, default 5 000; 0 turns scrollback off.
  - `TerminalOptions.MaxScrollbackBytes`: an optional memory budget, default none.
- **On the controller.** `TerminalController.Scrollback` and `TerminalController.MaxScrollbackBytes`
  are settable signals, initialised from the options, so a settings page binds to them. A change
  takes effect at once, under the lock, through the normal buffer's `TerminalBuffer.SetScrollback`.
  - **Shrinking** trims the oldest lines first, the engine's rule, so the screen and the newest
    history stay.
  - **Growing** keeps everything and lets the next output fill the room.
  - **On the alternate screen** the change is held and applied to the normal buffer when the
    program returns. The adapter keeps the normal buffer from the start (XTerm.NET makes both
    buffers once, and RIS clears them in place), so it can always reach it.
- **How the two combine.** When a byte budget is set, the effective line count is the smaller of
  `Scrollback` and the budget divided by the measured cost of a line at the current width (about
  24.7 B per cell, measured). It is recomputed at every width change, so a wide window keeps fewer
  lines in the same memory, not more memory.
- **Bounds.** Both are validated: a negative value throws; a line count above 1 000 000 is clamped
  and says so in the docs. At 200 columns a million lines is 4.6 GiB, and a width change there
  costs seconds even with patch 0001.
- **The resize policy follows the effective line count** (see *Resize policy*), so raising the
  scrollback also moves the point where width changes start to wait.
- **A later engine improvement** (one more patch): store a line that scrolls off at its content
  length instead of the width. A typical shell line is short, and the 24-byte cell would stay.

## Search

The find bar uses `HultUI.Core`'s `TextSearch`, so it behaves like the four apps' bars:

- **The text.** `Bind(lineCount, 1, textAt)` with logical lines (wrapped rows joined) read from a
  snapshot of the scrollback. It is re-bound, debounced, as output arrives.
- **Hits.** They map back to rows and columns through the wrap.
- **Painting.** It uses `Decorate`'s background tints.
- **Known limits.** Matching is per logical line and synchronous on the UI thread. That is fine at
  5 000 lines; **verify** at 100 000. XTerm.NET's own `BufferSearch` measured 3.7 ms per search
  and is the fallback if it is not.

## Accessibility

HultUI has no UIA TextPattern and no AT-SPI Text interface; text elements expose their whole text
as a Value. It has no live-region or announcement API either.

- **First version.** The element is `AccessibilityRole.Document`, named after the session's title
  unless the app names it. Step 2 built that much. **Not yet:** the visible screen as its value,
  so readers can read the screen; they are not told about new output either way.
- **Seams A1 and A2 (below)** give it what Windows Terminal has: a text range model a reader can
  review by line and word, and announcements of new output.
- **TextArea benefits too.** A1 is the same work TextArea needs; its whole-text Value is the same
  limitation.

## Windows process launch (`HultUI.Terminal.Pty`)

- **Default shell:** pwsh, then powershell, then cmd on Windows; `$SHELL`, then `/bin/bash`,
  elsewhere.
- **The command line** is built with the MSVCRT rules and passed with `VerbatimCommandLine`
  (problem 4).
- **Environment:** `TERM=xterm-256color`, `COLORTERM=truecolor`, plus the app's own variables.
- **ConPTY.** The connection reports `WantsWin32Input` and `ReflowsItself`. Out-of-band ConPTY 1.24
  is the default, with Porta.Pty's fallback to the in-box one. `PORTAPTY_CONPTY` stays the override.
- **Packaging.** A build target removes the other architecture's `OpenConsole.exe` from a
  RID-specific publish (problem 16). Porta.Pty already answers ConPTY's startup DA1 query and
  hides it from the stream; without that the first output waits 3 s.
- **AOT.** Porta.Pty and XTerm.NET publish with no IL2xxx/IL3xxx warnings; the spike's executable
  was 3.76 MB.

## Framework seams

Each is useful beyond the terminal. No terminal package may need anything not listed here; if
implementation finds another, it goes here first. Step 2 needed none. T1 is needed by step 3;
K1–K4 and A1–A2 make later steps right rather than possible.

**T1: monospace cell metrics and a default monospace family (`HultUI.Core`,
`HultUI.Rendering.Skia`).**

- **What is missing.** `SkiaFontCollection` resolves a family name with `SKTypeface.FromFamilyName`
  and falls back to the default typeface, so `"monospace"` works only where fontconfig maps it, and
  not on Windows. Apps pick concrete fonts themselves (Junction: Consolas, Menlo, …).
- **What to add:**
  - `FontFamilies.Monospace`, resolved per platform: Cascadia Mono, then Consolas on Windows;
    fontconfig's `monospace` on Linux; Menlo on macOS.
  - `TextMeasure.Monospace(family, size)`, returning advance, ascent, descent, line gap, and
    underline position and thickness.
  - Whether the family has a glyph for a code point. `IFontCollection.HasGlyph` exists inside the
    text pipeline; the element cannot reach it, which is why problem 18 is solved with fixed
    ranges.

**T2: positioned text runs (`HultUI.Core`). Step 2 measured no need for speed** (see
*Rendering*); what is left for it is the baseline of a glyph drawn on its own.
`IRenderContext.DrawTextRun(string text, Point origin, TextRunStyle style)`: shape once (cached),
draw at a point, with no rectangle, layout or wrapping.

**T3: a cached picture per row (`HultUI.Core`). Step 2 measured no need.**
`IRenderContext.DrawCached(key, Size, Action<IRenderContext>)`: replay a recorded picture for a key
seen before, record it otherwise. A content hash makes a scroll redraw one row.

**K1: the layout key on `KeyDownEvent` (`HultUI.Platform.SDL3`, `HultUI.Core`).**

- **What to add.** `KeyDownEvent.LayoutKey`: the code point SDL's layout keycode (`evt->key.key`)
  names, for keys that have one.
- **Why.** Ctrl+letter and Alt+letter use it, so the terminal sends what the keycap says on QWERTZ,
  AZERTY or Dvorak. Shortcut matching in every app has the same need.

**K2: left/right modifiers and AltGr.**

- **What to add.** `KeyModifiers.AltRight`, `CtrlRight` (or a `Location`), plus an `AltGr` flag
  from `SDL_KMOD_MODE` where SDL sets it, and from right-Alt + left-Ctrl on Windows (Windows
  Terminal's heuristic).
- **Why.** Without it a terminal cannot tell `@` on a Swedish keyboard from Ctrl+Alt+2.

**K3: keypad and F13–F24 key codes.** `KeyCode.Numpad0`…`NumpadEnter`, `F13`…`F24`. Today they
arrive as `Unknown` with only a scan code, so DECKPAM (application keypad) cannot be honoured.

**K4: the text a keystroke produced (`HultUI.Platform.SDL3`).**

- **What to add.** `KeyDownEvent.Text`: the `TEXT_INPUT` that follows the `KEY_DOWN` in the same
  pump, found with `SDL_PeepEvents`.
- **Why.** A key and its character can then be handled as one. Win32-input-mode needs the
  character in the key event, and K2's AltGr rule needs to know that text is coming.
- **Verify** that SDL queues the two in the same pump on Windows, X11 and Wayland, IME excluded.

**A1: a text range model for screen readers (`HultUI.Core`, both bridges).**

- **What to add.** UIA `ITextProvider`/`ITextRangeProvider` on Windows and AT-SPI `Text` on Linux,
  over an element-supplied model: lines, a caret, ranges by character, word and line.
- **Who uses it.** The terminal and TextArea first.

**A2: announcements (`HultUI.Core`, both bridges).**

- **What to add.** `Element.Announce(text, politeness)`: `UiaRaiseNotificationEvent` on Windows, an
  AT-SPI announcement or live region on Linux.
- **Who uses it.** The terminal announces new output in batches. Toasts can use it too.

## Testing

- **`HultUI.Terminal.Tests`** (headless; built in step 2):
  - The real XTerm.NET emulator fed scripted bytes through a `FakeConnection`, which records what
    was sent; the controller's real reader and writer threads run against it.
  - Waiting is deterministic: the controller counts the bytes it has fed and whether a wake is
    pending, so a test waits for exactly its own output and the UI thread's handling of it,
    never for a time.
  - The element is tested through the headless window: the grid it sizes, keys (with the AltGr
    rule), text, IME, selection and copy, paste bracketing, the wheel, mouse and focus reports,
    titles, and what it draws, through the recorded render log.
  - Still to add from the spike: question 3's reflow checks, and the resize policy when it is
    built.
- **`HultUI.Terminal.Pty.Tests`:**
  - Real shells: pwsh or cmd on Windows, sh or bash elsewhere; skipped when missing.
  - Exit codes, resize seen by the shell, the narrowed-prompt check, and a 1 MiB paste through
    the writer queue.
  - A teardown that must not hang (a time limit on the test, not a sleep).
- **Real-window UI tests,** for IME placement and the drag-resize cost.
- **Linux CI** runs both test projects; `[HanFontsFact]`'s pattern applies to wide-glyph rendering
  tests.

## Implementation order

0. **Spike: done on Windows 2026-10-03** (`spikes/XtermNet`), with the four engine patches measured.
   The Linux half is open:
   - forkpty through Porta.Pty's shim;
   - the cut on a raw pty, and whether bash's SIGWINCH redraw agrees with `ReflowCursorLine`;
   - tmux, htop and vttest byte streams;
   - the paste-deadlock threshold (the N_TTY buffer is 4 KiB);
   - linux-x64 AOT.

   It runs on the Linux machine before step 4 ships there; `q7` and `q8` already pick bash, vim
   and sh off Windows.
1. **The patched engine: done 2026-10-03.**
   - **`external/`:** `XTerm.NET` holds upstream `56ea809` with patches 0001–0004 (moved from the
     spike): 167 files. Beside it are `XTerm.NET.version`, a README, and a `.gitattributes` that
     keeps git from converting the vendored bytes. Upstream's `Charsets.cs` carries CR bytes that a
     line-ending conversion would corrupt.
   - **`scripts/vendor-xterm-net.ps1`.** It generated that tree from a fresh shallow clone, after
     upstream's suite passed on the patched source. It has three further modes:
     - `-Commit <sha>`: move to a new upstream commit.
     - `-FromClone <dir>`: finish an update whose patch a person resolved.
     - `-Verify`: rebuilds in a temporary folder and compares bytes. It was seen to fail on an
       edited file and on an extra file, and to pass again once both were undone. CI's Windows job
       runs it before the build, because that runner has pwsh and git.
   - **`src/HultUI.Terminal.XTermNet`:** `HultUI.Terminal.XTermNet.dll`, no warnings.
   - **`tests/HultUI.Terminal.XTermNet.Tests`:** upstream's suite against it, 2 238 passing and one
     skipped, as upstream. It is in the solution and the cross-platform filter.
   - **Found on the way:** upstream's
     `SelectionTests.SelectionText_UsesLineFeedLineEndings` has no `[Fact]` and has never run.
     xUnit1013 is suppressed in the test project rather than the file edited; a fix would be a
     patch of its own.
2. **`HultUI.Terminal`: done 2026-10-03.**
   - **The package:** the emulator seam (internal) and the XTerm.NET adapter with
     `ReflowCursorLine` on; the controller with its two threads, the teardown order and the
     scrollback signals; `StreamConnection`, `TerminalPipe` and `FakeConnection`; the element,
     with the renderer, keys, text, IME, selection, copy and paste, scrollback scrolling, and
     three things the plan had in step 5: synchronized output, mouse reporting and the OSC 52
     policy. No warnings; `HultUI.Core` grants it internals as planned.
   - **Tests:** `tests/HultUI.Terminal.Tests`, 72 headless tests, in the solution and the
     cross-platform filter. Negative controls: the AltGr test fails without the hold, the
     scrollback tests without patch 0005, and the disposed-controller test without its fix.
   - **The sample:** `samples/HultUI.Samples.Terminal`, a terminal on an in-process demo shell
     (`help` lists what it shows) behind a header bound to the controller's signals. The
     screenshot driver mounts it as `--app terminal`.
   - **Measured on a real window** with the sample's `--bench` (see *Rendering → Cost*): one
     `DrawText` per row is fast enough, so T2 and T3 are not built.
   - **Found on the way:**
     - Problem 17, a row shrink leaving rows below the screen, fixed as engine patch 0005.
     - Problem 18, symbols the font lacks drifting their row, fixed in the element.
     - An unbounded in-process pipe let a fast program queue its whole output; `TerminalPipe`
       takes a capacity now.
     - A disposed controller left the element swapping in a stale frame; it now keeps its last
       screen.
3. **Seam T1,** then the renderer's default font and cell metrics on all three platforms.
4. **`HultUI.Terminal.Pty`:**
   - `PtyConnection` on Windows and Linux, with quoting, exit and teardown;
   - the arm64/x64 `OpenConsole.exe` trim;
   - real-shell tests;
   - the sample's shell, and NativeAOT `--check` as the WebView sample does.
5. **Resize policy, search, hyperlinks**, and what step 2 left as **not yet** in *Rendering* and
   *Input*: the styled underlines and underline colour, blink, block selection, window-size
   queries, and the screen as the accessible value.
6. **Windows input:** win32-input-mode for the ambiguous keys, verified against Windows Terminal
   with Git's vim. Then seams **K1–K4**.
7. **Accessibility:** seams **A1** and **A2**, and the element's text range model and output
   announcements.
8. **Later:**
   - images (Sixel and Kitty, through a `PixelFrameSink` per image);
   - `HultUI.Terminal.Ssh` (Vesper's SFTP brief makes SSH the next transport);
   - libghostty-vt as a second emulator once it tags a release.
