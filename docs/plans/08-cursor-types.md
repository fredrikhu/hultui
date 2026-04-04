# Plan 08: ColResize / RowResize Cursor Types

**Priority:** Low | **Effort:** Tiny | **Status:** Pending

## Problem

`CursorType` enum lacks `ColResize` and `RowResize`, which are standard CSS cursor values
used for split pane drag handles. Users must use `ResizeEW` / `ResizeNS` as substitutes.

## Analysis Steps

1. **Find platform cursor mappings.** Search for where `CursorType` enum values are
   translated to platform-specific cursors (SDL, X11, Wayland, etc.). The new enum
   values need mappings there.
2. **Check platform support.** On many platforms, `col-resize` and `ew-resize` map to the
   same underlying cursor. Determine if any platform actually has distinct cursors for
   these, or if they'll just map to the same value as `ResizeEW`/`ResizeNS`.

## Implementation Steps

1. Add to `CursorType` enum in `InheritedStyle.cs`:
   ```csharp
   ColResize,
   RowResize,
   ```
2. Update every platform backend cursor mapping to handle the new values.
   - If the platform has distinct cursors, use them.
   - If not, map to the same cursor as `ResizeEW` / `ResizeNS`.
3. Verify build succeeds.

## Files

- `src/HultUI.Core/Styling/InheritedStyle.cs`
- Platform backend files (identified during analysis)

## Notes

- Even if they map to the same cursor on current platforms, having semantic names is
  valuable. A split pane should say `ColResize`, not `ResizeEW` — the intent is different
  even if the visual is the same today.
