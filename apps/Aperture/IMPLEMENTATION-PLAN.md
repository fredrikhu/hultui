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

## Slice 7 — requests, through Jellyseerr / Seerr (**slice 7a done**, 2026-09-30)

Researched first: Jellyseerr and Overseerr merged into **Seerr** (seerr-team/seerr, 3.x,
same `/api/v1` as Jellyseerr 2.x; Overseerr is archived and Plex-only). **No .NET client
exists on NuGet**, and the OpenAPI spec is missing the fields that matter here, so
`Aperture.Core/Requests` is hand-written: `SeerrHttp` (auth, errors), `SeerrServer`
(`IRequestServer`, mapping), `SeerrConnection` (address walk on 5055, probe, three sign-ins,
resume), `SeerrJson` (source-generated STJ), `FakeRequestServer`.

| 7a — done | |
|---|---|
| Connect | Settings card: Quick Connect bridge (no typing, Seerr 3.4+), Jellyfin password, or API key acting as your user; secret in the credential store, paired with the Jellyfin server |
| Discover | rail button; trending, popular and upcoming shelves with status badges; "Your requests" |
| Title page | TMDB hero, cast, "More like this", state line; Request (film), season picker (series), 4K where allowed; Approve/Decline for managers; the title's requests with Withdraw |
| Requests | rail button with a waiting count for managers; filters; everyone's / mine; approve, decline, withdraw on the row |
| Search | the request server answers beside the library: "Not in your library" |
| Jump | a title the library has opens the library's page (`jellyfinMediaId`) |

Traps found on the way, each now in a comment or a test:

1. **Status 6 is Blocklisted** (was "Blacklisted" before 3.0), 7 is Deleted. The spec says
   6 is Deleted — that is Overseerr's numbering.
2. **The request list has no "declined" filter**, and the validator answers any unknown query
   parameter or value with a 400 — including parameters a newer server added. Version-gate
   anything new (`RequestServerInfo.ParsedVersion`).
3. **`POST /request` can answer 202 with `{message}`** instead of a request (nothing left to
   ask for). Read as a failure with the server's sentence.
4. **`/auth/jellyfin` with a `hostname` is a 500** on a configured server — send only
   username and password.
5. **The poster cache sent the Jellyfin token to every host.** Now scoped to the Jellyfin
   origin (`PosterCache.Authorize`), with a test.
6. **Framework: `MaxWidth` on a stretched box clamped the box but not its children's
   layout** — a button at the far end of such a card was drawn outside it and could not be
   hit. Worked around in the picker at first; then it turned out the Settings page had it
   too, and **1.1.0 shipped with a Connect button that took no click**. Fixed in the layout
   engine (`ResolveAxis`: min and max bind before the content is laid out), which also
   fixed max-width text being measured unwrapped. Lesson: the headless tests clicked
   everything but the one button on the far side of a capped page.

**Verified against the lab server** (`http://jellyseer.home:5055` — one "r" — Seerr **3.3.0**
titled "Jellyseerr", key in `~/.secrets/seerr.key`), read-only: the six live tests pass,
the probe reads shelves, a series in full and the 27 requests with their titles, and the
views render real artwork. **All 22 `jellyfinMediaId`s on its requests resolve on
`jellyfin.home:8096`** (checked through the anonymous image endpoint), so the library jump
joins. 3.3.0 is below the Quick Connect bridge, so the Settings card asks for the Jellyfin
password there. **Not exercised live:** making, deciding and withdrawing a request — those
start real downloads, so they are covered by the wire tests only until someone asks for
something on purpose.

### 7b — done (two small things left, at the end)

**Done (2026-09-30):**

| | |
|---|---|
| Request from the library | a series' own Jellyfin page offers "Request Seasons 4–5" beside Play, and names seasons already asked for ("Season 3 on its way · 42%"); the join is the TMDB id Jellyfin scraped (`ItemFields.ProviderIds` → `MediaItem.TmdbId`) |
| Download progress | `mediaInfo.downloadStatus[]` — not in the spec, filled from Radarr/Sonarr's queue whenever the server loads a title, so request lists carry it too. A bar on the title page, re-read every 15 s while something downloads (and only then); "On its way · 68%" in the list |
| Quota | under the Request button ("3 of 5 film requests left this week"), which is disabled when spent; the season picker ticks no more seasons than the allowance and won't send more |
| Hide switches | `hideAvailable` and `hideBlocklisted` applied as the server's web client applies them (the latter only for blocklist managers); `hideRequested` is not applied by that client, so not here either |

Two things worth knowing: the server's `limit: 0` means *unlimited*, and a download's
`timeLeft` is the downloader's own .NET `TimeSpan` string (`1.02:03:04` past a day). The
library-page button is covered headless only — there is no Jellyfin key on the dev box to
drive it against the real pair.

**Done, second half (2026-09-30):**

| | |
|---|---|
| The administrator's layout | Discover follows `/settings/discover` (open to any signed-in user): its order, what is switched off, and custom rows by the name they were given (genre, keyword, search, studio, network) |
| Genres | "Films by genre" / "Series by genre" rows of backdrop cards (`/discover/genreslider/*`), each opening a genre page that pages with "Show more" |
| People | a name in a title's cast opens their page: picture, biography, and every film and series they are credited with, once each, newest first |
| Watchlist | a bookmark on the title page and a "Your watchlist" row; the list carries only ids, so titles and artwork come from the detail cache |
| Advanced requests | for `REQUEST_ADVANCED` (or managers): a "Request with options…" button beside Request, and the same chips in the season picker — server (when there is more than one), quality profile, root folder with the room left in it |
| Report a problem | from the player's title strip: picture / sound / subtitles / something else, this episode or the whole series, a description; filed against the request server's own record of the title |
| Arrival toast | "Dune: Part Two is in your library", with Open — polled every five minutes (the server has no push); the first look announces nothing, and what was announced is kept in the settings file |
| CSRF protection | the `_csrf` secret and `XSRF-TOKEN` are kept from any response and sent back on writes; a first write refused for CSRF fetches them and is sent again once |

Traps on the way:

1. **A new call swallowed a 401.** The layout read fell back to the default layout on any
   failure — including a session the server had ended, which the rest of the app treats
   as "close the request server". Caught by the existing session-ended test.
2. **Escape bubbles.** A handled Escape in the report panel still reached the player
   sheet's own handler, which stops the film. The panel stops propagation, and the sheet
   now ignores a handled Escape. Keys typed into the report go to the text box, not the
   player's shortcuts (a space in "out of sync" used to be Space: pause).
3. **Removing from the watchlist takes `mediaType` from 3.2** and a 3.1 server refuses the
   parameter outright, so it is sent by version. Adding answers 409 for "already on it",
   which is success.
4. **Left out, deliberately:** recently added (the library's Home has it, from Jellyfin),
   studios and networks (the server's web page draws them from logo lists compiled into
   it), streaming-service rows (their data is an undocumented string), and `hideRequested`
   (the server's web client does not apply it either).

Verified against the lab server, read-only: the layout (all twelve built-in rows), the
genre rows and a genre page, a person, the watchlist, and Radarr's profiles and folders
(`radarr.home`). Making a request, reporting a problem and the watchlist writes are covered
by the wire and headless tests only.

**Since:** the watchlist has a row on Home, after the "what now" rows — what the library
already has first, since that is what can be played — and moves, hides and comes back
like any other row. Home's rows can now hold discovery titles (`HomeRow.Titles`).

**Still open:**

- Studios and networks, if a logo source turns up; streaming-service rows once their data
  format is pinned down.
- Issues as a list — reading and commenting on reports, for someone who manages them.

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
