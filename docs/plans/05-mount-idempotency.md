# Plan 05: Mount() Idempotency

**Priority:** Medium | **Effort:** Small | **Status:** Done

## Problem

`Component.Mount()` creates a new `ComponentHost` every call. It's ambiguous whether
users should:
- Call `Mount()` once in the constructor and cache the result, or
- Call `Mount()` each time in `Build()` (which creates a new host each time, forcing
  the tree differ to reconcile it)

## Goal

Make `Mount()` safe to call repeatedly — return the same `ComponentHost` on subsequent
calls.

## Analysis Steps

1. Read how `ComponentHost` lifecycle works: what happens on unmount? Is the Effect
   disposed? Can a disposed host be re-used?
2. Search codebase for existing `Mount()` usage patterns to understand what users expect.
3. Check if `ComponentHost` can be re-parented (moved from one parent to another) or if
   that violates assumptions.

## Design Decision — Ask for Feedback

Two options:

**Option A: Cache forever**
```csharp
private ComponentHost? _host;
public ComponentHost Mount() => _host ??= new(this);
```
- Simple. Once created, always returns the same host.
- Risk: if the host is unmounted (Effect disposed), returning the stale host could break.

**Option B: Cache with unmount reset**
```csharp
private ComponentHost? _host;
public ComponentHost Mount()
{
    if (_host is null || _host.IsDisposed)
        _host = new(this);
    return _host;
}
```
- Safer for unmount/re-mount cycles.
- Requires an `IsDisposed` flag on `ComponentHost` (or clear `_host` from the unmount
  callback).

**Option C: Clear on unmount via callback**
```csharp
private ComponentHost? _host;
public ComponentHost Mount() => _host ??= new(this);

protected internal override void OnUnmount()
{
    _host = null;
}
```
- Clean: the cached host is cleared when the component unmounts, so re-mounting creates
  a fresh one.
- No extra flag needed.

**Recommendation:** Option C — simplest, correct lifecycle, no extra state.

Ask user which option they prefer.

## Implementation Steps

1. Add `private ComponentHost? _host` field to `Component`.
2. Change `Mount()` to `=> _host ??= new(this)`.
3. In `OnUnmount()`, set `_host = null`.
4. Verify build succeeds.

## Files

- `src/HultUI.Core/Components/Component.cs`
