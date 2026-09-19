# Skill: Memory Allocation Traps

<!-- trigger: allocation, hidden, boxing, closure, trap | Finding hidden allocation sources | Performance -->

## Reference Parts

- [Part 1](../references/memory-allocation-traps-part-1.md)
- [Part 2](../references/memory-allocation-traps-part-2.md)
- [Part 3](../references/memory-allocation-traps-part-3.md)

## Quick Reference: Allocation Costs

[Read section](../references/memory-allocation-traps-part-1.md#quick-reference-allocation-costs)

## Trap 1: foreach through an interface

[Read section](../references/memory-allocation-traps-part-1.md#trap-1-foreach-through-an-interface)

## Trap 2: LINQ Methods

[Read section](../references/memory-allocation-traps-part-1.md#trap-2-linq-methods)

### [LINQ Allocation Breakdown](../references/memory-allocation-traps-part-1.md#linq-allocation-breakdown)

## Trap 3: Closures Capturing Variables

[Read section](../references/memory-allocation-traps-part-1.md#trap-3-closures-capturing-variables)

### [Static Lambdas (C# 9+)](../references/memory-allocation-traps-part-1.md#static-lambdas-c-9)

## Trap 4: params Methods

[Read section](../references/memory-allocation-traps-part-1.md#trap-4-params-methods)

### [Common params Traps](../references/memory-allocation-traps-part-1.md#common-params-traps)

## Trap 5: Delegate Assignment in Loops

[Read section](../references/memory-allocation-traps-part-2.md#trap-5-delegate-assignment-in-loops)

## Trap 6: Enum Dictionary Keys

[Read section](../references/memory-allocation-traps-part-2.md#trap-6-enum-dictionary-keys)

## Trap 7: Structs Without IEquatable<T>

[Read section](../references/memory-allocation-traps-part-2.md#trap-7-structs-without-iequatable)

## Trap 8: String Operations

[Read section](../references/memory-allocation-traps-part-2.md#trap-8-string-operations)

### [String Comparison Trap](../references/memory-allocation-traps-part-2.md#string-comparison-trap)

## Trap 9: Boxing Value Types

[Read section](../references/memory-allocation-traps-part-2.md#trap-9-boxing-value-types)

## Trap 10: Unity API Array Properties

[Read section](../references/memory-allocation-traps-part-3.md#trap-10-unity-api-array-properties)

### [Array-Returning Properties to Avoid](../references/memory-allocation-traps-part-3.md#array-returning-properties-to-avoid)

## Detection: Finding Hidden Allocations

[Read section](../references/memory-allocation-traps-part-3.md#detection-finding-hidden-allocations)

### [Unity Profiler](../references/memory-allocation-traps-part-3.md#unity-profiler)

### [Search Patterns (Regex)](../references/memory-allocation-traps-part-3.md#search-patterns-regex)

## Related Skills

[Read section](../references/memory-allocation-traps-part-3.md#related-skills)
