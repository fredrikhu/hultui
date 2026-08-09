# HultUI Framework Feedback

Issues and friction points discovered while building Junction, back when it lived in
`samples/` as the Git client sample.

## 1. Namespace conflict with `Text` builder

**Problem**: Any code in a namespace under `HultUI.*` (e.g. `HultUI.Samples.Whatever`) cannot use `Text.New()` because C# resolves `Text` as the namespace `HultUI.Text` (the text rendering library) before the class `HultUI.Core.Builders.Text`. This is C#'s name resolution — it walks up the namespace hierarchy and finds `HultUI.Text` as a sibling namespace before considering `using` imports.

**Workaround**: Had to set a `RootNamespace` outside the `HultUI.*` hierarchy — `Junction` today. Alternatively requires `global using Text = HultUI.Core.Builders.Text;` aliases.

**Suggestion**: Rename the `HultUI.Text` assembly/namespace to something that doesn't collide with the builder class name. For example `HultUI.TextEngine`, `HultUI.Typography`, or `HultUI.Core.Text`. This is a real pain point for anyone building an app inside the `HultUI.*` namespace tree — which is the natural thing to do for samples.

## 2. No `MarkDirty()` on Component — RESOLVED

**Status**: `Component.MarkDirty()` now exists (`src/HultUI.Core/Components/Component.cs:35`) and forwards to `Host?.ForceRebuild()`. Junction has been swept — `App`, `CommitList`, `StagingArea`, and `Sidebar` no longer carry the `[Reactive] int RefreshTrigger` workaround; their `Refresh()` methods call `MarkDirty()` directly.

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

## 10. Direct style mutation does not invalidate layout — RESOLVED

**Problem**: `FlexStyle`, `BoxStyle`, and `PositionStyle` are plain classes with auto-property setters. Setting `el.Flex.FlexBasis = …` (or any other style property) at runtime does *not* mark the element's layout dirty. The next frame's layout pass sees no reason to re-run, so the change is silently dropped. The fluent extensions (`.Width(…)` etc.) have the same issue, but they're typically called during initial element construction — before the first layout pass — so they "work" by accident because every element starts with `DirtyFlags.All`.

This bit the SplitPane drag handler badly: pointer-down + cursor + hover all worked, but the panes didn't move because the flex basis change never reached the layout engine. Spent meaningful time before realizing the problem wasn't in the event dispatch or coordinate math.

**Resolution**: `Element.MarkLayoutDirty()` and `Element.MarkPaintDirty()` are now `public` (previously `internal`). Imperative code that mutates style properties at runtime — drag handlers, animations, custom resize logic — must call `MarkLayoutDirty()` after the mutation.

**Open question**: Should the style property setters auto-invalidate? That would require each style class to hold a back-pointer to its owning Element, and every property write would walk to the root. Static UI construction would pay a cost it doesn't need. The current approach (explicit invalidation as an escape hatch) seems right; the docs on `MarkLayoutDirty` should make this clear and the SplitPane sample serves as the canonical example.

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

## 9. Component.Mount() creates a new host every call — RESOLVED

**Status**: `Mount()` now returns `Host ?? new ComponentHost(this)` (`src/HultUI.Core/Components/Component.cs:40`) — once a component is hosted, repeated `Mount()` calls return the same host. Calling `Mount()` from inside `Build()` on every rebuild is now the documented-by-behavior pattern.
