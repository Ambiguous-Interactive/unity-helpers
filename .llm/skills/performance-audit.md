# Skill: Performance Audit

<!-- trigger: audit, review, optimize, sensitive | Reviewing performance-sensitive code | Performance -->

## Reference Parts

- [Part 1](../references/performance-audit-part-1.md)
- [Part 2](../references/performance-audit-part-2.md)
- [Part 3](../references/performance-audit-part-3.md)

## Allocation Checklist

[Read section](../references/performance-audit-part-1.md#allocation-checklist)

### [❌ LINQ in Hot Paths](../references/performance-audit-part-1.md#-linq-in-hot-paths)

### [❌ Closures That Capture Variables](../references/performance-audit-part-1.md#-closures-that-capture-variables)

### [❌ String Operations in Loops](../references/performance-audit-part-1.md#-string-operations-in-loops)

### [❌ Frequent Collection Allocations](../references/performance-audit-part-1.md#-frequent-collection-allocations)

## Pooling Patterns

[Read section](../references/performance-audit-part-1.md#pooling-patterns)

### [Collection Pooling](../references/performance-audit-part-1.md#collection-pooling)

### [Array Pooling](../references/performance-audit-part-1.md#array-pooling)

## Struct vs Class

[Read section](../references/performance-audit-part-1.md#struct-vs-class)

### [Use Structs When](../references/performance-audit-part-1.md#use-structs-when)

### [Avoid Boxing](../references/performance-audit-part-1.md#avoid-boxing)

## Stack Allocation

[Read section](../references/performance-audit-part-2.md#stack-allocation)

## Editor Code Is NOT Exempt

[Read section](../references/performance-audit-part-2.md#editor-code-is-not-exempt)

## Unity-Specific Audit Points

[Read section](../references/performance-audit-part-2.md#unity-specific-audit-points)

### [Component Access](../references/performance-audit-part-2.md#component-access)

### [Array-Valued APIs](../references/performance-audit-part-2.md#array-valued-apis)

### [Tag/Name Comparisons](../references/performance-audit-part-2.md#tagname-comparisons)

### [Material Access](../references/performance-audit-part-2.md#material-access)

## Profiling Checklist

[Read section](../references/performance-audit-part-2.md#profiling-checklist)

## Performance Metrics & Thresholds

[Read section](../references/performance-audit-part-2.md#performance-metrics--thresholds)

### [Target Values](../references/performance-audit-part-2.md#target-values)

### [Unity Profiler Workflow](../references/performance-audit-part-2.md#unity-profiler-workflow)

### [Frame Debugger Workflow](../references/performance-audit-part-2.md#frame-debugger-workflow)

### [Profiler Markers](../references/performance-audit-part-2.md#profiler-markers)

## Allocation Verification Tests

[Read section](../references/performance-audit-part-2.md#allocation-verification-tests)

### [NUnit Allocation Test Pattern](../references/performance-audit-part-2.md#nunit-allocation-test-pattern)

## Quick Wins

[Read section](../references/performance-audit-part-2.md#quick-wins)

## Example Transformation

[Read section](../references/performance-audit-part-3.md#example-transformation)

### [Before (Allocating)](../references/performance-audit-part-3.md#before-allocating)

### [After (Zero-Allocation)](../references/performance-audit-part-3.md#after-zero-allocation)

## Related Skills

[Read section](../references/performance-audit-part-3.md#related-skills)
