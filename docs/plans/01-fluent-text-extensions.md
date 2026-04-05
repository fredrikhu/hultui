# Plan 01: Fluent TextElement Extensions (Overflow, MaxLines)

**Priority:** High | **Effort:** Tiny | **Status:** Done

## Problem

`TextElement` has `Overflow` and `MaxLines` properties but no fluent extension methods,
forcing users to break out of the builder chain:

```csharp
var text = Text.New("hello").FontSize(13f);
text.Overflow = TextOverflow.Ellipsis;
text.MaxLines = 1;
```

## Goal

Enable:
```csharp
Text.New("hello").FontSize(13f).TextOverflow(TextOverflow.Ellipsis).MaxLines(1)
```

## Steps

1. Add to `ElementExtensions.cs` in a new `// ──── Text properties ────` section:
   - `TextOverflow<T>(this T el, TextOverflow overflow) where T : TextElement`
   - `MaxLines<T>(this T el, int lines) where T : TextElement`
2. Verify build succeeds.

## Files

- `src/HultUI.Core/Builders/ElementExtensions.cs`

## Notes

- Method named `TextOverflow` rather than `Overflow` to avoid collision with the box
  overflow extensions (Plan 03) and with the `TextOverflow` enum type.
- Follows the exact same one-liner pattern as every other fluent extension.
