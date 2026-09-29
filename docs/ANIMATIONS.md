# Animations

HultUI includes a tween-based animation system for smoothly transitioning element properties over time. Animations are created with the `Animate` factory, configured with a fluent builder, and managed centrally so they tick automatically every frame.

---

## Quick Example

```csharp
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;

var box = Box.New().Width(Dimension.Px(0)).Height(60).Background(new Color(99, 102, 241));

Animate.Width(box, Dimension.Px(0), Dimension.Px(300))
    .Duration(0.6f)
    .Ease(Easing.EaseOutBack)
    .Start();
```

The box expands from 0 to 300px over 0.6 seconds with an overshoot easing.

---

## Creating Animations

All animations start with a static method on `Animate` and end with `.Start()`:

```csharp
Animate.Property(target, from, to)
    .Duration(seconds)    // default: 0.3s
    .Delay(seconds)       // default: 0s
    .Ease(easing)         // default: Easing.EaseOutCubic
    .OnComplete(callback) // optional
    .Start();             // returns TweenHandle
```

### Layout Properties

These mark the element's layout dirty each frame, triggering relayout:

```csharp
Animate.Width(element, Dimension.Px(0), Dimension.Px(300))
Animate.Height(element, Dimension.Px(40), Dimension.Px(200))
Animate.Padding(element, Thickness.Zero, new Thickness(16))
Animate.Margin(element, Thickness.Zero, new Thickness(8))
Animate.Gap(element, 0f, 16f)
```

### Visual Properties

These mark the element's paint dirty each frame, triggering repaint without relayout:

```csharp
Animate.Opacity(element, 0f, 1f)
Animate.Background(boxElement, Color.White, Color.Black)
Animate.BorderColor(boxElement, oldColor, newColor)
Animate.CornerRadius(boxElement, new CornerRadius(0f), new CornerRadius(16f))
```

### Generic Value

Animate any value with a custom setter:

```csharp
// Float
Animate.Value(0f, 100f, v => myLabel.Content = $"{v:F0}%").Start();

// Color
Animate.Value(Color.Red, Color.Blue, c => myBox.BackgroundColor = c).Start();

// Custom type with custom lerp
Animate.Value(startVal, endVal, MyType.Lerp, v => Apply(v)).Start();
```

---

## Easing Functions

The `Easing` class provides standard easing curves:

| Function | Description |
|----------|-------------|
| `Easing.Linear` | Constant speed |
| `Easing.EaseInQuad` | Accelerate (quadratic) |
| `Easing.EaseOutQuad` | Decelerate (quadratic) |
| `Easing.EaseInOutQuad` | Accelerate then decelerate |
| `Easing.EaseInCubic` | Accelerate (cubic) |
| `Easing.EaseOutCubic` | Decelerate (cubic) -- **default** |
| `Easing.EaseInOutCubic` | Accelerate then decelerate (cubic) |
| `Easing.EaseInExpo` | Exponential acceleration |
| `Easing.EaseOutExpo` | Exponential deceleration |
| `Easing.EaseOutBack` | Overshoot then settle |
| `Easing.EaseOutElastic` | Elastic oscillation |

Custom CSS-style cubic bezier:

```csharp
var custom = Easing.CubicBezier(0.25f, 0.1f, 0.25f, 1.0f);
Animate.Opacity(box, 0f, 1f).Ease(custom).Start();
```

---

## Cancellation

`.Start()` returns a `TweenHandle` you can use to cancel an animation mid-flight:

```csharp
TweenHandle handle = Animate.Opacity(box, 0f, 1f).Duration(1f).Start();

// Later...
handle.Cancel();
```

This is especially useful for hover animations where enter and leave can interrupt each other:

```csharp
TweenHandle hoverAnim = default;

box.On<PointerEnterEvent>(_ =>
{
    hoverAnim.Cancel();
    hoverAnim = Animate.Background(box, normal, hover).Duration(0.15f).Start();
});

box.On<PointerLeaveEvent>(_ =>
{
    hoverAnim.Cancel();
    hoverAnim = Animate.Background(box, hover, normal).Duration(0.25f).Start();
});
```

---

## Repeating and Looping

```csharp
Animate.Value(0f, 1f, v => { _phase = v; MarkPaintDirty(); })
    .Duration(1.2f)
    .Ease(Easing.Linear)
    .Loop()          // forever; shorthand for Repeat(0)
    .Start();

Animate.Opacity(dot, 0.3f, 1f).Duration(0.5f).Repeat(3).Yoyo().Start();
```

| Builder | Meaning |
|---|---|
| `.Repeat(int times)` | run the tween `times` times over; zero or less means forever |
| `.Loop()` | `Repeat(0)` |
| `.Yoyo(bool = true)` | every second cycle runs backwards, so a repeat eases to and fro instead of snapping back to the start; only meaningful with a repeat |

The delay applies once, before the first cycle. The cycle index and the phase within it
are **derived from elapsed time, never accumulated per cycle**: a frame almost never
lands on a cycle boundary, and a restart that dropped the overshoot would make every
cycle a frame longer than the last — a spinner built that way visibly slows down and
drifts out of step. A finished repeat holds its end value; a running loop never renders
the terminal frame at all.

### A looping tween outlives anything that forgets it

`AnimationManager` is **process-global**: one static list, ticked once per frame by the
application, with no notion of which element or window a tween belongs to. A loop keeps
ticking after the element that started it has been replaced by a rebuild, removed from
the tree, or dropped with its whole root by `SetRoot`, and its callback keeps writing
into a corpse. Two rules for an element that drives its own animation:

1. **Start it from `Render`, not from the constructor or `OnMounted`.** `OnMounted`
   means "got a parent", which the freshly built twin of a rebuild also gets — and the
   twin is thrown away by reconciliation a moment later. Only what is painted is on
   screen.
2. **Check `IsHosted` on every tick and cancel when it goes false.** `Element.IsHosted`
   walks to the root and asks whether a window is showing it; a twin's root has no host,
   and a swapped-out root or removed subtree has had its host cleared. Removal runs
   `OnUnmounted` on every element in the removed subtree, but neither the twin nor a root
   dropped by `SetRoot` is ever unmounted, so the tick is the only notice those get.

This is what the framework's own `SpinnerElement` and indeterminate `ProgressBarElement`
do:

```csharp
private TweenHandle _tween;   // default(TweenHandle) is safe to Cancel()

public override void Render(IRenderContext ctx)
{
    Start();
    // ...
}

private void Start()
{
    if (_spinning) return;
    _spinning = true;
    _tween = Animate.Value(0f, 1f, v =>
        {
            if (!IsHosted) { Stop(); return; }
            _phase = v;
            MarkPaintDirty();
        })
        .Duration(PeriodSeconds).Ease(Easing.Linear).Loop().Start();
}

private void Stop() { _tween.Cancel(); _spinning = false; }

protected override void OnUnmounted() => Stop();
```

**In tests, cancel every loop you start.** The manager is shared and `Reset` is internal;
a headless session resets it on dispose, but a plain unit test that starts a `Loop()` and
returns leaves it ticking through every test that follows. Hold the handle and cancel
it. A tween that stands in for a timer should count steps off the tween's own elapsed
value rather than detecting wrap-around, because a coarse headless pump can skip a
whole cycle between ticks.

`Animate.CancelAll(element)` exists but is a placeholder that does nothing; do not rely
on it.

---

## Chaining Animations

Use `OnComplete` to sequence animations:

```csharp
Animate.Width(box, Dimension.Px(60), Dimension.Px(300))
    .Duration(0.4f)
    .OnComplete(() =>
    {
        Animate.Background(box, oldColor, newColor)
            .Duration(0.3f)
            .Start();
    })
    .Start();
```

---

## Staggered Animations

Use `Delay` to stagger multiple elements:

```csharp
for (int i = 0; i < items.Length; i++)
{
    Animate.Opacity(items[i], 0f, 1f)
        .Duration(0.4f)
        .Delay(i * 0.08f)
        .Start();
}
```

---

## Animations in Components

When animating elements created in `Build()`, remember that the tree differ patches existing elements rather than replacing them. To force a fresh element (so the animation runs on a new instance), include a changing value in the element's key:

```csharp
[Reactive] public partial int TriggerCount { get; set; }

public override Element Build()
{
    var gen = TriggerCount;
    var box = Box.New().Width(0).Height(60);
    box.Key = $"expandBox-{gen}";
    Animate.Width(box, Dimension.Px(0), Dimension.Px(300)).Duration(0.5f).Start();
    return box;
}
```

Without the key suffix, the differ patches the existing box and the animation targets a discarded element. See [Reactivity Guide -- Keys and Animations](REACTIVITY.md#keys-and-animations).

---

## Key Imports

| Namespace | What it provides |
|-----------|-----------------|
| `HultUI.Core.Animation` | `Animate`, `Easing`, `TweenBuilder<T>`, `TweenHandle` |
