# XTerm.NET terminal spike

**Result: XTerm.NET 2.0.2 fed by Porta.Pty 2.2.3 can carry HultUI.Terminal on Windows.** It is
fast enough (100–220 MiB/s, nothing allocated per byte), AOT-clean, and right on 39 of 40 behaviour
checks and 45 of 49 key encodings. pwsh and Microsoft Edit run cleanly through ConPTY. Four
problems, in order of how visible they are, and what solves each:

1. **The cursor's own line is cut, not reflowed, on a narrowing resize, and ConPTY does not repaint
   it.** Solved by an engine option, `ReflowCursorLine` ([patch 0004](patches/)): the prompt stays
   whole and typing continues it.
2. **A width resize cost O(scrollback × columns):** 25 ms at 10 000 lines × 200 columns. A
   capacity-keeping line resize ([patch 0001](patches/)) brings that to 3.6 ms, and to 0.26 ms for
   short lines. 100 000 lines still costs 31 ms; the element's resize policy covers that.
3. **Scrollback is stored full width:** about 25 bytes per cell, whatever the line holds. Not
   patched; the element's default scrollback and a memory budget cover it.
4. **Porta.Pty's default Windows argument quoting breaks `cmd /c`.** Solved in the host: MSVCRT
   quoting, passed verbatim.

The four engine patches also fix overline and three key encodings, so behaviour goes to 40 of 40 and
keys to 49 of 49. All 2 238 of XTerm.NET's own tests pass with them (2 221 existing, 17 new).
2026-10-03, on the machine below. The design built on this is
[docs/design/TERMINAL.md](../../docs/design/TERMINAL.md).

This is the spike behind the engine choice for an embedded terminal (its own project and package,
the way WebView is). The packages compared were XTerm.NET, libghostty-vt, RoyalTerminal, and
xterm.js in a WebView. The recommendation was XTerm.NET behind an engine seam, with libghostty-vt
as a second engine once it tags a stable release. Before building the package: is XTerm.NET
right, fast, and shaped the way a HultUI element needs? Eight questions, cheapest first.

**Machine.** Intel i7-14700K, 64 GB, Windows 11 Pro 25H2 (build 26200). .NET SDK 10.0.300,
runtime 10.0.12. pwsh 7.6.6, Git for Windows' vim 9.1 (MSYS2), Microsoft Edit 1.2.1. ConPTY is the
out-of-band 1.24 one Porta.Pty ships (`conpty.dll` + `OpenConsole.exe`) unless a run says in-box.
No Linux run yet (see [Open](#open)). Raw output of every run is under [`results/`](results/),
named by question; the patched engine's runs are under [`results/patched/`](results/patched/).

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
  from timing before tier-1), and percentiles. `Probe.Options` is what every terminal is built
  with; in a patched build it turns `ReflowCursorLine` on.
- `patches/`: the four engine fixes as a `git format-patch` series against XTerm.NET `56ea809`
  (2.0.2 plus one merged PR), with their tests.

## Running it

```bash
dotnet build spikes/XtermNet -c Release
spikes/XtermNet/bin/Release/net10.0/XtermNet q2
```

Probes: `q1`, `q2`, `q3`, `q3-detail`, `q4 [seconds]`, `q4 memory`, `q5`, `q6`,
`q7 shell|vim|edit|flood [MiB]|exit` (no argument runs all), `q8 a|b|c [KiB]` (one case per
process: `a` can leave the process unable to close its pseudoconsole). `PORTAPTY_CONPTY=inbox`
selects Windows' in-box ConPTY instead of the out-of-band one.

Against the patched engine, the same probes from a second build beside the first:

```bash
git clone https://github.com/tomlm/XTerm.NET <checkout> && git -C <checkout> checkout 56ea809
git -C <checkout> am spikes/XtermNet/patches/*.patch
dotnet build spikes/XtermNet -c Release -p:XtermNetSource=<checkout>
spikes/XtermNet/bin/patched/Release/net10.0/XtermNet q3
```

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
| 2 | Does the public API carry what a renderer and host read, and is it right? | **Yes: 39 of 40; 40 of 40 patched.** Text, wrap, CUP/ED/EL, scroll regions, IL/DL/ICH/DCH/ECH, SGR including curly underlines with colour, CJK/combining/ZWJ widths, UTF-8 split across reads, alternate screen, DECTCEM, DECSCUSR, OSC 0/2/7/8/52/133, BEL, mode 2026, DA1/DSR/DECRQM/kitty replies, DECCKM, bracketed paste with ESC stripped, SGR mouse, focus, and scrollback all pass. **The gap was SGR 53 (overline)**, which no case in the SGR switch handled (patch 0002). **Two facts the README gets wrong or leaves out:** colour mode 0 is a palette index where **256 and 257 are the default foreground and background**, and mode 1 is RGB (the README says 0 default, 1 palette, 2 RGB); and a full row leaves **X == Cols** (the deferred wrap), so the renderer clamps the cursor. **No damage tracking:** `RenderEventArgs` exists, nothing raises it. |
| 3 | Is reflow right, and what does a resize cost? | **Right everywhere except the cursor's own line**, which is cut to the new width instead (xterm.js's rule: "the program will handle fixing up wrapped lines with the cursor"). 30 of 100 characters were lost, and a width-2 cell is left in the last column. **Patched with `ReflowCursorLine`: 7 of 7 checks pass**, nothing lost, the cursor follows its text. Scrollback reflows losslessly (500 lines, 120 → 60 → 120); the alternate screen is not reflowed. **A width change cost O(scrollback × columns),** because every line got a new array: p50 0.5 ms at 1 000 × 120, 14 ms at 10 000 × 120, 25 ms at 10 000 × 200, 235 ms at 100 000 × 200. **Patched: 0.25, 2.3, 3.6 and 31 ms**; 0.26 ms at 10 000 × 200 when lines are short. **A height change is about 0.02 ms** either way. |
| 4 | How fast does it parse, and what does scrollback cost? | **102–222 MiB/s by corpus, warmed** (ASCII 138, SGR per word 130, full-screen redraws 222, Unicode 102), with 0–0.04 bytes allocated per input byte (0.75 for Unicode clusters). **Memory: about 25 bytes per cell at full width**, whatever the line holds: 10 000 lines × 200 columns is 47 MiB; 100 000 lines is 471 MiB. |
| 5 | What does a renderer pay to read the grid? | **Next to nothing.** Walking 200 × 60 into styled runs costs 21 µs per frame with nothing allocated (43 µs through `cell.Content` strings). Copying the viewport out costs 12.6 µs; hashing every row for dirty detection costs 53 µs. A row cache keyed by content (not position) finds every row again after a scroll. |
| 6 | Do HultUI key events map to the right bytes? | **45 of 49; 49 of 49 patched** (patch 0003), against xterm's encodings: arrows, Home/End, F-keys and modifiers in normal and DECCKM mode, Ctrl+letters and punctuation, Alt as ESC-prefix, and kitty level 1. **The engine gaps were** Alt+Enter and Alt+Backspace dropping the Alt, Ctrl+/ giving `/` instead of `^_`, and Ctrl+Backspace equalling Backspace. **HultUI gaps,** which no engine patch can fix: `KeyCode` is a scancode position, so Ctrl+letter is wrong on AZERTY, Dvorak and QWERTZ (Y/Z); there are no keypad codes; and left/right Alt cannot be told apart, so AltGr (Ctrl+Alt on Windows) looks like Ctrl+Alt. |
| 7 | Do real programs run through a real pseudoconsole? | **pwsh and Microsoft Edit: yes, fully.** pwsh: first byte 1–5 ms, prompt about 250 ms, keystroke → grid p50 0.3–0.5 ms / p95 1.5–2.3 ms (one run of nine had a 97 ms p95), and the shell sees the resize. **A narrowed prompt line stays cut with the stock engine:** ConPTY does not repaint it (PSReadLine rewrites it at the next keystroke; a shell that does not stays cut). **Patched: whole, and typing continues the command.** **Git's vim (MSYS)** works. Through the out-of-band ConPTY a bare ESC races: the first keystroke after a resize was lost in 2 to 9 runs of 10 across sessions. ESC sent as a win32-input-mode key press, and the in-box ConPTY, were 10 of 10 clean. **Exit codes are right only with verbatim MSVCRT quoting:** Porta.Pty's default wraps every argument (`cmd.exe "/c" "exit 3"`) and cmd exits 1 every time. **Flood:** out-of-band ConPTY passes 100.1 % of the bytes through. End to end 100–112 MiB/s cold under JIT, 212 MiB/s under AOT. The parse is the bottleneck (70–89 % of wall time); the lock is held 0.18–0.56 ms per 62 KB read (p50), 4.4 ms at most. |
| 8 | Which thread may write to the pty? | **Only a writer thread of its own.** A paste written inline by the reader thread **deadlocks for good at 256 KiB** (64 KiB gets through). Killing the child does not free it, and `Dispose` (ClosePseudoConsole) then hangs too. Through a writer queue: `Send` returns in 0.1 ms and 1 MiB is through in 1.24 s. Written synchronously on the "UI thread": the UI blocks for 0.9 s. |

## The fixes, measured

[`patches/`](patches/), applied to XTerm.NET `56ea809`. XTerm.NET's suite: 2 221 of 2 221 before,
2 238 of 2 238 after (the 17 new tests are `Buffer/CursorLineReflowTests.cs`). Spike runs:
[`results/patched/`](results/patched/).

| Patch | What it changes | Stock | Patched |
|---|---|---|---|
| 0001 | `BufferLine.Resize` keeps the array as a capacity: narrowing moves the length, widening fills only what it uncovers, and the array is reallocated only to grow or when it is more than twice the line. The enumerator and `CopyFrom` respect the length. | width change at 10 000 × 200: 25 ms (short lines 22 ms); 100 000 × 200: 235 ms | 3.6 ms (0.26 ms); 31 ms |
| 0002 | SGR 53/55 set and clear overline. | 39/40 | 40/40 |
| 0003 | Alt prefixes Enter and Backspace; Ctrl+Backspace is BS; Ctrl+/ is US. | 45/49 | 49/49 |
| 0004 | `TerminalOptions.ReflowCursorLine` (default off): the cursor's wrapped group reflows too. The cursor is held as the group's first line object plus a cell offset, both of which survive reflow, and put back after every other clamp. The reflow's "pop the bottom row" now scrolls instead when that row has content. | reflow 5/7; pwsh prompt cut after narrowing | 7/7; prompt whole, typing continues it |

What is left of the resize cost is the reflow itself: re-wrapping wrapped text, and
`GetTrimmedLength` on each line to find the ones that need it. That is proportional to the text,
not to the columns.

### 1. Consuming it

`XTerm.NET.dll` is 302 KB and `Porta.Pty.dll` 80 KB. XTerm.NET depends on `Unicode.net` and `Wcwidth`, which compile in. Porta.Pty depends on `Microsoft.Windows.Console.ConPTY` 1.24, which puts `conpty.dll` and an `OpenConsole.exe` per architecture under `runtimes/`. `PtyProvider.PseudoConsoleImplementation` reports `oob`. Porta.Pty also answers ConPTY's startup DA1 query itself and filters it from the stream. Without that answer the first output waits 3 s, and with a second answer the shell echoes `^[[?1;2c` at the prompt. Porta.Pty passes no flags to `CreatePseudoConsole`. The AOT publish has no IL2xxx/IL3xxx warnings ([`q1-aot-publish.txt`](results/q1-aot-publish.txt)), matching the framework's zero. The AOT binary's runs are in [`q1-aot-run.txt`](results/q1-aot-run.txt).

### 2. The seam

Every row in [`q2-behaviour.txt`](results/q2-behaviour.txt) writes a sequence and reads the answer the way the element would: cells for the renderer, properties and events for the host, replies for the pty.

- **Overline.** `AttributeData` has `IsOverline` and `SetOverline`, but the SGR switch had no `case 53` or `case 55` (patch 0002).
- **Colour encoding,** from `AttributeData` itself:

  ```csharp
  Default => new AttributeData { Fg = 256, Bg = 257 }
  GetFgColorMode() => Fg >> 25
  ```

  The Avalonia host reads it the same way. The renderer maps palette 0–255 through the theme, 256 and 257 to the theme's own foreground and background, and mode 1 straight to RGB.
- **Cursor.** The deferred wrap is X = Cols, as in xterm.js. Draw the cursor at `min(X, Cols - 1)`.
- **Win32-input-mode.** ConPTY's handshake sends `CSI ? 9001 h`, and `Win32InputMode` turns on, but `GenerateKeyInput` ignores it. Hosts that want it call `Win32InputGenerator` themselves (question 7).

### 3. Reflow and resize

[`q3-reflow.txt`](results/q3-reflow.txt), the two cut cases screen by screen in [`q3-detail.txt`](results/q3-detail.txt), and the patched engine in [`patched/q3-reflow.txt`](results/patched/q3-reflow.txt).

**The cut.** XTerm.NET ports xterm.js's `_reflowSmaller` and `_reflowLargerGetLinesToRemove`, which both skip the wrapped group holding the cursor; the rows are then cut to the new width. In a shell that group is the prompt and the command being typed. xterm.js gets away with it because bash and zsh redraw on SIGWINCH. On Windows nothing redraws: ConPTY reflows its own buffer and expects the terminal to have done the same, as Windows Terminal does (question 7). Ghostty, kitty and Windows Terminal all reflow the cursor's line. Patch 0004 adds that as an option, off by default so xterm.js parity stays the default upstream.

**Cost.** Every width change gave every line a new array, so lines that do not wrap cost as much as lines that do: `short` lines cost about the same as `prose`. During a window drag the grid changes width only when a whole column's worth of pixels has moved, so the cost is paid at most once per frame. At 10 000 × 200 that once was 25 ms, past a 60 Hz frame; patched it is 3.6 ms. A height change is free. At 100 000 lines even the patched engine takes 31 ms, which the element's resize policy answers (the design doc: a VS Code-style debounce of width changes when the buffer is large).

### 4. Throughput and memory

[`q4-throughput.txt`](results/q4-throughput.txt), [`q4-memory.txt`](results/q4-memory.txt).

- **Throughput.** The numbers are this machine's version of XTerm.NET's own MacBook figures (126–221 MiB/s), and a terminal at 100+ MiB/s is not the bottleneck of anything a person watches.
- **Memory.** Lines are allocated at the terminal's width and keep that width, so short lines cost as much as full ones. For comparison, VS Code's terminal stores 12 bytes per cell, also at full width, with a 1 000-line default. Ghostty stores pages and compresses idle scrollback.
- **Defaults this suggests.** At 200 columns: 5 000 lines ≈ 24 MiB per terminal; 1 000 lines ≈ 5 MiB.

### 5. Reading the grid

[`q5-render-read.txt`](results/q5-render-read.txt).

- **Where the cost is.** The walk is not where a frame's time will go; shaping and drawing text are.
- **What that suggests.** Walk every frame (there is no damage API to save the 21 µs anyway), hash each row's content, and keep the shaped row keyed by that hash. A scroll then redraws one row.
- **Clipping.** Clip each row to the grid: question 3's cut (stock engine) can leave a width-2 cell in the last column.

### 6. Keys

[`q6-keys.txt`](results/q6-keys.txt), [`patched/q6-keys.txt`](results/patched/q6-keys.txt). The mapping in `TerminalKeys` is the one HultUI.Terminal would ship.

- **Text keys.** Printable keys without Ctrl or Alt return nothing: their bytes come from `TextInputInput`, so layouts and IMEs work, and nothing is typed twice.
- **Engine fixes** are patch 0003. Alt+Backspace is readline's backward-kill-word, which is why it matters.

The HultUI gaps are a framework change, not a terminal one:

- **Layout key.** `InputEvent.KeyInput` would need the layout-mapped key (`SDL_Keycode`) beside the scancode position.
- **Keypad.** Keypad codes are missing.
- **Left and right Alt.** These have to be distinguishable. On a Swedish keyboard `@ $ { [ ] } \ | ~` are all AltGr, which Windows reports as Ctrl+Alt. Until AltGr is distinguishable, the rule is: when a `TextInputInput` follows a Ctrl+Alt key, the text wins and the key sends nothing.

### 7. Through ConPTY

[`q7-shell.txt`](results/q7-shell.txt), [`patched/q7-shell.txt`](results/patched/q7-shell.txt), [`q7-edit.txt`](results/q7-edit.txt), [`q7-vim.txt`](results/q7-vim.txt), [`q7-vim-repeats.txt`](results/q7-vim-repeats.txt), [`q7-vim-inbox.txt`](results/q7-vim-inbox.txt), [`q7-vim-tally.txt`](results/q7-vim-tally.txt), [`q7-exit.txt`](results/q7-exit.txt), [`q7-flood.txt`](results/q7-flood.txt).

- **The cut, on Windows.** A 104-character prompt line at 120 columns, then `Term.Resize(60)` alone, cuts it to `…segment3-se`. `Pty.Resize(60)` afterwards brings no repaint within 2 s (seven runs of seven). Typing `XYZ` then continues the command on screen, because PSReadLine rewrites the whole line at a keystroke; cmd would not. With patch 0004, `Term.Resize(60)` alone leaves the line whole, and `XYZ` lands right after it, which shows the engine's cursor and ConPTY's agree.
- **Quoting.** `PtyOptions.CommandLine` is quoted per argument with `"`, embedded quotes doubled. That is fine for programs that parse with CommandLineToArgvW, but wrong for cmd's switches and anything with backslashes before a quote. `Session.QuoteArg` applies the MSVCRT rules and passes the result with `VerbatimCommandLine`.
- **Vim (MSYS)** ([`q7-vim-tally.txt`](results/q7-vim-tally.txt)).
  - **Bare ESC on the out-of-band ConPTY races.** The first keystroke after a resize is sometimes lost: `:echo` arrives as `echo`, and the line becomes `hello from vo &columns &lines`. That happened in 9 of 10 runs in one session and 2 of 10 in a later one.
  - **The clean cases.** ESC sent as a win32-input-mode key press was 10 of 10. So was the in-box ConPTY, with either ESC.
  - **A correction.** An earlier version of this file said win32-input ESC left vim blind to the resize. The check behind that wanted the last row to read exactly `80 20`, but the in-box ConPTY (and some out-of-band runs) draw vim's ruler on the same row. At least one of those earlier screens did show vim answering `100 30`, so that variant may race too; the tally did not catch it.
  - **The design answer.** Speak win32-input-mode when ConPTY asks for it, as Windows Terminal does, for every key, not just ESC.
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

## Open

For the Linux session; `q7` and `q8` already pick bash, `/usr/bin/vim` and `sh -c` off Windows:

- **forkpty.** Porta.Pty's native shim on Linux: spawn, resize, exit codes, Kill. `q7 shell`, `q7 exit`, `q7 vim`.
- **The cut on a raw pty.** Does bash/readline's SIGWINCH redraw paint over it, as xterm.js assumes? And does `ReflowCursorLine` fight that redraw or agree with it?
- **Real byte streams.** tmux, htop and vttest. XTerm.NET's `tools/vttest/VtDrive` spawns through WSL, which this machine does not have; on Linux, run vttest natively.
- **The paste deadlock.** Where its threshold sits on Linux (the N_TTY canonical buffer is 4 KiB). `q8 a 4|16|64`.
- **AOT.** `linux-x64` AOT.

On Windows:

- **vim under Windows Terminal.** The same Git vim and resize, beside a full win32-input-mode encoder in the element.
- **Real-window cost.** The drag cost on a real window once the element exists.
