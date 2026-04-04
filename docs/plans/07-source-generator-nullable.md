# Plan 07: Source Generator Nullable Fix

**Priority:** Medium | **Effort:** Small | **Status:** Pending

## Problem

Declaring `[Reactive] public partial GitCommit? SelectedCommit { get; set; }` produces
warnings because the generated `Signal<T>` type parameter may not include the `?`
annotation.

## Analysis Steps

1. **Check `ToDisplayString` output.** The generator uses
   `propSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)` at line 58
   of `ReactiveGenerator.cs`. Verify whether this format includes `?` for nullable
   reference types (NRTs). It depends on the `NullableFlowState` and the format's
   `MiscellaneousOptions.IncludeNullableReferenceTypeModifier` flag.
2. **Check current generated output.** Build the Git client sample and inspect the
   generated `.g.cs` file to see what's actually produced for a nullable property.
3. **Determine the fix.** If `FullyQualifiedFormat` omits `?`:
   - Create a custom `SymbolDisplayFormat` that includes
     `IncludeNullableReferenceTypeModifier`, OR
   - Append `?` manually when `IsNullable` is true (already extracted at line 90).

## Implementation Steps

1. If `PropertyType` doesn't include `?` for NRTs:
   ```csharp
   // In GenerateClass, line 153:
   var signalType = prop.IsNullable && !prop.PropertyType.EndsWith("?")
       ? $"{prop.PropertyType}?"
       : prop.PropertyType;
   sb.AppendLine($"    private readonly Signal<{signalType}> {fieldName} = new({defaultExpr});");
   ```
2. Alternatively, fix at extraction time by using a format that includes NRT annotations:
   ```csharp
   var format = SymbolDisplayFormat.FullyQualifiedFormat
       .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
   var typeStr = propSymbol.Type.ToDisplayString(format);
   ```
   This is cleaner — it fixes the type string once and all downstream usage is correct.
3. Verify the generated code compiles without warnings.
4. Build succeeds.

## Files

- `src/HultUI.SourceGenerators/ReactiveGenerator.cs`

## Notes

- `IsNullable` already distinguishes NRTs from value types. For `int?` (Nullable<int>),
  `ToDisplayString` already produces `int?`. The issue is specifically with reference
  types like `GitCommit?` where NRT annotations may be stripped.
- The `FullyQualifiedFormat` default does NOT include
  `IncludeNullableReferenceTypeModifier` — this is very likely the root cause.
