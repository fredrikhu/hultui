# Meridian

A native, keyboard-first HTTP/API client. Collections are **folders of plain
text files** — no cloud, no login, no account wall — so a request is something
you review in a pull request, and the same files run in CI.

Three projects:

| Project | What it is |
|---|---|
| `Meridian.Core` | The engine: file format, variables, HTTP, JSONPath, assertions, chain, runner, reports. No UI reference — testable headless on any platform. |
| `Meridian` | The app (HultUI, Native AOT). Requests · Runner · Discovery · History · Environments on keys 1–5. |
| `Meridian.Cli` | `meridian`, the console binary CI runs. Argument parsing over Core; nothing about a run is decided there. |

## The files

```
payments-api/
  envs/
    local.env          base_url=http://localhost:8080
    ci.env             secret client_secret     ← flag only; the value is not here
  auth/
    login.req
  charges/
    _folder.req        folder-level defaults (auth, headers)
    get-charge.req
  runs/                appended run logs
  .meridian/           derived state (trusted certs, caches) — gitignore this
```

A `.req` file is line-oriented and lossless — comments, blank lines and your
own line endings survive every edit the app makes:

```
# Retrieve one charge.
GET {{base_url}}/v1/charges/{{charge_id}}
name Get charge
header Authorization: Bearer {{token}}

body json
{ "expand": ["refunds"] }
end

assert status equals 200
assert body $.status equals "succeeded"
assert time under 500 ms
extract body $.id -> charge_id
```

**Variables** resolve environment-first, then values extracted by earlier
requests; `{{$uuid}}` and `{{$timestamp}}` are built-ins and can't be shadowed.
**Secrets** are flagged in the `.env` file and stored in the OS keyring (or, in
CI, read from an environment variable of the same name) — never in the
collection.

**Environments are made in the app** (key 5), with no file to write by hand:

- **New environment…** (the empty state, or **＋ new environment** under the
  list) asks for a name and writes an empty `envs/<name>.env`, making `envs/`
  when the collection has none. The new environment becomes the active one.
  Names with `/`, `\`, `..`, a leading or trailing dot, a character Windows
  refuses or a Windows device name (`con`, `nul`, `com1` …), and names already
  taken in any letter case, are refused in the prompt.
- **＋ add key** adds a key to the active environment; a key another
  environment lacks shows **missing — add to …** in that column (a secret
  arrives as its flag alone); **✕** at a row's end removes the key from every
  environment. Removing a secret also forgets this machine's keyring value for
  it, on the save that writes the files without it.
- The **SECRET** column's **○ / ●** toggles a key's secret flag in every file
  that has it: on appends `secret <key>`, off takes it out again (a key with
  only the flag keeps its place as `key=`). A flagged key's cell then asks for
  the value, which goes to the OS keyring. A value already typed into the file
  shows **still in local.env — move to keyring** (or **— clear**).
- Every edit waits in the table until **Ctrl+S**, which rewrites only the lines
  that changed.

**Files are renamed and deleted in the app** too. Every row of the Requests
tree (requests and folders) and of the environment list has a **⋯** at its end,
and a right-click on the row opens the same menu: **Rename…** and **Delete…**.
**F2** and **Delete** do the same for the request open in the editor, or for
the active environment on the Environments view.

- **Rename** moves the file (`get-charge.req`, `envs/staging.env`) or folder,
  under the same name rules as a new environment, refusing a name a sibling
  already has in any letter case; a change of letter case alone is allowed.
  An open tab follows its file, unsaved edits and all, and saves to the new
  name; a folder takes its `_folder.req` defaults and everything else in it
  along. A renamed environment keeps its unsaved edits and stays active. The
  tree shows a request's `name` line when it has one, so renaming the file
  leaves that label as it is.
- **Delete** asks first, naming what goes: the file, or the folder with the
  number of requests and other files in it, and any open tab with unsaved
  changes. It goes to the **Recycle Bin** on Windows and the **Trash** on
  Linux (the freedesktop.org trash the file managers restore from). Where
  there is no bin the confirmation says the delete is permanent, and where the
  bin refuses a path (a network share, another file system than the trash) a
  second confirmation offers to delete it for good. Open tabs of what went
  close. Deleting the active environment makes `local` active, else the first
  left — as opening a collection does. Its secrets stay in the keyring: they
  belong to the collection, and the file can come back from the bin.

## Running in CI

```bash
meridian run payments-api/ --env ci --report junit:reports/run.xml
```

```
✓ Login                        200 86ms  1/1 ✓
✓ Create charge                201 142ms  3/3 ✓
✗ Get charge                   200 96ms  1/2 ✓
payments-api/ (ci) 3 requests
✓ 2  ✗ 1  ! 0  − 0  wall 0.3s
✗ Get charge  $.status expected "succeeded", got "requires_capture"
exit code 1
```

Those summary lines are produced by the same code the app's Runner view
renders, so a CI log and the screen say the same thing in the same order.

| Exit | Meaning |
|---|---|
| 0 | every request passed |
| 1 | a request failed, was unreachable, or the run was aborted |
| 64 | usage error — bad arguments, missing collection, unknown environment |

An **assertion failure is red**; an **unreachable host is amber** and never
red. Both exit 1, but the report distinguishes them, because "the API returned
the wrong thing" and "the network is down" are different pages of the runbook.

Other commands and flags:

```bash
meridian run charges/ --env local --bail     # stop at the first failed assertion
meridian run . --env ci --quiet              # summary only, no per-request stream
meridian list payments-api/                  # what's in scope, without sending
```

GitHub Actions:

```yaml
- run: meridian run payments-api/ --env ci --report junit:reports/api.xml
  env:
    client_secret: ${{ secrets.CLIENT_SECRET }}   # a flagged secret, injected
```

## Keys

| | |
|---|---|
| `Ctrl+P` | command palette — every command, and every request by name |
| `1`–`5` | Requests · Runner · Discovery · History · Environments |
| `Ctrl+Enter` / `Esc` | send · cancel |
| `Ctrl+O` / `Ctrl+S` / `Ctrl+W` | open a collection · save (the request, or the environments on their view) · close tab |
| `Ctrl+E` | switch environment |
| `F2` / `Delete` | rename · delete the open request, or the active environment on its view |
| `Ctrl+R` | run the collection, from any view |
| `Ctrl+I` | import an OpenAPI spec |
| `Ctrl+F` | find in the response |
| `Ctrl+\` | flip the response pane right ⇄ bottom |
| `↑↓ · Enter · r` | in History: walk rows · open · restore |

The app reads its keys from one table, `Meridian/Shortcuts.cs`; the hints in the palette and on
the buttons come from it too.

## Installing

Tagging `meridian-v<x.y.z>` builds the Windows installer and publishes a
release ([workflow](../../.github/workflows/meridian-installer.yml)). The
installer ships the app as `Meridian.exe` and the `meridian` CLI in a `cli`
folder beside it, and offers to put that folder on PATH. The folder is not
optional: Windows file names ignore case, so the two cannot share one.

The app icon is generated, not hand-drawn — `python
scripts/generate-meridian-icon.py` redraws it from the Azimuth palette.

## Building

```bash
dotnet build apps/Meridian/Meridian/Meridian.csproj
dotnet build apps/Meridian/Meridian.Cli/Meridian.Cli.csproj
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj
```

Design handoff: [docs/design/meridian](../../docs/design/meridian/README.md) ·
Plan and phase status: [IMPLEMENTATION-PLAN.md](IMPLEMENTATION-PLAN.md)
