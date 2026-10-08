# Handoff: Meridian — HTTP/API Client (full app design)

## Overview
Complete visual/UX design for **Meridian**, the third HultUI app (alongside Vesper Commander and Junction): a native, keyboard-first HTTP/API client where everything is plain text files on disk — no cloud, no login. Covers the new **Azimuth** design system plus all eight surfaces from the design brief (§1, §A–§G): main shell, environments, collection runner, chaining & assertions, discovery mode, history, auth & cookies.

## About the Design Files
The `.dc.html` files in this bundle are **design references created in HTML** — static annotated mockups showing intended look, states, and behavior. They are NOT production code. The task is to **recreate these designs in the HultUI framework** (C# theme register + existing components: VirtualList, TextArea, HultUI.Syntax, TextSearch, diff view, tabs, split panes, command palette, segmented controls, toasts), following the same structure as `apps/VesperCommander/Theme/Nocturne.cs` and `apps/Junction/Theme/`. Open each file in a browser; every page carries margin notes, numbered callout legends, token names, dip values, and key bindings.

## Fidelity
**High-fidelity.** Colors, spacing, type sizes and states are final and specified by token name. Recreate pixel-perfectly with the values below. All frames are drawn at 1920×1080 dip; state insets at 1:1.

## Files (implementation order)
- `00 Index.dc.html` — handoff README as a page: page map, full key map, decisions record
- `01 Azimuth Tokens.dc.html` — **the theme register spec** (build `Theme/Azimuth.cs` from this)
- `02 Shell.dc.html` — main shell, both response-pane orientations, first-run, 9 response/editor states
- `03 Environments.dc.html` — compare table, switcher, secret/keyring states
- `04 Runner.dc.html` — pre-flight, live run, summary, CI report correspondence
- `05 Chaining.dc.html` — assertion rows w/ live preview, extractions, chain view
- `06 Discovery.dc.html` — spec-declared vs observed endpoint map, promote flow
- `07 History.dc.html` — VirtualList log + read-only archive + restore
- `08 Auth.dc.html` — auth types, inheritance bar, token chips, cookie jar
- `candidates/` — the two rejected identity directions (Isobar, Antipode), for the record
- `support.js` — viewer runtime for the .dc.html files; ignore for implementation

## Design Tokens — Azimuth (`az-*`)
Surfaces & text:
- `az-bg` #0C1118 · `az-surface` #121A24 (rail, titlebar, inputs) · `az-raised` #18222F (selection, hover)
- `az-border` #223041 · `az-border-strong` #33455C (pane-flip divider) · `az-text-faint` #586878
- `az-text-muted` #8A99AC · `az-text-dim` #C6D0DC · `az-text` #E8EEF5

Accent ramp: `az-accent-100` #D9F6FA (text on 700/900) · `-300` #86DFEB (links, var chips) · `-500` #38C5D8 (the accent) · `-700` #1E7E8D (pressed, find-match fill) · `-900` #0E3B44 (tint fills).
Accent = focus, selection, activity, send, unsaved marks. Never floods a region: max a 2dip edge, a 16% tint, or a glyph.

Method register (chips = JetBrains Mono 600, 10px caps / 8px at tree scale, radius 3, tint = color @16% alpha, text always present):
- `az-method-get` #4EA1F2 · `-post` #4FBE7E · `-put` #E8763F · `-patch` #A78BE0 · `-delete` #E5636B ("DEL" at tree scale)

Status register (glyph always accompanies color — ✓ ✗ ! −):
- `az-status-pass` #4FBE7E (2xx, assertion pass) · `az-status-fail` #E5636B (5xx, assertion fail)
- `az-status-warn` #E8C14A (4xx, unresolved var, ALL infra failures) · `az-status-skip` #8A99AC
- Rule: infra failures (DNS/refused/TLS/timeout/unreachable) are warn, never fail.

Type: Inter (UI) — 15/600 pane titles, 13/500 labels+tabs, 12/400 body+tree. JetBrains Mono — 13 URL bar, 12 JSON/paths/tables, 10 caps chips/section labels/key hints. Mono for anything greppable.

Spacing/geometry: scale 4 8 12 16 24 (`az-s-1…5`) · radius `az-r-1` 3dip, `az-r-2` 6dip · rows 24dip (tree/lists), 28dip (runner/history rows), 32dip (forms) · rail 48dip · tree 264dip · response pane 560dip right / 380dip bottom.

States (never browser defaults): hover = az-raised or 12% accent tint; pressed = az-accent-700 fill + -100 text; focus = 2px az-accent-500 outline, offset 2; disabled = 45% opacity. Selection ::selection = accent-700/accent-100. Primary button = accent outline, no fill.

Letterform: "MERIDIAN" Inter 500, tracking 0.35em, flanking 1px rules fading outward from the wordmark (accent at near end). Titlebar: 12px, az-text-muted, no rules. The wordmark never takes the accent color.

## Screens / Views
Each page documents itself in detail; summary of the load-bearing decisions:

**Shell (02).** Junction idiom: 48dip view rail, keys 1–5 (Requests/Runner/Discovery/History/Environments); collection tree only inside Requests. Response pane right 560dip by default, Ctrl+\ flips to bottom (380dip), auto-bottom < 1320dip (1100dip in the mocks; moved with the user's approval on 2026-10-08 so the editor beside the pane keeps 448dip — at 1100 it had 228, almost no URL beside Send). Editor footer always shows file path (mono) + saved/● unsaved + Ctrl+S — the file-backed honesty line. `{{var}}` chips: resolved = accent-300 on 12% tint; unresolved = warn + dashed underline; secret = resolved-but-masked + "keyring". 4xx/5xx render as full responses (status word in semantic color), never as error screens. External file change = bar over editor (reload / keep mine / diff), never a modal. First-run: four entry cards (open folder ⌃O / new collection ⌃N / import OpenAPI ⌃I / discover), Discover carries the accent border.

**Environments (03).** Compare table IS the editor: envs as columns, missing cells = warn tint + "add to ci". Secrets: flag in the .env file, value in OS keyring only; masked previews, timed reveal (5 s). Switcher popover on ⌃E; "no environment" is legal.

**Runner (04).** Left rail = pre-flight (scope / options / run), locks during run, Run↔Abort(Esc). Rows 28dip in VirtualList: glyph, method chip, name, status code, duration, n/m ✓. Expanded failure: per-assertion glyphs + expected/actual box + "open response". Unreachable = warn glyph + "unreachable" in the code slot; downstream assertions read − skip. Summary card mirrors the CLI/JUnit text report word for word (see the correspondence inset). "Re-run failures only" is the primary action after a red run.

**Chaining (05).** Row idiom source ▾ / operator ▾ / value / live preview (re-evaluates per keystroke vs stored last response; focused row's JSONPath highlights its target in the response pane). Extractions: source/path/→variable/"✓ selects …" preview; dead paths go warn + "last matched n runs ago". Chain tab is a list (uses/produces + provenance + freshness + derived run order), never a graph canvas. Tree shows →token badge and ⛓n count. Empty assertions = nudge with one-click starters. Stale preview = warn banner + "send now".

**Discovery (06).** Confidence is a chip treatment: spec-declared = solid tint chip; observed = dashed hollow chip + italic path + "seen n× · codes". Deprecated = line-through + warn tag. Detail pane merges spec (params, schema) with traffic (seen codes). Promote button says what it does: "Write 24 files". Parse error names file:line:col + jump action.

**History (07).** 28dip rows: time, method chip, URL (TextSearch matches highlight), status, duration, env. Older than today = 60% opacity + relative day. Archive is read-only; Restore opens a new unsaved tab, never overwrites.

**Auth (08).** Inheritance bar atop the Auth tab: "inherited from charges/ — override for this request"; editing inherited auth edits `_folder.req` (footer says so). Type segmented control: None/Basic/Bearer/API key/OAuth2, each type only its fields, values default to var chips. Token chip trio: valid (pass) / expires soon (warn, auto-refresh on send) / expired (fail + refresh action); chip also rides the Send button corner. Cookie jar per domain, flags in text, session-only, never serialized to the collection.

## Interactions & Behavior
Key map (Ctrl = Win/Linux glyphs in mocks): Ctrl+Enter send · Esc cancel/abort · Ctrl+E env switch · Ctrl+P palette · 1–5 views · Ctrl+F find (TextSearch, reused verbatim) · Ctrl+S save · Ctrl+T/W tabs · Ctrl+R run · Ctrl+\ flip response pane. Every surface shows its keys in a mono footer strip.
In-flight: Send → Cancel (warn outline) + live elapsed; previous response greys underneath. Huge body: warn banner "showing first 1 MB of n" + open full/save. Binary: hex stub + content type + preview/save.
No animations are specified beyond spinners (2px ring, accent on accent-700 track); transitions instant, native-app feel.

## State Management (per surface)
Shell: open tabs + dirty flags (file-backed), response cache per request, pane orientation (persisted), env selection. Runner: run state machine (idle/running/aborted/done), per-request result stream, expanded rows. Chaining: stored last response per request + staleness flag (request edited after send). Discovery: map cache (`.meridian/discovery/*.map`), source toggles. History: append-only log (`.meridian/history.log`, capped 10k). Auth: token cache in keyring with expiry. **No UI-only state may exist** — anything editable must serialize to the text format.

## Assets
No images. Icons in mocks are placeholder glyphs (▤ ▶ ◎ ≣ ⬡ etc.) — use the HultUI icon set; the rail icons need Requests/Runner/Discovery/History/Environments equivalents. Fonts: Inter + JetBrains Mono (or the framework's equivalents).

## Out of scope (per brief §4/§5)
File format itself, CLI args, report emitters, HTTP engine, OAuth network legs, keyring integration, OpenAPI parsing, multi-line editor internals, WebSocket/SSE/gRPC (no stub UI — URL bar layout just leaves room left of the method chip for a future protocol picker), runner iterations/perf view (pre-flight shows the disabled slot).
