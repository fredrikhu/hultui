# Packaging

Every `src/` project packs; nothing else does (the root `Directory.Build.props`
turns packing off repo-wide, `src/Directory.Build.props` turns it back on with the
shared metadata). One version for the whole family, set once as `VersionPrefix` /
`VersionSuffix` in the root props — currently `0.1.0-preview`. Project-to-project
references become exact-version package dependencies automatically.

```bash
dotnet pack HultUI.slnx -c Release -o artifacts/packages
```

produces fourteen packages plus symbol packages (snupkg):

| Package | What it is |
|---|---|
| `HultUI` | The everything-included meta package. The `[Reactive]` source generator and the HULTUI analyzers ship *inside it* under `analyzers/` — install one package, everything works. |
| `HultUI.Core` | Zero-dependency core: elements, layout, reactivity, events, styling. |
| `HultUI.Text` | Text layout, UAX #14/#9 via platform ICU. |
| `HultUI.Rendering.Skia` | SkiaSharp renderer. |
| `HultUI.Platform.SDL3` | Windowing/input/clipboard/dialogs. |
| `HultUI.Platform.Accessibility.Windows` / `.Linux` | UIA and AT-SPI bridges. |
| `HultUI.WinForms` | The WinForms host: mount a HultUI tree as a `System.Windows.Forms.Control`. The only `net10.0-windows` package, and the only one not in the cross-platform solution filter. |
| `HultUI.Headless` | Headless testing host — consumers test their apps with it. |
| `HultUI.Media` | Video playback without a decoder: the backend contract, the reactive `MediaPlayer`, `VideoElement`, and a fake backend for tests. A decoder is a separate package. |
| `HultUI.Icons`, `HultUI.Syntax`, `HultUI.Themes` | Satellite libraries. |
| `HultUI.SourceGenerators` | The generator/analyzers standalone, for Core-only projects. Development dependency; no lib output, no symbols package. |

## Verifying a pack

The proof is consumption, not creation: a scratch console with a `nuget.config`
pointing at `artifacts/packages`, referencing `HultUI` + `HultUI.Headless`, using a
`[Reactive] partial` property, pumping a `HeadlessSession` and querying the
rendered text. That exercises restore, the embedded analyzer, and the runtime in
one page of code. (First verified 2026-08-16.)

## Before a release

A green build is not the same as a tested release here, and the gap is deliberate
rather than accidental — so it has to be closed by hand.

**1. The suites CI runs.**

```bash
dotnet test HultUI.slnx -c Release
```

The real-window tests in `HultUI.UiTests` report as *skipped* in this run. That is
the gate working, not a problem, but it means this command proves nothing about a
hosted control.

**2. The suites CI does not run.**

```bash
dotnet test tests/HultUI.UiTests/HultUI.UiTests.csproj -c Release -e HULTUI_UITESTS=1
```

These launch real sample windows and drive them through UI Automation, exactly as a
screen reader does. They take the foreground and cannot share a machine with someone
using it — which is why they are gated, and why they are easy to forget. They are the
only coverage of accessibility, of pointer and keyboard input reaching a hosted tree,
and of Tab moving into, through and back out of one.

**3. Drag and drop, by hand.** Run `samples/HultUI.Samples.WinFormsHost`, open its
*Drag and drop* tab, and check both directions: drop files from Explorer onto the drop
zone (including dragging over it and away again without releasing, which is the cancel
path), then drag the button out to Explorer and confirm a file lands.

This one is not automatable, and the reason is worth knowing rather than assuming it
is laziness: OLE drag-and-drop runs a nested modal loop driven by the physical mouse,
and posted messages — which is how every other interaction here is driven, precisely
so the tests do not fight for the input queue — never enter it. It is also not
hypothetical. The first hand-test of the outbound direction found that it never
started at all: the input translator holds mouse capture so a drag keeps receiving
moves after leaving the control, and OLE will not take a capture someone else holds.
Nothing automated would have caught that.

**4. Keyboard layout, if you have one to hand.** The WinForms backend names keys by
physical position, translating virtual keys back through their scan code, so that a
key binding means the same thing there as in a HultUI window. `KeyMapTests` guards the
table and runs in CI, but the layout independence itself cannot be tested without
changing the machine's active layout. On an AZERTY or QWERTZ install, confirm that a
binding follows the key's position rather than its printed letter.

**5. The pack, consumed.** See *Verifying a pack* above.

## The documentation site

`docfx.json` builds the guides in this folder plus an API reference harvested
from the XML doc comments — eleven of the fourteen packable projects, listed
explicitly — into `artifacts/docs`. The generated API pages land in `docs/api/`, which is ignored;
nothing generated is committed.

```bash
dotnet tool restore && dotnet docfx docfx.json --serve
```

DocFX is pinned in `.config/dotnet-tools.json`, so the version is the same
everywhere. CI runs the same command with `--warningsAsErrors` and uploads the
site as an artifact — a bad cref or a stale link breaks the build instead of the
site. Three packages are absent from the API reference. `HultUI.SourceGenerators`
is a build-time analyzer with no runtime surface for a consumer to call;
`HultUI.WinForms` targets `net10.0-windows`, which will not build on the Linux
runner this job uses; `HultUI.Themes` is an omission rather than a decision, and
worth adding.

## Before anything is published

- **License.** The repository has no LICENSE file, so the packages deliberately
  carry no `PackageLicenseExpression` — choosing a license is a decision packing
  must not make. Pick one, add the file, set the property in
  `src/Directory.Build.props`.
- **Publishing** to nuget.org (or a private feed) is not wired anywhere and should
  be an explicit, tag-gated CI step when wanted — the release-tag convention
  (`vesper-v*`, `junction-v*`) has room for a `hultui-v*`.
- **Hosting the docs site** is the same decision. CI builds it and keeps it as an
  artifact; deploying it (GitHub Pages is the obvious target, and the workflow
  needs `pages: write`) is a separate, deliberate step.
- **Stability statement.** Everything is `0.1.0-preview`; nothing is API-stable
  yet, and the version says so.
