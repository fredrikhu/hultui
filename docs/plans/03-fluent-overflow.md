# Plan 03: Fluent Overflow Extensions (Box Clipping)

**Priority:** High | **Effort:** Tiny | **Status:** Pending

## Problem

Clipping children requires direct property access on `Box`:

```csharp
element.Box.OverflowX = Overflow.Hidden;
element.Box.OverflowY = Overflow.Hidden;
```

No fluent extension exists, unlike most other Box properties.

## Goal

Enable:
```csharp
Box.New().Overflow(Overflow.Hidden)
// or individual axes:
Box.New().OverflowX(Overflow.Scroll).OverflowY(Overflow.Hidden)
```

## Steps

1. **Naming analysis:** The method name `Overflow` collides with the `Layout.Overflow`
   enum type used as the parameter type. In C#, a method and a type can share a name in
   certain contexts, but it creates ambiguity. Need to verify whether
   `Overflow<T>(this T el, Overflow overflow)` compiles, or if we need a different name
   like `ClipOverflow` or `OverflowMode`.
   - If collision: **ask for feedback** on preferred alternative name.
2. Add to `ElementExtensions.cs` in a new `// ──── Overflow ────` section:
   - Combined: sets both `OverflowX` and `OverflowY`
   - `OverflowX`: sets `el.Box.OverflowX`
   - `OverflowY`: sets `el.Box.OverflowY`
3. Constraint is `Element` (not `BoxElement`) since `Box.OverflowX/Y` lives on `BoxStyle`
   which is available on all elements.
4. Verify build succeeds.

## Files

- `src/HultUI.Core/Builders/ElementExtensions.cs`

## Decision Point

If `Overflow` as a method name causes a compile error, options are:
- `ClipOverflow` — descriptive, no collision
- `OverflowMode` — matches the feedback file's suggestion for TextOverflow
- Just fully qualify the enum: `Layout.Overflow` in the parameter type

Present options and ask for feedback before proceeding.
