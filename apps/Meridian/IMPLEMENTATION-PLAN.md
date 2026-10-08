# Implementation Plan — Meridian (HTTP/API client, app #3)

> **Status: all twelve phases shipped** (P0–P11, milestones M1–M3). The
> sections below are the plan as written before the build; deviations taken
> along the way are recorded in "What changed" at the bottom.

Responds to `docs/design/meridian/` (Azimuth token sheet + surfaces 02–08). Design resolved
every open question from `docs/API-CLIENT-DESIGN-HANDOVER.md`; this plan is about *how*, not
*whether*. High-fidelity mocks — colors, spacing, states, and copy are final.

## Structural decision — three projects, not one

The pipeline pillar means the run engine must execute with no window. A WinExe can't write to
a CI log on Windows, so the CLI is its own binary:

- **`apps/Meridian/Meridian.Core/`** — class library, zero HultUI references. File model,
  variable resolution, HTTP engine, JSONPath subset, assertions/extractions/chains, runner,
  report emitters, discovery map. Everything here is testable without a session.
- **`apps/Meridian/Meridian/`** — the GUI (WinExe, `PublishAot`, Junction csproj shape).
  References Core + HultUI + HultUI.Syntax + HultUI.Icons + SourceGenerators-as-analyzer.
- **`apps/Meridian/Meridian.Cli/`** — console `Exe`, references Core only. `meridian run
  collection/ --env ci --report junit`. Tiny: argument parsing + Core + exit codes.

`tests/Meridian.Tests` covers all three (`InternalsVisibleTo` from each).

## Framework prerequisite (parallel track, in HultUI)

**F1 — Editable multi-line text.** The Body tab, `.env` values, and assertion value cells
assume real editing; `TextInputElement` is single-line. Build an editable mode on the
TextArea stack (it already has metrics, wrap, selection, scroll — the Vesper reader and both
diffs share it): caret + edit ops + IME via the existing `ITextInputHost`, no undo tree in
v1 (single-level undo is enough for a request body). This is the long pole — start it first,
in parallel with P0–P2, which don't need it. Until it lands, the Body tab can ship read-only
with a "replace from clipboard" action, so no phase hard-blocks.

## Approved design decisions (from the handoff README)

Azimuth register verbatim from mock 01 (`az-*` tokens, method + status registers, infra =
warn never fail, glyphs always beside color). Junction idiom: 48dip rail, views on 1–5
(Requests / Runner / Discovery / History / Environments). Response right 560dip default,
Ctrl+\ flips to 380dip bottom, auto-bottom < 1320dip (was < 1100dip; the user approved the
move on 2026-10-08: at 1100 the editor beside the pane was 228dip, the URL bar's method chip
and Send with almost no URL between them; 1320 = rail 48 + tree 264 + editor 448 + pane 560,
and 448 holds an ordinary request's six sub-tabs unscrolled and ~227dip of URL beside GET).
The bottom pane is 380dip where the window has room; under 736dip tall it gives way so the
editor keeps 316dip (chrome + five form rows), down to its own 162dip floor (status line,
tabs, six lines of body); both floors fit the 760×540 minimum window (2026-10-09: with the
breakpoint at 1320 a fixed 380 left a 1280×672 window — 1080p at 150 %, maximized — 112dip
of sub-editor). No splitter: the size is the design's, and a dragged one is UI state the
design says must persist, which Meridian has no settings file for yet.
Key map as mocked (Ctrl+Enter send, Esc cancel, Ctrl+E env, Ctrl+P palette, Ctrl+F find,
Ctrl+S save, Ctrl+T/W tabs, Ctrl+R run). **Fonts:** follow the `Fonts.cs` precedent — platform sans + Consolas/Menlo; bundling
Inter/JetBrains Mono is a separate later decision, not a blocker.

## The file format (engineering-owned, defined in P1)

`.req` files: line-oriented plain text, section-tagged (method/url line, `header`, `body`
fenced block, `auth`, `assert`, `extract` lines) — diff-friendly, no JSON envelope.
`_folder.req` for folder-level defaults (auth inheritance). `envs/*.env`: `key=value` +
`secret key` flag lines, values for secrets live in the OS keyring only. `.meridian/` for
derived state (history log, discovery maps, run logs) — gitignored by `meridian new`.
Format details are decided inside P1 with round-trip tests, not in this plan.

---

## P0 — Scaffold + Azimuth register
Solution entries, the three projects, `Theme/Azimuth.cs` transcribed from mock 01,
`Swatch`-style helpers, Program.cs + custom chrome + titlebar wordmark (12px muted, no
rules), empty 48dip rail with the five views switching on 1–5, first-run front door
(mock 02 frame 3) with all four entry cards inert except "Open a folder".
`tests/Meridian.Tests` hosts the app headless (JunctionAppHostTests shape): every view
builds without throwing, rail buttons carry accessible names. Phosphor icons for the rail
via the existing generator.

## P1 — File model (Core)
`.req`/`.env`/`_folder.req` parse + write with byte-for-byte round-trip; collection scan
into a tree model; dirty tracking; `FileSystemWatcher` → external-change event (the GUI
bar comes in P3); `{{var}}` tokenizer + resolution order (env → extracted → built-ins like
`{{$uuid}}`). Unit tests: round-trip on every construct, resolution precedence, malformed
lines surface as line-numbered errors (never throw).

## P2 — HTTP engine (Core)
Send with `SocketsHttpHandler`: per-phase timings (DNS/TCP/TLS via `ConnectCallback`
instrumentation, TTFB/download from the response stream), cancellation, redirect + cookie
jar (session-only), body capture with the 1 MB viewer cap + full spill to temp file,
binary/content-type classification, the four infra failures as typed results (DNS /
refused / TLS / timeout — each carrying the mock's copy: host, IP:port, cert CN, elapsed).
Tests against an in-process Kestrel-free `HttpListener` fixture: timings present, cap
enforced, each infra failure classified correctly (wrong-port, bad-host, stalled-response
cases).

## P3 — Shell: tree · editor · response (GUI, two commits)
**P3a editor side:** collection tree (VirtualList, 24dip rows, dirty dots, method chips at
8px), request tabs (Ctrl+T/W, dirty dot), URL bar with var chips (resolved/unresolved/
secret treatments), Params/Headers sub-editors (row grids), Send/Cancel state machine,
editor footer (path · saved/● unsaved · Ctrl+S), external-change bar (reload / keep mine /
diff — diff reuses the shared side-by-side view).
**P3b response side:** status line (glyph + semantic color + duration + size), Body tab
(HultUI.Syntax JSON/XML, shared TextSearch bar verbatim, pretty/raw), Headers/Cookies
tables, Timeline mini-waterfall from P2 timings, pane flip (Ctrl+\ + <1320dip breakpoint,
1100 until 2026-10-08 — Junction's compact-breakpoint idiom), and all eight response states from mock 02 (never
sent, in-flight with greyed previous, huge body, binary hex stub, DNS, refused, TLS with
scoped trust action, timeout).
Headless tests: state renders via accessibility tree; RenderLog asserts the find-bar tint
actually draws (the FilesFindTests lesson).

## P4 — Environments (GUI + Core keyring)
Compare table as the editor (envs as columns, missing = warn + add action), switcher
popover on Ctrl+E re-resolving chips in view, "no environment" legal, unresolved-chip
popover with add/switch actions. **Keyring:** Windows Credential Manager P/Invoke
(CredRead/CredWrite — AOT-safe, Vesper's Elevation P/Invoke is the precedent), libsecret
on Linux; `secret` flag in the file, value never on disk; timed 5 s reveal;
missing-on-this-machine state wired to send and runner pre-flight.

## P5 — JSONPath + assertions + extractions + chain (Core then GUI)
Core first: a JSONPath subset evaluator (`$.a.b`, `[n]`, `[*]`, filters deferred) over
`System.Text.Json`, assertion operators (status/header/body/time × equals/contains/under),
extraction into the variable store, chain resolution (uses/produces graph → run order,
cycle = line-numbered error). Then the GUI: assertion rows with per-keystroke live preview
against the stored last response, focused row's path highlighting its target span in the
response pane (span merge — mind the sorted-spans contract), extraction rows with
"✓ selects…" / "matches nothing — last matched n runs ago", Chain tab as a list
(uses/produces/freshness/run order), tree badges (→token, ⛓n), empty-state one-click
starters, stale-preview banner. Evaluator gets exhaustive unit tests; preview wiring gets
headless coverage.

## P6 — Runner (Core engine + GUI view)
Core: run executor honoring chain order + stop-on-failure, per-request result stream
(callback/`IAsyncEnumerable`), text + JUnit report emitters, append-only
`runs/*.run.log`. GUI: pre-flight rail (scope/options lock during run, Run↔Abort),
28dip streaming VirtualList rows, expanded failure with expected/actual inset, unreachable
= warn + downstream − skip, summary card **worded identically to the text report** (one
test asserts the correspondence literally: same order, same glyphs, same words), aborted
partial results with resume-from-#n, zero-scope disabled Run.

## P7 — CLI + CI story
`Meridian.Cli`: `meridian run <path> --env <name> [--report junit:<file>] [--bail]`,
exit 0/1 on green/red (infra-only failures exit 1 too but are distinguishable in the
report), `--list` for scope preview. Reuses P6's engine + emitters wholesale. Tests run
the published binary against the P2 fixture server. CI docs snippet in the README.

## P8 — Discovery (Core map + GUI view)
Core: OpenAPI **JSON** parse (paths/methods/params/schemas/deprecated) over
`System.Text.Json` — YAML specs deferred (see risks); well-known-path probe list; observed-
traffic recorder (endpoint template inference from concrete sends); map cache
`.meridian/discovery/*.map`. GUI: sources panel, endpoint map with solid vs dashed-hollow
confidence chips, detail pane merging spec + traffic, shape inference labeled as inference,
promote flow ("Write N files" writing real `.req` files via P1), parse error with
file:line:col + jump, auto-found toast, empty map.

## P9 — History
Core: append-only `.meridian/history.log` (capped 10k, oldest drop). GUI: 28dip VirtualList,
TextSearch verbatim, method/status/env filters, older-than-today at 60% + relative day,
read-only archive pane, Restore = new unsaved tab pointing at the original file.
Light phase — schedule early if dogfooding wants it sooner; nothing depends on it.

## P10 — Auth
Folder inheritance via `_folder.req` (inheritance bar, "editing edits the folder file" in
the footer), type segmented control (None/Basic/Bearer/API key/OAuth2), OAuth2
client-credentials in Core (token endpoint POST, cache in keyring with expiry), token chip
trio (valid / expiring auto-refresh-on-send / expired) riding the Auth tab and the Send
button corner, cookie jar UI per domain (session-only, never serialized).
**Auth-code grant (browser + loopback listener) is a stretch goal** — land
client-credentials first; the type dropdown ships without auth-code rather than with a
stub (the phantom-shortcut lesson).

## P11 — Palette, polish, release
Command palette (Ctrl+P — port the Vesper/Junction palette), options surface (only what
exists: default env, timeout, response cap), icon + wordmark assets, a11y pass
(DumpAccessibilityTree review per view), `meridian.iss` + `meridian-installer.yml` cloned
from Junction's (tags `meridian-v*`; **note:** the workflows build the whole solution, so
Meridian tests will gate Vesper/Junction releases too — same as today's arrangement),
README, `docs/` entry.

---

## Sequencing & milestones

F1 starts immediately, parallel to P0–P2 (Core phases don't touch text editing).
Order: P0 → P1 → P2 → P3 → P4 → P5 → P6 → P7 → P8 → P9 → P10 → P11; P9 may float earlier.
One commit per phase (P3 two), full suite green at each — the established rhythm.

- **M1 — usable client** (P0–P4): open a collection, edit, send, inspect, environments.
- **M2 — the differentiator** (P5–P7): chains, assertions, runner, CI binary.
- **M3 — full brief** (P8–P11): discovery, history, auth, shipped installer.

## Verification bar

Every phase lands with tests at the layer the /testing skill prescribes; every new
assertion is run against deliberately broken code before it counts; UI states assert via
the accessibility tree, paint-level claims via RenderLog. Core stays HultUI-free so M2 is
provable headless in CI on Linux.

## Risks / deferred

- **YAML OpenAPI specs** (common in the wild): YamlDotNet is reflection-heavy for AOT.
  Deferred from P8 — options are a vendored minimal YAML reader or trimming-safe config,
  decided when P8 starts. JSON specs + live probing cover v1.
- **OAuth2 auth-code** — stretch (P10). **Iterations/perf runner** — out (slot mocked
  disabled). **WebSocket/SSE/gRPC** — out, URL bar reserves the space. **Font bundling** —
  separate decision. **Import from Postman/Bruno** — not in the brief; note for later.
- **Multi-line editor scope creep** — F1 is caret/edit/IME only; no undo tree, no
  soft-wrap changes, no syntax-aware editing. The Body tab renders highlights read-only
  style while typing plain (highlight-while-edit is a later framework feature).


---

## What changed

The plan held. The deviations worth recording:

- **The multi-line editor (F1) never became a blocker.** TextArea's editing was
  good enough for the body, `.env` values and assertion rows, so the parallel
  framework track was not needed. Highlight-while-editing is still absent —
  the response viewer highlights, the body editor does not.
- **P3 split as planned into P3a/P3b**, and the runner (P6) and CLI (P7) landed
  in the planned order. The chain-order engine ended up shared: the app's
  Runner view and the CLI call the same `CollectionRunner`, and both print
  `RunReports`, asserted line-for-line by a test.
- **JSONPath highlighting in the response pane is deferred.** The focused
  assertion row does not tint its target span; it needs a JSON-text locator
  with byte offsets. Everything else in P5 shipped.
- **OAuth2 auth-code stayed out** (as the plan allowed) — client-credentials
  only, with no stub UI for the missing grant.
- **YAML OpenAPI stayed out** (as the plan allowed). JSON specs plus live
  probing cover discovery; a YAML reader is the one obvious P12 candidate.
- **The Linux desktop keyring is unimplemented.** Secrets resolve from the
  Windows Credential Manager, then from a process environment variable — which
  is what CI uses, so the pipeline story is unaffected on Linux.
- **Three bugs found in neighbouring code** while building: a toast that
  swallowed clicks near the window's bottom edge (Junction and Vesper share the
  idiom), the CLI/GUI assembly-name collision on Windows, and the OEM codepage
  mangling report glyphs in redirected logs.
