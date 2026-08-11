# HTTP/API Client — Design Handover (New App)

**For:** Claude Design
**From:** implementation
**Date:** 2026-08-11
**Scope:** Full visual/UX design for a new HultUI app — app #3 alongside Vesper Commander and Junction — *before* implementation starts.

---

## 0. What this is

A native, keyboard-first HTTP/API client for developers. The market is Electron with an account wall (Postman, Insomnia, Bruno); we ship a fast native binary with **everything stored as plain text files on disk** — git-diffable, reviewable, no cloud, no login.

Three pillars, all designed up front:

1. **UI client** — compose requests, send them, inspect responses.
2. **Pipeline runner** — the same collections run headless in CI (`app run collection/ --env ci`). The UI is an *editor over files*; anything you can do in the UI must serialize to the text format, so no UI-only state may exist. Design consequence: every surface must make the file-backed nature legible (what file am I editing, is it dirty, what does a run report look like).
3. **Discovery mode** — point it at an API you don't know: import an OpenAPI/Swagger spec into a browsable endpoint tree, or probe a live base URL and build up a map from real traffic.

**Deliverable from Design:** static HTML/CSS mockups in the same handoff idiom used for Vesper (Nocturne-style `styles.css` token sheet + one page per surface), showing every listed state, annotated with spacing, color roles by token name, and key bindings. Plus a **new design system** for this app (see §1).

| # | Surface | Design load | Section |
|---|---------|-------------|---------|
| 1 | Design system / visual identity | **Heavy** | §1 |
| 2 | Main shell: collection tree + request editor + response viewer | **Heavy** | §A |
| 3 | Environments & variables | **Medium** | §B |
| 4 | Collection runner (UI-side of the pipeline story) | **Heavy** | §C |
| 5 | Chaining & assertions ("programmable" surfaces) | **Heavy** | §D |
| 6 | Discovery mode | **Heavy** | §E |
| 7 | History | **Light** | §F |
| 8 | Auth & cookies | **Medium** | §G |

---

## 1. Design system — new identity, same skeleton

Vesper has **Nocturne**, Junction has **Swatch**. This app gets its own theme: dark-first, a single accent with a ramp, neutral surface ramp, token-named colors, radii, two densities where lists appear. Study `apps/VesperCommander/Theme/Nocturne.cs` and `apps/Junction/Theme/` for the shape of a theme register — same structure, different personality. Propose the identity (name it); it should read as a sibling, not a clone.

Non-negotiable inherited conventions:

- **Mono for anything tabular/technical**: URLs, headers, status codes, byte counts, durations, JSON.
- **Status colors must survive CI-grep semantics**: pass/fail/skip need a green/red/neutral trio that also works as plain text glyphs (✓ ✗ −) — color is never the only signal (accessibility bridge is live UIA/AT-SPI).
- **HTTP method colors** — the one place a broader palette is justified. GET/POST/PUT/PATCH/DELETE each get a token; these appear as small chips everywhere (tree, tabs, history, runner) so they must be legible at ~10px caps.

## 2. Framework components available — compose from these

- **VirtualList** — proven at millions of rows; use for history, runner output, header tables.
- **TextArea** — read-only viewer with selection/copy, line numbers, horizontal scroll or wrap toggle, char-level wrapping; plus an editable TextInput (single-line; multi-line editing is engineering work in progress — design as if a real editor exists).
- **Syntax highlighting** (`HultUI.Syntax`) — JSON, XML, YAML, and ~40 more; response bodies come pre-colored.
- **TextSearch** — the shared find-in-text bar (match count, prev/next, regex toggle) used by four existing search surfaces; reuse verbatim in response viewer and history.
- **Diff view** — side-by-side text diff with intra-line highlights exists in both apps; reuse for "compare two responses" and "response changed since last run".
- **Grid layout, tabs, split panes, command palette, context menus, Backdrop/Card modal pattern, progress overlays, segmented controls, toasts/flash** — all built, all themable.

---

## A. Main shell — *heavy*

Three-region layout (proposal — challenge it if you have better):

1. **Left: collection tree.** A *directory of text files* rendered as a tree: workspace → folders → requests. Method chip + name per request. Dirty-file dot. Multiple collections open at once. Also hosts top-level nav to Runner / Discovery / History / Environments.
2. **Center: request editor.** URL bar (method dropdown + editable URL with inline `{{variable}}` chips — design how a resolved vs unresolved variable looks), tabbed sub-editors: Params / Headers / Body / Auth / Assertions / Chain. Send button with keyboard hint (Ctrl+Enter).
3. **Right or bottom: response viewer.** Status line (code with semantic color, duration, size), tabs: Body (syntax-highlighted, find bar, pretty/raw toggle) / Headers / Cookies / Timeline (DNS/TCP/TLS/TTFB waterfall — design the mini-waterfall).

**Tabs across the top** for multiple open requests (Vesper's locked-tab idiom exists if useful).

**States:** no collection open (first-run — this is the front door, make it earn attention: open folder / new collection / import OpenAPI / discover); request never sent; in-flight (cancellable); response ok; HTTP error (4xx/5xx are *successful* renders, not error states); network failure (DNS, refused, TLS, timeout — each distinguishable); huge body (truncated view + "open full"); binary body (hex/preview stub); unsaved changes.

**Open questions:** response right vs bottom (or user-flippable)? Are collection tree and nav rail one region or two?

## B. Environments & variables — *medium*

Environments are files (`envs/local.env`, `envs/ci.env` — format is engineering's call; assume key/value with a secret flag). Design:

1. **Environment switcher** — always visible in the shell (top-right?); switching re-resolves every `{{var}}` chip in view.
2. **Editor** — table of key/value/secret per environment, side-by-side compare across environments (which keys are missing in `ci`?).
3. **Secret treatment** — masked in UI, *never written to disk*; design the "this value lives only in your OS keyring / CI injects it" affordance and the unresolved-secret error state.
4. **Inline resolution preview** — hovering/focusing a `{{var}}` chip shows the resolved value (masked if secret).

**States:** no environment selected, variable unresolved (chip goes warning-colored), secret missing in current env.

## C. Collection runner — *heavy*

The UI face of the pipeline pillar. Pick a folder/collection, an environment, run it: every request executes in order (respecting chains, §D), assertions evaluate, results stream in.

Design:

1. **Pre-flight** — scope (whole collection / folder / tagged subset), environment, iterations/delay, stop-on-failure toggle.
2. **Live run view** — streaming list (VirtualList): per request a row with method chip, name, status, duration, assertion count ✓/✗; expandable to per-assertion detail with expected/actual diff. Progress + running totals.
3. **Summary** — totals, wall time, failures surfaced first, "re-run failures only".
4. **The CI connection** — the same run emits a text/JUnit report on the CLI; the UI summary should visually correspond to that report so a developer can read a CI log and picture this screen. Show a small "what this looks like in CI" annotation in the mock (engineering will match it).

**States:** running, all-pass, failures, aborted mid-run, request unreachable vs assertion-failed (different colors — one is infra, one is a real failure), zero-request selection.

## D. Programmability: chaining & assertions — *heavy*

No embedded scripting language in v1 (Native AOT makes arbitrary scripting hostile; engineering constraint). Instead **declarative, file-serializable** primitives — design surfaces that make these feel powerful, not like a lesser Postman:

1. **Extractions** — after a response, pull values by JSONPath/header/regex into named variables (`token = $.auth.access_token`). Design the editor row (source → path → variable name) and a live preview against the last response ("this path currently selects: `eyJhbG…`").
2. **Assertions** — status equals, header matches, body JSONPath comparison, response-time under N ms. Same row idiom; live pass/fail preview against the last response.
3. **Chain view** — a request that consumes `{{token}}` produced by another has a dependency; design a lightweight visualization (in the tree? per-request "uses: token ← Login" line?) so run order is legible. Full graph canvas **not** wanted in v1 — keep it inline and cheap.

**States:** extraction path matches nothing (warning + which run it last matched), assertion editor with 0 rows (nudge, not nag), preview stale (response older than the request definition).

## E. Discovery mode — *heavy*

Two entry paths, one destination (a browsable API map that can be promoted into a collection):

1. **Spec import** — open an OpenAPI/Swagger file or URL → endpoint tree grouped by tag/path, per-endpoint detail (params, schemas, examples), one-click "generate request into my collection" (single or bulk). Design the tree, the detail pane, and the promote flow.
2. **Live probing** — give a base URL; the app tries well-known spec locations (`/openapi.json`, `/swagger.json`, `/.well-known/…`), and failing that, every request you send by hand gets recorded into the map (observed endpoints, seen status codes, inferred shapes). The map grows as you explore. Design how *observed* (inferred, uncertain) endpoints look vs *spec-declared* ones — two confidence levels, visually distinct.

**States:** spec parse error (line/position), spec found automatically (delight moment), nothing discovered yet (map empty, instructions), endpoint with no example schema, deprecated endpoint (spec flag).

**Open question:** is Discovery a separate full view (Junction-style view switch) or a mode of the collection tree? Recommend separate view with a promote-to-collection bridge; challenge if you disagree.

## F. History — *light*

Every sent request is appended to a local history log (file-backed, capped). Design: a VirtualList of past sends (time, method chip, URL, status, duration), filter/search bar (reuse TextSearch), click to view the archived request+response read-only, "restore to editor". States: empty, filtered-to-nothing.

## G. Auth & cookies — *medium*

Per-request or inherited-from-folder auth: None / Basic / Bearer / API-key / OAuth2 (client-credentials + auth-code). Design the auth tab per type, the inheritance indicator ("inherited from *Payments/*, override?"), and the OAuth token status chip (valid / expires in 4m / expired — refresh affordance). Cookie jar viewer per domain, clearable. Secrets follow §B rules — masked, keyring-backed, never on disk.

---

## 3. Cross-cutting requirements

- **Dark-first**, all colors as named tokens in the new register; light variant explicitly out of scope for v1.
- **Keyboard-first.** Propose the map: Ctrl+Enter send, Ctrl+E environment switch, Ctrl+P palette (both existing apps have one), 1–5 view switch (Junction precedent), Ctrl+F find-in-response. Every mock shows its keys.
- **Every surface:** idle/empty, in-progress, success, no-result, error. Omissions ship as blank screens.
- **Accessibility:** live UIA/AT-SPI bridge; every control labeled; no color-only status anywhere (method chips carry text, pass/fail carries glyphs).
- **File-backed honesty:** surfaces never hide that the source of truth is a folder of text files — show paths (mono), dirty markers, and external-change handling (file changed on disk under an open editor: design the reload/keep-mine bar).

## 4. Explicitly out of scope for Design (engineering-only)

- The on-disk file format itself (we'll spec it; likely a Bruno-adjacent plain-text format), CLI argument design, JUnit/report emitters, the HTTP engine, OAuth flows' network legs, keyring integration, OpenAPI parsing.
- Multi-line text *editing* internals — design assumes a capable editor; making TextInput multi-line is a parallel framework track.

## 5. Open questions to resolve with the requester

1. App name (needed for the theme name too).
2. Response pane: right, bottom, or flippable?
3. Discovery: separate view vs tree mode (recommendation in §E).
4. Runner iterations/perf-mode (N runs, timing distribution) in v1 or later? Design assumes later; the pre-flight leaves room.
5. WebSocket/SSE/gRPC — out of v1 entirely, or stub tabs? Design assumes out.
