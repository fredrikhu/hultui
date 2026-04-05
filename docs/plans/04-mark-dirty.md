# Plan 04: MarkDirty() on Component

**Priority:** High | **Effort:** Small | **Status:** Done

## Problem

When a component needs to rebuild due to non-reactive state changes (e.g., toggling a
plain `bool IsExpanded`), there's no way to trigger a rebuild. Users resort to a hack:

```csharp
[Reactive] int RefreshTrigger { get; set; }

void Toggle() {
    _expanded = !_expanded;
    RefreshTrigger++;  // force rebuild
}
```

## Goal

```csharp
void Toggle() {
    _expanded = !_expanded;
    MarkDirty();  // clean escape hatch
}
```

## Analysis Steps

1. Read `ComponentHost.Rebuild()` and `ComponentHost.ForceRebuild()` to understand the
   rebuild pathway. Confirm calling `Rebuild()` outside an Effect tracking scope is safe.
2. Check whether the Effect system coalesces multiple rebuilds per frame (so calling
   `MarkDirty()` twice doesn't cause two rebuilds).
3. Verify that `Host` is null before mount, making pre-mount calls naturally safe as no-ops.

## Design Decision — Ask for Feedback

The feedback file suggested both `MarkDirty()` and `Rebuild()`. These imply different
semantics:

| Name | Implies | Behavior |
|------|---------|----------|
| `MarkDirty()` | "Schedule a rebuild when convenient" | Could batch with other dirty marks |
| `Rebuild()` | "Do it now, synchronously" | Immediate, no batching |

**Recommendation:** `MarkDirty()` — it's more honest about what happens (the framework
decides when to actually rebuild), and it leaves room for future batching optimizations.

Ask user which name they prefer before implementing.

## Implementation Steps

1. On `Component`, add:
   ```csharp
   protected void MarkDirty() => Host?.ForceRebuild();
   ```
2. If `ForceRebuild()` is `internal` on `ComponentHost` (it currently is), this works
   because `Component` is in the same assembly.
3. Verify build succeeds.

## Files

- `src/HultUI.Core/Components/Component.cs`
- `src/HultUI.Core/Components/ComponentHost.cs` (read-only, unless changes needed)
