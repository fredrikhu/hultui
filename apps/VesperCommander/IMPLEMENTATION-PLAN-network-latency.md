# Implementation Plan — Network latency: never block the UI on a remote path

Prerequisite for UNC support, but not specific to it: every fix here also covers a sleeping USB disk, a stalled mapped drive, and a VPN that dropped. UNC is what makes it routine.

> **Status: P1–P4 built.** Three things landed differently from the plan below, each noted at
> its phase: `GotoPath` keeps its synchronous path for local targets (P2), `FileSystem.List`
> gained an enumeration seam so the between-entries check is testable (P3/P4), and the
> connecting threshold is measured on the application clock rather than the wall (P3).
> `FileSystem.FreeLabel` remains a UI-thread network call on a **mapped** network drive — see
> Deferred. Browsing a bare `\\server` followed as a fifth slice, on the same rule: the host is
> dialled before the enumeration, so an absent server costs the probe's budget rather than the
> operating system's patience.

## The measurements (this machine, 2026-09-22)

| Call | Target | Time |
|---|---|---|
| `Directory.Exists` | dead hostname (DNS fails) | **1.2 s** |
| `Directory.Exists` | unroutable IP (TCP times out) | **21.0 s** |
| `Socket.ConnectAsync(:445)` + 2 s budget | dead hostname | 1.2 s (`SocketException`) |
| `Socket.ConnectAsync(:445)` + 2 s budget | unroutable IP | 2.0 s (cancelled) |

The second pair is the plan's load-bearing fact: **a socket connect is cancellable and a filesystem call is not.** 21 s of frozen window becomes a 2 s bounded wait, and only because we ask the network a question we are allowed to give up on.

## Architecture grounding (current code)

- **Listing is already off-thread** — `Listing()` (`CommanderApp.cs:545`) drives `AsyncLoader<DirListing>` keyed `"{dir}#{gen}"`. `FileSystem.List` (`FileSystem.cs:93`) never throws; it maps failures onto `PaneState`. The UI thread is not the problem here — *uncancellability* is.
- **Four UI-thread blockers**, all `Directory.Exists`/`File.Exists`:
  `GotoPath` (`CommanderApp.cs:865`, `:870`, `:881`) · session restore (`CommanderApp.cs:460–461`) · `OpenPathAsTab` (`CommanderApp.cs:517`) · startup arg (`Program.cs:28`).
- **Pane states** — `PaneState { Ok, Empty, Offline, Denied }` (`FileSystem.cs:21`), dispatched at `CommanderApp.Pane.cs:216`, rendered by `BuildStateBlock` (`CommanderApp.Pane.cs:879`). Review shortcuts Ctrl+Shift+E/O/D/K force them (`CommanderApp.cs:2744`).
- **Cancellation is already the house pattern** — `FindEngine.Walk` (`FindEngine.cs:144`) and `SyncEngine` (`SyncEngine.cs:42`) both take a `CancellationToken` and check it between entries. `FileSystem.List` is the one walk that never got one.
- **`AsyncLoader` retries a throw** — 3 attempts, 0.5 s/1.5 s/4.5 s backoff (`src/HultUI/AsyncLoader.cs`). A cancelled producer must **return a sentinel, not throw**, or every abandoned navigation relaunches itself three times.
- **Headless determinism** — `AsyncLoader` producers run at the top of the next `PumpFrame`. Raw `Task.Run` does not, and needs `PumpUntil`. Anything new that wants a deterministic test should ride the loader or take an injectable clock.
- **`ListingsReady`** (`CommanderApp.cs:2881`) is the test-hosting seam; it flips on delivery, so an Offline result still counts as ready. Keep that true.

---

## P1 — `PathProbe`: the one primitive

New file `PathProbe.cs`. Everything else in this plan calls it.

```csharp
public enum PathKind { Directory, File, Missing, Unreachable, TimedOut }

public static class PathProbe
{
    public static bool IsRemote(string path);                                  // UNC, or a DriveType.Network letter
    public static Task<PathKind> KindAsync(string path, TimeSpan budget, CancellationToken ct);
    public static Task<bool>     ReachableAsync(string path, TimeSpan budget, CancellationToken ct);
}
```

- **`IsRemote`** — `\\` prefix, or `GetPathRoot` is a letter whose `DriveInfo.DriveType == Network`. Cache the letter→type map once per `Drives()` refresh; do not construct `DriveInfo` per call.
- **`ReachableAsync`** — pull the host out of `\\host\share\…`, `Dns.GetHostAddressesAsync(host, ct)`, then `Socket.ConnectAsync(addr, 445, ct)` under a CTS linked to `budget`. An IP literal skips DNS.
  - **Only a *timeout* means unreachable.** A fast `SocketException` (refused, unreachable, no such host) falls through and lets the real filesystem call answer. We are buying our way out of the long wait, not second-guessing the OS — a live server on an odd port must never be declared offline by this check.
  - Per-host result cache, short TTL (~5 s reachable / ~3 s not). The breadcrumb and footer probe often; one dial per host per navigation is the budget.
- **`KindAsync`** — remote path → `ReachableAsync` first, and return `Unreachable` if that times out. Then race `Task.Run(() => Directory.Exists ? Directory : File.Exists ? File : Missing)` against `Task.Delay(budget)`.

> **Honesty about the worker.** A timed-out probe *abandons* its thread; it cannot kill it. The thread stays parked in the syscall until the OS gives up (~21 s worst case) and then discards its answer. We bound the UI's wait, not the syscall. This is why `ReachableAsync` comes first — it is the only part that genuinely gives the time back, and why probes are cached rather than fired per keystroke.

## P2 — The four blockers

**The invariant to review against: no synchronous filesystem call on a remote path from the UI thread.** Local paths keep their sub-millisecond `Directory.Exists` and change nothing — that is what keeps this a small diff.

- **`GotoPath` (`CommanderApp.cs:857`) → async *for a remote target only*.** The only site that genuinely needs the probe: it must tell directory from file from archive to decide where to land. While one is out, the prompt shows `Spinner.New(12f)` + "Checking…" with the confirm button inert, and **Esc / Cancel cancels the CTS** and closes. Budget 10 s. `Unreachable`/`TimedOut` navigates anyway — the pane resolves it and shows Offline + Reconnect, which is a better place to wait than a modal dialog and the only one that can retry.
  - **As built:** a local target keeps the straight-line synchronous path rather than going async too, which is the same local/remote split the rest of P2 uses. All five existing `GotoPathTests` then pass unmodified, including the ones that assert navigation completes inside the key press.
- **Session restore (`CommanderApp.cs:460–461`) → filter local, trust remote.** The existence check stays for a local folder (microseconds) and is skipped for a share, which is where the stall was. An unreachable share reopens as an Offline tab with a Reconnect button, which is the tab you wanted back.
  - **As built:** the plan said to delete the filter outright. That was wrong, and the suite caught it: with nothing filtered, a saved session from an earlier test restored tabs pointing at temp folders that test had already deleted, and four unrelated `DiffPartnerMarkTests` began failing in roughly half of all runs. Applying the same local/remote split the other three call sites use fixes it and is more consistent than the original plan.
- **`Program.cs:28` and `OpenPathAsTab` (`CommanderApp.cs:517`) → probe only when local.** `PathProbe.IsRemote(p) ? accept : (Directory.Exists(p) ? accept : fallback)`. A remote argument is explicit user intent; open it and let the pane report. Local behavior — including "a file argument falls back to cwd" — is untouched.

## P3 — `Connecting` pane state + a token through the listing

- **`PaneState.Connecting`** — new enum member (`FileSystem.cs:21`). `BuildStateBlock` case: `Spinner.New(20f)`, `Ui.Mono(root)` + "Connecting…", and a **Cancel** ghost button beside the existing Reconnect idiom. Cancel aborts the load and returns the tab to its previous folder.
- **When it shows** — `PaneStateOf` (`CommanderApp.cs:532`) returns `Connecting` when the loader is in flight **for a different folder than the one on screen**. Add a per-side `string? _shownDir`, set in `onLoaded`; compare with `PathEquals`. The stale-while-loading behavior for a *refresh* of the same folder stays exactly as it is.
- **Don't flash it.** Only after the load has been in flight past a threshold (~350 ms), so a local folder never shows it. Record the load start per side and schedule a `MarkDirty` at the threshold (the `Task.Delay` + `InvokeOnMainThread` idiom from `AsyncLoader.RetryAfter`), skipping the repaint when the folder has already landed.
  - **As built:** the threshold is measured on `Application.Time` — the platform's `TimeProvider`, newly exposed for this — not `Environment.TickCount64`. Under a headless session that clock advances only when a frame is pumped, so the threshold is a count of frames rather than a race with machine load. Timing it on the wall made four unrelated `DiffPartnerMarkTests` pass alone and fail in a full run. The settable field remains, for tests that want the state immediately.
- **Review shortcut** — extend the force-state set at `CommanderApp.cs:2744` with Ctrl+Shift+N so the state can be reviewed without a server.
- **`FileSystem.List(..., CancellationToken ct)`** — check `ct` inside the `EnumerateFileSystemInfos` loop (`FileSystem.cs:115`), matching `FindEngine.Walk`. On cancellation **return `DirListing.Loading`, do not throw** — see the `AsyncLoader` retry note in the grounding. Owner: a per-side CTS, cancelled and replaced when the loader key changes.
  - **As built:** two checks, one before the walk and one between entries, plus an internal `EnumerateOverride` seam supplying the sequence a walk iterates. Without the seam the between-entries check is untestable — "partway through" is not a moment a test can otherwise name — and a negative control proved it: removing that check failed nothing. (`FindEngine`'s own token has the same gap; its tests all pass `CancellationToken.None`.)
- **Skip the `DriveInfo.IsReady` precheck for network roots** (`FileSystem.cs:110`). It already no-ops for UNC (the ctor throws), but for a *mapped* dead drive it is a second full network wait before the enumeration that is about to fail anyway.

> **Scope, honestly.** P3's token bounds a huge or slow *directory* and stops work we no longer want. It does **not** un-stick a dead host — the enumeration syscall is not cancellable. The dead host is P1's reachability precheck, and nothing else in this plan fixes it.

## P4 — Tests

- **`PathProbeTests`** (unit) — host extraction from UNC shapes incl. `\\server` and `\\server\share`; `IsRemote` classification; the cache TTL; **the refusal-vs-timeout rule** (a fast `SocketException` must *not* yield `Unreachable`). Point `ReachableAsync` at `127.0.0.1` on a closed port for the refusal case and at an unroutable RFC-5737 address for the timeout case, both with a short budget.
- **`FileSystemCancelTests`** (unit) — a pre-cancelled token returns the sentinel and **does not throw**; a token cancelled mid-walk stops early. Negative control: remove the `ct` check and confirm the test fails.
- **`ConnectingStateTests`** (headless, hosting `CommanderApp`) — with the threshold zeroed, a load in flight for a different folder renders `Connecting`; a same-folder refresh does not; `ListingsReady` stays false until delivery. Assert the state, not eventual pixels — per the skill's polling warning, a `PumpUntil` loop here would pass against a pane that never re-renders.
- **`GotoPathAsyncTests`** (headless) — Busy state renders; Esc during Busy closes and cancels; a `Missing` result keeps the dialog open with the error, matching today's synchronous behavior.
- **Manual, once:** point a pane at an unroutable IP (`\\10.255.255.1\share`) and confirm the window stays responsive throughout. That is the acceptance test the measurements above define.

---

## Sequencing & scope

Four phases, one commit each. **P1 + P2 alone fix the reported freeze** and are shippable on their own — P3 is the polish that explains to the user what the app is waiting for, and P4 locks it down. If the batch has to be cut, cut from the back.

**Decided:** only a connect *timeout* short-circuits reachability (P1); session restore stops filtering rather than learning to probe (P2); a cancelled listing returns a sentinel rather than throwing (P3).

**Deferred (not in this batch):** `WNetAddConnection2` credential prompts; the drive-picker and breadcrumb work for letterless roots; the `longPathAware` manifest; throttling `ThumbnailCache` concurrency on a remote pane. These are the rest of UNC support and none of them block this one.

**Deferred, and the one thing here that is still a UI-thread network call:**
`FileSystem.FreeLabel` (`FileSystem.cs:413`), which `BuildFooter` asks on every build for both
panes. On a UNC root `DriveInfo` throws immediately, so it costs nothing and shows nothing — the
footer's missing free-space figure. On a **mapped network drive** it is a real round trip, and on
one whose server has gone away it is the full 21 s, on the UI thread. It was left alone
deliberately: the honest fix is `GetDiskFreeSpaceEx` (which does work on a UNC path) behind a
short-lived cache with a repaint when it lands — which restores the figure for UNC rather than
taking it away from mapped drives, and is its own small piece of work rather than a line
smuggled into this one.
