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
    PtyConnection.Start(PtyLaunch.DefaultShell(workingDirectory: repoRoot)),
    new TerminalOptions { Scrollback = 5000 });

Box.New().Children(
    TabStrip(shell),                         // shell.Title, shell.State, shell.WorkingDirectory are signals
    TerminalElement.New(shell).FlexGrow(1));

shell.Exited += code => ...;
shell.Send("git status\r");                  // through the writer queue; any thread
```

- **The connection is what the bytes travel over.**
  - `PtyConnection` (in `HultUI.Terminal.Pty`) runs a local process under a pseudoconsole: ConPTY on Windows, forkpty on Linux and macOS.
  - `SshConnection` (in `HultUI.Terminal.Ssh`) runs a shell on another machine.
  - `StreamConnection` (in the core package) wraps any pair of streams: a serial port, a test script.
- **The controller owns the emulator, the connection, and the two threads between them.**
- **The element draws the controller and turns input into bytes.**

The element is `TerminalElement.New`, not a `Terminal.New` builder: nothing in the package is
named `Terminal`, because a class named after its namespace (`HultUI.Terminal.Terminal`) is
unreachable by its short name, the `Text` trap WebView also avoided.

**Out of scope for the first version:**

- Tabs and splits (that is the app's layout).
- Ligatures.
- Shell integration UI beyond what the engine already reports.

## Problems found, and what solves each

The spike's findings, each with its solution and where the solution lives. "Measured" means the
spike ran the fix; "proposed" means it is design only.

| # | Problem | Solution | Lives in | State |
|---|---|---|---|---|
| 1 | The cursor's own line is cut, not reflowed, on a narrowing resize, and ConPTY does not repaint it: a narrowed prompt stays cut. | `TerminalOptions.ReflowCursorLine` in the engine, on in HultUI | engine patch 0004 | measured: pwsh's prompt stays whole and typing continues it |
| 2 | A width resize costs O(scrollback × columns): 25 ms at 10 000 × 200. | Capacity-keeping line resize (3.6 ms), plus a resize policy for big buffers | engine patch 0001; element | measured; policy built in step 5 |
| 3 | Scrollback is stored full width, about 25 B per cell: 47 MiB at 10 000 × 200. | Default 5 000 lines; a byte budget converted to lines at each width; both live signals | controller | built in step 2 |
| 4 | Porta.Pty's default Windows quoting wraps every argument: `cmd "/c" "exit 3"` exits 1. | MSVCRT quoting, passed verbatim | `HultUI.Terminal.Pty` | measured; built in step 4, and its test fails with Porta.Pty's quoting |
| 5 | A paste written by the reader thread deadlocks for good at 256 KiB; ClosePseudoConsole then hangs too. | A writer thread with a queue; a fixed teardown order | controller | measured; built in step 2 |
| 6 | A reader that wakes the UI while holding the engine lock is the THREADING.md live-resize deadlock. | Wake only after releasing the lock | controller | built in step 2 |
| 7 | The engine has no damage tracking. | Copy the viewport out under the lock every frame and draw all of it. No row cache: step 2 measured drawing fast enough without one | element | built in step 2; the copy costs 12.6 µs |
| 8 | SGR 53/55 (overline) never reach a cell. | Two switch cases | engine patch 0002 | measured |
| 9 | Alt+Enter and Alt+Backspace drop the Alt; Ctrl+/ sends `/`; Ctrl+Backspace equals Backspace. | Fixed in the legacy encoder | engine patch 0003 | measured: 49/49 |
| 10 | HultUI's key event is a scancode position: Ctrl+letter is wrong on AZERTY/QWERTZ; there are no keypad codes; AltGr cannot be told from Alt (through SDL, Windows' AltGr is right Alt alone). | Seams K1–K4: the layout key, the modifier keys' sides and AltGr, keypad and F13–F24 codes, and the text a key typed | framework; element | built in step 6; AltGr+2 sent `ESC 2 @` through SDL until then, and each piece's test fails without it |
| 11 | Through ConPTY, Ctrl+Space reaches a console program as the wrong key (virtual key 0x40, no character), and Escape, Alt+key and Ctrl+Backspace each arrive after a bare press of their modifier. (The spike also blamed a bare ESC for the first key lost after a resize; step 6 found that is Git for Windows' runtime, problem 22.) | Send Escape, Alt+key, Ctrl+Space and Ctrl+Backspace as win32-input-mode events when ConPTY asks for them | element, Windows | built in step 6: a native console program reads all four exactly, and its test fails without them |
| 12 | Engine facts a renderer must know: default colours are palette 256/257, the deferred wrap is X == Cols, and a width-2 cell can end a row. | Renderer rules | element | built in step 2 |
| 13 | HultUI has no positioned-glyph-run or offscreen-surface API for elements. | One `DrawText` per row, with a span per style. Seams T2/T3 were for the case where that was too slow, and it is not | element | measured in step 2: about 7 ms a frame for a 207 × 58 full-screen redraw |
| 14 | HultUI has no monospace default font and no cell-metrics API. | Seam T1: `FontFamilies.Monospace`, family lists, `MeasureFont`, `HasGlyph` | framework | built in step 3 |
| 15 | HultUI has no UIA TextPattern, no AT-SPI Text, and no announcements. | Seams A1/A2: an element's text a reader moves through, and `Element.Announce` | framework; element | built in step 7: checked from outside with Windows' own UI Automation client (the text, its lines and words, boxes, and every new line of `ping` announced); AT-SPI unit-tested only |
| 16 | Porta.Pty puts arm64's `OpenConsole.exe` (1.1 MB) in a win-x64 publish. | **Not a problem: keep it.** Microsoft's own ConPTY targets ship both hosts for x64 ("x64 can only run on x64 and ARM64"), because the x64 `conpty.dll` checks the machine (`IsWow64Process2`) and starts the arm64 host on ARM64 Windows. Trimming it would drop an x64 app on ARM64 back to the in-box console host, silently | `HultUI.Terminal.Pty` | decided in step 4 from the package's targets and the DLL's imports |
| 17 | A row shrink with the cursor near the top leaves the cut rows below the screen, so the scrollback keeps that many lines fewer, for good. A terminal created at 80 × 24 and sized to its pane before its first screenful is that case. | Drop the blank rows below the cursor, as xterm.js does | engine patch 0005 | measured: found in step 2; the new upstream test fails without it |
| 18 | A symbol the monospace font lacks (Consolas lacks some arrows and maths operators) is drawn from a fallback font whose advance pushes the rest of its row off its columns. | A character the face has no glyph for is drawn on its own at its column. Step 2 used fixed code point ranges; step 3 asks the face (`HasGlyph`, seam T1) | element | measured: found in step 2 by the sample; the font check built in step 3 |
| 19 | ConPTY keeps the output open after its program exits, until the pseudoconsole is closed, so the controller, which reports an exit when the output ends, never would. | The connection closes the pseudoconsole once the output has been quiet for 100 ms after the exit | `HultUI.Terminal.Pty` | measured in step 4: the output stayed open in 5 of 5 programs; with the fix the exit is reported within 0.7 s of the last output, and the test fails without it |
| 20 | A quick program can end before anyone listens: `cmd /c echo hi` is gone in 60 ms, before Porta.Pty's exit event has a handler or the controller exists. A plain event loses that exit for good. | `ConnectionExit`: raised once, and a handler added after the end is called at once. Every connection in the packages uses it; `PtyConnection` also asks Porta.Pty whether the program already ended | `HultUI.Terminal`, `HultUI.Terminal.Pty` | found in step 4 (an exit reported with no code); the test fails without it |
| 21 | A background process a program started can hold a pseudoterminal open after the program exits (`sleep 60 &` then `exit 4`). | The controller reports the exit anyway one second after it, and leaves the input open to whatever still reads it | controller | built in step 4; the test fails without it |
| 22 | Git for Windows' MSYS2 runtime (3.6.6 in Git 2.53; unchanged in 3.6.11 and in Cygwin's `main`) misses about one resize in six until the next input arrives, and throws away that input's first character: bash runs `cho` for `echo`, vim loses a `:`. Both ConPTYs alike, so Windows Terminal very likely too. | **None in the terminal.** A race in the runtime's console code: one thread drops its lock to send SIGWINCH, the other takes the resize, and the first then blocks in a read that discards the next input record. Every sacrificial record a terminal could send fails: key-ups and modifier presses never reach that queue (the program reads VT input); a same-size resize is no event; a focus report reaches only programs that asked for one, and leaves `[I` behind as a command; a real resize re-arms the race | upstream (msys2-runtime), not reported | measured in step 6: 5 of 30 resizes with bash, twice; a native console program lost none of 40. An opt-in test re-measures it |
| 23 | The engine decodes Sixel, kitty and iTerm2 pictures, and its device attributes offer Sixel, but nothing drew them: a program that asked was shown blank cells. | Draw the engine's per-line picture runs (*Rendering*, item 9) | element | built in step 8 |
| 24 | The engine lays a picture out against a 10 × 20 pixel cell unless told otherwise, so a picture took the wrong number of cells: one 30 pixels wide took three whatever the font. | The element sets the engine's cell to its own, in device pixels, rounded | element | built in step 8; the test fails without it |
| 25 | Kitty graphics: a placement sent again under the same ids, and an image sent again under the same id, both left the old picture on screen; `d=f` deleted nothing; and the registry's byte count fell below zero as animated images were deleted, which switches its budget off. | Replace as the specification says; delete a frame as kitty does; the registry records what it charged each image | engine patch 0006 | measured in step 8: 14 new upstream tests, and each fix's test fails without it |
| 26 | The engine counts an animation as running for as long as it holds it, shown or not, so a cleared animation kept the element drawing four times a second. | A frame clock only while a picture on screen animates | element | built in step 8; the test fails without it |
| 27 | SSH.NET trusts every host key unless a handler says otherwise (`HostKeyEventArgs.CanTrust` starts true). | The connection always decides: known connects, revoked never, unknown and changed only on the app's decision, none without one | `HultUI.Terminal.Ssh` | built in step 8b; without the handler four tests fail |
| 28 | SSH.NET gives a shell no exit status: `ShellStream` keeps its channel private, and `SshCommand`, which reads the status, cannot ask for a terminal. | Listen on the channel's internal request event (`UnsafeAccessor`, and one constant-name field read), against SSH.NET pinned exactly; a version that moves it reports no code | `HultUI.Terminal.Ssh` | built in step 8b; three tests fail without it, and it works under NativeAOT |
| 29 | SSH.NET acknowledges data as it arrives, not as it is read, so a program writing faster than the terminal draws fills memory without limit. | Past a cap of unread output, hold SSH.NET's receiving thread until the reader catches up: TCP then stops the server | `HultUI.Terminal.Ssh` | built in step 8b: 32 MiB flood held at 256 KiB; the test fails without it |
| 30 | A connection reset leaves SSH.NET's shell stream open, and a reader waits on it forever. | Close the session on the shell stream's error | `HultUI.Terminal.Ssh` | built in step 8b; without it the test times out |
| 31 | SSH.NET opens an encrypted PKCS#8 key with an empty passphrase instead of asking for one (the decryption's error comes out), and with the right passphrase cannot read an encrypted PKCS#8 EC key as .NET writes it (an ASN.1 error: it expects the public key where the optional curve comes). A key handed to it ready-made it offers only as `ssh-rsa` (SHA-1), which OpenSSH refuses since 8.8. | Read PKCS#8 with .NET, and offer an RSA key as `rsa-sha2-512` and `-256` first | `HultUI.Terminal.Ssh` | built in step 8b; two tests fail without it |
| 32 | SSH.NET's password sign-in takes the password before connecting, so an app would ask for one the server may not take, and could not ask again. | A sign-in way that asks when the server takes passwords, and again while wrong | `HultUI.Terminal.Ssh` | built in step 8b; two tests fail with one attempt |
| 33 | A failed sign-in said only "Permission denied (publickey)": SSH.NET names the last way tried. The owner's first real host (2026-10-04) had no agent running, none of the default key files, and took passwords too; nothing said so. | `SshSignInException`: what each way did, and what the server takes (asked with SSH's `none` method when no way asked it) | `HultUI.Terminal.Ssh` | built after step 8b; two tests fail when the server is not asked |
| 34 | A controller needs its connection when it is made, and an SSH connection asks its questions (host key, password) on the UI thread while it connects: made there, it waits for itself. The sample did exactly that. | `DeferredConnection`: the terminal shows at once and connects in the background; the controller reads traits when it uses them | `HultUI.Terminal` | built after step 8b; each of its parts fails a test without it, and the sample's whole sign-in was run headless against the test server |
| 35 | `Element.Focus()` did nothing inside an overlay (its content had no focus manager), so a dialog's field took the keyboard only when clicked; and closing a dialog that held the focus left it on elements that had gone. | Overlay content gets the window's focus manager; closing hands the focus back to what had it | framework (`Window`) | built after step 8b; both tests fail without it. `docs/FOCUS.md` |
| 36 | A user's keys are often chosen per host in `~/.ssh/config` (the owner's are), which nothing read, so only the agent and the default key files could be offered. | `SshConfigFile` | `HultUI.Terminal.Ssh` | built after step 8b; two tests fail when the last value wins |

**DECIDED (engine patches): kept as HultUI's patches, not offered upstream** (2026-10-03). XTerm.NET is
therefore built from source: upstream at a pinned commit with patches 0001–0006 applied, never the
NuGet package. All 2 254 of XTerm.NET's tests pass with the patches, 33 of them new. How the
patched source is kept, built and updated is in *The patched engine* below. The alternative
weighed was offering them upstream and building on the stock package meanwhile, with problem 1
visible on Windows until a release carried `ReflowCursorLine`.

## Packages

```
external/
  XTerm.NET/                    upstream's src/XTerm.NET and src/XTerm.NET.Tests at the pinned
                                commit, patches applied, plus its LICENSE; generated, committed
  XTerm.NET.patches/            0001–0006: everything HultUI's engine differs from upstream by
  XTerm.NET.version             upstream URL and the pinned commit
src/
  HultUI.Terminal.XTermNet/     builds external/XTerm.NET as HultUI.Terminal.XTermNet.dll
  HultUI.Terminal/              controller, element, renderer, key mapping, emulator seam,
                                the XTerm.NET adapter, StreamConnection, DeferredConnection,
                                fake connection — no natives
  HultUI.Terminal.Pty/          PtyLaunch, PtyConnection: Porta.Pty (ConPTY + OpenConsole on
                                Windows, the forkpty shim elsewhere), quoting, ending
  HultUI.Terminal.Ssh/          SshConnection over SSH.NET, KnownHostsFile, SshConfigFile, the
                                ways to sign in
tests/
  HultUI.Terminal.XTermNet.Tests/  XTerm.NET's own suite, from external/ (2 254 tests, ~3 s)
  HultUI.Terminal.Tests/        headless: scripted bytes through the real emulator
  HultUI.Terminal.Pty.Tests/    real pseudoterminals: a child program of its own, and the shells
  HultUI.Terminal.Pty.Tests.Child/  that child: prints its arguments, size, input count, a flood
  HultUI.Terminal.Ssh.Tests/    against an SSH server and an agent in the test process
samples/
  HultUI.Samples.Terminal/      the user's shell, or an in-process demo shell; --check, --bench
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
  yet stable and is definitely going to change"; re-checked in step 8c).

`HultUI.Core` grants `InternalsVisibleTo HultUI.Terminal` for `OnOwn`, `PatchFrom`,
`WantsTextInput` and `MeasureContent`, as it does for WebView and Media. Everything the package
uses on `Window` and `Application` is public already.

Licences: XTerm.NET, Porta.Pty and `Microsoft.Windows.Console.ConPTY` are all MIT. XTerm.NET is
built into a package of ours, so `HultUI.Terminal.XTermNet` carries its LICENSE
(`licenses/XTerm.NET/`). Porta.Pty and ConPTY are package dependencies of `HultUI.Terminal.Pty`
and come with their own.

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
  - **Output held open.** When the program has exited but something else holds its output open
    (a background job on a pseudoterminal), the exit is reported one second after it anyway, and
    the input stays open (problem 21).
  - **Early exits.** A connection's `Exited` calls a handler added after the end at once
    (`ConnectionExit`), so a controller made after a quick program has ended still hears of it
    (problem 20).
  - **After `Dispose`** nothing is reported: the application ended the session itself.
- **Synchronized output (mode 2026).** While it is set, the element keeps drawing its last
  complete snapshot, for at most 150 ms (terminals use 100–200 ms). This stops editors tearing
  mid-redraw. The limit's clock is a tween, so it runs on the frame loop's time (virtual under a
  headless session) and wakes a frame when it runs out; past it the live screen shows until the
  program closes the update. Built in step 2.

## Rendering

The element draws the snapshot copied at the start of its `Render`. All of it uses public
`IRenderContext` calls. This is what steps 2 and 3 built; what they left for later says so.

1. **Metrics.**
   - **The face** is `TerminalOptions.FontFamily` followed by `FontFamilies.Monospace`, so a
     family that is not installed falls back to the platform's monospace face rather than its
     proportional default (seam T1).
   - **Cell width** is the face's `CharacterWidth` from `ctx.MeasureFont` (seam T1): the shaped
     advance of `0`, and **not** rounded. A row's glyphs step by that advance (item 3), so a
     rounded cell would drift from the text by up to half a pixel a column. Background and
     selection edges are snapped to device pixels instead, so neighbouring fills meet without a
     seam.
   - **Cell height** is the face's line height, rounded up to whole device pixels, so rows land
     on pixel edges.
   - **Grid size.** Columns and rows are the element's content box divided by the cell, truncated.
     A change is applied after the frame, never inside it: it reaches the program, and ConPTY
     answers a resize synchronously.
   - **Window queries.** `CSI 14 t`, `CSI 16 t` and `CSI 18 t` are answered from the grid in device
     pixels, not from the element: the strip below the last row belongs to no row. The element
     tells the controller the cell's pixel size; until it does they answer zeroes. Built in
     step 5.
2. **Backgrounds.** One `DrawRect` per run of equal background along a row, with default-background
   runs skipped.
3. **Text.** One `DrawText` per **row**, with a `TextSpan` per change of style: colour (dim as
   half alpha), weight, slant, strikethrough (underlines are item 5). One call per row rather than
   per run,
   because then every glyph shares the row's baseline and steps by the font's advance, which is
   the cell, with no position worked out per run.
   - A cell that would not step by the cell's advance is drawn on its own at its column, clipped
     to it, with a space in its place in the row: a wide character, a grapheme cluster, and a
     character the face has no glyph for (`ctx.HasGlyph`, seam T1, cached per code point). Text
     would draw that from a fallback font whose advance need not be the cell's (problem 18).
   - Such a glyph sits on its own font's baseline, which can be up to about a pixel off the row's.
     Seam T2 would fix that; nothing else needs it.
   - Right-to-left letters are drawn one cell at a time too, even when the face has them, so they
     are never reordered, which is what a terminal must do.
4. **Box drawing and blocks** (U+2500–259F) are drawn as geometry with `DrawRect`, not from the
   font, so lines meet across cells: light, heavy and double lines with device-pixel thickness,
   eighths, shades and quadrants. This is what Windows Terminal, kitty and Ghostty do. **Not yet:**
   rounded corners (drawn square), dashed and diagonal lines (left to the font), the powerline
   triangles.
5. **Decorations,** drawn as geometry after the text (step 5).
   - **Underlines** in all five styles (single, double, dotted, dashed, and curly as a wave of
     short lines, one period a cell), in the SGR 58 colour or the text's, where the face's
     metrics put them and as thick as they say, in whole device pixels. Not the text's own
     decoration, which has one style in the text's colour. Dot and dash patterns are laid on the
     grid, so a colour change mid-run does not restart them.
   - **Overline,** a `DrawRect` along the top of the cell, as thick as the underline.
   - **Blinking text** (SGR 5) is left out in the off half of the blink.
6. **The cursor.** A block (with the character under it in the background colour), bar or
   underline, as the program asks (DECSCUSR), blinking when it asks for a blinking one. When the
   element is unfocused, a hollow block whatever the shape.
   - **Blinking** runs on one 530 ms clock (Windows' caret rate) shared with blinking text: a
     no-op tween on the frame loop's time, so it advances in a headless session's virtual time
     and costs one frame per phase, not a frame a tick. It runs only while something blinks,
     and a key shows the cursor at once.
7. **Overlays** within the element: search hits behind the text, the selection, the hovered
   link's underline, and the IME preedit at the cursor.
8. **Clipping.** The element clips to its content box; a special cell to its own cells. With
   `ReflowCursorLine` on, the engine no longer leaves a width-2 cell in the last column (the stock
   engine's cut did), but a renderer that trusts `Width` there would draw past the grid if it ever
   did.
9. **Pictures** (step 8): Sixel, the kitty graphics protocol (Unicode placeholders included) and
   iTerm2's inline images. The engine decodes them and keeps, on each line a picture covers, a run
   naming the picture, the columns and the slice of its pixels; the snapshot copies the runs on
   screen.
   - **Where.** A run is laid on the grid in cells: its pixels over the pixels a cell covered when
     the picture was laid out, times the cell's size now. The element sets the engine's cell to
     its own in device pixels, rounded (problem 24), so a picture fills exactly the cells it took,
     row against row, and stays in them when the font changes. A stretched kitty picture (`c`,
     `r`) carries its own pixels per cell; kitty's pixel offsets (`X`, `Y`) shift it within its
     cell. A run is cut at the grid's right edge: a narrowing resize hides part of a picture and
     widening shows it again. Edges snap to device pixels, as fills do.
   - **Order.** Kitty's z decides: below zero is drawn after the row's backgrounds and before its
     text, anything else after the text, as Sixel and iTerm2 pictures are.
   - **Textures.** One still image per picture and animation frame (`SkiaImageSource.FromPixels`,
     straight alpha, a new framework call), made when it first shows and dropped once a frame
     draws without it, so only what is on screen is held. The plan said a `PixelFrameSink` per
     image; a frame sink is for frames arriving from another thread and is uploaded on every
     draw, where a picture is still and uploaded once.
   - **Animation.** The engine owns no clock. While a picture on screen animates, a frame clock
     (a no-op tween, as the cursor's blink) hands it each frame's time and the element draws when
     a frame changed. An animation off screen waits and goes on when shown (problem 26); kitty
     runs it regardless.
   - **Options.** `TerminalOptions.Images` (on by default; off, the device attributes stop
     offering Sixel and nothing is decoded) and `MaxImageBytes`, the engine's budget for the
     pictures lines hold, scrollback included (64 MiB; the oldest go first). Kitty's store of
     pictures sent but not shown keeps the engine's own 32 MiB.

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
patch 0003. Step 6 gave it the framework's layout key, the modifier keys' sides, the keypad and the
text a key typed (seams K1–K4):

- **Printables.** Printable keys without Ctrl or Alt send nothing on KeyDown; their text comes
  from `TextInputEvent`, as UTF-8. That way layouts, dead keys and IMEs work, and nothing is typed
  twice.
- **Chords are the layout's** (K1). A Ctrl or Alt chord is the character
  `KeyDownEvent.LayoutKey` says, Shift applied:
  - Ctrl+Z is the key labelled Z on QWERTZ, which sent Ctrl+Y before;
  - Alt+Shift+comma is `ESC <` (readline's M-<), which sent `ESC ,`;
  - Alt on the ISO key beside the left Shift (`<` on a Swedish keyboard), which `KeyCode` has no
    name for, sends `ESC <`; it sent nothing.

  The element's own Ctrl+Shift+C, V and F are the layout's letters too. Where the platform gives
  no layout key (the WinForms host, headless input), the character at the key's US position.
- **A key and its text are one** (K4). A key the element sends as itself never also types the
  text it produced: Ctrl+Space (Windows types a space for it) sends NUL alone, and a keypad key in
  application keypad mode its sequence alone. A shortcut an application takes types nothing either.
- **AltGr** (K2). AltGr, and Ctrl+Alt (which Windows takes for AltGr), send the character they
  typed (`@` on a Swedish keyboard); one that typed nothing sends its Ctrl+Alt sequence at once.
  Where the platform cannot say whether text follows (the WinForms host), step 2's hold stays: a
  Ctrl+Alt key is held back and flushed by work posted to the UI thread, which runs after the
  frame's input events, so the text a key typed arrives first and drops it.
  - **Found in step 6:** through SDL, Windows' AltGr never looked like Ctrl+Alt. SDL drops the
    left Ctrl that Windows fakes ahead of AltGr, so AltGr arrived as right Alt alone, and AltGr+2
    sent `ESC 2` and then `@`. Step 2's hold, written for Ctrl+Alt, never ran there: its test
    drove the window with Ctrl+Alt, which is what Windows' own key messages say, not what SDL says.
- **The keypad** (K3). With NumLock on, the keypad's own keys, which follow application keypad
  mode (DECKPAM: `ESC O p` to `ESC O y`, `ESC O M` for Enter, `ESC O X` for `=`). With it off,
  their navigation twins: Home, Up, PageUp, Left, Begin (`CSI E`), Right, End, Down, PageDown,
  Insert and Delete. The operators and Enter are the keypad's either way. Where the platform cannot
  say what a key typed, a keypad key that types leaves it to its text, so application keypad mode
  needs a platform that says (SDL does).
- **F13 to F20** send VT220's codes (`CSI 25~` to `CSI 34~`); F21 to F24 have none in xterm and
  send nothing.
- **Everything else.** Ctrl and Alt combinations, navigation keys, F-keys and Escape are encoded
  from the key.
- **Kitty.** The kitty keyboard protocol is used when the application pushes it; the engine
  tracks the flags. Its encoder reads a web page's key and code, which are `LayoutKey` and
  `KeyCode`.
- **Tab** reaches KeyDown before focus traversal (FOCUS.md); the element stops it.
- **Shortcuts.** Ctrl+Shift+C and Ctrl+Insert (copy), Ctrl+Shift+V and Shift+Insert (paste), and
  Shift+PageUp/PageDown (scroll the scrollback) are the element's own; Ctrl+Shift+F comes with
  search. Any other key with Ctrl, Alt or Super held, and F1–F12, gets the WebView treatment:
  offered to the ancestors first; a handler that stops one keeps it from the terminal. Built in
  step 2.

**win32-input-mode on Windows (problem 11). Built in step 6.** ConPTY asks for it
(`CSI ? 9001 h`) at startup. A key sent that way reaches a console program as the input record
Windows itself would have made.

- **What goes that way:** Escape, Alt with a key that types (or with Backspace, Enter or Space),
  Ctrl+Space and Ctrl+Backspace, while the connection says `WantsWin32Input` and the program has
  the mode on. Everything else stays VT, and text UTF-8.
- **The event** (`Win32Keys`) is pressed and released at once, and carries:
  - the virtual key: a letter key's letter, Windows' answer for the scan code on the current
    layout otherwise (`MapVirtualKeyW`), a US keyboard's off Windows;
  - the set-1 scan code, from the key's HID usage through the translation Windows' keyboard driver
    makes; a test runs every key no layout moves through `MapVirtualKeyW` and finds Windows'
    own key;
  - the character Windows' layout gives for the key with its modifiers (0x7F for Ctrl+Backspace,
    none for Ctrl+Alt, which Windows takes for AltGr);
  - the modifier state with left and right apart (AltGr as right Alt plus left Ctrl, as Windows
    reports it), NumLock, CapsLock and the enhanced-key flag.
- **Measured** with a native console program reading input records (the Pty test child's
  `events` mode), through the element. As win32 events all four keys arrive exactly. As VT bytes,
  Ctrl+Space arrives as virtual key 0x40 with no character, a key no program binds; Ctrl+Backspace,
  Alt+A and Escape arrive right, each after a bare press of its modifier.
- **Not the key lost after a resize.** The spike lost the first key after a resize in Git's vim
  and blamed the bare ESC sent before it. Step 6 lost it with no ESC sent at all, through both
  ConPTYs and with bash too, while a native program lost none: problem 22, Git for Windows'
  runtime, which the terminal cannot work around.

**Mouse.**

- **When the application tracks the mouse** (`?1000`, `?1002`, `?1003`, with SGR encoding),
  presses, drags, releases and the wheel go to it. Shift held keeps the selection for the user,
  as in every terminal.
- **Otherwise** the pointer selects:
  - drag selects a range;
  - a double click selects a word;
  - a triple click selects a line;
  - Alt+drag selects a block, the same columns on every line, copied line by line (step 5).

  A selection is held in absolute lines, so it stays on its text while output scrolls; typing
  and switching screens drop it.
- **The wheel otherwise.** It scrolls the scrollback, three lines a notch, adding up a precise
  wheel's fractions. On the alternate screen it sends arrow keys. XTerm.NET does not track
  alternate scroll (`?1007`), so the element always does this there, as most terminals do by
  default.
- **Links (OSC 8, step 5).** Under the pointer a link is underlined, every stretch of it when the
  program gave it an id (a link that wrapped is one link), and the pointer becomes a hand.
  Ctrl+click raises `TerminalController.LinkActivated` with the link as the program wrote it,
  even while the program tracks the mouse; a plain click selects. Nothing opens a link unless
  the application does, and it should check the scheme: a `file:` link to a program would run
  it. The sample opens `http` and `https` only. Links the program did not mark (a URL in plain
  text) are not detected.

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
  - **Above 20 000 lines** in the normal buffer (31 ms at 100 000 × 200) a width change waits
    until the size has been still for 100 ms, as VS Code's terminal does for large buffers: a
    drag reflows once, at its end. Meanwhile the old grid is drawn clipped or padded. Built in
    step 5, on the lines the buffer holds rather than its capacity: a new terminal with room for
    100 000 lines reflows at once. A height change in the same drag still applies at once.
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
- **The resize policy follows the lines held** (see *Resize policy*), so a big scrollback makes
  width changes wait only once it has filled.
- **A later engine improvement** (one more patch): store a line that scrolls off at its content
  length instead of the width. A typical shell line is short, and the 24-byte cell would stay.

## Search

Built in step 5. `TerminalElement.Search` is `HultUI.Core`'s `TextSearch`, so a query means what it
means in the four apps' bars; the bar itself is the application's.

- **The text.** `Bind(lineCount, 1, textAt)` over the current screen's buffer as logical lines
  (wrapped rows joined, so a match across the wrap is one hit), read under the lock. It binds when
  a query is first set and again 200 ms after output pauses, keeping the current hit by its
  place in the buffer, which output does not move. Switching screens rebinds at once.
- **Hits** map back to columns through each row's starting offset and the cells' text lengths,
  so wide characters and clusters land on the right columns. They are tinted behind the text
  (`TerminalTheme.SearchHit` and `SearchCurrent`), not through `Decorate`: the row's spans are
  the text's.
- **Moving** to a hit (`Search.Move`) or changing the query scrolls it into view, centred, when
  it is off the screen.
- **Keys.** Ctrl+Shift+F raises `TerminalElement.FindRequested` for the application's bar; while
  nothing handles it, the key goes to the program. The sample's bar takes Enter and Shift+Enter,
  Alt+C and Alt+R, and Esc back to the terminal, as Junction's does.
- **Measured** (Release, this machine): reading 100 000 rows of 52 columns takes 15–25 ms
  (50–100 ms before the read stopped allocating a string per character), and a scan a few more.
  At the default 5 000 lines a rebind is about a millisecond. At 100 000 rows of 200 columns
  expect about 70 ms each time output pauses with a query open: a visible hitch. Binding only
  the lines added since the last bind is the fix if that matters; XTerm.NET's `BufferSearch`
  (3.7 ms a search) is the other.

## Accessibility

The element is a document (`AccessibilityRole.Document`), named after the session's title unless
the app names it, whose value is the visible screen (`TerminalController.ReadScreen`). Step 7 gave
it what Windows Terminal has: text a reader moves through, and announcements of new output.

- **The value** (step 5). `Element.AccessibleValue`, an internal seam, lets an element the
  framework's own types do not cover supply a value. Windows offers a document's value through a
  read-only Value pattern (`SetValue` answers `UIA_E_ELEMENTNOTENABLED`).
- **The text** (seam A1, step 7). The screen's rows, trailing blanks dropped, down to the last row
  with text or the cursor's, with the cursor as the caret and the selection mapped onto it (a
  block selection is not: it is no range of the text).
  - **Built when a reader asks,** on its thread, from a copy of the screen, and kept until the
    emulator's generation, the selection or the cell grid changes: an unchanged screen is the same
    snapshot.
  - **Geometry** is the cell grid, from where the element last drew: a character's columns times
    the cell width, a wide one two cells wide.
- **When** (steps 5 and 7). A change is told once the output has paused for 150 ms, or after 2 s
  of unbroken output: one update per burst, not one a frame. That one rebuild tells the value, the
  text and the caret, whichever changed (UIA's text-changed and text-selection-changed events,
  AT-SPI's insert, delete and caret-moved). Nothing is timed while no assistive tool listens.
  - Step 5 waited 500 ms, for the value alone. A caret that moves with the arrow keys is read at
    the caret, and half a second was too slow for that.
- **Announcements** (seam A2, step 7). At the same settle, the output since the last announcement,
  on the normal screen:
  - **not a full-screen program's**, which a reader reviews through the text instead; the place is
    taken again on the way back;
  - **not the echo of typing**: once the user has typed (or pasted), what comes back up to the first
    line break is the echo, which the reader spoke as it was typed, and is left out, and a line's
    editing that never reaches a line break is not announced at all;
  - **a screenful at most**: a flood is announced by its end;
  - **from where the last one ended**: a logical line and the characters of it already said. The
    first settle only takes the place, so a reader that starts is not read the whole screen.
- **Checked from outside** with Windows' own UI Automation client (FlaUI's UIA3, a scratch program,
  read-only) against the sample running `ping` (`--run`, new in step 7):
  - every reply was announced as it came, about one a second, from the document, as an "Other"
    notification with activity `HultUI.Announcement`;
  - the document range held the screen's twelve lines;
  - the line at a point was "Pinging 127.0.0.1 with 32 bytes of data:", its box one row down and
    40 cells of Cascadia Mono (7.6 px) wide;
  - a word was "Pinging " and two words on was ".";
  - the caret's line was the cursor's.
- **Found:**
  - **Windows never told the terminal a reader was listening.** Only the Linux bridge reported to
    `AccessibilityClients`, so on Windows the settle never ran and nothing was told at all; step 5's
    check read the value by asking, which does not need it. With the report taken out again, the
    same client heard no announcements.
  - **The Linux bridge advertised a Value interface** for every string value, with nothing behind
    it; AT-SPI's Value is numeric, and it is gone.
- **Not verified:** AT-SPI (Orca) ran nothing here; the Text interface's answers are unit-tested,
  the signals are not.
- **TextArea benefits too.** A1 is the work TextArea and text inputs need; they still expose their
  text only as a value.

## Process launch (`HultUI.Terminal.Pty`)

Built in step 4. `PtyConnection.Start(PtyLaunch, columns, rows)` starts a program under a
pseudoterminal; `StartAsync` is the same without blocking. The element resizes it to its grid on
the first frame.

- **What to run.** `new PtyLaunch(program, args…) { WorkingDirectory, Environment }`. The program
  is a path, or a name looked up on `PATH` (with `PATHEXT` on Windows, so `pwsh` finds
  `pwsh.exe`); the current directory is not searched. One that is not found throws
  `FileNotFoundException` on every platform, before anything starts. `PtyLaunch.FindProgram` is
  that lookup, for an app that wants to ask first.
- **Default shell** (`PtyLaunch.DefaultShell`): on Windows `pwsh -NoLogo` when it is on `PATH`,
  then Windows PowerShell, then `cmd.exe`; elsewhere `$SHELL`, then `/bin/bash`, then `/bin/sh`,
  as a login shell on macOS, as Terminal starts it.
- **The command line** is built with the MSVCRT rules and passed with `VerbatimCommandLine`
  (problem 4). Arguments with spaces, quotes, trailing backslashes, `&|<>` and non-ASCII
  characters arrive exactly as given. `cmd.exe` parses its own command line and is the exception,
  as it is for every program that starts it.
- **Environment:** the app's own, plus `TERM=xterm-256color` and `COLORTERM=truecolor`, plus the
  launch's variables; a null value removes one.
- **ConPTY.** The connection reports `WantsWin32Input` and `ReflowsItself`. Out-of-band ConPTY 1.24
  is used, with Porta.Pty's fallback to the in-box one. `PORTAPTY_CONPTY` stays the override. A test
  checks that the test build really runs its own `OpenConsole.exe`, and the AOT sample was seen
  running its `x64\OpenConsole.exe`.
- **Ending.** Porta.Pty's exit event carries the code; `Terminate` kills the program and what
  shares its console (a job object on Windows), with exit code -1. ConPTY keeps the output open
  after the program exits, so the connection closes the pseudoconsole once the output has been
  quiet for 100 ms, counted from the exit too (problem 19). Porta.Pty's `Dispose` closes the pipe
  we read along with the pseudoconsole, which is why it waits for quiet rather than closing at the
  exit: what is still in the pipe would be lost.
- **Packaging.** Both `OpenConsole.exe` hosts stay in an x64 build, as Microsoft's targets ship
  them (problem 16). Porta.Pty answers ConPTY's startup DA1 query and hides it from the stream;
  without that the first output waits 3 s.
- **AOT.** The sample publishes for win-x64 with no IL2xxx/IL3xxx warnings: a 4.9 MB executable
  beside `conpty.dll`, the two hosts, SDL3, Skia and HarfBuzz. Its `--check` passes under AOT in
  about a second.

## SSH (`HultUI.Terminal.Ssh`)

Built in step 8b. A shell on another machine, through SSH.NET 2026.0.0 (MIT) and SshNet.Agent
(MIT): managed code only, no natives, so an app that never opens one carries neither.

```csharp
var host = new SshConfigFile(SshConfigFile.UserFilePath).Resolve("build");   // ~/.ssh/config
var options = new SshConnectOptions
{
    Host = host.HostName, Port = host.Port ?? 22, User = host.User ?? Environment.UserName,
    Authentication = [SshAuthentication.Agent(), .. host.IdentityFiles.Select(f => SshAuthentication.KeyFile(f, AskPassphrase)),
                      SshAuthentication.Password(AskPassword)],
    VerifyHostKey = (key, ct) => ShowTrustDialog(key, ct),   // Unknown or Changed only
};
// The terminal shows at once; the prompts show on the UI thread while it connects.
var shell = new TerminalController(new DeferredConnection(
    async ct => await SshConnection.ConnectAsync(options, ct), connecting: "Connecting to build…"));
```

- **Trust.** The host's key is checked before anything is sent, against OpenSSH's `known_hosts`
  (`KnownHostsFile`; the user's `~/.ssh/known_hosts` by default, so a host trusted with `ssh` is
  trusted here and the other way round). A known key connects; a revoked one never does; an unknown
  or changed one only on `VerifyHostKey`'s decision (`Reject`, `AcceptOnce`, `Trust`), and without a
  verifier never (problem 27). `SshHostKey` carries the SHA-256 fingerprint as `ssh` prints it and,
  for a changed key, the fingerprints on file: what Vesper's SFTP brief asks its two dialogs to
  show. Trusting a changed key replaces that host's keys of that type; every other line is kept.
  The file reader takes hashed names, patterns, negation, `[host]:port` and `@revoked`;
  `@cert-authority` lines are skipped, so a host certificate is asked about by its key.
- **Signing in.** The ways given are tried in order among those the server accepts:
  - `Password`: asked for only when the server takes passwords, again while wrong (problem 32).
  - `KeyFile`: OpenSSH, PEM, PuTTY and PKCS#8 files. The passphrase is asked for only when the
    file has one, and again while wrong. PKCS#8 is read by .NET (problem 31).
  - `Agent`: `SSH_AUTH_SOCK`, else on Windows the OpenSSH agent's pipe and then Pageant.
  - `KeyboardInteractive`: the server's own questions, as it wrote them.
  Prompts are called on a background thread that waits for the answer; nothing is stored, so
  keeping a password (in the keyring, as the brief says) is the app's choice. A null answer skips
  that way. A missing key file, or no agent, is skipped too.
- **When none is accepted,** `SshSignInException` says what each way did and what the server
  takes, one line each, which a terminal can show as it is (problem 33):

  ```
  Could not sign in to jellyfin.home as root.
  The server takes: publickey, password.
    SSH agent: Windows' OpenSSH agent is not running; Pageant is not running
    key file C:\Users\you\.ssh\id_ed25519: not found
    password: not given
  ```

  A way that fails before asking the server (no key, no agent) asks it what it takes with SSH's
  `none` method, one round trip; a way the server does not take says so.
- **The user's own setup.** `SshConfigFile` reads `~/.ssh/config` as `ssh` does for what a
  connection needs: `HostName`, `User`, `Port`, `IdentityFile` (adding up), `IdentitiesOnly`,
  `UserKnownHostsFile`, and `ProxyJump` so an app can say it is not supported. `Host` patterns
  with negation, `Include`, the first value winning, and `~` and `%` tokens; `Match` blocks apply
  to nothing. `SshConfigFile.DefaultIdentityFiles` are the files `ssh` tries when no
  `IdentityFile` applies (problem 36).
- **Connecting without blocking.** `DeferredConnection` (in `HultUI.Terminal`) gives the
  controller a connection at once: it shows what it is connecting to, runs the connect on a pool
  thread, keeps what is typed and the last size meanwhile, and writes a failure into the terminal
  (problem 34). The controller reads a connection's traits when it uses them, so a Windows
  server's ConPTY traits count once connected.
- **The shell** gets a pseudoterminal of `TerminalName` (`xterm-256color`) with `IUTF8` and DEL as
  erase, follows the element's grid with `window-change`, and keeps the session alive every 30 s.
  Windows' OpenSSH server (`OpenSSH_for_Windows` in its version) runs the shell under ConPTY, so
  such a connection reports ConPTY's traits and gets win32-input-mode keys. Not verified against
  one.
- **Ending.** `Exited` carries the remote exit status, or 128 plus a signal's number (problem 28).
  It is null when the connection was lost or closed here, and when the program ended within the
  last round trip of connecting, before the status could be listened for. A lost connection ends
  the session (problem 30); `Terminate` closes the channel and the connection.
- **Flow.** Past `MaxBufferedBytes` (4 MiB) of unread output the connection stops reading from the
  network until the terminal catches up (problem 29).
- **AOT.** The package has no trim or AOT warnings; the sample publishes with it for win-x64 with
  none. A NativeAOT client was run against the test server in another process: it connected, read
  the shell's output and got its exit code.
- **Not done:** port forwarding, X11 and agent forwarding, jump hosts, host certificates, a
  password change the server demands at sign-in, reconnecting.

## libghostty-vt, checked against the seam (step 8c)

**Not built, by the rule in *Packages*: it has no release.** Checked 2026-10-04 at ghostty `main`
`f96c971`. No tag or release names libghostty (the tags are the app's, v1.0.0 to v1.3.1, and the
`tip` nightly). `vt.h` still says it is "not yet stable and is definitely going to change", and
the headers changed on 2026-10-01. The nightly ships the C source, a WebAssembly build and an
Apple framework; Windows and Linux libraries are built with Zig 0.16
(`zig build -Demit-lib-vt`).

The emulator seam (`ITerminalEmulator`) against its headers:

| Seam | libghostty-vt | Fit |
|---|---|---|
| `Feed`, `Resize`, `SetScrollback`, `ScrollViewport` | `vt_write`; `resize` (reflows the primary screen, never the alternate); scrollback limit in lines or bytes, page-rounded; `scroll_viewport` | covered; whether the cursor's own line reflows (problem 1) needs a test |
| `EncodeKey`, `EncodeMouse`, `EncodeFocus`, `Paste` | key encoder (legacy, DECCKM, keypad, modifyOtherKeys, kitty); mouse encoder (all tracking modes and formats, positions in surface pixels); focus; paste (bracketing, unsafe bytes) | covered, except **win32-input-mode**: no mode 9001, and its key event has no virtual key or scan code |
| `CopySnapshot` | render state: rows, cells with grapheme and resolved colours, style flags, underline styles, cursor, palette, modes, scrollbar | mostly: the host resolves default colours, the underline colour and inverse; an OSC 8 link only as a URI per cell (a slow path) and **no link id**; pictures per placement, not per row |
| `GetText`, `WordAt`, `LineAt`, `ReadLogicalLines` | selection formatter (unwrap, trim), word and line selection; search of its own (literal, no regex) | covered; logical lines come from the formatter, not an index |
| `Generation` | dirty state per frame and per row | partly: dirty flags the host clears, not a counter |
| `SetCellPixels` | only through `resize`; size reports answered by a host callback | partly |
| `AdvanceAnimations` | kitty images expose their current frame; nothing moves it on | **gap** |
| events | callbacks for replies, bell, title and directory (read back after), clipboard; no screen-switch event (diff the active screen) | partly |
| Sixel, iTerm2 pictures | neither (a DCS is never reported to the host; OSC 1337 is swallowed) | **gap** |

What the seam would have to absorb: events as native callbacks on the feeding thread that must not
feed again (`UnmanagedCallersOnly` trampolines and a `GCHandle`); a snapshot copied cell by cell
from the render state rather than one call; pixels in four formats; and a pre-scan of the byte
stream for what libghostty-vt does not do (win32-input-mode, Sixel, iTerm2 pictures), split with
its write-until-ground call. None of that changes the seam's members: it stays internal, and is
re-checked when a release is tagged.

## Framework seams

Each is useful beyond the terminal. No terminal package may need anything not listed here; if
implementation finds another, it goes here first. Step 2 needed none; step 3 built T1; step 6
built K1–K4; step 7 built A1 and A2.

**T1: monospace cell metrics and a default monospace family (`HultUI.Core`, `HultUI.Text`,
`HultUI.Rendering.Skia`). Built in step 3.**

- **What was missing.** `SkiaFontCollection` resolved a family name with
  `SKTypeface.FromFamilyName`, which never says a family is missing: it answers the platform's
  default face. So `"monospace"` worked only where fontconfig maps it; on Windows it drew in Segoe
  UI, a proportional face, wherever it was used (the DevTools overlay, two samples, three headless
  tests). Apps pick concrete fonts themselves (Junction: Consolas, Menlo, …). Nothing outside the
  text pipeline could read a font's metrics or glyph coverage.
- **What was built:**
  - **`FontFamilies.Monospace`** (`"monospace"`), resolved by the font collection: Cascadia Mono,
    then Consolas, then Courier New on Windows; Menlo, then Monaco on macOS; on Linux fontconfig's
    own `monospace` when it answers a fixed-pitch face, else DejaVu Sans Mono, Liberation Mono or
    Noto Sans Mono. `FontFamilies.SansSerif` names the default.
  - **Family lists,** as CSS's `font-family`: the first family installed wins, judged by the font
    manager's family match (which, unlike the legacy lookup, says when there is none). A single
    name resolves exactly as before. A list chooses the face only; fallback for missing characters
    is unchanged.
  - **`FontMetrics`** from `MeasureFont(style)` on `IMeasureContext` and `IRenderContext`, and
    `TextMeasure.MeasureFont` at build time: the face actually used, ascent, descent, line gap,
    `LineHeight`, `CharacterWidth` (the shaped advance of `0`), `IsFixedPitch`, and the underline
    and strikeout position and thickness the font gives (estimated when it gives none).
  - **`HasGlyph(style, codePoint)`** on the same three: whether the face itself has the glyph.
  - Both are default interface methods, so measure contexts that cannot read a font (test stubs,
    the headless fixed-metrics context) keep compiling and answer estimates. The headless
    recording context forwards them to the real one.
  - **Not done:** the renderer's own underline and strikeout still use its size-based estimates,
    not the font's; the apps' own `Fonts.Mono` constants are unchanged.
- **Measured on this Windows machine:** Cascadia Mono 13 px has a 7.62 advance, ascent 12.06,
  descent 3.05, underline 0.63 below the baseline and 0.63 thick. Consolas and Cascadia Mono both
  lack `∀` and `⇒`; DejaVu Sans Mono has them.

**T2: positioned text runs (`HultUI.Core`). Step 2 measured no need for speed** (see
*Rendering*); what is left for it is the baseline of a glyph drawn on its own.
`IRenderContext.DrawTextRun(string text, Point origin, TextRunStyle style)`: shape once (cached),
draw at a point, with no rectangle, layout or wrapping.

**T3: a cached picture per row (`HultUI.Core`). Step 2 measured no need.**
`IRenderContext.DrawCached(key, Size, Action<IRenderContext>)`: replay a recorded picture for a key
seen before, record it otherwise. A content hash makes a scroll redraw one row.

**K1–K4: what a key is, beyond its position (`HultUI.Core`, `HultUI.Platform.SDL3`). Built in
step 6.** `KeyCode` names a position, translated from SDL's scancode; nothing read SDL's layout
keycode, left and right modifiers were one, the keypad had no names, and a key and the text it
typed were unrelated events. All four are new members, so no existing code changes meaning.

- **K1: `KeyDownEvent.LayoutKey`.** The character the key types on the layout, as a code point,
  with Shift, CapsLock and AltGr applied and Ctrl and Alt ignored: a web page's `key` where
  `KeyCode` is its `code`. Ctrl+Z on QWERTZ is `'z'` on the key `KeyCode.Y` names; Shift+comma is
  `'<'`. Letter keys on a non-Latin layout give their Latin letter and the French number row its
  digits, as SDL's own key code does, so shortcuts work there. SDL's key code applies no modifier,
  so the SDL backend reads the shifted and AltGr levels from its keymap.
- **K2: `KeyDownEvent.ModifierKeys`,** a new flags enum: the modifier keys held, left and right
  apart, and `AltGr`. A separate value, not new `KeyModifiers` bits, because code compares
  `KeyModifiers` with `==` (a drag in `Window`, a click in Junction).
  - **AltGr** is not Alt: with it set, neither `AltRight` nor `KeyModifiers.Alt` is.
  - **X11 and Wayland** report AltGr as ISO_Level3_Shift, SDL's `MODE`.
  - **Windows** does not: SDL drops the left Ctrl Windows fakes ahead of AltGr, so AltGr is right
    Alt alone, as a US keyboard's right Alt is. What tells them apart is the text. With Alt held
    Windows sends `WM_SYSCHAR`, which SDL makes no text of, so a right Alt that typed something was
    AltGr. AltGr on a key that types nothing stays right Alt.
  - **The WinForms host** sees Windows' own key messages, where AltGr is Ctrl+Alt, and sets
    neither.
- **K3: keypad and F13–F24 key codes.** `Numpad0`…`Numpad9`, `NumpadDecimal`, `NumpadAdd`,
  `NumpadSubtract`, `NumpadMultiply`, `NumpadDivide`, `NumpadEnter`, `NumpadEquals`, `F13`…`F24`,
  added at the end of the enum. A keypad key keeps its name with NumLock off, where it means Home or
  Up; `KeyModifiers.NumLock` says which. The SDL, WinForms and WebView2 backends and CEF's key
  tables name them; WinForms and WebView2 used to report the keypad's digits as the number row's.
  `KeyDownEvent.Digit` gives 0–9 for the number row and, with NumLock on, the keypad; the time
  picker and the video player use it, so the keypad types there on every backend.
- **K4: `KeyDownEvent.Text`.** The text the keystroke typed, which still follows as a
  `TextInputEvent`: a look ahead, so a key and its character can be handled as one. Empty when the
  key types nothing (a chord, an arrow, a dead key, text input off); null when the platform cannot
  say, or an input method has the key.
  - **The SDL backend** peeks at the head of SDL's queue (`SDL_PeepEvents`). SDL queues a key's
    text right behind it, in the same pump, on Windows (`WM_CHAR`, posted by `TranslateMessage`),
    X11 and Wayland, read from SDL 3's source. A type-filtered peek would find the next key's text;
    the test fails with one.
  - **The edge:** on Windows a pump that has already taken more than three new messages stops, and
    the character lands in the next one; the key then reads as typing nothing.
  - **Ctrl and Alt:** SDL filters control characters, so Ctrl+letter types nothing; Windows types
    nothing for Alt+letter (`WM_SYSCHAR`), and X11 and Wayland send no text with Ctrl or Alt held.
- **The headless window** takes all four on `KeyDown` and `PressKey`; a key given `text` also sends
  it as text input after the key, as SDL does.
- **Tests:** `EventTranslatorKeyTests` runs real SDL on its dummy video driver with the events a
  backend would queue; one negative control each for the peek, the type-filtered peek, the shifted
  layout key and the Windows AltGr rule. `CefKeysTests`, `WebView2MappingTests`, `KeyMapTests` and
  a time picker test cover the backends' new names.

**A1: text a reader moves through (`HultUI.Core`, both bridges). Built in step 7.**

- **The model.** `AccessibleText`: an immutable snapshot of the text, its lines, a caret, a
  selection, the visible part, and `AccessibleTextGeometry`, which maps offsets to boxes and back.
  It answers character, word and line steps itself, so both bridges move alike: a character is a
  surrogate pair whole, a word a run with the blanks after it, a line with its line break.
- **The seam.** `Element.AccessibleTextSource` (internal) hands out an `IAccessibleTextSource`,
  which the bridges read on their own threads. The node carries the source and the snapshot the
  tree was built with. The differ compares snapshots and tells `Text` (with the previous node)
  and `TextSelection`.
- **Windows:** `ITextProvider2` and `ITextRangeProvider`, hand-built vtables like the other
  patterns, offered by any node with text. Ranges never move onto the empty end; selecting from a
  reader is refused. Text changes raise `Text_TextChanged`, caret moves `Text_TextSelectionChanged`.
- **Linux:** `org.a11y.atspi.Text` (its answers in plain functions, `AtSpiText`), and `TextChanged`
  insert/delete, `TextCaretMoved` and `TextSelectionChanged`. A sentence and a paragraph are a line.
- **Who uses it.** The terminal. Text inputs and TextArea are next.

**A2: announcements (`HultUI.Core`, both bridges). Built in step 7.**

- **The API.** `Element.Announce(text, politeness)`, polite or assertive. The window gives it to
  the bridge once the frame's tree holds the element, from the element's node. Nothing is kept
  while no reader listens. An element not in a window yet says it once a window takes it, and one
  removed first never does, so a rebuild's discarded twin says nothing.
- **Windows:** `UiaRaiseNotificationEvent`, kind Other, activity `HultUI.Announcement`; polite is
  `All`, assertive `ImportantAll`. **Linux:** AT-SPI 2.46's `Object:Announcement`, politeness in
  detail1.
- **Who uses it.** The terminal, for new output, and toasts, for their message (an error
  assertively): the docs said a toast's alert was announced unprompted, and nothing announced it.

## Testing

- **`HultUI.Terminal.Tests`** (headless; built in step 2):
  - The real XTerm.NET emulator fed scripted bytes through a `FakeConnection`, which records what
    was sent; the controller's real reader and writer threads run against it. They draw with the
    platform's real monospace face, so a check that depends on glyph coverage asks the face
    (`TextMeasure.HasGlyph`) rather than assuming one.
  - Waiting is deterministic: the controller counts the bytes it has fed and whether a wake is
    pending, so a test waits for exactly its own output and the UI thread's handling of it,
    never for a time.
  - The element is tested through the headless window: the grid it sizes, keys (with the AltGr
    rule), text, IME, selection and copy, paste bracketing, the wheel, mouse and focus reports,
    titles, and what it draws, through the recorded render log.
  - Still to add from the spike: question 3's reflow checks, and the resize policy when it is
    built.
  - Accessibility (step 7): the text, the caret, the geometry and the selection; the source read
    off the UI thread; the text and caret changes told; announcements, the echo of typing left
    out, a full-screen program not announced, a flood by its last screenful, nothing while no
    reader listens.
  - Pictures (step 8): each protocol drawn (by the recorded call, and by its pixels where a crop
    or a colour is the point), in the cells it took, under or over the text, cut at the edge, let
    go off screen, and animated by the frame clock only while shown.
- **`HultUI.Terminal.Pty.Tests`** (built in step 4; real pseudoterminals, about 10 s):
  - **A child program of its own** (`HultUI.Terminal.Pty.Tests.Child`, run as `dotnet <dll>`)
    prints its arguments, its window size, how many lines it read, a flood, and the exit code it
    is told. So most tests depend on no shell's behaviour and run the same on every platform.
  - Arguments arriving exactly, exit codes, the resize seen by the program, a 1 MiB paste through
    the writer queue, a flood, the environment and working directory, a missing program, an exit
    while a background process holds the terminal, and two teardowns that must not hang (time
    limits on the test, not sleeps) and must end the process.
  - **Shells:** the default shell works something out, prints it and exits with 7; with pwsh on
    Windows, the narrowed prompt stays whole and typing continues it (problem 1, through real
    ConPTY). On Windows a test checks the out-of-band `OpenConsole.exe` runs.
  - **Keys through ConPTY** (step 6, Windows), driven through the element: the child's `events`
    mode reports every console input record, so a test reads the ambiguous keys exactly as a
    native program gets them, and another sees 20 resizes and the key after each. Git's vim, when
    installed, leaves insert mode on Escape. `GitsPrograms_LoseNoKeyAfterAResize` measures
    problem 22 with Git's bash, only when `HULTUI_TERMINAL_MEASURE=1`: it fails until the runtime
    is fixed.
- **`HultUI.Terminal.Ssh.Tests`** (built in step 8b; about 25 s):
  - **A real SSH server in the test process:** Microsoft's Dev Tunnels SSH library (MIT) on a
    loopback port, whose shell answers `exit N`, `kill SIG`, `size`, `flood N` and `drop` (a TCP
    reset). Nothing is installed or configured on the machine, and loopback asks no firewall.
  - **An SSH agent of its own** on a private pipe (Windows) or socket, answering the key list and
    signatures, so the user's agent is never asked.
  - Host keys (unknown, trusted and recorded, changed and rejected or replaced, revoked, no
    verifier), every way to sign in (wrong passwords and passphrases asked again, SEC1, PKCS#8 and
    encrypted RSA keys, an `ssh-keygen` key with a passphrase when `ssh-keygen` is found), the
    terminal's type, size and modes, resizes, the exit status and signals, a lost connection,
    `Terminate`, flow control, and a `TerminalController` running the shell to its exit code.
  - `known_hosts` on its own: names, ports, hashes, patterns, negation, revocation, and writes
    that keep every other line.
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
3. **Seam T1: done 2026-10-03** (see *Framework seams*).
   - **The framework:** `FontFamilies.Monospace`, family lists, `FontMetrics` through `MeasureFont`
     and `HasGlyph` on the measure context, the render context and `TextMeasure`. Tests: 8 in
     `HultUI.Rendering.Skia.Tests` against the platform's real fonts, 3 in `HultUI.Core.Tests` for
     the estimates, 2 in `HultUI.Headless.Tests` that build time and draw time agree, recorded or
     not. Negative controls: without the generic name, `monospace` resolved to Segoe UI and two
     tests failed; without the recording context's forwarding, the headless test failed.
   - **The terminal:** its face is the configured family followed by `FontFamilies.Monospace`; the
     cell comes from `MeasureFont`; a missing glyph is found with `HasGlyph` instead of step 2's
     fixed ranges (the test fails if the element assumes every glyph is there); right-to-left
     letters are still drawn on their own. 75 tests.
   - **On this Windows machine** the default face moved from Consolas to Cascadia Mono, so the
     sample's 980 × 640 window holds 126 columns instead of 134.
   - **Not verified:** Linux and macOS ran nothing here (no WSL distribution on this machine).
     The Linux behaviour rests on Skia's fontconfig manager: a family match drops fontconfig's
     weak defaults, so an unknown name matches nothing and `monospace` keeps its preferred
     aliases. Linux CI is the first real check; macOS has no runner.
4. **`HultUI.Terminal.Pty`: done on Windows 2026-10-03** (see *Process launch*).
   - **The package:** `PtyLaunch` (the program found on `PATH`, the default shell, the
     environment) and `PtyConnection` over Porta.Pty 2.2.3, with MSVCRT quoting, the exit code, and
     the close after quiet that ConPTY needs (problem 19).
   - **The controller** gained `ReadScreen()` (the visible text, for a check or a reader), an exit
     reported when the output stays open (problem 21), no report after `Dispose`, and connections
     whose exit a late listener still hears (`ConnectionExit`, problem 20). Code points above the
     BMP are drawn whole; a cast to `char` had made 𝐀 a Hangul syllable in faces that have it.
   - **The OpenConsole trim was not built:** Microsoft's rule keeps both hosts (problem 16).
   - **Tests:** 23 in `HultUI.Terminal.Pty.Tests`, stable over six runs of the suite; 81 in
     `HultUI.Terminal.Tests`. Negative controls: Porta.Pty's own quoting fails the argument test;
     without the close after quiet, the prompt-exit test fails; without the late listener, two
     tests fail; without the exit fallback, its test times out.
   - **The sample** runs the user's shell (the header switches to the demo shell, and `--demo`
     starts with it); `--check` drives the real shell end to end and passes in about 0.9 s under
     JIT and 1.0 s under NativeAOT, which publishes with no warnings.
   - **Not verified:** Linux and macOS ran nothing here. The forkpty half of every Pty test, and
     step 0's Linux list, wait for the Linux machine and Linux CI.
5. **Resize policy, search, hyperlinks, and step 2's "not yet": done 2026-10-03,** in four commits.
   - **Rendering and input:** the five underline styles in their colours, blinking text and
     cursor, Alt+drag block selection, the window-size reports, and the resize policy.
   - **Links:** OSC 8 hover, underline and hand; Ctrl+click to `LinkActivated`.
   - **Search:** `TerminalElement.Search` over the scrollback, tinted and revealed; the sample's
     find bar.
   - **Accessibility:** the screen as the document's value, through a new internal seam
     (`Element.AccessibleValue`) and a read-only Value pattern on Windows.
   - **Tests:** 115 in `HultUI.Terminal.Tests`; each feature has a negative control (the resize
     hold, the blink, the underline colour, the link id, the kept search hit, the joined rows,
     the reveal, the settle and its limit, the early title). The framework's Core and headless
     suites pass unchanged.
   - **Found on the way:** the early title above, and a search read that allocated a string per
     character (now 3× faster).
   - **Still not built:** rounded, dashed and diagonal box drawing and the powerline triangles;
     URL detection in plain text; incremental search binding for very large scrollbacks.
6. **Windows input and seams K1–K4: done on Windows 2026-10-04** (see *Input* and *Framework
   seams*).
   - **The framework:** `KeyDownEvent.LayoutKey`, `ModifierKeys` with AltGr, `Text`, `Digit`, and
     the keypad and F13–F24 key codes, in the SDL, WinForms, WebView2 and CEF backends and the
     headless window. The time picker and the video player take the keypad's digits.
   - **The terminal:** chords by the layout's character, the exact AltGr rule, a key's own text
     never typed twice, the keypad with NumLock and application keypad mode, F13–F20,
     win32-input-mode for the four ambiguous keys on ConPTY, and Windows' Alt codes.
   - **Tests:** 136 in `HultUI.Terminal.Tests` (21 new), 9 in `EventTranslatorKeyTests` on real
     SDL, 4 new on ConPTY in `HultUI.Terminal.Pty.Tests`, and new cases in the CEF, WebView2,
     WinForms and time picker tests. Each mechanism has a negative control (the peek, the
     type-filtered peek, the shifted layout key, the Windows AltGr rule, the key's text, the win32
     path, the layout's chords and own keys, the hold, NumLock, Alt codes, CEF's names, the time
     picker's keypad). The whole solution passes.
   - **Found on the way:**
     - Through SDL, Windows' AltGr was right Alt alone, so AltGr+2 sent `ESC 2 @`; step 2's
       Ctrl+Alt hold never ran there (problem 10).
     - Alt+Shift+comma sent `ESC ,`, and Alt on a key `KeyCode` has no name for sent nothing.
     - Ctrl+Space typed NUL and then the space Windows types for it.
     - The spike's lost key after a resize is Git for Windows' runtime, not the ESC (problem 22).
       Verifying "against Windows Terminal" became a native console program reading the input
       records, which says exactly what a program gets; driving Windows Terminal would need
       keystrokes sent to a real window.
   - **Not verified:** X11 and Wayland ran nothing here; the SDL behaviour there (AltGr as
     `MODE`, a key's text in the same pump) is read from SDL 3's source.
7. **Accessibility: done on Windows 2026-10-04** (see *Accessibility* and seams A1 and A2).
   - **The framework:** `AccessibleText` and its geometry, `IAccessibleTextSource`, the node's text,
     the differ's `Text` and `TextSelection`; UIA's text pattern and notifications; AT-SPI's Text
     interface, text signals and announcements; `Element.Announce`; toasts announcing.
   - **The terminal:** its screen as text with the cursor as caret, the cell grid as geometry, the
     selection, a 150 ms settle, and announcements of new output. The sample takes
     `--run <program> [arguments]`.
   - **Tests:** 146 in `HultUI.Terminal.Tests` (10 new); `AccessibleTextTests` (7) in Core;
     `UiaTextPatternTests` (9) and `AnnouncementTests` (5) headless; `AtSpiTextTests` (5). Negative
     controls: the window handing announcements on, the announcement kept until hosted, the differ's
     text change, the echo, the alternate screen, the flood's cap, the range's stop before the end,
     and the Value interface; the Windows report to `AccessibilityClients` by the outside client,
     which heard nothing without it.
   - **Found on the way:** Windows never reported a listening reader, and the Linux bridge
     advertised a Value interface it did not serve (both under *Accessibility*). A toast mounted in a
     tree still being built had no window to announce through, which is why an element keeps an
     announcement until a window takes it.
   - **Not verified:** AT-SPI and Orca; selecting text from a reader is not offered on either
     platform.
8. **Images, SSH and libghostty-vt.**
   - **8a, pictures: done 2026-10-04** (see *Rendering*, item 9).
     - **The terminal:** Sixel, kitty (placeholders included) and iTerm2 pictures drawn from the
       engine's runs, the engine's cell set from the element's, kitty's z order, textures held
       only while shown, the animation clock, and `TerminalOptions.Images` and `MaxImageBytes`.
       The demo shell's `images` command shows all of it.
     - **The framework:** `SkiaImageSource.FromPixels`; the headless recorder keeps a draw's source
       rectangle, where it used to draw the whole image squashed into the crop's box.
     - **The engine:** patch 0006 (problem 25).
     - **Tests:** 158 in `HultUI.Terminal.Tests` (12 new), 2 254 in the engine's suite (14 new).
       Negative controls: the engine's cell, the layer split, the edge cut, the sweep, the clock,
       the option, the clock only while shown, iTerm2, and the recorder's crop; in the engine,
       each of patch 0006's seven mechanisms, and its two accounting fixes together, which
       reproduce the drift.
     - **Seen in the sample** through the headless driver: the four pictures, and the animation
       stepping.
     - **Found on the way:** problems 23 to 26.
   - **8b, SSH: done on Windows 2026-10-04** (see *SSH*).
     - **The package:** `HultUI.Terminal.Ssh` on SSH.NET 2026.0.0 and SshNet.Agent: `SshConnection`,
       `KnownHostsFile`, `SshAuthentication`'s four ways, host-key decisions, the exit status, flow
       control.
     - **The sample** takes `--ssh user@host[:port]`, signing in with the agent or a key file in
       `~/.ssh` without a passphrase, and trusting a new host only with `--accept-new`.
     - **Tests:** 36 in `HultUI.Terminal.Ssh.Tests`, against a real SSH server and agent in the test
       process. Negative controls: the host-key handler, the password's second attempt, the PKCS#8
       reader, the exit-status hook, flow control, closing on error, replacing a changed key,
       revocation, the terminal modes, and negation in `known_hosts`.
     - **Found on the way:** problems 27 to 32, all in SSH.NET and solved in the package; none
       reported upstream. The Pty environment test timed out once in a full-solution run beside
       Junction's suite and passes alone; ConPTY's 100 ms close after the exit (problem 19) is the
       likely cause under load, and it is a task of its own.
     - **Not verified:** a real OpenSSH server (none here, and installing one changes the machine:
       the sample's `--ssh` is how to try one), Windows' OpenSSH server's ConPTY traits, Linux.
     - **After the first real host (2026-10-04).** The owner's `--ssh root@jellyfin.home` showed
       "Permission denied (publickey)" and fell back to the demo shell. OpenSSH's `none` method
       showed the server took passwords too; the owner's keys are chosen per host in
       `~/.ssh/config`, no agent runs, and no default key file exists. So: problems 33 to 36. The
       sign-in failure says what each way did and what the server takes; `SshConfigFile`;
       `DeferredConnection`; and a framework fix so a dialog's field can take the keyboard. The
       sample now signs in as `ssh` would: `--ssh [user@]host[:port]` through `~/.ssh/config`, the
       agent unless `IdentitiesOnly`, the host's key files or the default ones, then the server's
       questions and a password, each asked in a dialog in the window; an unknown host's
       fingerprint is asked about too (`--accept-new` still trusts without asking), a changed key
       only replaced after typing the host's name. OpenSSH also accepted the `known_hosts` line the
       package wrote for that host. Tests: 48 in `HultUI.Terminal.Ssh.Tests`, 4 more in
       `HultUI.Terminal.Tests`, 2 in `HultUI.Headless.Tests`; negative controls for each new
       mechanism; the sample's whole sign-in (unknown host, a wrong password, the shell; and a
       cancelled one, with the report) run headless against the test server.
   - **8c, libghostty-vt: checked 2026-10-04, not built** (see *libghostty-vt, checked against the
     seam*). Still no release, and its header still warns the API will change. Against the seam:
     input encoding, selection, scrolling and most of the snapshot are covered; win32-input-mode,
     kitty animation, Sixel, iTerm2 pictures and OSC 8 link ids are not, and would stay with the
     host. The seam keeps its members and stays internal until a release is tagged.
