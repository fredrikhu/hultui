# Plan 02: Fluent BorderWidth Extension

**Priority:** High | **Effort:** Tiny | **Status:** Done

## Problem

`BoxElement` supports `Box.BorderWidth` (used in rendering), and `BorderColor` has a
fluent extension, but `BorderWidth` does not. Users must break the chain:

```csharp
var box = Box.New().BorderColor(Colors.Gray);
box.Box.BorderWidth = new Thickness(1);
```

## Goal

Enable:
```csharp
Box.New().BorderColor(Colors.Gray).BorderWidth(1f)
```

## Steps

1. Add to `ElementExtensions.cs` in the `// ──── Box visual properties ────` section,
   next to `BorderColor`:
   - `BorderWidth<T>(this T el, Thickness width) where T : BoxElement`
   - `BorderWidth<T>(this T el, float uniform) where T : BoxElement`
2. Verify build succeeds.

## Files

- `src/HultUI.Core/Builders/ElementExtensions.cs`

## Notes

- Matches the `Padding`/`Margin` pattern of having both a `Thickness` and `float` overload.
- Constraint is `BoxElement`, not `Element`, since `BorderWidth` is meaningful only when
  combined with `BorderColor` rendering on `BoxElement`.
