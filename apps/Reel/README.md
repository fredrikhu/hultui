# Reel

A local video player: open a file, play it, remember where you stopped. App #5, and the
smallest — the picture and its transport bar are `HultUI.Media`'s `VideoPlayer`, the same
overlay [Aperture](../Aperture/README.md) uses, so what lives here is only what a *file*
needs and a library does not.

```bash
dotnet run --project apps/Reel -- "D:\films\Stalker.mkv"
```

Reel plays through **libmpv**. Without one it starts anyway and says where it looks (see
`src/HultUI.Media.Mpv/README.md`); everything else in the window is honest about that
rather than refusing to open.

## Installing it

Tagging `reel-v<x.y.z>` builds a Windows installer and publishes it as a release
(`.github/workflows/reel-installer.yml`): the **Native AOT** publish — one executable,
no runtime to install — with the trimmed LGPL libmpv beside it, so a fresh machine
plays video without hunting for a decoder. Per-user by default, and the wizard offers
to register the file types (the same `--register` as below; Windows still asks before
anything becomes the default). Uninstalling unregisters them again.

Locally:

```bash
dotnet publish apps/Reel -c Release -r win-x64
```

```bash
"%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" /DAppVersion=0.1.0 apps\Reel\Installer\reel.iss
```

(Copy `libmpv-2.dll` and a `LICENCE-mpv.txt` into the publish folder first — CI pulls
both from the `mpv-natives-ci` release.)

## Why it is not part of Aperture

Aperture's shell is a server: a rail of libraries, home rows, search, and playback sessions
reported back to Jellyfin. A file on disk has none of that, and a default player has
requirements that pull the other way — start fast enough that double-clicking an MKV feels
instant, be single-instance so selecting twelve episodes opens one window, and never ask
about a server that was never configured. What the two share is the expensive part, and
they share it as a library rather than as a fork.

## What it does

- **Opens what it is given**: a path on the command line, `Ctrl+O`, or a file dropped on
  the window.
- **The folder is the running order.** Page Down / Page Up (or N / P) walk the playable
  files beside the one that is open, sorted the way a file manager sorts — "Episode 2"
  before "Episode 10", which ordinal comparison gets backwards on exactly the files people
  watch in sequence. Either end is the end; it does not wrap.
- **Remembers where you stopped**, keyed by path *and* size, so the same name holding a
  different encode is a different film. Under 30 seconds counts as not started; inside the
  last 20 counts as finished — the point is cleared rather than resuming in the credits.
- **Chapters from the container.** mpv reports the file's own marks, and they cut the
  scrubber into one piece per chapter. **C** lists them; Page Down / Page Up step between
  them, and roll on to the next file past either end, so one key covers a whole evening
  whether or not the file has marks. Back from inside a chapter restarts it before it leaves
  it — press twice to reach the one before, as a CD player does.
- **Stills on the scrubber.** A second, silent mpv walks the file in the background and the
  frames fill in as they are made — up to 120 of them, spread across the film, so a three-hour
  epic gets one every ninety seconds rather than none after the fortieth minute. Nothing is
  decoded on the way to the screen: hovering reads what is already there, or shows the nearest
  still that is.
- **Remembers the audio and subtitle language you chose**, by language rather than track
  number, and applies it to the next file that has one — the same rule Aperture follows, and
  the same code. Switching off subtitles counts as a choice too. What the player applies
  never overwrites the preference; only you do.
- **Single instance.** A second launch hands its file to the running window over a named
  pipe instead of opening another one — Vesper's arrangement, and the reason "set as
  default and select a season" does not produce twelve windows.

## Being the default player (Windows)

```bash
dotnet run --project apps/Reel -- --register
```

or press **Make Reel the default…** in the empty window. Both do the same two things:
write the registration, then open the Settings page where the choice is made.

**An application cannot make itself the default, and this one does not pretend to.**
Windows 8 ended that, and 10 and 11 enforce it with a signed `UserChoice` hash only the
shell can write — anything that forges it is malware by Microsoft's own definition, and the
shell undoes it. What an app may do is *register*: a ProgID, the command to open a file
with, an icon, and the extensions it handles, so Reel appears under "Open with" and as a
choice in Settings → Default apps. The last word is yours.

Everything is written under `HKEY_CURRENT_USER` — no administrator, nothing machine-wide —
and `--unregister` removes all of it. `--register --quiet` skips opening Settings, for an
installer. The keys, if you want to look:

| Key | What it does |
|---|---|
| `Software\Classes\Reel.Video\shell\open\command` | how to open a file (exe and `%1` both quoted) |
| `Software\Classes\<ext>\OpenWithProgids\Reel.Video` | offers Reel without touching whatever owns the extension |
| `Software\Reel\Capabilities\FileAssociations` | what Settings reads to list the types |
| `Software\RegisteredApplications\Reel` | what puts Reel on that page at all |

## Not yet
- **Subtitles beyond what mpv loads by itself**, and a playlist you can see.
- **Stills beside the chapter rows.** The scrubber has them now; the chapter list does not.
- ***Per-file* track memory** — the languages are remembered globally, not per title.

## Tests

```bash
dotnet test tests/Reel.Tests
```

The folder ordering, the resume rules and the empty states are covered headlessly over a
fake decoder — no libmpv, no real media. To look at it:

```bash
dotnet run .claude/skills/run-hultui-app/driver.cs -- --app reel --media clip.mkv --out shots settle:3000 shot:reel
```
