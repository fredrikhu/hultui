# Getting Started with HultUI

Build your first HultUI desktop application from scratch.

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later
- A GPU with OpenGL 3.3+ support
- Linux (X11 or Wayland), Windows, or macOS

## Create a New Project

```bash
mkdir MyApp && cd MyApp
dotnet new console
```

Edit `MyApp.csproj` to reference HultUI and the source generator:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\HultUI\HultUI.csproj" />
    <ProjectReference Include="..\..\src\HultUI.SourceGenerators\HultUI.SourceGenerators.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

Adjust the `Include` paths to match your directory structure relative to the HultUI source.

## Minimal App

Replace `Program.cs` with:

```csharp
using HultUI;
using HultUI.Core.Builders;

var root = Text.New("Hello, HultUI!");

using var app = new Application("My App", 800, 600);
app.SetRoot(root);
app.Run();
```

Run it:

```bash
dotnet run
```

A window opens with the text "Hello, HultUI!" rendered on screen. That is the entire program -- three lines of setup.

## Understanding the Tree

Everything in HultUI is an **Element**. Elements form a tree. You pass the root of that tree to the `Application`, which renders it into a window.

```
Application
  └── root Element
        ├── child Element
        │     └── ...
        └── child Element
```

There is no XAML, no markup language. You build the tree directly in C# using the builder API:

- `Box.New()` -- a container (like `<div>`)
- `Text.New("...")` -- text content (like a text node)
- `Flex.Row()` / `Flex.Column()` -- a container with flex direction pre-set

Every builder returns an element. Fluent methods chain off of it to set properties. `.Children(...)` attaches child elements.

## Adding Layout

HultUI uses CSS Flexbox semantics. `Flex.Row()` lays out children horizontally. `Flex.Column()` lays them out vertically. The `gap` parameter adds spacing between children.

```csharp
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;

var root = Flex.Column(gap: 16).Padding(24).Children(
    Text.New("Header").FontSize(24f),

    Flex.Row(gap: 12).Children(
        Box.New().Width(100).Height(100).Background(Color.FromHex("#4F46E5")),
        Box.New().Width(100).Height(100).Background(Color.FromHex("#7C3AED")),
        Box.New().Width(100).Height(100).Background(Color.FromHex("#06B6D4"))
    ),

    Box.New().FlexGrow(1).Background(Color.FromHex("#F1F5F9"))
);

using var app = new Application("Layout Demo", 800, 600);
app.SetRoot(root);
app.Run();
```

Key layout concepts:

| Method | Effect |
|--------|--------|
| `Flex.Row(gap: 8)` | Horizontal container with 8px gap between children |
| `Flex.Column(gap: 16)` | Vertical container with 16px gap between children |
| `.Padding(24)` | 24px padding on all sides |
| `.Padding(new Thickness(top, right, bottom, left))` | Per-side padding |
| `.Width(100)` / `.Height(50)` | Fixed dimensions |
| `.FlexGrow(1)` | Expand to fill remaining space |
| `.AlignItems(AlignItems.Center)` | Center children on the cross axis |

`AlignItems` and other layout enums live in `HultUI.Core.Layout`.

## Styling

Style any element with fluent methods. No separate stylesheet, no cascade -- the style is right there on the element.

```csharp
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

var root = Flex.Column(gap: 16).Padding(24).Children(
    Text.New("Bold Title")
        .FontSize(28f)
        .FontWeight(FontWeight.Bold)
        .TextColor(Color.FromHex("#1E293B")),

    Box.New()
        .Padding(16)
        .Background(Color.FromHex("#4F46E5"))
        .CornerRadius(8f)
        .TextColor(Color.White)
        .Cursor(CursorType.Pointer)
        .Children(Text.New("Styled Button"))
);

using var app = new Application("Styling Demo", 800, 600);
app.SetRoot(root);
app.Run();
```

Common style methods:

| Method | What it does |
|--------|--------------|
| `.FontSize(16f)` | Set font size in pixels |
| `.FontWeight(FontWeight.Bold)` | Set font weight |
| `.TextColor(Color.White)` | Set text color |
| `.Background(Color.FromHex("#hex"))` | Set background color |
| `.CornerRadius(8f)` | Round corners |
| `.Cursor(CursorType.Pointer)` | Change cursor on hover |

Text properties like `FontSize`, `FontWeight`, and `TextColor` are inherited by child elements, so setting them on a container applies to all text within it.

## Handling Events

Attach event handlers with `.OnClick()`, `.OnPointerEnter()`, and `.OnPointerLeave()`.

```csharp
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

var baseColor = Color.FromHex("#4F46E5");
var hoverColor = Color.FromHex("#3730A3");

var button = Box.New()
    .Padding(12)
    .Background(baseColor)
    .CornerRadius(8f)
    .TextColor(Color.White)
    .FontWeight(FontWeight.Bold)
    .Cursor(CursorType.Pointer)
    .Children(Text.New("Hover me"));

button.OnPointerEnter(_ => button.BackgroundColor = hoverColor);
button.OnPointerLeave(_ => button.BackgroundColor = baseColor);
button.OnClick(_ => Console.WriteLine("Clicked!"));

var root = Flex.Column(gap: 16).Padding(24).Children(button);

using var app = new Application("Events Demo", 800, 600);
app.SetRoot(root);
app.Run();
```

When you mutate an element's property (like `BackgroundColor`), the framework automatically marks it dirty and re-renders only what changed.

## Theme Switching

HultUI includes built-in light and dark themes. Set the theme on the `Application` instance:

```csharp
app.Theme = Theme.Dark;   // switch to dark
app.Theme = Theme.Light;  // switch back to light
```

The `Theme` class lives in `HultUI.Core.Styling`. Theme switching is immediate -- the window re-renders on the next frame.

Here is a full example with a toggle button:

```csharp
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

var isDark = false;

var label = Text.New("Dark Mode");
var toggle = Box.New()
    .Padding(12)
    .Background(Color.FromHex("#F59E0B"))
    .CornerRadius(8f)
    .TextColor(Color.White)
    .FontWeight(FontWeight.Bold)
    .Cursor(CursorType.Pointer)
    .Children(label);

var root = Flex.Column(gap: 16).Padding(24).Children(
    Text.New("Theme Demo").FontSize(24f),
    toggle
);

using var app = new Application("Theme Demo", 800, 600);
app.SetRoot(root);

toggle.OnClick(_ =>
{
    isDark = !isDark;
    app.Theme = isDark ? Theme.Dark : Theme.Light;
    label.Content = isDark ? "Light Mode" : "Dark Mode";
});

app.Run();
```

## DevTools

Press **F12** at any time while your app is running to toggle the DevTools overlay. It shows:

- Bounding boxes on every element
- A hover inspector displaying element type, name, and dimensions
- A performance HUD with FPS, style/layout/render/present timing, and element count

This is built into every HultUI application. No extra setup required.

## Next Steps

- [Layout Guide](LAYOUT.md) -- flexbox layout in depth: grow, shrink, wrap, alignment, nesting
- [Elements Reference](ELEMENTS.md) -- all built-in elements and their properties
- [Reactivity Guide](REACTIVITY.md) -- signals, computed values, effects, and the Component model
