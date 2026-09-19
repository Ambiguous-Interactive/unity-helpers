# linq-elimination-patterns - Part 3

## Split Content

## Quick Conversion Reference

| LINQ Pattern                  | Zero-Allocation Replacement            |
| ----------------------------- | -------------------------------------- |
| `.Where(predicate)`           | `for` loop with `if` check             |
| `.Select(transform)`          | `for` loop building result             |
| `.FirstOrDefault(predicate)`  | `for` loop with early `break`          |
| `.Any(predicate)`             | `for` loop returning `bool`            |
| `.All(predicate)`             | `for` loop with early `return false`   |
| `.Count(predicate)`           | `for` loop incrementing counter        |
| `.Sum(selector)`              | `for` loop accumulating                |
| `.OrderBy(key).ToList()`      | Copy to pooled list, then `Sort()`     |
| `.GroupBy(key)`               | Pooled dictionary with manual grouping |
| `.Distinct()`                 | Pooled HashSet for deduplication       |
| `.Take(n)` / `.Skip(n)`       | `for` loop with index bounds           |
| `.ToList()` / `.ToArray()`    | Pooled collection with explicit copy   |
| `.Aggregate(seed, func)`      | `for` loop with accumulator variable   |
| `.SelectMany(collection)`     | Nested `for` loops                     |
| `.Concat(other)`              | Two sequential `for` loops             |
| `.Zip(other, resultSelector)` | Single `for` loop with dual indexing   |

---

## Related Skills

- [refactor-to-zero-alloc](../skills/refactor-to-zero-alloc.md) - Complete refactoring workflow
- [high-performance-csharp](../skills/high-performance-csharp.md) - Core performance philosophy
- [avoid-allocations](../skills/avoid-allocations.md) - Closure and boxing avoidance
- [use-pooling](../skills/use-pooling.md) - Collection pooling patterns
- [memory-allocation-traps](../skills/memory-allocation-traps.md) - Hidden allocation sources
