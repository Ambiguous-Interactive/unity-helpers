# Skill: Refactor to Zero-Allocation Patterns

<!-- trigger: refactor, zero, alloc, legacy, migrate | Converting allocating code to zero-allocation | Performance -->

## Reference Parts

- [Part 1](../references/refactor-to-zero-alloc-part-1.md)
- [Part 2](../references/refactor-to-zero-alloc-part-2.md)
- [Part 3](../references/refactor-to-zero-alloc-part-3.md)

## When to Use This Skill

[Read section](../references/refactor-to-zero-alloc-part-1.md#when-to-use-this-skill)

## Refactoring Process Overview

[Read section](../references/refactor-to-zero-alloc-part-1.md#refactoring-process-overview)

## Struct vs Class Decision Matrix

[Read section](../references/refactor-to-zero-alloc-part-1.md#struct-vs-class-decision-matrix)

## Step 1: Identify Allocations

[Read section](../references/refactor-to-zero-alloc-part-1.md#step-1-identify-allocations)

### [Common Allocation Sources Checklist](../references/refactor-to-zero-alloc-part-1.md#common-allocation-sources-checklist)

### [Search Regex Patterns](../references/refactor-to-zero-alloc-part-1.md#search-regex-patterns)

## Step 2: Apply Targeted Refactoring

[Read section](../references/refactor-to-zero-alloc-part-1.md#step-2-apply-targeted-refactoring)

## Complete Refactoring Example

[Read section](../references/refactor-to-zero-alloc-part-1.md#complete-refactoring-example)

### [Before: Multiple Allocation Issues](../references/refactor-to-zero-alloc-part-1.md#before-multiple-allocation-issues)

### [After: Zero-Allocation](../references/refactor-to-zero-alloc-part-2.md#after-zero-allocation)

## Verification: Confirming Zero Allocations

[Read section](../references/refactor-to-zero-alloc-part-2.md#verification-confirming-zero-allocations)

### [Unity Profiler Method](../references/refactor-to-zero-alloc-part-2.md#unity-profiler-method)

### [Profiler Markers](../references/refactor-to-zero-alloc-part-2.md#profiler-markers)

### [Allocation Test Pattern](../references/refactor-to-zero-alloc-part-2.md#allocation-test-pattern)

### [Memory Profiler (Detailed Analysis)](../references/refactor-to-zero-alloc-part-2.md#memory-profiler-detailed-analysis)

## Quick Refactoring Checklist

[Read section](../references/refactor-to-zero-alloc-part-2.md#quick-refactoring-checklist)

## Method Signature Refactoring

[Read section](../references/refactor-to-zero-alloc-part-3.md#method-signature-refactoring)

### [Return Value to Out Parameter](../references/refactor-to-zero-alloc-part-3.md#return-value-to-out-parameter)

### [IEnumerable to Callback/Visitor](../references/refactor-to-zero-alloc-part-3.md#ienumerable-to-callbackvisitor)

## Common Refactoring Pitfalls

[Read section](../references/refactor-to-zero-alloc-part-3.md#common-refactoring-pitfalls)

### [Pitfall 1: Forgetting using Statement](../references/refactor-to-zero-alloc-part-3.md#pitfall-1-forgetting-using-statement)

### [Pitfall 2: Storing Pooled Reference](../references/refactor-to-zero-alloc-part-3.md#pitfall-2-storing-pooled-reference)

### [Pitfall 3: Early Return Without Dispose](../references/refactor-to-zero-alloc-part-3.md#pitfall-3-early-return-without-dispose)

## Related Skills

[Read section](../references/refactor-to-zero-alloc-part-3.md#related-skills)
