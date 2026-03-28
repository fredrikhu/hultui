# HultUI Reactivity System and Component Model

## Overview

HultUI uses **fine-grained reactivity** inspired by SolidJS. The system is built on three primitives:

- **Signal\<T\>** -- a mutable reactive value
- **Computed\<T\>** -- a derived reactive value (read-only, lazily recomputed)
- **Effect** -- a side effect that re-runs when its dependencies change

When a signal changes, only the effects that actually read that signal re-run. There is no virtual DOM diff of the entire tree and no broad invalidation pass. Dependencies are tracked automatically at read time.

---

## Signal\<T\>

A `Signal<T>` holds a single mutable value. It is the foundational building block of the reactivity system.

```csharp
using HultUI.Core.Reactivity;

var count = new Signal<int>(0);

count.Value = 5;           // set -- notifies subscribers
int x = count.Value;       // get -- tracks the read in the current reactive scope
int y = count.Peek();      // get -- reads without tracking (no dependency created)
```

**Key behavior:**

- **`.Value` (get)** -- returns the current value and registers a dependency in whichever reactive scope is active (an Effect or Computed). Outside a reactive scope, it simply returns the value.
- **`.Value` (set)** -- updates the stored value. If the new value is equal to the old value (using the default equality comparer), no notification is sent.
- **`.Peek()`** -- returns the current value without creating a dependency. Useful when you need to read a signal inside an Effect without re-triggering that effect when the signal changes.

---

## Computed\<T\>

A `Computed<T>` represents a derived value that is lazily recomputed when any of its dependencies change.

```csharp
using HultUI.Core.Reactivity;

var count = new Signal<int>(3);
var doubled = new Computed<int>(() => count.Value * 2);

Console.WriteLine(doubled.Value); // 6

count.Value = 10;
Console.WriteLine(doubled.Value); // 20
```

**Key behavior:**

- **`.Value`** -- reads the cached result. If any upstream signal has changed since the last computation, the factory function re-runs first. The read is tracked, so a Computed can be a dependency of an Effect or another Computed.
- **`.Peek()`** -- reads the cached value without tracking and without forcing recomputation.
- The factory function passed to the constructor runs inside a tracking scope. Any signals or other computeds read during that function become dependencies automatically.
- Recomputation is **lazy**: it only happens when `.Value` is read after a dependency has changed, not immediately when the dependency changes.

---

## Effect

An `Effect` runs a side-effect function immediately and then re-runs it whenever any of its tracked dependencies change.

```csharp
using HultUI.Core.Reactivity;

var count = new Signal<int>(0);

var effect = new Effect(() =>
{
    Console.WriteLine($"Count is {count.Value}");
});
// Prints "Count is 0" immediately

count.Value = 1;
// Prints "Count is 1"

count.Value = 2;
// Prints "Count is 2"

effect.Dispose(); // stop watching -- the effect will not run again
```

**Key behavior:**

- The callback runs **immediately** on construction. All signal/computed reads inside the callback are tracked.
- When any tracked dependency changes, the callback is scheduled to re-run.
- Call `.Dispose()` to tear down the effect and stop all future executions.
- Dependencies are re-captured on every run. If a conditional branch causes a signal to not be read in a particular run, that signal is no longer a dependency until it is read again.

---

## ReactiveRuntime

`ReactiveRuntime` provides utilities for controlling when effects execute.

### Batching

When you update multiple signals at once, each update would normally trigger its own round of effect re-runs. `Batch` defers all effect execution until the batch completes, so effects only run once with the final state.

```csharp
using HultUI.Core.Reactivity;

var firstName = new Signal<string>("Alice");
var lastName = new Signal<string>("Smith");

var effect = new Effect(() =>
{
    Console.WriteLine($"Name: {firstName.Value} {lastName.Value}");
});
// Prints "Name: Alice Smith"

ReactiveRuntime.Batch(() =>
{
    firstName.Value = "Bob";
    lastName.Value = "Jones";
});
// Prints "Name: Bob Jones" -- only once, not twice
```

Without the batch, the effect would fire after `firstName` is set (printing "Bob Smith") and again after `lastName` is set (printing "Bob Jones"). With the batch, only the final result triggers the effect.

---

## Component Model

HultUI components are class-based. You extend `Component` and override the `Build()` method to return an element tree.

### Basics

```csharp
using HultUI.Core.Components;
using HultUI.Core.Elements;

class Greeting : Component
{
    private readonly Signal<string> _name = new("World");

    public override Element Build()
    {
        return Text.New($"Hello, {_name.Value}!");
    }
}

// Mount it into a layout:
var root = Flex.Column().Children(
    new Greeting().Mount()
);
```

### How it works

1. **`Build()` runs inside an Effect.** Every signal or computed read during `Build()` is tracked as a dependency.
2. **When a dependency changes, `Build()` re-runs automatically.** The component produces a new element tree.
3. **The new tree is diffed against the old tree** using `TreeDiffer`. Existing elements are patched in-place rather than replaced wholesale. This preserves element identity, focus state, scroll position, and other transient state.
4. **`.Mount()`** returns a `ComponentHost` element that can be placed in any element tree. The `ComponentHost` manages the component's lifecycle and holds its rendered output.

### Lifecycle hooks

Components expose lifecycle hooks that you can override:

| Method | When it runs |
|---|---|
| `OnMount()` | After the component is first mounted into the tree |
| `OnUnmount()` | When the component is removed from the tree |
| `OnBeforeBuild()` | Immediately before each `Build()` call |
| `OnAfterBuild()` | Immediately after each `Build()` call |

---

## [Reactive] Source Generator

The `[Reactive]` attribute eliminates boilerplate when declaring reactive properties on components.

### Without the generator

```csharp
class Counter : Component
{
    private readonly Signal<int> _count = new(0);

    public int Count
    {
        get => _count.Value;
        set => _count.Value = value;
    }

    public override Element Build() { /* ... */ }
}
```

### With the generator

```csharp
using HultUI.Core.Reactivity;

partial class Counter : Component
{
    [Reactive] public partial int Count { get; set; }

    public override Element Build()
    {
        var count = Count; // reads Signal<int> -- tracked by the Build effect

        return Flex.Row(gap: 12).AlignItems(AlignItems.Center).Children(
            Box.New().Padding(12).Background(Color.FromHex("#4F46E5")).CornerRadius(8f)
                .TextColor(Color.White).Cursor(CursorType.Pointer)
                .OnClick(_ => Count++)
                .Children(Text.New("+")),
            Text.New($"Count: {count}")
        );
    }
}
```

The source generator creates a `Signal<T>` backing field and wires the property getter/setter through it. Reading the property inside `Build()` creates a reactive dependency just as if you read `.Value` on a signal directly.

### Requirements

- The class must be `partial`.
- The property must be `partial` (C# 13+).
- The project must reference the SourceGenerators project as an analyzer.

---

## Tree Diffing

When a component rebuilds, `TreeDiffer` reconciles the new element tree against the previous one.

### Reconciliation rules

1. **Same type at the same position** -- the existing element is patched in-place. Its properties are updated to match the new tree, but the element instance is preserved. This keeps focus, animations, and other transient state intact.
2. **Different type at the same position** -- the old element is removed and the new element is inserted.
3. **Keyed children** -- use `.Key` on elements to provide stable identity. Keyed elements are matched by key across reorders, insertions, and deletions.
4. **Unkeyed children** -- matched by index. This is simpler but can cause unnecessary patches or state loss if the list order changes.

### When to use keys

Use `.Key` when rendering a dynamic list where items can be added, removed, or reordered:

```csharp
public override Element Build()
{
    return Flex.Column().Children(
        _items.Value.Select(item =>
            Text.New(item.Name).Key(item.Id)
        )
    );
}
```

Without keys, inserting an item at the beginning of the list would cause every element to be patched with the next item's data instead of shifting elements down.

---

## Direct Mutation

For performance-critical paths, you can skip the component rebuild cycle entirely and mutate elements directly.

```csharp
var label = Text.New("Hello");

// Later, update it directly:
label.Content = "Updated";  // marks paint dirty, no rebuild needed
```

Direct mutation and reactive components can coexist in the same application. Use components for sections of UI with complex state dependencies, and direct mutation for hot paths where you want precise control over what gets updated.

---

## Key Imports

| Namespace | What it provides |
|---|---|
| `HultUI.Core.Reactivity` | `Signal<T>`, `Computed<T>`, `Effect`, `ReactiveRuntime`, `[Reactive]` |
| `HultUI.Core.Components` | `Component`, `ComponentHost` |
| `HultUI.Core.Builders` | `Flex`, `Box`, `Text`, and other element builders |
