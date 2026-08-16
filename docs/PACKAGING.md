# Packaging

Every `src/` project packs; nothing else does (the root `Directory.Build.props`
turns packing off repo-wide, `src/Directory.Build.props` turns it back on with the
shared metadata). One version for the whole family, set once as `VersionPrefix` /
`VersionSuffix` in the root props — currently `0.1.0-preview`. Project-to-project
references become exact-version package dependencies automatically.

```bash
dotnet pack HultUI.slnx -c Release -o artifacts/packages
```

produces ten packages plus symbol packages (snupkg):

| Package | What it is |
|---|---|
| `HultUI` | The everything-included meta package. The `[Reactive]` source generator and the HULTUI analyzers ship *inside it* under `analyzers/` — install one package, everything works. |
| `HultUI.Core` | Zero-dependency core: elements, layout, reactivity, events, styling. |
| `HultUI.Text` | Text layout, UAX #14/#9 via platform ICU. |
| `HultUI.Rendering.Skia` | SkiaSharp renderer. |
| `HultUI.Platform.SDL3` | Windowing/input/clipboard/dialogs. |
| `HultUI.Platform.Accessibility.Windows` / `.Linux` | UIA and AT-SPI bridges. |
| `HultUI.Headless` | Headless testing host — consumers test their apps with it. |
| `HultUI.Icons`, `HultUI.Syntax` | Satellite libraries. |
| `HultUI.SourceGenerators` | The generator/analyzers standalone, for Core-only projects. Development dependency; no lib output, no symbols package. |

## Verifying a pack

The proof is consumption, not creation: a scratch console with a `nuget.config`
pointing at `artifacts/packages`, referencing `HultUI` + `HultUI.Headless`, using a
`[Reactive] partial` property, pumping a `HeadlessSession` and querying the
rendered text. That exercises restore, the embedded analyzer, and the runtime in
one page of code. (First verified 2026-08-16.)

## Before anything is published

- **License.** The repository has no LICENSE file, so the packages deliberately
  carry no `PackageLicenseExpression` — choosing a license is a decision packing
  must not make. Pick one, add the file, set the property in
  `src/Directory.Build.props`.
- **Publishing** to nuget.org (or a private feed) is not wired anywhere and should
  be an explicit, tag-gated CI step when wanted — the release-tag convention
  (`vesper-v*`, `junction-v*`) has room for a `hultui-v*`.
- **Stability statement.** Everything is `0.1.0-preview`; nothing is API-stable
  yet, and the version says so.
