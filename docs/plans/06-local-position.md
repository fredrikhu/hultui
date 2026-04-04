# Plan 06: LocalPosition on Pointer Events

**Priority:** Medium | **Effort:** Medium | **Status:** Pending

## Problem

Pointer event positions are window-relative. For drag interactions, split panes, or
custom drawing, users need element-relative coordinates. Currently requires manual math:

```csharp
var contentBox = element.Layout.AbsoluteContentBox;
var localX = e.Position.X - contentBox.X;
var localY = e.Position.Y - contentBox.Y;
```

## Goal

```csharp
var local = e.LocalPosition;  // already relative to the receiving element
```

## Analysis Steps (must complete before implementation)

1. **Find the event dispatch pipeline.** Search for where pointer events are created,
   where the target element is resolved, and where handlers are invoked. Key search
   terms: `PointerDownEvent`, `Dispatch`, `HitTest`, `EventDispatcher`.
2. **Identify the dispatch point** where both the event and the target element are known.
   That's where `LocalPosition` should be computed.
3. **Check event reuse:** Is the same event instance dispatched to multiple elements
   (e.g., bubbling)? If so, `LocalPosition` must be recomputed per element, which
   rules out a simple `init` property.
4. **Check `ScrollEvent`:** It has its own `Position` property — should it also get
   `LocalPosition`?

## Design Decision — Ask for Feedback

Three options:

**Option A: Computed at dispatch, stored on event**
```csharp
public Point LocalPosition { get; init; }
```
- Simplest consumer API.
- Requires the dispatch system to set it per target element.
- If events bubble, a new event instance (or internal setter) is needed per element.

**Option B: Method on event**
```csharp
public Point GetLocalPosition(Element element)
    => new(Position.X - element.Layout.AbsoluteContentBox.X,
           Position.Y - element.Layout.AbsoluteContentBox.Y);
```
- No dispatch changes needed.
- Slightly less ergonomic but more flexible.
- Works correctly with bubbling — caller passes their own element.

**Option C: Internal setter, set during dispatch**
```csharp
public Point LocalPosition { get; internal set; }
```
- Clean consumer API.
- Dispatch pipeline updates it as the event traverses elements.
- Mutable event — may violate immutability expectations.

**Recommendation:** Option B is safest and simplest to implement with zero dispatch
changes. Option A is nicest to use but requires dispatch pipeline work. Present both
and ask.

## Implementation Steps (after design decision)

1. Add chosen API to `PointerEvent` base class.
2. If Option A or C: update event dispatch to compute local position.
3. Consider adding to `ScrollEvent` as well.
4. Verify build succeeds.

## Files

- `src/HultUI.Core/Events/PointerEvents.cs`
- Event dispatch files (identified during analysis)
