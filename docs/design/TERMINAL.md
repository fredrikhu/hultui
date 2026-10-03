# HultUI.Terminal — Design

A terminal emulator as an element, shipped as packages an application opts into, the way WebView
is: an application that never shows a terminal carries no emulator, no pseudoconsole and no
natives.

Written 2026-10-03 from the spike `spikes/XtermNet` (its README has the answers, `results/` the raw
runs, `patches/` the engine fixes), which ran on Windows only. Every number here comes from that
spike unless it says otherwise.
Where a claim still needs confirming it says **verify**. Decisions that are the owner's are marked
**TO DECIDE**, each with a recommendation.

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
| 3 | Scrollback is stored full width, about 25 B per cell: 47 MiB at 10 000 × 200. | Default 5 000 lines; a byte budget converted to lines at each width | element | proposed |
| 4 | Porta.Pty's default Windows quoting wraps every argument: `cmd "/c" "exit 3"` exits 1. | MSVCRT quoting, passed verbatim | `HultUI.Terminal.Pty` | measured |
| 5 | A paste written by the reader thread deadlocks for good at 256 KiB; ClosePseudoConsole then hangs too. | A writer thread with a queue; a fixed teardown order | controller | measured |
| 6 | A reader that wakes the UI while holding the engine lock is the THREADING.md live-resize deadlock. | Wake only after releasing the lock | controller | proposed (rule) |
| 7 | The engine has no damage tracking. | Copy the viewport out under the lock; cache drawn rows by content hash | element | measured cost: 12.6 µs copy, 53 µs hash |
| 8 | SGR 53/55 (overline) never reach a cell. | Two switch cases | engine patch 0002 | measured |
| 9 | Alt+Enter and Alt+Backspace drop the Alt; Ctrl+/ sends `/`; Ctrl+Backspace equals Backspace. | Fixed in the legacy encoder | engine patch 0003 | measured: 49/49 |
| 10 | HultUI's key event is a scancode position: Ctrl+letter is wrong on AZERTY/QWERTZ; there are no keypad codes; AltGr looks like Ctrl+Alt. | Seams K1–K4 | framework | proposed |
| 11 | A bare ESC through the out-of-band ConPTY races: the first key after a resize was lost in 2–9 runs of 10. | Send ESC (and the other ambiguous keys) as win32-input-mode events when ConPTY asks for them | element, Windows | measured for ESC: 10/10 |
| 12 | Engine facts a renderer must know: default colours are palette 256/257, the deferred wrap is X == Cols, and a width-2 cell can end a row. | Renderer rules | element | measured |
| 13 | HultUI has no positioned-glyph-run or offscreen-surface API for elements. | DrawText per run first, measured; seams T2/T3 only if it is too slow | element; framework | proposed |
| 14 | HultUI has no monospace default font and no cell-metrics API. | Seam T1 | framework | proposed |
| 15 | HultUI has no UIA TextPattern, no AT-SPI Text, and no announcements. | Seams A1/A2; until then, role and value | framework | proposed |
| 16 | Porta.Pty puts arm64's `OpenConsole.exe` (1.1 MB) in a win-x64 publish. | A build target that keeps only the publishing RID's copy | `HultUI.Terminal.Pty` | proposed |

**TO DECIDE (engine patches).** Patches 0001–0004 are written to go upstream as they are. All
2 238 of XTerm.NET's tests pass with them, 17 of those new. XTerm.NET is MIT, active (last commit
2026-09-27), and Porta.Pty is by the same author. The options:

1. **Offer them upstream now, and build steps 1–3 against the stock 2.0.2 package meanwhile.**
   Problem 1 then stays visible on Windows until a release has `ReflowCursorLine`. Problem 2 is
   covered by the resize policy alone. Problems 8 and 9: overline does not draw, and the element
   carries the key overrides.
2. **Offer them upstream, and build against a fork until they land:** `src/External/XTerm.NET`, a
   subtree of upstream plus the patches, compiled into `HultUI.Terminal`. Everything works from
   day one, but the fork has to be kept in step with upstream.
3. **Fork for good.** Not recommended: XTerm.NET moves fast (471 commits, the last a week ago)
   and has done the conformance work.

**Recommended: 1, falling back to 2** if upstream has not released them by the time step 4
(the pty) lands. Problem 1 is invisible until a real shell is attached.

## Packages

```
src/
  HultUI.Terminal/          controller, element, renderer, key mapping, emulator seam,
                            XTerm.NET emulator, StreamConnection, fake connection — no natives
  HultUI.Terminal.Pty/      PtyConnection: Porta.Pty (ConPTY + OpenConsole on Windows,
                            the forkpty shim elsewhere), quoting, teardown
tests/
  HultUI.Terminal.Tests/        headless: scripted bytes through the real emulator
  HultUI.Terminal.Pty.Tests/    real shells; skipped where the shell is missing
samples/
  HultUI.Samples.Terminal/      a shell, a find bar, an overlay over the grid
```

The split differs from WebView's on purpose:

- **Emulator.** WebView keeps every engine out of its core package because each one is native,
  platform-specific and large. XTerm.NET is 302 KB of managed code with no natives and works
  everywhere, so it ships inside `HultUI.Terminal`.
- **Pseudoconsole.** This is what carries natives: `conpty.dll` + `OpenConsole.exe` (2.4 MB
  across x64 and arm64) and the Unix shims. It is the opt-in piece. An app whose terminal talks
  SSH or a serial port never loads ConPTY.
- **libghostty-vt.** It would come later as `HultUI.Terminal.Ghostty` plus `.Natives`, behind
  the same emulator seam, once it tags a stable release (its header still says the API "is not
  yet stable and is definitely going to change").

`HultUI.Core` grants `InternalsVisibleTo HultUI.Terminal` for `OnOwn`, `PatchFrom`,
`WantsTextInput` and `MeasureContent`, as it does for WebView and Media. Everything the package
uses on `Window` and `Application` is public already.

**TO DECIDE (packages).** The split above (recommended), or one package with Porta.Pty inside.
One package is simpler, but every app with a terminal would carry ConPTY's 2.4 MB whether it
runs a local process or not.

Licences: XTerm.NET, Porta.Pty and `Microsoft.Windows.Console.ConPTY` are all MIT. The package's
third-party notices name them.

## The seams inside the package

```csharp
namespace HultUI.Terminal;

public interface ITerminalConnection : IDisposable   // the bytes; threads are the controller's
{
    Stream Output { get; }                // read by the controller's reader thread, blocking
    Stream Input { get; }                 // written by its writer thread only
    void Resize(int columns, int rows);
    TerminalConnectionTraits Traits { get; } // e.g. WantsWin32Input (ConPTY), ReflowsItself (ConPTY)
    event Action<int?>? Exited;           // exit code when there is one
}

public interface ITerminalEmulator : IDisposable     // NOT thread-safe: the controller's lock guards it
{
    void Feed(ReadOnlySpan<byte> output); // raises Event synchronously, inside the lock
    void Resize(int columns, int rows);
    void CopyViewport(TerminalSnapshot into); // rows, cells, cursor, modes, scroll position
    string? EncodeKey(in TerminalKey key);    // legacy, DECCKM, kitty: engine state decides
    string? EncodeMouse(in TerminalMouse mouse);
    string? EncodeFocus(bool focused);
    byte[] EncodePaste(string text);          // bracketed, or announced (5522), ESC stripped
    event Action<TerminalEvent>? Event;       // Reply(bytes), Title, Bell, Directory, Clipboard,
                                              // Mark, CursorStyle, SynchronizedOutput
}
```

- **The snapshot.** `TerminalSnapshot` is engine-neutral: per cell a code point or cluster id,
  width, foreground, background, flags, underline style and colour, and a link id; plus the
  cursor, the modes the element needs (mouse tracking, focus events, bracketed paste,
  win32-input), and the scroll position. Copying 200 × 60 out of XTerm.NET under the lock took
  12.6 µs, and the renderer works from the copy with the lock released.
- **The XTerm.NET adapter** maps the encoding the spike pinned down: palette mode 0 with 256/257
  as the defaults, RGB mode 1, and X == Cols clamped to the last column with `PendingWrap` set.
- **The shape is a sketch.** It is refined in step 2 against XTerm.NET alone and checked against
  libghostty-vt's render-state API before it is called stable.

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
- **Exit.** Exit is reported once: from `ProcessExited` or from EOF, whichever comes first, after
  the child is reaped. The Avalonia host found EOF can beat the reap and read a false 0.
- **Synchronized output (mode 2026).** While it is set, the element keeps drawing its last
  complete snapshot, up to 150 ms (**verify** the value; terminals use 100–200 ms). This stops
  editors tearing mid-redraw.

## Rendering

The element draws the snapshot copied at the start of its `Render`. All of it uses public
`IRenderContext` calls.

1. **Metrics.**
   - **Cell size.** The cell width is the advance of the monospace font's `0`; the height is
     ascent + descent + line gap. Both are rounded to whole device pixels, so the grid's rows and
     columns land on pixel edges (the pixel-snapping rule: fills snap, text does not).
   - **Grid size.** Columns and rows are the element's content box divided by the cell, truncated.
   - **Window queries.** `CSI 14 t` and `CSI 16 t` are answered from the grid, not the element: the
     strip below the last row belongs to no row.
2. **Backgrounds.** One `DrawRect` per run of equal background along a row, with default-background
   runs skipped.
3. **Text.** One `DrawText` per run of equal style, positioned at its first column. A run breaks at
   every style change, at every width-2 cell, and at every cell the primary font cannot draw (so a
   fallback glyph's advance cannot push the rest of the row off its columns). Styles go through
   `TextSpan`: colour, weight, slant, underline, strikethrough.
4. **Box drawing and blocks** (U+2500–259F, plus the powerline triangles) are drawn as geometry
   with `DrawRect` and `DrawLine`, not from the font, so lines meet across cells. This is what
   Windows Terminal, kitty and Ghostty do.
5. **Decorations the spans do not cover:** curly, dotted and dashed underlines, underline colour,
   overline, as `DrawLine` segments.
6. **The cursor.** A block, bar or underline, from `CursorStyleChanged`, blinking when the
   application asks. When the element is unfocused, a block is drawn hollow.
7. **Overlays** within the element: the selection, search hits (`TextSearch.Decorate`, see below),
   a hovered hyperlink's underline, and the IME preedit at the cursor.
8. **Clipping.** Each row is clipped to the grid, because the stock engine's cut (problem 1) can
   leave a width-2 cell in the last column.

**Cost.**

- **Reading and hashing.** Walking 200 × 60 into styled runs costs 21 µs. Hashing every row's
  content costs 53 µs.
- **Drawing.** The spike did not measure `DrawText` for the 635 runs of a busy 200 × 60 screen.
  `SkiaRenderContext` caches shaped run blobs (2 048 entries), so a repeated run should cost
  little more than the draw.
- **Step 2 measures this first,** on a real window, with a full-screen SGR-heavy redraw and a
  scrolling flood:
  - **Seam T2** (positioned runs without layout) comes in only if DrawText's per-call layout
    shows up.
  - **Seam T3** (a cached picture per row, keyed by content hash) comes in only if drawing itself
    does. A scroll then redraws one row: the hash found every row of the scrolled frame in the
    previous one.

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
- **Shortcuts.** Ctrl+Shift+C/V, Shift+Insert and Ctrl+Shift+F are the element's own. The app's
  shortcuts get the WebView treatment: offered to the ancestors first; a handler that stops one
  keeps it from the terminal.

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

**Mouse.**

- **When the application tracks the mouse** (`?1000`, `?1002`, `?1003`, with SGR encoding),
  presses, drags, releases and the wheel go to it. Shift held keeps the selection for the user,
  as in every terminal.
- **Otherwise** the pointer selects:
  - drag selects a range;
  - a double click selects a word;
  - a triple click selects a line;
  - Alt+drag selects a block.
- **The wheel otherwise.** It scrolls the scrollback; on the alternate screen it sends arrow keys,
  if the application asked for alternate scroll (**verify** that XTerm.NET tracks `?1007`).
- **Links.** Ctrl+click opens an OSC 8 link through an app callback; the default does nothing.

**Clipboard.**

- **Copy** puts the selection's logical text on `Window.Clipboard`, with wrapped rows joined and
  trailing blanks dropped.
- **Paste** goes through `EncodePaste`, which brackets it and strips ESC (measured), and then
  through the writer queue.
- **OSC 52 writes** go through an app policy callback. OSC 52 reads are off, as the engine
  defaults them.
- **No PRIMARY selection.** HultUI has none, and Linux middle-click paste waits for one.

**IME.** `WantsTextInput` is true. While focused, the element calls `TextInputHost.SetInputArea`
with the cursor cell's rectangle every render (as WebView does) and draws the `TextEditingEvent`
preedit at the cursor.

**Focus.** On focus and blur the element sends `EncodeFocus` when the application asked for focus
events (`?1004`), and draws a hollow cursor while unfocused.

## Resize, scrollback, memory

**Resize policy (problem 2).**

- **Height changes are applied at once:** about 0.02 ms at any size.
- **Width changes are applied at once while the buffer is small.**
  - **Patched engine:** a one-column change costs 0.25 ms at 1 000 × 120, 2.3 ms at 10 000 × 120,
    and 3.6 ms at 10 000 × 200.
  - **Above about 20 000 lines** (31 ms at 100 000 × 200) a width change waits 100 ms after the
    last one, as VS Code's terminal does for large buffers. Meanwhile the old grid is drawn
    clipped or padded.
  - **Stock 2.0.2:** the threshold is about 1 000 lines.
  - That is a hold on the reflow, not on frames: every frame of the drag still renders. The
    resize-stretch lesson was that frames must not stop inside the modal loop.
- **The emulator and the connection resize together, always.** ConPTY reflows its own buffer and
  the emulator has to agree. That agreement is exactly what made `XYZ` land after the command in
  the patched run.

**Scrollback (problem 3).**

- **The default.** `Scrollback = 5000` lines: about 24 MiB per terminal at 200 columns, 10 MiB at
  80.
- **The budget.** `MaxScrollbackBytes`, when set, wins over the line count: at each width the
  element converts it to lines with the normal buffer's `TerminalBuffer.SetScrollback`, which
  trims the oldest. **Verify** the adapter can reach the normal buffer while the alternate screen
  is active.
- **A later upstream improvement:** store a line that scrolls off at its content length instead
  of the width. A typical shell line is short, and the 24-byte cell would stay.

**TO DECIDE (scrollback default).**

- **5 000 lines (recommended).** About 24 MiB at 200 columns.
- **10 000.** Windows Terminal's 9 001 is close to it; 47 MiB.
- **1 000.** VS Code's default; under 5 MiB, but short for builds.

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

- **First version.** The element is `AccessibilityRole.Document`, named after the session's title,
  with the visible screen as its value. Readers can read the screen, but are not told about new
  output.
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
implementation finds another, it goes here first. None is needed for step 2. T1 is needed by
step 3; K1–K4 and A1–A2 make later steps right rather than possible.

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

**T2: positioned text runs (`HultUI.Core`, only if step 2 measures the need).**
`IRenderContext.DrawTextRun(string text, Point origin, TextRunStyle style)`: shape once (cached),
draw at a point, with no rectangle, layout or wrapping.

**T3: a cached picture per row (`HultUI.Core`, only if measured).**
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

- **`HultUI.Terminal.Tests`** (headless):
  - The real XTerm.NET emulator fed scripted bytes through a `StreamConnection` over in-memory
    pipes.
  - The spike's question 2 (behaviour), 3 (reflow) and 6 (keys) checks become tests.
  - The element is tested through the headless window: snapshot, render, selection, paste
    bracketing, focus events, resize policy.
  - A `FakeConnection` records what was sent.
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
1. **Upstream.** Offer patches 0001–0004 to XTerm.NET, and report Porta.Pty's quoting. This is
   the owner's call (**TO DECIDE (engine patches)**).
2. **`HultUI.Terminal`:**
   - the emulator seam and the XTerm.NET adapter;
   - the controller with its two threads and the teardown order;
   - `StreamConnection` and `FakeConnection`;
   - the element: snapshot, renderer, keys, text, IME, selection, copy and paste, scrollback
     scrolling;
   - headless tests and the sample on a scripted connection.

   Measure DrawText runs on a real window here; T2 and T3 enter only on numbers.
3. **Seam T1,** then the renderer's default font and cell metrics on all three platforms.
4. **`HultUI.Terminal.Pty`:**
   - `PtyConnection` on Windows and Linux, with quoting, exit and teardown;
   - the arm64/x64 `OpenConsole.exe` trim;
   - real-shell tests;
   - the sample's shell, and NativeAOT `--check` as the WebView sample does.
5. **Resize policy, synchronized output, search, hyperlinks, mouse reporting, OSC 52 policy.**
6. **Windows input:** win32-input-mode for the ambiguous keys, verified against Windows Terminal
   with Git's vim. Then seams **K1–K4**.
7. **Accessibility:** seams **A1** and **A2**, and the element's text range model and output
   announcements.
8. **Later:**
   - images (Sixel and Kitty, through a `PixelFrameSink` per image);
   - `HultUI.Terminal.Ssh` (Vesper's SFTP brief makes SSH the next transport);
   - libghostty-vt as a second emulator once it tags a release.
