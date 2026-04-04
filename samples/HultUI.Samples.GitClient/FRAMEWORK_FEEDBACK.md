# HultUI Framework Feedback

Issues and friction points discovered while building the Git client sample.

## 1. Namespace conflict with `Text` builder

**Problem**: Any code in a namespace under `HultUI.*` (e.g., `HultUI.Samples.GitClient`) cannot use `Text.New()` because C# resolves `Text` as the namespace `HultUI.Text` (the text rendering library) before the class `HultUI.Core.Builders.Text`. This is C#'s name resolution — it walks up the namespace hierarchy and finds `HultUI.Text` as a sibling namespace before considering `using` imports.

**Workaround**: Had to set `<RootNamespace>GitClient</RootNamespace>` in the csproj to break out of the `HultUI.*` hierarchy. Alternatively requires `global using Text = HultUI.Core.Builders.Text;` aliases.

**Suggestion**: Rename the `HultUI.Text` assembly/namespace to something that doesn't collide with the builder class name. For example `HultUI.TextEngine`, `HultUI.Typography`, or `HultUI.Core.Text`. This is a real pain point for anyone building an app inside the `HultUI.*` namespace tree — which is the natural thing to do for samples.

## 2. No `MarkDirty()` on Component

**Problem**: When a component needs to rebuild due to non-reactive state changes (e.g., toggling a plain bool `IsExpanded` on a tree node), there's no way to tell the component to rebuild. `Component` doesn't expose a public `MarkDirty()` method.

**Workaround**: Added a `[Reactive] int Version` or `[Reactive] int RefreshTrigger` counter property, read it in `Build()`, and increment it to force a rebuild. This works but feels like a hack — every component that needs imperative refresh has a meaningless counter.

**Suggestion**: Expose a `protected void Rebuild()` or `protected void MarkDirty()` method on `Component`. The reactive system should still be the primary driver, but sometimes you need an escape hatch for imperative invalidation.

## 3. No fluent methods for TextElement.Overflow and TextElement.MaxLines

**Problem**: `TextElement` has `Overflow` and `MaxLines` properties, but there are no fluent extension methods for them in `ElementExtensions.cs`. This breaks the fluent builder chain:

```csharp
// Have to do this:
var text = Text.New("hello").FontSize(13f);
text.Overflow = TextOverflow.Ellipsis;
text.MaxLines = 1;

// Want to do this:
Text.New("hello").FontSize(13f).OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
```

**Workaround**: Added local extension methods in the project's `GlobalUsings.cs`.

**Suggestion**: Add `OverflowMode` (or `Overflow`) and `MaxLines` fluent extensions to `ElementExtensions.cs`.

## 4. No fluent `BorderWidth` on BoxElement

**Problem**: `Box.BorderWidth` lives on `element.Box.BorderWidth` (the `BoxStyle`), but there's no fluent extension method for it like there is for `BorderColor`.

**Workaround**: Removed the border width from the badge component, or set it directly via `element.Box.BorderWidth = new Thickness(1)`.

**Suggestion**: Add a `BorderWidth<T>(this T el, Thickness width)` and `BorderWidth<T>(this T el, float uniform)` extension in `ElementExtensions.cs`, matching the pattern of `Padding` and `Margin`.

## 5. SplitPane required knowledge of internal layout structure

**Problem**: Building a resizable split pane required accessing `element.Layout.AbsoluteContentBox` to get the container's rendered size during a drag operation. This works, but:
- It wasn't obvious that `Layout` was the property name (initially tried `LayoutResult`)
- The `Layout` property is `LayoutResult` type but the property is named `Layout` — slightly confusing
- Position values from `PointerMoveEvent.Position` are absolute (screen-relative), so I had to subtract `contentBox.X/Y` to get local coordinates

**Suggestion**: Consider adding a `LocalPosition` property to pointer events that gives coordinates relative to the element that received the event. This is a very common need for drag interactions and custom drawing.

## 6. Missing resize cursor types

**Problem**: `CursorType` doesn't have `ColResize` or `RowResize` for split pane drag handles. Had to use `ResizeEW` and `ResizeNS` instead.

**Suggestion**: These are standard CSS cursor values. Consider adding `ColResize` and `RowResize` to `CursorType`, or document that `ResizeEW`/`ResizeNS` are the intended equivalents.

## 7. Reactive property source generator nullability mismatch

**Problem**: The source generator for `[Reactive]` doesn't handle nullable reference types correctly. Declaring `[Reactive] public partial GitCommit? SelectedCommit { get; set; }` produces warnings because the generated backing `Signal<GitCommit>` initializes with `null` but the type parameter isn't nullable.

**Workaround**: Just warnings, not errors — can be ignored. But clutters the build output.

**Suggestion**: The source generator should detect the `?` annotation and generate `Signal<GitCommit?>` with appropriate null handling.

## 8. No way to set overflow to Hidden via fluent API

**Problem**: To make a panel clip its children (needed for split pane panels), you have to write:
```csharp
element.Box.OverflowX = Overflow.Hidden;
element.Box.OverflowY = Overflow.Hidden;
```

There's no fluent extension for this, unlike most other Box properties.

**Suggestion**: Add `Overflow<T>(this T el, Overflow overflow)` that sets both X and Y, and optionally `OverflowX`/`OverflowY` for individual control.

## 9. Component.Mount() creates a new host every call

**Problem**: In `App.Build()`, calling `_sidebar.Mount()` on each rebuild creates a new `ComponentHost`. The tree differ has to reconcile this each time. It wasn't clear whether I should call `.Mount()` once in the constructor and cache the `ComponentHost`, or call it each time in `Build()`.

**Suggestion**: Document the intended pattern. If Mount() should only be called once, make it throw on double-mount. If it's fine to call in Build(), make it return the same host. Currently it's ambiguous.
