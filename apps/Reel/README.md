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
  scrubber into one piece per chapter.
- **Single instance.** A second launch hands its file to the running window over a named
  pipe instead of opening another one — Vesper's arrangement, and the reason "set as
  default and select a season" does not produce twelve windows.

## Not yet

- **Shell integration**: registering the file associations, and an "Open with" entry.
  Windows 11 will not let an application make itself the default — you register, then
  confirm once in Settings — so this is a per-user registration plus a link to that page.
- **Scrubber previews.** Aperture gets stills from the server; a local file has none, so
  this would mean generating them from a second mpv instance.
- **Subtitles beyond what mpv loads by itself**, a playlist you can see, and per-file track
  memory (the languages are remembered globally, as in Aperture).

## Tests

```bash
dotnet test tests/Reel.Tests
```

The folder ordering, the resume rules and the empty states are covered headlessly over a
fake decoder — no libmpv, no real media. To look at it:

```bash
dotnet run .claude/skills/run-hultui-app/driver.cs -- --app reel --media clip.mkv --out shots settle:3000 shot:reel
```
