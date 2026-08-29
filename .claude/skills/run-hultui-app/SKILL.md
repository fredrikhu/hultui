---
name: run-hultui-app
description: Run, drive, and screenshot the HultUI desktop apps — Junction, Vesper Commander, Meridian. Use when asked to run one of them, look at a UI change, take a screenshot, check how something renders, or interact with the app rather than its tests.
---

Renders a HultUI app off-screen and writes PNGs of what it drew, via
`.claude/skills/run-hultui-app/driver.cs`. The driver mounts the real app in a
`HeadlessSession` — the same element tree, layout, input dispatch and Skia renderer the
window uses — against a CPU surface, so a captured frame is the app's own pixels.

All paths below are relative to the repo root.

**Do not screenshot the real window.** `dotnet run --project apps/Junction` puts an SDL
window on the user's desktop and there is no safe way to drive or capture it — see
[Gotchas](#gotchas). The driver is the path.

## Prerequisites

The .NET SDK, already required to build this repo. Nothing else — no display, no GPU.

```bash
dotnet --version   # 10.0.300 when this was written
```

The driver is a .NET file-based app: the `#:project` lines at the top of `driver.cs` pull in
`HultUI.Headless` and the three app projects, so `dotnet run` builds what it needs. First run
takes ~40s; later ones are a few seconds.

## Run (agent path)

Start with no steps. You get a frame plus the accessibility tree, which is how you find out
what is on screen and what its names are:

```bash
dotnet run .claude/skills/run-hultui-app/driver.cs -- --app junction --repo . --out /tmp/shots
```

Then add steps. They run in order, left to right:

```bash
dotnet run .claude/skills/run-hultui-app/driver.cs -- --app junction --repo /tmp/demo --out /tmp/shots \
  key:Digit4 'waitfor:Stage all' shot:changes 'drag:598,400,768,400' shot:changes-wide \
  key:Digit3 settle:1500 shot:commit
```

**Then read the PNG.** A frame you did not look at proves nothing.

### Options

| Option | Meaning |
|---|---|
| `--app junction \| vesper \| meridian` | Which app to mount. Default `junction`. |
| `--repo <path>` | Junction: the repository to open. Defaults to the git root of the working directory. |
| `--dir <path>` / `--dir2 <path>` | Vesper: the left and right panes' folders. |
| `--out <dir>` | Where PNGs land. Created if missing. Default `shots`. |
| `--size 1400x900` | Window size in layout units. |

### Steps

| Step | Does |
|---|---|
| `shot:<name>` | Writes `<out>/<name>.png`. |
| `dump` | Prints the accessibility tree — the fastest way to find a target. |
| `key:<KeyCode>[+Ctrl][+Shift][+Alt]` | e.g. `key:Digit4`, `key:Down+Shift`, `key:F+Ctrl`. Names come from `KeyCode`. |
| `click:<x>,<y>` or `click:@<text>` | `@` finds an element by its accessible name and clicks its centre. |
| `drag:<x1>,<y1>,<x2>,<y2>` or `drag:@<text>,<dx>[,<dy>]` | Press, move, release as separate events. |
| `waitfor:<text>[,<seconds>]` | Pumps until an element with that name appears. Default 30s. |
| `settle[:<ms>]` | Pumps frames for that long. Default 1200ms. |
| `size:<w>x<h>` | Resizes the client area mid-run, then settles. |

Two more that were run while writing this:

```bash
# Vesper, both panes pointed somewhere, then Shift+↓ marking
dotnet run .claude/skills/run-hultui-app/driver.cs -- --app vesper --dir /tmp/demo/src/core --out /tmp/shots \
  key:Down key:Down+Shift key:Down+Shift shot:vesper-marked

dotnet run .claude/skills/run-hultui-app/driver.cs -- --app meridian --out /tmp/shots shot:meridian
```

### Measuring a frame, not eyeballing it

Alignment and hairline questions do not survive a visual check — a doubled divider three
pixels apart reads as one line at a glance. Scan a row and print where the colour changes.
This is how the splitter's smeared second line was found:

```powershell
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap "C:\Users\fredr\AppData\Local\Temp\shots\changes.png"
$prev = $null
for ($x = 260; $x -lt 900; $x++) {
  $c = $bmp.GetPixel($x, 400)
  $hex = "{0:X2}{1:X2}{2:X2}" -f $c.R, $c.G, $c.B
  if ($prev -ne $null -and $hex -ne $prev) { "x=$x  $prev -> $hex" }
  $prev = $hex
}
$bmp.Dispose()
```

## Run (human path)

The real app, on the user's desktop. Useful to hand back to them; useless to you.

```bash
dotnet run --project apps/Junction -- /path/to/repo
```

Set `XDG_CONFIG_HOME` (and `VESPER_CONFIG_DIR` for Vesper) to a scratch directory first, or
it opens and rewrites the user's real settings.

## Gotchas

- **Never capture the real window.** `SetForegroundWindow` from a background shell routinely
  fails to raise an SDL window, so a screen grab at the window's rectangle returns whatever
  is sitting on top of it — the user's own screen, not the app. Synthesized input has the
  same failure: it lands wherever focus actually is. `.claude/skills/testing/SKILL.md` says
  the same about keyboard input; it goes for pixels too.
- **Isolate the config before mounting anything.** All the apps read real settings on startup
  and write them back, so an unguarded run reorders the user's Junction workspaces or leaves
  their file manager showing whatever folder the driver visited. Junction reads
  `XDG_CONFIG_HOME`, Vesper reads `VESPER_CONFIG_DIR`, Meridian persists nothing. The driver
  sets both to a temp directory before constructing anything — keep it that way.
- **Vesper ignores the folders passed to its constructor.** They are a seed; the app then
  restores the panes from its saved session, so it shows wherever it was last left. The
  driver calls the private `NavigateTo` for both panes after mounting, which is what the
  Vesper tests do too.
- **Resize with `size:`, not with a second run.** A fresh process at the new width measures
  everything correctly and shows nothing; the bugs worth finding are the ones where a
  relayout reuses something measured at the old width. Junction's wrapped diff drew every
  row over its neighbour that way, and only a resize *within* one session reproduced it.
- **`settle` pumps frames; it does not sleep.** Work lands on the UI thread when the session
  pumps, so a wall-clock wait with no pumping finishes with the loaders exactly where they
  were.
- **Prefer `waitfor:` over `settle:` for anything git-backed.** Junction's Changes and Commit
  tabs come from subprocesses; a fixed number of frames is a race that passes on an idle
  machine and captures a "Loading…" pane on a busy one.
- **A 1px line centred in an even-width box lands on a half pixel** and rasterises two pixels
  wide at half intensity. Next to a crisp border it reads as a rendering fault. Scan the row
  (above) rather than trusting your eye.
- **Not everything has an accessible name.** The pane splitter is a bare box with a cursor and
  no label, so `@` cannot find it — take a `shot`, read the x off it, and drag by coordinates.
- **Junction's view keys are Digit1–Digit6**: Overview, History, Commit, Changes, Branches,
  Tags.
- **`/tmp` in the Bash tool is not `/tmp` in PowerShell.** Git Bash maps it to
  `%LOCALAPPDATA%\Temp`, so a driver run that writes to `/tmp/shots` leaves its PNGs in
  `C:\Users\<you>\AppData\Local\Temp\shots` as far as the PowerShell tool is concerned. Pass
  the Windows path when handing a file between the two.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `nothing on screen reads "X"` | Run with no steps, or insert `dump`, and read the tree. The accessible name is often not the visible label. |
| Frame shows "Loading…" or an empty pane | Add `waitfor:<text>` for something that only exists once loaded, rather than more `settle`. |
| `unknown step '…'` | Steps are `verb:arg`. Quote any step containing a space: `'waitfor:Stage all'`. |
| Junction opens the wrong repository | `--repo` defaults to the git root of the working directory; pass it explicitly. |
| Vesper shows a folder you did not ask for | Its saved session won. Confirm `VESPER_CONFIG_DIR` is being set before the app is constructed. |
