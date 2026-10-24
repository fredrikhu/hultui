# Themes

`HultUI.Themes` ships a set of named colour schemes — Gruvbox, Nord, Dracula, Solarized,
Tokyo Night, Catppuccin, Everforest, Rosé Pine — plus the tonal-ramp maths that turns a
handful of seed colours into the scales an app styles against. Junction and Vesper Commander
both offer the whole set; the project references nothing but `HultUI.Core`, so a palette can
be read without depending on how anything draws.

## The shape of a theme

A `NamedTheme` is about a dozen values, not forty:

| Field | What it is |
|---|---|
| `Id` | Stable key, persisted in settings. **Never rename one** — a renamed id reads back as "unknown" and silently drops the user to the default. |
| `Name`, `Blurb` | What the picker shows. |
| `IsDark` | Whether the ground is darker than the text. Decides which way every ramp runs, so it is not cosmetic. |
| `Bg`, `Surface`, `Text` | The ground, the raised bars and menus, the primary text. |
| `Neutral` | The mid-tone the grey ramp is spun from. |
| `Accent`, `Accent2` | Primary and secondary accents. |
| `Red`, `Green`, `Amber` | Deletions, additions, search hits — the three that carry meaning. |
| `SyntaxTheme` | Which TextMateSharp theme colours code. |

The published schemes are terminal and editor palettes: sixteen flat colours, no ramps. The
ramps are generated, which is why a scheme reads as itself here without matching any
particular editor's rendering of it pixel for pixel.

## Ramp numbering is relative to the background

This is the part that is easy to get wrong.

**Step 100 is always the step furthest from the app's background, and step 900 the one
closest to it.** On a dark theme that runs light → dark. On a light theme it runs dark →
light.

That inversion is what lets a call site mean one thing everywhere:

- `Neutral900` is the hover tint that barely lifts off the ground.
- `Neutral300` is text you can read.
- `Accent900` backs a selected row and `Accent200` is the label on it.

Number a light theme's ramp the other way and every one of those flips — the selected row
comes out near-black with near-black text on it. `ThemeCatalogTests` pins the direction, the
monotonicity, and the `Accent200`-on-`Accent900` contrast for every scheme in the catalogue.

The neutral ramp's last two steps are pulled toward the theme's own ground
(`Ramp.GenerateNeutral`), because those steps are hover and zebra tints: they have to sit a
hair off the background the app actually paints, and a generic hue at 20% lightness lands
somewhere else entirely on a ground of `#1d2021`.

## How an app adopts it

Each app keeps a colour register — `Junction.Theme.Nocturne`, `VesperCommander.Theme.Nocturne`
— that every view reads. It is a facade over a `Palette` resolved for the active theme.

**Every member is a property, and that is load-bearing.** A `static readonly Color` freezes at
type-init, so a colour captured that way keeps the startup theme for the life of the process.
The same goes for arrays built out of the register (`LanePalette`, `Swatches.All`): they are
`=>` properties for exactly this reason.

The app's own design system is not generated. Nocturne's ramps were drawn by hand in the
design handoff, so `Palette.NocturneDefault` is a literal transcription and `Palette.For`
returns it directly for that id; everything else goes through the generator. `ThemeTests` in
both app test projects pins the default palette channel-for-channel, which is what keeps
"switchable" from quietly meaning "slightly different".

## Switching at runtime

`Nocturne.Use(id)` swaps the palette and points `SyntaxHighlighter` at the matching code
theme. Three things must follow it, and none of them happen inside:

1. **Invalidate the icon cache.** Glyphs are rasterized in the colour they were asked for
   (`Ui.InvalidateIconCache()` in Junction, `Icons.InvalidateCache()` in Vesper).
2. **Reassign `Application.Theme`** from `ToHultTheme()`. The framework's built-in widgets —
   text fields, context menus, checkboxes — read their own copy, and `StyleResolver`
   re-applies it every style pass.
3. **Drop cached syntax spans.** A span is a plain colour once handed out; nothing about it
   says which theme produced it, so every tokenized diff, preview and viewer keeps the old
   palette until it is re-tokenized.

Then rebuild.

## The framework's own controls

Each built-in control reads its knobs from a `Theme` extension (`Theme.Button`,
`Theme.TextInput`, …) and re-applies them on **every** style pass, so a colour or border
set on the element itself does not stick — see "The theme owns the box" in
[ELEMENTS.md](ELEMENTS.md#3-textinputelement). Override the extension instead:

```csharp
app.Theme = Theme.Dark
    .With(new ButtonTheme { Secondary = new() { Border = brand } })
    .With(new DropdownTheme { BorderWidth = 1.5f });
```

Where a colour is nullable, null means "the palette's answer", so an override of one
knob keeps the rest. A border is the most common thing to want, and every control that
draws one exposes it:

| Extension | Colour | Width | Notes |
|---|---|---|---|
| `ButtonTheme` | per variant `ButtonStyle`: `Border`, `HoverBorder` | `BorderWidth` (all variants; a `ButtonStyle` may override) | see [ButtonElement](ELEMENTS.md#18-buttonelement) |
| `TextInputTheme` | `BorderColor`, `FocusBorderColor`, `UnderlineColor` | `BorderWidth`, `UnderlineThickness` | shared by `TextArea`; a per-field `TextInputStyle` layers over it, see [TextInputElement](ELEMENTS.md#3-textinputelement) |
| `DropdownTheme` | `BorderColor` | `BorderWidth`, `FocusBorderWidth` | also the box of `DatePicker`, `TimePicker`, `NumericUpDown`; a per-field `DropdownStyle` layers over it, see [NumericUpDownElement](ELEMENTS.md#12-numericupdownelement) |
| `CheckboxTheme`, `RadioButtonTheme` | `BorderColor` | `BorderWidth` (null keeps the element's 2px) | |
| `ToggleSwitchTheme` | `OffBorder` | `OffBorderWidth` | the off state is outlined, the on state filled |
| `SliderTheme` | `ThumbBorderColor` | `ThumbBorderWidth` | |
| `ContextMenuTheme` | `BorderColor` | `BorderWidth` | the open list of a dropdown too |
| `CalendarTheme` | `BorderColor` | `BorderWidth` | the open calendar of a date picker |
| `ToastTheme` | `Border` | `BorderWidth` | |
| `TooltipTheme` | `BorderColor` (null: none) | `BorderWidth` | |

`ProgressBarTheme`, `SpinnerTheme` and `SplitterTheme` draw no border. `FocusVisualTheme`
is the two-ring focus indicator, not a border, and is drawn outside the control.

**Writing a control's text style from the theme.** A control whose text takes its colour,
size or weight from the theme — `ButtonElement`, `ToastElement`, the text fields — writes it
into its own `Inherited` style in `ApplyTheme`, and its children read it from there.
`StyleResolver` calls `ApplyTheme` *before* it resolves the element's inherited style, so
those writes count in the same pass. (Until 2026-09-30 it was the other way round: a
button's label drew in its parent's colour and size until something else re-dirtied the
button, and the button tests never saw it because they called `ApplyTheme` directly and
read the button, not the label. Test what the child resolves.) `ApplyTheme` must not read
`ResolvedInherited` — it is not this pass's yet.

**Marking layout from the theme.** `ApplyTheme` runs on every style pass that reaches the
element: each theme switch, and each rebuild that patches it. Mark layout there only when a
size the element measures with changed — compare the new value with the one it had, as
`CheckboxElement`, `NumericUpDownElement` and `CalendarElement` do — or a light/dark switch
lays out every such control and everything up to the root (the perf suite's
`theme/light-dark switch` laid out 78 elements and allocated 8.7 KB a switch for this; it
lays out none and allocates nothing since plan 17 round 3). A size written into a
*part's* box or flex style (`Box.Padding`, `MinHeight`, `Flex.Gap`) marks nothing by itself:
mark that part's layout, not only the control's, or the part keeps its old box (an
expander's header did). The same holds for the control's *own* box: `ButtonElement`,
`KbdElement`, `TextInputElement`, `MenuBarElement` and the table's header, cells and grips
write their padding, border and size in `ApplyTheme`, compare each with the value it had, and
mark layout when one changed — a theme switch, or a `Style` or `Variant` set on the live
control. They no longer get that layout from a rebuild: since plan 17 round 4 a rebuild
patches them against a twin themed the way the style pass will theme them
(`PatchThemesTwin`), and one that changes nothing lays nothing out (Junction's Commit view:
elements laid out per key 17.7 → 1.1, measurements 1,274 → 85). A third-party control's patch
still lays it out every time, so for one the rule above is about theme switches only. A
framework control that opts in has an `ApplyTheme` that touches only itself and its own
children, since the patch runs it on the twin too.

## Adding a scheme

Add a `NamedTheme` to `ThemeCatalog` and put it in `All`. Transcribe the scheme's published
ground, surface, foreground, accents and red/green/amber; pick the nearest bundled
TextMateSharp theme for `SyntaxTheme`. The catalogue tests run against every entry
automatically — they will tell you if the text does not clear 4.5:1 on the ground or the
surface, if `IsDark` disagrees with the ground, or if two of the semantic colours collided.
