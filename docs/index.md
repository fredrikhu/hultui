# HultUI

A fluent, retained-mode UI framework for .NET desktop applications. Flexbox and
grid layout, fine-grained reactivity in the SolidJS mould, HarfBuzz text with
full Unicode line breaking and BiDi, GPU rendering through SkiaSharp, and a
headless host that lets an application be tested without a screen.

Everything is `0.1.0-preview`: the framework is in real use by three
applications built on it, and nothing about its API is stable yet.

## Start here

- **[Getting started](GETTING-STARTED.md)** — a window on screen, then a
  component that reacts to state.
- **[Elements](ELEMENTS.md)** — the built-in element and control vocabulary.
- **[Layout](LAYOUT.md)** and **[Grid](GRID.md)** — how boxes get their sizes.
- **[Reactivity](REACTIVITY.md)** — signals, computeds, effects, and what
  re-runs when.
- **[Focus and keyboard](FOCUS.md)** — Tab order, where keys go, and the ways
  focus silently goes wrong.
- **[Video playback](MEDIA.md)** — `MediaPlayer`, `VideoElement` and the
  libmpv backend.

## While building

- **[Headless testing](HEADLESS.md)** — drive an app with a virtual clock and
  injected input, and assert on what it rendered.
- **[DevTools](DEVTOOLS.md)** — the in-app inspector and frame HUD.
- **[Threading model](THREADING.md)** — what the UI thread owns and how to get
  back onto it.
- **[Environment variables](ENVIRONMENT.md)** — every `HULTUI_*` switch and
  build define, with exact values.

## Reference

- **[API reference](api/HultUI.yml)** — every public type, generated from the source.
- **[Design decisions](DECISIONS.md)** — the choices behind the framework, and
  the things that were measured and then declined.
- **[Packaging](PACKAGING.md)** — what ships, and what has to happen before
  anything is published.
- **[WinForms interop](design/WINFORMS-INTEROP.md)** — hosting HultUI inside a
  WinForms application, spiked and costed.
