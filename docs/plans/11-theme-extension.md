# Plan 11: Theme extension by key

**Priority:** Medium now, high the day someone ships a control library | **Effort:** Design-heavy, code-light | **Status:** Implemented (facade approach)

## Problem

`Theme` names its control themes one property at a time:

```csharp
public ButtonTheme Button { get; init; } = new();
public TextInputTheme TextInput { get; init; } = new();
public CheckboxTheme Checkbox { get; init; } = new();
public RadioButtonTheme RadioButton { get; init; } = new();
public TooltipTheme Tooltip { get; init; } = new();
public ContextMenuTheme ContextMenu { get; init; } = new();
```

A control that ships outside this repo has no slot. It can be themed only by whatever its
author invents, and none of the available inventions work:

- **Constructor or fluent parameters** — every consumer wires the colours by hand at every
  call site, and nothing re-reads them when `app.Theme` changes at runtime. Theme switching
  silently stops working for third-party controls.
- **A static global** — one theme per process. Breaks the DevTools inspector window (which
  is a second window with its own tree), breaks any app that themes two windows
  differently, and breaks runtime switching.
- **Forking `Theme`** — not possible; it is `sealed`.

So the practical answer today is "vendor the control into the app", which is the outcome a
framework exists to prevent.

Worth being precise about the complaint: this is *not* that inline styling is wrong. Inline
styling is the spirit of the framework — predictable, debuggable, no selector cascade at
runtime — and the `Theme` object already solves the problem correctly for built-in
controls. The gap is only the seam between "a control author picks defaults" and "an app
imposes its palette on a control it did not write".

## Goal

One idiom a third-party control author can follow, and one line an app writes to override
it:

```csharp
// In the control library
public sealed class GaugeTheme : IThemeExtension<GaugeTheme>
{
    public Color Track { get; init; }
    public Color Fill { get; init; }
    public float Thickness { get; init; } = 4f;

    // Defaults derive from the app's palette, so an unstyled gauge already looks
    // like it belongs in whatever theme it lands in — including a dark one.
    public static GaugeTheme CreateDefault(Theme theme) => new()
    {
        Track = theme.SurfaceVariant,
        Fill = theme.Primary,
    };
}

public sealed class GaugeElement : Element
{
    protected internal override void ApplyTheme(Theme theme)
    {
        var gauge = theme.Get<GaugeTheme>();
        _track = gauge.Track;
        _fill = gauge.Fill;
    }
}

// In the app, at startup
app.Theme = Theme.Dark.With(new GaugeTheme { Fill = BrandRed });
```

It stays inline-resolved — one dictionary lookup at style time, no cascade, no specificity.
It stays debuggable — the theme object is a plain object the inspector can print. And it
gives third parties exactly one thing to copy.

## Why `static abstract` rather than a registry

The obvious alternative is a registration call:
`ThemeExtensions.Register<GaugeTheme>(t => new GaugeTheme { … })`. Rejected:

- It is global mutable state, with an ordering hazard — a control used before its library's
  module initializer has run gets no default.
- It has a failure mode that the type system can otherwise rule out entirely: `Get<T>()` for
  an unregistered `T`.
- A `static abstract` member is resolved at the call site and stays NativeAOT-friendly,
  which is one of this framework's sharper selling points.

```csharp
public interface IThemeExtension<TSelf> where TSelf : IThemeExtension<TSelf>
{
    static abstract TSelf CreateDefault(Theme theme);
}
```

`CreateDefault` takes the `Theme` on purpose, and the repo already argues both sides of
this. `ButtonElement.ApplyTheme` derives its colours from semantic ones
(`theme.Primary`, `theme.PrimaryText`, `theme.FocusRing`), so `ButtonTheme` carries no
colours at all and a new palette just works. `Theme.Dark`, by contrast, copy-pastes a
literal dark `TooltipTheme` and `ContextMenuTheme` into itself — seven hardcoded slate
values that any third palette would have to copy again. Handing the theme to
`CreateDefault` is what makes the first pattern the easy one.

## Design decisions — settled

### 1. Do the built-in control themes migrate to the same mechanism? — **Yes, as facades**

The review's actual complaint was the asymmetry: built-ins get a property, everyone else
gets nothing. The six properties are now facades over the store:

```csharp
public ButtonTheme Button { get => Get<ButtonTheme>(); init => Set(value); }
```

`new Theme { Button = … }` still compiles, `Theme.Dark`'s initializer still works, all three
apps built unchanged, and there is one mechanism underneath. The built-in defaults stayed
literal rather than becoming derived — they are the values these controls already shipped
with, and deriving them would have repainted every app. `CreateDefault` still receives the
theme, so new extensions get the good pattern for free.

### 2. Naming

`theme.Get<GaugeTheme>()` / `theme.With(new GaugeTheme { … })` reads well at both call
sites, and `With` matches the copy-on-write convention it actually implements. Alternatives
considered: `Extension<T>()` (accurate, wordier), `Extend<T>()` (reads like mutation, which
it must not be).

### 3. Per-instance overrides — **none**

No framework mechanism. A control author who wants a local tweak exposes it
on their own fluent chain (`gauge.FillColor(x)`), exactly as built-in elements do. Adding a
per-element theme slot would be the first step toward a cascade, and the existing builder
covers the need.

## Analysis steps

1. ~~Confirm the style pass picks extensions up on a theme switch with no extra work.~~
   **Confirmed, no extra work needed.** `Application.Theme`'s setter marks every window
   style-dirty and `StyleResolver` calls `ApplyTheme` on each element it visits, so a
   control reading `theme.Get<T>()` picks up the new palette on the next frame. Pinned by
   `ThirdPartyControlThemingTests.ARuntimeThemeSwitch_ReachesTheExtension`.
2. Measure the cost of `Get<T>()`. It runs per themed element per style pass, so it must be
   a dictionary hit and nothing more once the default has been materialized. If it shows up,
   the fallback is a per-closed-generic static cache keyed by theme identity.
3. Check `Theme` is genuinely safe to cache into. `Theme.Light` and `Theme.Dark` are shared
   process-wide statics, so materializing a default into an internal dictionary must be
   referentially transparent — same input, same output, no observable mutation. It is, but
   confirm nothing else assumes `Theme` has no mutable state.
4. Decide whether the DevTools inspector lists extension themes for the selected element.
   `ElementProperties` already enumerates resolved properties; extensions are the kind of
   thing that is invisible until something prints it.

## Implementation steps

1. `IThemeExtension<TSelf>` in `HultUI.Core.Styling`.
2. On `Theme`: an internal `Dictionary<Type, object>` of overrides, a lazily populated cache
   of materialized defaults, `Get<T>()`, and `With<T>(T)` returning a **new** `Theme` that
   shares nothing mutable with the original. `Theme.Light` and `Theme.Dark` are shared
   statics — `With` that mutated in place would leak one app's brand colour into every other
   consumer of `Theme.Light` in the process, including the inspector window.
3. A sample control in `samples/` that ships its own theme, to prove the idiom is followable
   from outside — the seam is the whole point, so exercising it from inside the framework
   proves nothing.
4. Headless tests: default derives from the palette; `With` overrides it; `With` does not
   mutate the source theme; a runtime theme switch re-runs `ApplyTheme` and repaints.
5. Docs: a "theming a control you didn't write" section, and a note in `DECISIONS.md`
   recording that extension-by-key was chosen over a selector cascade and why.

## Explicitly not included

- **CSS selectors, style classes, implicit styles.** The complexity this framework exists to
  escape, and the performance and debuggability cost lands exactly where it hurts. Already
  on the out-of-spirit list in [the capability roadmap](10-capability-roadmap.md).
- **Cascading extension themes down a subtree.** One theme per `Application`, as today.

## Files

- `src/HultUI.Core/Styling/IThemeExtension.cs` (new)
- `src/HultUI.Core/Styling/Theme.cs`
- `src/HultUI.Core/DevTools/ElementProperties.cs` (if step 4 of the analysis says yes)
- `samples/` — one control that ships its own theme
- `docs/DECISIONS.md`
