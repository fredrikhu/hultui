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

## Slice 2 — the next obvious things

- **Season and episode pages beyond one level.** A season page lists episodes;
  a series with many seasons pages them. Next-up is wired, seasons are not deep.
- **Resume across devices mid-playback.** The heartbeat is there; what is missing
  is noticing that another client moved the position while this one was paused.
- **Audio and subtitle tracks.** `MediaPlayer` exposes both track lists and the
  selection; the server's `PlaybackInfo` names the defaults. The overlay has the
  CC button already — it needs the menu behind it.
- **Keyboard through the grid.** Arrow keys move a cursor between posters, Enter
  opens, Space plays. Today the grid is pointer-driven; the rail and the player
  have their keys.
- **Search that searches as you type**, debounced, over `SearchTerm` — currently
  every keystroke past two letters starts a query.

## Slice 3 — what would make it a daily driver

- **Native AOT publish.** `IsAotCompatible` is on and ILC compiles the Kiota SDK
  without a trim warning, but nothing has run as a native binary; `PublishAot`
  stays off until it has.
- **Multiple servers.** The settings file already holds a list and the credential
  store is keyed by server id; the UI assumes one.
- **Offline shelf.** Artwork is cached in memory only, so a server that goes away
  takes the posters with it. A disk cache under the config directory would make
  the app open instantly and survive a reboot.
- **Downloads.** `/Items/{id}/Download` is one call; the work is the library of
  what has been downloaded and playing from it when the server is unreachable.
- **Quick Connect.** `client.QuickConnect` is in the SDK: sign in by typing a code
  on the server's web UI rather than a password into this window.

## Things deliberately not done

- **A device profile.** Aperture tells the server it can direct-play and lets it
  decide, because mpv plays what the server would otherwise spend CPU converting.
  A profile would be needed for a client that could not.
- **SyncPlay, live TV, music.** Each is its own app inside the API; the music
  libraries are filtered out of the home screen rather than half-rendered.
- **Writing metadata.** Aperture reads the library and reports playback. Editing
  what a film *is* belongs in the server's own dashboard.
