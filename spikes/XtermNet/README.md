# XTerm.NET terminal spike

**Result: XTerm.NET 2.0.2 fed by Porta.Pty 2.2.3 can carry HultUI.Terminal on Windows, with
four things to patch or work around.** It is fast enough (100–220 MiB/s, nothing allocated per
byte), AOT-clean, and right on 39 of 40 behaviour checks and 45 of 49 key encodings. pwsh and
Microsoft Edit run cleanly through ConPTY. The four, in order of how visible they are:

1. **The cursor's own line is cut, not reflowed, on a narrowing resize.** ConPTY does not repaint it.
2. **A width resize costs O(scrollback × columns):** 25 ms at 10 000 lines × 200 columns.
3. **Scrollback is stored full width:** about 25 bytes per cell, whatever the line holds.
4. **Porta.Pty's default Windows argument quoting breaks `cmd /c`.**

2026-10-03, on the machine below.

This is the spike behind the engine choice for an embedded terminal (its own project and package,
the way WebView is). The packages compared were XTerm.NET, libghostty-vt, RoyalTerminal, and
xterm.js in a WebView. The recommendation was XTerm.NET behind an `ITerminalEngine` seam, with
libghostty-vt as a second engine once it tags a stable release. Before building the package: is
XTerm.NET right, fast, and shaped the way a HultUI element needs? Eight questions, cheapest first.

**Machine.** Intel i7-14700K, 64 GB, Windows 11 Pro 25H2 (build 26200). .NET SDK 10.0.300,
runtime 10.0.12. pwsh 7.6.6, Git for Windows' vim 9.1 (MSYS2), Microsoft Edit 1.2.1. ConPTY is the
out-of-band 1.24 one Porta.Pty ships (`conpty.dll` + `OpenConsole.exe`), not the in-box conhost.
No Linux run yet (see [Open](#open)). Raw output of every run is under [`results/`](results/),
named by question.

## What it is

A console program, deliberately not in `HultUI.slnx`. It references `XTerm.NET` 2.0.2 and
`Porta.Pty` 2.2.3 from NuGet. It also references `HultUI.Core`, for the key types question 6 maps
from (Core has no dependencies, so the AOT publish stays this program).

- `Session.cs`: one terminal wired the way HultUI.Terminal would wire it.
  - A dedicated reader thread does blocking reads and calls `Write(bytes)` under one lock, then
    pulses it. The Avalonia host measured a pooled `ReadAsync` losing output under load.
  - A writer thread drains a queue, so neither the UI thread nor the reader ever writes to the
    pty. Question 8 says why.
  - Everything else reads the terminal under the same lock. `WaitFor` sleeps on the pulse, not on
    `Thread.Sleep`, whose 15.6 ms tick on Windows made the first latency numbers fiction.
  - The command line is built with MSVCRT quoting and passed verbatim (question 7).
- `Children` (also in `Session.cs`) are the programs run under the pseudoconsole: this same
  executable, so nothing has to be installed.
  - `echo` counts the lines it reads until `END`.
  - `flood N` writes N MiB of 80-column text.
- `Q1.cs` … `Q8.cs`: one file per question. `Probe.cs` holds the shared helpers: escape display,
  the check table, timing warmed to convergence (XTerm.NET's own notes measured a 3× understatement
  from timing before tier-1), and percentiles.

## Running it

```bash
dotnet build spikes/XtermNet -c Release
spikes/XtermNet/bin/Release/net10.0/XtermNet q2
```

Probes: `q1`, `q2`, `q3`, `q3-detail`, `q4 [seconds]`, `q4 memory`, `q5`, `q6`,
`q7 shell|vim|edit|flood [MiB]|exit` (no argument runs all), `q8 a|b|c [KiB]` (one case per
process: `a` can leave the process unable to close its pseudoconsole).

Native AOT needs the MSVC environment and `vswhere` on PATH ([NATIVE-AOT.md](../../docs/NATIVE-AOT.md)):

```bat
call "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
set "PATH=C:\Program Files (x86)\Microsoft Visual Studio\Installer;%PATH%"
dotnet publish spikes\XtermNet -c Release -r win-x64 -p:PublishAot=true -o spikes\XtermNet\bin\aot
```

## Answers

| # | Question | Answer |
|---|---|---|
| 1 | What does consuming the two packages bring? | **Two managed assemblies (381 KB) plus Microsoft's out-of-band ConPTY.** Porta.Pty brings `conpty.dll` and `OpenConsole.exe` for x64 and arm64 (about 1.1 MB each; the arm64 copy also lands in a `win-x64` publish) and its `libporta_pty` fork/exec shims for linux-x64/arm64 and osx-x64/arm64. A cell is 24 bytes with no references. **AOT: zero IL2xxx/IL3xxx warnings**, a 3.76 MB executable, and every question gives the same answers under AOT. |
| 2 | Does the public API carry what a renderer and host read, and is it right? | **Yes: 39 of 40.** Text, wrap, CUP/ED/EL, scroll regions, IL/DL/ICH/DCH/ECH, SGR including curly underlines with colour, CJK/combining/ZWJ widths, UTF-8 split across reads, alternate screen, DECTCEM, DECSCUSR, OSC 0/2/7/8/52/133, BEL, mode 2026, DA1/DSR/DECRQM/kitty replies, DECCKM, bracketed paste with ESC stripped, SGR mouse, focus, and scrollback all pass. **Gap: SGR 53 (overline) never sets** (no case in the SGR switch). **Two facts the README gets wrong or leaves out:** colour mode 0 is a palette index where **256 and 257 are the default foreground and background**, and mode 1 is RGB (the README says 0 default, 1 palette, 2 RGB); and a full row leaves **X == Cols** (the deferred wrap), so the renderer clamps the cursor. **No damage tracking:** `RenderEventArgs` exists, nothing raises it. |
| 3 | Is reflow right, and what does a resize cost? | **Right everywhere except the cursor's own line**, which is cut to the new width instead (xterm.js's rule: "the program will handle fixing up wrapped lines with the cursor"). 30 of 100 characters were lost, and a width-2 cell is left in the last column. Scrollback reflows losslessly (500 lines, 120 → 60 → 120); the alternate screen is not reflowed. **A width change costs O(scrollback × columns):** p50 0.5 ms at 1 000 × 120, 14 ms at 10 000 × 120, 25 ms at 10 000 × 200, 235 ms at 100 000 × 200. **A height change is about 0.02 ms.** |
| 4 | How fast does it parse, and what does scrollback cost? | **102–222 MiB/s by corpus, warmed** (ASCII 138, SGR per word 130, full-screen redraws 222, Unicode 102), with 0–0.04 bytes allocated per input byte (0.75 for Unicode clusters). **Memory: about 25 bytes per cell at full width**, whatever the line holds: 10 000 lines × 200 columns is 47 MiB; 100 000 lines is 471 MiB. |
| 5 | What does a renderer pay to read the grid? | **Next to nothing.** Walking 200 × 60 into styled runs costs 21 µs per frame with nothing allocated (43 µs through `cell.Content` strings). Copying the viewport out costs 12.6 µs; hashing every row for dirty detection costs 53 µs. A row cache keyed by content (not position) finds every row again after a scroll. |
| 6 | Do HultUI key events map to the right bytes? | **45 of 49** against xterm's encodings: arrows, Home/End, F-keys and modifiers in normal and DECCKM mode, Ctrl+letters and punctuation, Alt as ESC-prefix, and kitty level 1. **Engine gaps:** Alt+Enter and Alt+Backspace drop the Alt; Ctrl+/ gives `/` instead of `^_`; Ctrl+Backspace equals Backspace (a convention, not a bug). **HultUI gaps:** `KeyCode` is a scancode position, so Ctrl+letter is wrong on AZERTY, Dvorak and QWERTZ (Y/Z); there are no keypad codes; and left/right Alt cannot be told apart, so AltGr (Ctrl+Alt on Windows) looks like Ctrl+Alt. |
| 7 | Do real programs run through a real pseudoconsole? | **pwsh and Microsoft Edit: yes, fully.** pwsh: first byte 1–5 ms, prompt about 250 ms, keystroke → grid p50 0.3–0.5 ms / p95 1.6–2.3 ms (one run of seven had a 97 ms p95), and the shell sees the resize. **But a narrowed prompt line stays cut:** ConPTY does not repaint it. **Git's vim (MSYS)** works, but the first keystroke after a resize is lost (9 of 10 runs); sent as win32-input-mode, vim never sees the resize at all. That is Cygwin's console layer, not the engine: Edit has neither problem. **Exit codes are right only with verbatim MSVCRT quoting:** Porta.Pty's default wraps every argument (`cmd.exe "/c" "exit 3"`) and cmd exits 1 every time. **Flood:** out-of-band ConPTY passes 100.1 % of the bytes through. End to end 100 MiB/s cold under JIT, 212 MiB/s under AOT. The parse is the bottleneck (70–89 % of wall time); the lock is held 0.18–0.56 ms per 62 KB read (p50), 4.4 ms at most. |
| 8 | Which thread may write to the pty? | **Only a writer thread of its own.** A paste written inline by the reader thread **deadlocks for good at 256 KiB** (64 KiB gets through). Killing the child does not free it, and `Dispose` (ClosePseudoConsole) then hangs too. Through a writer queue: `Send` returns in 0.1 ms and 1 MiB is through in 1.24 s. Written synchronously on the "UI thread": the UI blocks for 0.9 s. |

### 1. Consuming it

`XTerm.NET.dll` is 302 KB and `Porta.Pty.dll` 80 KB. XTerm.NET depends on `Unicode.net` and `Wcwidth`, which compile in. Porta.Pty depends on `Microsoft.Windows.Console.ConPTY` 1.24, which puts `conpty.dll` and an `OpenConsole.exe` per architecture under `runtimes/`. `PtyProvider.PseudoConsoleImplementation` reports `oob`. Porta.Pty also answers ConPTY's startup DA1 query itself and filters it from the stream. Without that answer the first output waits 3 s, and with a second answer the shell echoes `^[[?1;2c` at the prompt. The AOT publish has no IL2xxx/IL3xxx warnings ([`q1-aot-publish.txt`](results/q1-aot-publish.txt)), matching the framework's zero. The AOT binary's runs are in [`q1-aot-run.txt`](results/q1-aot-run.txt).

### 2. The seam

Every row in [`q2-behaviour.txt`](results/q2-behaviour.txt) writes a sequence and reads the answer the way the element would: cells for the renderer, properties and events for the host, replies for the pty.

- **Overline.** `AttributeData` has `IsOverline` and `SetOverline`, but the SGR switch has no `case 53` or `case 55`. A one-line upstream fix.
- **Colour encoding,** from `AttributeData` itself:

  ```csharp
  Default => new AttributeData { Fg = 256, Bg = 257 }
  GetFgColorMode() => Fg >> 25
  ```

  The Avalonia host reads it the same way. The renderer maps palette 0–255 through the theme, 256 and 257 to the theme's own foreground and background, and mode 1 straight to RGB.
- **Cursor.** The deferred wrap is X = Cols, as in xterm.js. Draw the cursor at `min(X, Cols - 1)`.
- **Win32-input-mode.** ConPTY's handshake sends `CSI ? 9001 h`, and `Win32InputMode` turns on, but `GenerateKeyInput` ignores it. Hosts that want it call `Win32InputGenerator` themselves (question 7).

### 3. Reflow and resize

[`q3-reflow.txt`](results/q3-reflow.txt), and the two cut cases screen by screen in [`q3-detail.txt`](results/q3-detail.txt).

**The cut.** XTerm.NET ports xterm.js's `_reflowSmaller`, which skips the wrapped group holding the cursor; the rows are then cut to the new width. In a shell that group is the prompt and the command being typed. xterm.js gets away with it because bash and zsh redraw on SIGWINCH. On Windows nothing redraws: ConPTY reflows its own buffer and expects the terminal to have done the same, as Windows Terminal does (question 7). Two fixes:

- **Upstream (preferred):** an option to reflow the cursor's group too, moving the cursor with it. Ghostty, kitty and Windows Terminal all do this.
- **In the element:** re-request a repaint from ConPTY after a narrowing resize.

**Cost.** A width change touches every line of the scrollback, even lines that do not wrap: `short` lines cost about the same as `prose`. During a window drag the grid changes width only when a whole column's worth of pixels has moved, so the cost is paid at most once per frame. At 10 000 × 200 that once is 25 ms, past a 60 Hz frame. A height change is free. Three levers:

- A default scrollback of a few thousand lines.
- Holding the reflow until the drag ends. This needs care: the resize-stretch fix taught that work must not be throttled inside the OS's modal resize loop. Showing the old grid clipped during the drag is not that, but it has to be measured on a real window.
- An upstream change that reflows lazily.

### 4. Throughput and memory

[`q4-throughput.txt`](results/q4-throughput.txt), [`q4-memory.txt`](results/q4-memory.txt).

- **Throughput.** The numbers are this machine's version of XTerm.NET's own MacBook figures (126–221 MiB/s), and a terminal at 100+ MiB/s is not the bottleneck of anything a person watches.
- **Memory.** Lines are allocated at the terminal's width and keep that width, so short lines cost as much as full ones. For comparison, VS Code's terminal stores 12 bytes per cell, also at full width, with a 1 000-line default. Ghostty stores pages and compresses idle scrollback.
- **Defaults this suggests.** At 200 columns: 5 000 lines ≈ 24 MiB per terminal; 1 000 lines ≈ 5 MiB.

### 5. Reading the grid

[`q5-render-read.txt`](results/q5-render-read.txt).

- **Where the cost is.** The walk is not where a frame's time will go; shaping and drawing text are.
- **What that suggests.** Walk every frame (there is no damage API to save the 21 µs anyway), hash each row's content, and keep the shaped row (a text blob or a picture) keyed by that hash. A scroll then redraws one row.
- **Clipping.** Clip each row to the grid: question 3's cut can leave a width-2 cell in the last column.

### 6. Keys

[`q6-keys.txt`](results/q6-keys.txt). The mapping in `TerminalKeys` is the one HultUI.Terminal would ship.

- **Text keys.** Printable keys without Ctrl or Alt return nothing: their bytes come from `TextInputInput`, so layouts and IMEs work, and nothing is typed twice.
- **Engine fixes,** done in the host mapping until upstream takes them: an ESC prefix for Alt+Enter and Alt+Backspace (Alt+Backspace is readline's backward-kill-word), and Ctrl+/ → `0x1F`.

The HultUI gaps are a framework change, not a terminal one:

- **Layout key.** `InputEvent.KeyInput` would need the layout-mapped key (`SDL_Keycode`) beside the scancode position.
- **Keypad.** Keypad codes are missing.
- **Left and right Alt.** These have to be distinguishable. On a Swedish keyboard `@ $ { [ ] } \ | ~` are all AltGr, which Windows reports as Ctrl+Alt. Until AltGr is distinguishable, the rule is: when a `TextInputInput` follows a Ctrl+Alt key, the text wins and the key sends nothing.

### 7. Through ConPTY

[`q7-shell.txt`](results/q7-shell.txt), [`q7-edit.txt`](results/q7-edit.txt), [`q7-vim.txt`](results/q7-vim.txt) (the one clean run of ten where bare ESC went through), [`q7-vim-repeats.txt`](results/q7-vim-repeats.txt) (five of the other nine), [`q7-exit.txt`](results/q7-exit.txt), [`q7-flood.txt`](results/q7-flood.txt).

- **The cut, on Windows.** A 104-character prompt line at 120 columns, then `Term.Resize(60)` alone, cuts it to `…segment3-se`. `Pty.Resize(60)` afterwards brings no repaint within 2 s (three runs of three). Widening again does get a repaint.
- **Quoting.** `PtyOptions.CommandLine` is quoted per argument with `"`, embedded quotes doubled. That is fine for programs that parse with CommandLineToArgvW, but wrong for cmd's switches and anything with backslashes before a quote. `Session.QuoteArg` applies the MSVCRT rules and passes the result with `VerbatimCommandLine`.
- **Vim (MSYS).** These are MSYS problems, so they are noted but not chased:
  - With bare ESC, the first keystroke after a resize is lost (9 of 10 runs): `:echo` arrives as `echo`, and the line becomes `hello from vo &columns &lines`.
  - With ESC sent as a win32-input-mode key press, vim leaves insert mode but never learns the new size.

  What to compare next is the same vim under Windows Terminal, which sends every key as win32-input-mode, not just ESC.
- **Flood.** The out-of-band ConPTY no longer re-renders a flood into a screen-sized summary: every byte arrives. So on Windows, as on Linux, the parse decides how fast `cat` of a large file runs.

### 8. Writes

[`q8-a-reader.txt`](results/q8-a-reader.txt), [`q8-a-threshold.txt`](results/q8-a-threshold.txt), [`q8-b-queue.txt`](results/q8-b-queue.txt), [`q8-c-ui.txt`](results/q8-c-ui.txt).

- **The deadlock.** The child echoes what it reads. A writer that is also the only drainer stops draining; the pseudoconsole's output pipe fills, then its input side, and the write never returns. 64 KiB fits in the buffers, 256 KiB does not.
- **Teardown.** Once stuck, killing the child does not help (OpenConsole holds both pipes), and ClosePseudoConsole waits on the output nobody reads. The teardown order this implies:
  1. Stop the writer.
  2. Kill the child.
  3. Read until EOF.
  4. Only then close the pseudoconsole.
- **Replies.** `DataReceived` replies (DA, DSR) are raised inside `Write`, on the reader thread, so they go to the writer queue too.

## What it means for HultUI.Terminal

- **Engine:** XTerm.NET behind `ITerminalEngine`. Upstream (MIT, active, same author as Porta.Pty):
  - SGR 53/55.
  - An option to reflow the cursor's line.
  - Alt+Enter, Alt+Backspace and Ctrl+/ in the legacy encoder.
  - The README's colour table.

  Until those land, carry them in the element.
- **Threading:** `Session`'s shape.
  - The engine is touched only under one lock.
  - A dedicated reader thread writes into the engine and wakes the UI **after releasing the lock**. A frame waits for that lock, so a reader that posted while holding it would be "a thread the frame waits for" posting: the [THREADING.md](../../docs/THREADING.md) deadlock during a live resize.
  - A writer queue for everything sent: keys, pastes, replies.
  - The UI thread reads under the lock. It waits for the lock at most about 4 ms during a flood, usually under 0.6 ms.
- **Renderer:**
  - Walk every frame.
  - Cache shaped rows by content hash.
  - Clamp the cursor.
  - Clip width-2 cells in the last column.
  - Colours 256/257 are the theme's own.
- **Resize:** height changes are free; width changes cost O(scrollback × columns). Default scrollback in the low thousands, and measure the drag on a real window before deciding anything cleverer.
- **Input:** `TerminalKeys` plus the three overrides. Framework work for a layout key, keypad codes and left/right Alt.
- **Windows process launch:** verbatim command lines with MSVCRT quoting. Leave win32-input-mode off until it is investigated as a whole. Trim the other architecture's `OpenConsole.exe` from a RID-specific publish.

## Open

For the Linux session; `q7` and `q8` already pick bash, `/usr/bin/vim` and `sh -c` off Windows:

- **forkpty.** Porta.Pty's native shim on Linux: spawn, resize, exit codes, Kill. `q7 shell`, `q7 exit`, `q7 vim`.
- **The cut on a raw pty.** Does bash/readline's SIGWINCH redraw paint over it, as xterm.js assumes?
- **Real byte streams.** tmux, htop and vttest. XTerm.NET's `tools/vttest/VtDrive` spawns through WSL, which this machine does not have; on Linux, run vttest natively.
- **The paste deadlock.** Where its threshold sits on Linux (the N_TTY canonical buffer is 4 KiB). `q8 a 4|16|64`.
- **AOT.** `linux-x64` AOT.

On Windows:

- **vim under Windows Terminal.** The same Git vim and resize, to tell an MSYS quirk from a missing win32-input-mode encoder.
- **Real-window cost.** The drag cost on a real window once the element exists.
