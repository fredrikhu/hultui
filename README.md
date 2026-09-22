# HultUI

A cross-platform UI framework for C#/.NET. Build desktop applications with a fluent C# API, flexbox layout, and fine-grained reactivity. No XAML. No markup. Just code.

## Features

- **Fluent C# API** -- build UI trees with a clean, composable syntax
- **Flexbox layout** -- row, column, grow, shrink, wrap, alignment, gap
- **Fine-grained reactivity** -- Signal/Computed/Effect system (SolidJS-inspired)
- **Component model** -- class-based components with automatic reactive rebuilds
- **Source generators** -- `[Reactive]` attribute generates Signal-backed properties
- **Text rendering** -- HarfBuzz shaping, font fallback, emoji, Unicode line breaking,
  BiDi (mixed Arabic/Hebrew/Latin lines reorder per UAX #9, brackets mirror,
  paragraph direction can be auto-detected from the content, and caret, clicks,
  selection and arrow keys all follow the visual order; the *interface* itself
  does not mirror -- see docs/DECISIONS.md)
- **Built-in controls** -- text input, checkbox, radio button, dropdown, tabs, slider, scroll view, canvas
- **Animations** -- tween-based system with easing, delays, and chaining
- **Drag and drop** -- internal element drag and OS file/text drops
- **Custom window chrome** -- borderless windows with custom title bars and edge resize
- **Multi-window** -- multiple windows with shared reactive state
- **Theme support** -- light/dark themes, runtime switching, custom themes
- **GPU-accelerated** -- SkiaSharp rendering on OpenGL via SDL3
- **DevTools overlay** -- F12 to inspect elements, view layout bounds, and monitor performance
- **Zero-dependency core** -- layout, reactivity, and events have no external NuGet dependencies
- **NativeAOT-ready** -- publishes to a single self-contained native executable, no runtime install, no JIT warmup

## Quick Start

```csharp
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;

var root = Flex.Column(gap: 16).Padding(24).Children(
    Text.New("Hello, HultUI!").FontSize(24f).FontWeight(FontWeight.Bold),
    Box.New().Padding(12).Background(Color.FromHex("#4F46E5")).CornerRadius(8f)
        .TextColor(Color.White).Cursor(CursorType.Pointer)
        .Children(Text.New("Click me"))
);

using var app = new Application("My App", 800, 600);
app.SetRoot(root);
app.Run();
```

## Reactive Components

Define stateful components with automatic UI updates:

```csharp
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;

partial class Counter : Component
{
    [Reactive] public partial int Count { get; set; }

    public override Element Build()
    {
        return Flex.Row(gap: 12).AlignItems(AlignItems.Center).Children(
            Box.New().Padding(12).Background(Color.FromHex("#4F46E5")).CornerRadius(8f)
                .TextColor(Color.White).Cursor(CursorType.Pointer)
                .OnClick(_ => Count++)
                .Children(Text.New("Increment")),
            Text.New($"Count: {Count}").FontSize(18f)
        );
    }
}
```

When `Count` changes, `Build()` automatically re-runs and the UI updates via tree diffing.

## Project Structure

```
HultUI.Core                 Zero external dependencies. Elements, layout, reactivity, events, styling.
HultUI.Text                 Text pipeline: ICU4N segmentation, shaping, line breaking, caching.
HultUI.Rendering.Skia       SkiaSharp GPU renderer, HarfBuzz text shaping, font collection.
HultUI.Platform.SDL3        SDL3 windowing, OpenGL context, input translation.
HultUI.SourceGenerators      [Reactive] source generator (Roslyn incremental).
HultUI                      Meta-package. Application class ties everything together.

HultUI.Icons                Optional. SVG icons: the Phosphor set and file-type glyphs.
HultUI.Syntax               Optional. TextMate syntax highlighting for code views.
```

The last two are satellites an app references directly; nothing in the framework depends on
them, so an app that needs neither pays for neither.

### Dependency Graph

```
HultUI (Application)
  +-- HultUI.Platform.SDL3 --> HultUI.Rendering.Skia --> HultUI.Core
  |                                     |
  |                                     v
  |                               HultUI.Text ----------> HultUI.Core
  +-- HultUI.SourceGenerators (analyzer, no runtime dep)
```

## Layout

HultUI uses CSS Flexbox semantics. The `Flex.Row()` and `Flex.Column()` helpers create containers:

```csharp
// Horizontal layout with spacing
Flex.Row(gap: 8).Children(
    Box.New().Width(100).Height(100).Background(Colors.Red),
    Box.New().Width(100).Height(100).Background(Colors.Blue)
)

// Vertical layout with stretch
Flex.Column(gap: 16).Children(
    Text.New("Header").FontSize(24f),
    Box.New().FlexGrow(1).Background(Colors.LightGray)  // fills remaining space
)
```

All CSS Flexbox properties are supported: `FlexGrow`, `FlexShrink`, `FlexBasis`, `FlexWrap`, `JustifyContent`, `AlignItems`, `AlignSelf`, `AlignContent`, `Gap`.

## Built-in Elements

| Builder | Element | Description |
|---------|---------|-------------|
| `Box.New()` | BoxElement | Container with background, border, corner radius |
| `Text.New("...")` | TextElement | Text content with overflow and max lines |
| `TextInput.New()` | TextInputElement | Editable text field with selection, clipboard |
| `Checkbox.New()` | CheckboxElement | Toggle checkbox |
| `RadioButton.New("group")` | RadioButtonElement | Radio button with group exclusion |
| `ScrollView.New()` | ScrollViewElement | Scrollable container with scrollbar overlay |
| `Canvas.New(draw)` | CanvasElement | Custom drawing via callback |
| `Image.New(source)` | ImageElement | Image display with fit modes |
| `Flex.Row()` / `Flex.Column()` | BoxElement | Shorthand for flex containers |

## Themes

Switch between light and dark themes at runtime:

```csharp
app.Theme = Theme.Dark;  // or Theme.Light
```

Create custom themes:

```csharp
var custom = new Theme
{
    Background = Color.FromHex("#1a1a2e"),
    TextColor = Color.FromHex("#eaeaea"),
    Primary = Color.FromHex("#e94560"),
    FontFamily = "monospace",
    FontSize = 14f,
};
```

## DevTools

Press **F12** at runtime to toggle the performance overlay:
- Bounding boxes on every element
- Hover inspector showing element type, name, and dimensions
- Performance HUD: FPS, style/layout/render/present timing, element count

Press **Ctrl+Shift+I** for the element inspector, which opens in its own window: a live
element tree, the box model, and every resolved property of the selection.

All of it is one switch — `app.DevToolsHotkeysEnabled = false` keeps developer surfaces
out of a shipped build. See [DevTools](docs/DEVTOOLS.md).

## Documentation

- [Getting Started](docs/GETTING-STARTED.md) -- create your first app step by step
- [Layout Guide](docs/LAYOUT.md) -- flexbox layout in depth
- [Elements Reference](docs/ELEMENTS.md) -- all built-in elements and their properties
- [Reactivity Guide](docs/REACTIVITY.md) -- signals, computed values, effects, and components
- [Animations](docs/ANIMATIONS.md) -- tweens, easing, chaining, and cancellation
- [Drag and Drop](docs/DRAG-AND-DROP.md) -- internal element drag and OS file/text drops
- [Custom Window Chrome](docs/CUSTOM-CHROME.md) -- borderless windows with custom title bars
- [Multi-Window](docs/MULTI-WINDOW.md) -- multiple windows with shared state
- [Icons](docs/ICONS.md) -- SVG icon rendering, the bundled Phosphor set, and file-type glyphs
- [DevTools](docs/DEVTOOLS.md) -- element inspector (Ctrl+Shift+I) and performance HUD (F12)
- [Threading Model](docs/THREADING.md) -- what the UI thread owns, and how to get back onto it
- [Headless Testing](docs/HEADLESS.md) -- drive an app with a virtual clock and assert on what it rendered
- [NativeAOT](docs/NATIVE-AOT.md) -- publishing a self-contained native executable
- [Packaging](docs/PACKAGING.md) -- what ships, and the gates before anything is published
- [Self-Hosted Runners](docs/SELF-HOSTED-RUNNERS.md) -- optional Windows and Proxmox build hosts
- [Design Decisions](docs/DECISIONS.md) -- architectural rationale

### Building the docs site

The guides above plus a generated API reference for every public type build into
one browsable site with [DocFX](https://dotnet.github.io/docfx/):

```bash
dotnet tool restore && dotnet docfx docfx.json --serve
```

CI builds it on every push with `--warningsAsErrors`, so a broken
`<see cref="..."/>` or a link to a page that moved fails the build rather than
the published site. Nothing is deployed anywhere yet -- see
[docs/PACKAGING.md](docs/PACKAGING.md).

## Apps

Full applications built on HultUI, each with its own installer workflow and test project.
They are the framework's real proving ground -- the samples show one feature at a time,
these carry a whole product's worth of them.

- [`Junction`](apps/Junction/) -- git client: history graph, staging, diffs, conflicts,
  rebase, multi-repo workspaces ([tests](tests/Junction.Tests/))
- [`VesperCommander`](apps/VesperCommander/) -- dual-pane file manager: archives, sync,
  multi-rename, find ([tests](tests/VesperCommander.Tests/))
- [`Meridian`](apps/Meridian/) -- HTTP/API client over folders of plain text files:
  requests, collection runner, OpenAPI discovery ([tests](tests/Meridian.Tests/))
- [`Aperture`](apps/Aperture/) -- Jellyfin client: poster shelves over a real media server,
  with the framework's video player behind Play ([tests](tests/Aperture.Tests/))

## Samples

- [`HultUI.Samples.Minimal`](samples/HultUI.Samples.Minimal/) -- bare minimum app
- [`HultUI.Samples.HelloWorld`](samples/HultUI.Samples.HelloWorld/) -- kitchen sink demo
- [`HultUI.Samples.TodoList`](samples/HultUI.Samples.TodoList/) -- reactive todo list
- [`HultUI.Samples.Theming`](samples/HultUI.Samples.Theming/) -- custom themes and runtime switching
- [`HultUI.Samples.Animation`](samples/HultUI.Samples.Animation/) -- animation demos
- [`HultUI.Samples.DragDrop`](samples/HultUI.Samples.DragDrop/) -- internal drag and OS file drops
- [`HultUI.Samples.ZIndex`](samples/HultUI.Samples.ZIndex/) -- z-index layering
- [`HultUI.Samples.MultiWindow`](samples/HultUI.Samples.MultiWindow/) -- multi-window with shared state
- [`HultUI.Samples.CustomChrome`](samples/HultUI.Samples.CustomChrome/) -- borderless window with custom title bar
- [`HultUI.Samples.ThemedControl`](samples/HultUI.Samples.ThemedControl/) -- theming a control the app didn't write
- [`HultUI.Samples.Clipboard`](samples/HultUI.Samples.Clipboard/) -- one selection copied in several formats
- [`HultUI.Samples.Dropdown`](samples/HultUI.Samples.Dropdown/) -- single-select dropdowns, mouse and keyboard
- [`HultUI.Samples.Fluent`](samples/HultUI.Samples.Fluent/) -- the Windows 11 / WinUI look, built from HultUI primitives
- [`HultUI.Samples.VideoPlayer`](samples/HultUI.Samples.VideoPlayer/) -- open a video file (dialog, drop, or argument) and play it through libmpv, with the overlay that appears on pointer movement and fades
- [`HultUI.Samples.ProceduralVideo`](samples/HultUI.Samples.ProceduralVideo/) -- video from an algorithm: plasma, a Julia set, Life, computed per frame from the playhead's time, no decoder; a slider sets the delivery pace from 1 to 250 fps

## Requirements

- .NET 10.0+
- OpenGL 3.3+ capable GPU
- Linux (X11 or Wayland), Windows, or macOS

## License

MIT
