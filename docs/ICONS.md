# Icons

**Use SVG path data for new work.** `HultUI.Icons` rasterizes real vector artwork through Skia and caches the result. Unicode glyphs — `"⌕"`, `"✕"`, `"▾"` — were how this codebase drew icons before that library existed. They still appear in places that predate it; they are not the default any more.

---

## Why not Unicode glyphs

They look free and cost you four things:

- **They depend on a font you don't control.** A glyph that renders on your machine is a hollow box on a machine whose font lacks it. There is no per-glyph fallback guarantee, and the failure appears on someone else's computer.
- **Their metrics are the font's, not yours.** A Unicode glyph inks roughly 64% of its font size and a vector fills about 72% of its box — and that varies per glyph. Sizes and optical alignment never quite agree between two glyphs from different blocks.
- **They vanish when thin.** Phosphor's regular `minus` is a bar 6% of its box; at caption-button size that is 0.6px, straddling two pixel rows at ~25% alpha each. As a font glyph the same problem is invisible until someone reports a blank button.
- **There is no glyph for your product.** Anything specific — a Visual Studio mark, a brand — has no codepoint.

**Where a text glyph is still right:** inside `HultUI.Core`, which has zero external dependencies and therefore cannot reference the icon library. The context menu's submenu arrow is drawn as text for that reason. An app passes real artwork in through `ContextMenuItem.Icon` instead (below).

---

## What's in the box

`HultUI.Icons` is a satellite project — apps reference it, the framework does not. Two sets live there:

| Type | What it draws |
|---|---|
| `Phosphor` | [Phosphor Icons](https://phosphoricons.com) (MIT), 82 bundled: regular weight, plus `minus-bold` / `square-bold` / `x-bold` for window controls |
| `FileIcons` | File-list glyphs — folder, page, code, image, archive — plus the Visual Studio solution and project marks |

```csharp
using HultUI.Icons;
```

### Phosphor

| Member | Purpose |
|---|---|
| `Draw(name, size, color, scale)` | An `Element` ready to add to a tree |
| `Source(name, size, color, scale)` | The `IImageSource`, for callers that paint their own rows |
| `Has(name)` / `Names` | Membership, for tests and pickers |
| `InvalidateCache()` | Drop cached rasters after a scale or palette change |

Names are Phosphor's own, kebab-case: `git-branch`, `arrows-clockwise`. **An unknown name draws an empty, correctly sized box rather than throwing** — one typo costs a glyph, not a crash — which also means nothing at runtime tells you the name is wrong. Name your icons in constants and let a test check them:

```csharp
static class Icons { public const string Branch = "git-branch"; }
```

`Junction.Tests.IconNameTests` reflects over exactly such a class and asserts every constant is in the bundle. Copy that pattern rather than sprinkling literals — the check belongs to the app that owns the constants, not to the icon library.

### File icons

```csharp
var kind = FileIcons.KindFor(entry.Name, entry.IsDir);
var glyph = FileIcons.Draw(kind, palette, tint: null, size: 16f, scale: displayScale);
```

`KindFor` matches specific families first, then **defers to the syntax highlighter** — whatever `HultUI.Syntax` can colour gets the code glyph. That is deliberate: a hand-written list of "code" extensions drifts from the grammar set, and two apps' lists drift from each other. Asking is a table lookup, safe per row per frame: `SyntaxHighlighter.CanHighlight` answers from the bundled grammars' extensions and does not load TextMate's registry (about 5 MB and 7 ms in NativeAOT, 30 ms under the JIT). The first tokenize does load it, so tokenize off the UI thread. `FileIconPalette` takes six theme colours, so the library tints itself from your palette without knowing your theme.

### Context menus

`ContextMenuItem.Icon` is a `Func<Color, IImageSource?>` — a factory over colour, not a fixed image, so a disabled or danger item gets artwork in the colour its label resolved to:

```csharp
new ContextMenuItem { Label = "Refresh", Icon = c => Phosphor.Source("arrows-clockwise", 14f, c, scale) }
```

`Glyph` (a string) still works and is what the framework falls back to.

---

## Two rules that decide whether icons look right

### 1. Raster at device resolution

Pass the window's display scale. `Phosphor.Source("git-branch", 13f, …, 1.5f)` cuts a 20px bitmap for a 13-unit box, so the renderer resamples nothing.

Skip it and every icon is scaled on the way to the screen — soft at 100%, coarse at 150%. This is not a theoretical concern; it is what made the icons look low-resolution before the scale parameter existed.

```csharp
public static float DisplayScale
{
    get => _displayScale;
    set
    {
        if (MathF.Abs(_displayScale - value) < 0.001f) return;
        _displayScale = value;
        Phosphor.InvalidateCache();   // every cached raster was cut for the old scale
    }
}
```

Both apps keep a static like this, fed from `window.DisplayScale`.

### 2. Rebuild after invalidating

`InvalidateCache` **retires rather than disposes**. Elements already in the tree hold the sources handed out before the call, and freeing those bitmaps underneath them makes every icon vanish until something rebuilds. The retired batch is released on the *next* invalidation, by which time a rebuild has long replaced the references.

That only works if a rebuild actually follows. Dragging a window to a monitor with different scaling used to blank Junction's icons because the rebuild was tied to a responsive breakpoint — invalidate and mark dirty unconditionally.

---

## Sizing

A vector fills more of its box than a glyph fills its em, so **reusing sizes chosen for text glyphs draws icons about 13% oversized**. Junction converts once, at the boundary:

```csharp
public static float IconBox(float nominal) => MathF.Round(nominal * 0.88f * 2f) / 2f;
```

One ratio applied uniformly preserves the relative hierarchy the design chose — a 9 badge against a 15 button — and corrects only the systematic error. Rounding to halves keeps rasters landing on sensible pixel counts instead of a slightly different bitmap per call site. `IconSizeTests` pins all three properties.

If you are sizing fresh artwork with no legacy numbers to honour, just pick the box you want.

---

## Adding artwork

**Phosphor.** Add the name to [`scripts/phosphor-icons.txt`](https://github.com/fredrikhu/hultui/blob/main/scripts/phosphor-icons.txt) and regenerate:

```bash
./scripts/generate-phosphor-icons.ps1
```

That pulls `@phosphor-icons/core` from the npm registry, extracts one `d` string per listed icon verbatim, and rewrites `Phosphor.Paths.g.cs`. A name ending in a weight suffix comes from that weight — `minus-bold` from bold, `circle-fill` from fill — everything else from regular.

`-Check` regenerates to memory and fails if the result differs from the checked-in file, so you can confirm the bundle is reproducible without writing anything. `-Version` pins a different package version; it is recorded in the generated header, so artwork changing under a version bump shows up in the diff. `-Assets` points at an already-extracted package and skips the download.

A cheaper guard runs in the test suite: `TheBundleIsExactlyWhatTheManifestAsksFor` compares the manifest against `Phosphor.Names`, so hand-editing the generated file or forgetting to regenerate fails without needing pwsh or a network.

**Anything else.** `FileIcons.Parts` shows the shape: path data authored in a fixed box, drawn filled or stroked in a palette colour. Four things to get right:

- **One path, `fill="currentColor"`, no baked-in colour.** The caller supplies the colour.
- **The fill rule does not travel in the `d` string.** SVG carries it as a presentation attribute — often inside a CSS class — and `SKPath.ParseSvgPathData` reads only `d`, defaulting to nonzero. A shape whose holes are wound like its outline then fills solid. The Visual Studio mark's right notch did exactly that. Pass `SKPathFillType.EvenOdd` explicitly when the source declares it.
- **Record the authoring box** and scale from it, rather than re-authoring coordinates.
- **Check the licence, and write it in the file.** The bundled marks note theirs at the path.

---

## Testing

Guards worth copying. The library's own live in `tests/HultUI.Icons.Tests`; the per-app checks live with the app, in `tests/Junction.Tests` and `tests/VesperCommander.Tests`:

| Test | Catches |
|---|---|
| `EveryBundledIconRastersToVisiblePixels` | A malformed `d` — it parses to an empty path and draws nothing, silently |
| `EveryIconConstantNamesABundledIcon` | A typo or a rename in the generated set |
| `IconNamesAreNeverRenderedAsText` | Both directions of the icon/text mix-up (below) |
| `RastersAreCutAtDeviceResolution` | Losing the scale parameter at a call site |
| `HairlineIconsStaySolidAtWindowControlSize` | Sub-pixel strokes rendering as a ghost |
| `TheVisualStudioMarkHasBothNotchesCutOut` | The fill-rule trap above |

The first one matters most: a broken icon fails as a blank box, not an exception, so nothing tells you unless a test looks at pixels.

**The icon/text mix-up is the one to guard against by name.** Once icons are named strings, two mistakes compile cleanly and neither throws:

```csharp
Ui.Icon("✕")           // a character where a name belongs → draws nothing
Text.New(icon)         // a name where text belongs → prints the word "minus" on screen
```

Both happened during Vesper's conversion. The convention that makes them checkable is naming: a parameter carrying a Phosphor name is called `icon`, and text that happens to be a symbol — a keycap's `↵`, a sync rail's `+→` — is a `label` or a literal. A test then greps for `Text.New(` applied to anything named `icon`. Write the check so it allows a qualifier (`OptionPages[i].icon`), or it will miss the common case — the first version of that test did.

---

## Where the conversion stands

- **Junction** — fully on Phosphor, including window controls and context menus.
- **Vesper Commander** — converted. File-list and view-mode icons come from `FileIcons`; chrome, dialogs and status marks are Phosphor, named in its `Glyphs` class.
- **The framework** — the context menu's submenu arrow is text, and stays that way while `HultUI.Core` has no dependencies.

### What is deliberately still text

Converting everything that *looks* like a symbol is the wrong target. These stay text because they are text:

- **Keycaps** — `Ui.Kbd("↵")`. The chip renders a key's label.
- **Typographic values** — `…` for a truncated path, `—` for "no value".
- **Dense status columns** — the sync rail's `+→` / `←+` / `≠` / `∅`, which are two-character combinations no single icon expresses, and the diff gutter's `−` / `+`, which align to a monospace column.
- **Labels that happen to be short** — a bookmark's initial letter, the rename dialog's `Aa` and `.*` toggles.

Vesper's `IconKind` also keeps a few bespoke vector glyphs — the view-mode toggles and nav chevrons — drawn from its own path data. They are already SVG, so they are not the problem this page is about; folding them into Phosphor would be tidying, not a fix.
