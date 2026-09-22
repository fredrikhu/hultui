# Aperture

A native Jellyfin client: your server's libraries as shelves of artwork, and the
framework's own video player behind the Play button. No browser, no Electron —
the same HultUI shell the other three apps are built from, with the picture
decoded by libmpv.

Two projects:

| Project | What it is |
|---|---|
| `Aperture.Core` | The server side: the connection, the session, and one `IMediaLibrary` the views talk to. Built on the official **Jellyfin.Sdk** (Kiota-generated), with Aperture's own vocabulary over it — no SDK type reaches a view. No UI reference. |
| `Aperture` | The app (HultUI + HultUI.Media): Home, a library grid, an item page, search, settings, and the player sheet. |

## The shape

```
Connect  ──►  Home ──► Library grid ──► Item page ──► Player (a sheet, not a view)
                │                          │
                └── Search, Settings ──────┘
```

- **Home** is Continue watching, Next up, then what each library has added. Rows
  page sideways with two buttons rather than scrolling — a horizontal scroller
  nested in the page's vertical one is the framework's wheel-capture trap.
  That order is a default: the menu in each row's heading moves it, hides it, or
  folds the two “what now” rows into one “Up next”. The choices are keyed by row
  in the settings file, and a hidden row is listed at the foot of the page with a
  button that brings it back.
- **The grid** is rows of posters through a `VirtualListElement`, so a library of
  thousands realizes only the rows on screen and only those ask for artwork.
  Paging follows the viewport: the row that scrolls in asks for the page that
  would hold it.
- **The player** is a sheet over whatever you were looking at, so stopping a film
  puts you back where you started it with the navigation stack intact.

## Signing in

**Signing in** takes a password or a code. Quick Connect asks the server for a
six-character code, shows it, and polls on a timer until someone approves it on a
client that is already signed in — nothing secret is typed into this window, which
is what makes it the right door on a shared machine or a television.

The address is what people actually type. `jellyfin.home` is tried on port 80,
then **8096** — Jellyfin's own port, and the reason a bare hostname otherwise
answers "connection refused" on a stock install — then the same two over HTTPS.

What is kept, and where:

| | Where |
|---|---|
| Server address, id, user name, device id | `%APPDATA%\Aperture\settings.json` (XDG on Linux) |
| Access token | the OS credential store (Windows Credential Manager) |
| Password | nowhere — it is used for one request and dropped |

A machine with no credential store signs in each launch, and says so rather than
pretending it saved.

## Playback

Pressing Play asks the server how the item can be played (`/Items/{id}/PlaybackInfo`),
which is where a server decides to transcode, and hands the resulting URL to
`MediaPlayer`. Direct play streams the original file; a transcode is labelled as
such in the title strip, because the two feel identical until something stutters
and then it is the first question.

Settings → Playback → Quality caps the stream. **A bitrate on its own is
advisory**: with no device profile to evaluate, this server hands back the file and
the ceiling means nothing (measured against 10.10). So a capped request also sends a
profile that can direct-play nothing and transcodes to H.264/AAC over HLS — which is
what turns "keep it under this" into real re-encoding. Changing the ceiling re-opens
what is playing at the position it had reached.

Playback is reported back the way Jellyfin's own clients report it — started,
progress every ten seconds and on every pause, stopped — which is what drives
"now playing" in the dashboard, the Continue watching row, and the resume point
on every other device. Resume seeks client-side once the backend has loaded: the
stream URL always starts at the first byte.

## Running it

```bash
dotnet run --project apps/Aperture/Aperture
```

Without a libmpv the app still runs — browsing, artwork, everything but the
picture — and the Settings page says the decoder is missing. See
`src/HultUI.Media.Mpv/README.md` for where it looks.

To look at it without a window, the screenshot driver mounts the same tree:

```bash
# a fixture library, no server needed
dotnet run .claude/skills/run-hultui-app/driver.cs -- --app aperture --out shots

# a real server, signed in with an API key from Dashboard → API Keys
dotnet run .claude/skills/run-hultui-app/driver.cs -- --app aperture \
  --server http://jellyfin.home:8096 --api-key-file ~/jf.key --out shots settle:2000 shot:home
```

## Tests

```bash
dotnet test tests/Aperture.Tests
```

The live tests skip themselves unless `APERTURE_LIVE_SERVER` and
`APERTURE_API_KEY` (or `APERTURE_API_KEY_FILE`) are set — they exist because the
part of a client that breaks is never the part a fake can model. One of them
caught the thing this whole app turned on: **Jellyfin writes ids without dashes**,
which the SDK's JSON reader refuses. `Aperture.Core/JellyfinSerialization.cs` is
the fix, and it is why the client works at all.

## The look

**Halide** (`Theme/Halide.cs`) — near-black ground, warm off-white text, one
projector-amber accent. Two rules: the artwork is the brightest thing on screen,
and the accent is a mark rather than a surface. Sibling to Nocturne (Vesper), the
Junction register and Azimuth (Meridian).
