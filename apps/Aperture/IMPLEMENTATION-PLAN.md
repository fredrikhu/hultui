# Aperture — implementation plan

App #4: a Jellyfin client over the framework's video player. Written after slice
1 landed, so the first section is what exists rather than what was intended.

## Slice 1 — connect, browse, play (**done**)

| | |
|---|---|
| `Aperture.Core` | `JellyfinConnection` (address walk, probe, sign-in, API-key sign-in, resume), `JellyfinLibrary` over Jellyfin.Sdk, `IMediaLibrary` + the app's own records, `ApertureSettings` (file + credential store), `FakeMediaLibrary` |
| `Aperture` | Halide theme, shell (title bar + rail + navigation stack), connect form, Home, library grid, item page, search, settings, player sheet, `PosterCache` |
| tests | 35 unit + headless tests, 6 live-server tests (skipped without a server) |
| driver | `--app aperture`, with `--server` / `--api-key-file` for a real server |

Three things found on the way, each worth knowing before touching this code:

1. **Jellyfin sends ids without dashes.** `"Id":"1e2c7967a01c4918…"` is the `N`
   format; `System.Text.Json` reads only `D`. Kiota 1.x throws on the first user
   the client looks at, and 2.x silently yields `Guid.Empty`, which is worse. The
   fix is a converter on the serialization context the adapter is built with —
   `JellyfinSerialization.cs`. Without it the SDK cannot read this server at all.
2. **An API key is not a session.** It authenticates as the server, so playback
   reports are accepted (204) and not recorded: watch state belongs to a user
   session. Fine for browsing and screenshots, useless for proving the resume
   point — which is why that live test says so instead of failing.
3. **`/Items/Latest` hides what you have watched**, so a well-watched library's
   "recently added" row is empty. Aperture asks the ordinary item query sorted by
   date instead.

## Slice 2 — the Wholphin gap list, top tier (**done**)

Ranked against [Wholphin](https://github.com/damontecres/Wholphin)'s feature list, then
checked against what this server actually serves — which moved two of its headline
features down and one of ours up.

| | |
|---|---|
| Cast & crew | the detail page credits them, and a name opens everything that person is in |
| Favourites | a view and a rail key (0); the heart had nowhere to lead |
| Auto-play next | the server decides what follows; three in a row and it asks instead |
| Chapters | a list (C) and Page Up/Down; the films here carry 27 of them |
| Track memory | audio and subtitles remembered by language, "off" stays off |
| Keyboard | arrows walk every shelf, Enter opens, Space plays, and cards take real focus |

Framework changes that fell out: a rounded box clips its children round, and
`VideoPlayer.FocusPicture()` hands the picture the keyboard when it opens.

**What the server did not have yet** when this slice was written: trickplay images and
chapter images were not generated, `/MediaSegments` answers empty (no skip intro), and
there are no collections or playlists to browse. The first two were switched on later and
are now drawn — see slice 4.

## Slice 3 — the second tier (**done**)

| | |
|---|---|
| Play queue | play all, shuffle, and a queue you can see and jump around in |
| Filters | genre, unwatched and favourites, built from what the library actually holds |
| Card actions | mark watched, heart, play from the start — without leaving the shelf |
| Library as a list | the same virtual grid one across, with year, rating, runtime and genres |
| Home layout | rows move, hide and come back; the two “what now” rows fold into one |
| Quick Connect | sign in by approving a code, no password typed into this window |
| Streaming quality | a bitrate ceiling that really transcodes, applied to what is playing |

Framework changes that fell out: menus and other overlays are in the accessibility
tree, at the coordinates they are drawn, so assistive tech and the driver can reach
a menu entry at all.

One shape worth copying: Quick Connect waits on a **timer, one request per tick**,
not inside a single long call. A producer that blocks for minutes cannot draw the
code it is waiting for — and under the headless session, where producers run inline
at the top of a frame, it freezes the whole pump.

## Slice 4 — what the server generates (**done**)

| | |
|---|---|
| Scrubber stills | trickplay sheets, cut per position; the still's own time under it |
| Chapter pictures | the frame each mark starts on, beside its name in the list |

Framework changes that fell out: `IRenderContext.DrawImage(source, dest, sourceRect)` and
`ImageElement.SourceRect` (a sprite sheet is drawn, not cropped), `SliderElement.OnHoverValue`
(pointing at a track is not setting it), and `VideoPlayer.PreviewFrame`, which is how an app
answers for a position without the player knowing where stills come from.

## Slice 5 — the next obvious things

- **Season and episode pages beyond one level.** A season page lists episodes;
  a series with many seasons pages them.
- **Resume across devices mid-playback.** The heartbeat is there; what is missing
  is noticing that another client moved the position while this one was paused.
- **Search that searches as you type**, debounced, over `SearchTerm` — currently
  every keystroke past two letters starts a query.

## Slice 6 — what would make it a daily driver

- ~~**Native AOT publish.**~~ **Done 2026-09-24.** `PublishAot` is on, and there is an
  installer (`apps/Aperture/Installer/aperture.iss`, tagged `aperture-v*`). ILC does
  emit one trim warning — `Microsoft.Kiota.Serialization.Json` produced trim warnings
  (IL2104) — so the native binary was proven rather than assumed: `Aperture --probe
  <address> --api-key-file <path>` runs discovery, an API-key sign-in, the library
  list and a full item read (chapters, trickplay, image tags: the untyped payloads)
  and prints what came back. Against the real server, as a native binary, it does.
- **Multiple servers.** The settings file already holds a list and the credential
  store is keyed by server id; the UI assumes one.
- **Offline shelf.** Artwork is cached in memory only, so a server that goes away
  takes the posters with it. A disk cache under the config directory would make
  the app open instantly and survive a reboot.
- **Downloads.** `/Items/{id}/Download` is one call; the work is the library of
  what has been downloaded and playing from it when the server is unreachable.

## Things deliberately not done

- **A device profile, by default.** Aperture tells the server it can direct-play and
  lets it decide, because mpv plays what the server would otherwise spend CPU
  converting. The one exception is a quality ceiling: a `MaxStreamingBitrate` with no
  profile behind it is ignored, so a capped request sends a profile that can
  direct-play nothing (see the README).
- **SyncPlay, live TV, music.** Each is its own app inside the API; the music
  libraries are filtered out of the home screen rather than half-rendered.
- **Writing metadata.** Aperture reads the library and reports playback. Editing
  what a film *is* belongs in the server's own dashboard.
