# refactor-to-zero-alloc - Part 1

## Split Content

**Trigger**: When refactoring existing code that contains heap allocations to achieve zero-allocation in steady state. Use this skill when:

- Reviewing existing code for performance issues
- Migrating legacy code to high-performance patterns
- Fixing allocation hotspots identified in Unity Profiler
- Converting LINQ-heavy code to explicit loops

---

## When to Use This Skill

Use this skill when you need to systematically refactor allocating code to zero-allocation patterns. This skill provides:

- A step-by-step refactoring process
- Decision matrices for struct vs class
- Complete before/after refactoring examples
- Verification techniques to confirm zero allocations

For specific patterns, see the related skills linked throughout this document.

---

## Refactoring Process Overview

Follow these steps in order:

1. **Identify allocations** - Use Unity Profiler and search patterns
2. **Eliminate LINQ** - Convert to explicit loops (see [linq-elimination-patterns](../skills/linq-elimination-patterns.md))
3. **Eliminate closures** - Remove lambda captures (see [avoid-allocations](../skills/avoid-allocations.md))
4. **Migrate to collection pooling** - Replace `new List<T>()` (see [use-pooling](../skills/use-pooling.md))
5. **Apply StringBuilder patterns** - Replace string concatenation (see [use-pooling](../skills/use-pooling.md#stringbuilder-pooling))
6. **Use array pools** - Replace `new T[]` (see [use-array-pool](../skills/use-array-pool.md))
7. **Fix struct equality** - Implement `IEquatable<T>` (see [avoid-allocations](../skills/avoid-allocations.md#implement-iequatablet-to-avoid-boxing))
8. **Address foreach boxing** - Iterate the concrete collection, not an interface-typed reference to it

---

## Struct vs Class Decision Matrix

Use this matrix to decide when to use structs vs classes:

| Factor               | Use Struct          | Use Class                               |
| -------------------- | ------------------- | --------------------------------------- |
| Size                 | < 16 bytes          | > 16 bytes                              |
| Copying frequency    | Low (pass by ref)   | High (need shared reference)            |
| Number of references | Few (1-2)           | Many (3+)                               |
| Inheritance needed   | No                  | Yes                                     |
| Mutability           | Immutable preferred | Mutable OK                              |
| Heap overhead        | N/A                 | 16 bytes header + 8 bytes per reference |

**Memory Calculation Example (56-byte data):**

```csharp
// Class: 56 + 16 (header) + 8x2 (refs) = 88 bytes
// Struct with 2 copies: 56 x 2 = 112 bytes (worse!)
// Struct with 1 copy: 56 bytes (better!)
```

**Rule**: If you'll have 3+ references to the same data, use a class.

---

## Step 1: Identify Allocations

### Common Allocation Sources Checklist

| Pattern             | Allocation Type     | Search For                                                           |
| ------------------- | ------------------- | -------------------------------------------------------------------- |
| LINQ methods        | Iterator + delegate | `.Where(`, `.Select(`, `.Any(`, `.First(`, `.ToList()`, `.ToArray()` |
| Collection creation | Heap allocation     | `new List<`, `new Dictionary<`, `new HashSet<`                       |
| Closures            | Closure object      | Lambdas that capture local variables or `this`                       |
| String operations   | String allocation   | `string.Format`, `$"..."`, `+` in loops                              |
| Array creation      | Heap allocation     | `new T[`, `new byte[`, `new int[`                                    |
| Boxing              | Box allocation      | Struct assigned to `object`, non-generic interfaces                  |
| Interface iteration | Enumerator boxing   | `foreach` over `IEnumerable<T>` / `IList<T>` / `IReadOnlyList<T>`    |
| params methods      | Array allocation    | Method calls with `params` parameters                                |
| Enum dictionary     | Boxing per lookup   | `Dictionary<MyEnum, T>` without custom comparer                      |

For detailed trap descriptions, see [memory-allocation-traps](../skills/memory-allocation-traps.md).

### Search Regex Patterns

```text
LINQ:       \.Where\(|\.Select\(|\.Any\(|\.First\(|\.ToList\(|\.ToArray\(
Collections: new List<|new Dictionary<|new HashSet<
Closures:   => .*[^static]
foreach:    foreach\s*\([^)]*\bin\s+\w*([Ii]Enumerable|IList|IReadOnlyList|ISet)
```

---

## Step 2: Apply Targeted Refactoring

Once allocations are identified, apply the appropriate pattern from these skills:

| Allocation Type      | Skill Reference                                                                   |
| -------------------- | --------------------------------------------------------------------------------- |
| LINQ methods         | [linq-elimination-patterns](../skills/linq-elimination-patterns.md)                       |
| Closures/lambdas     | [avoid-allocations](../skills/avoid-allocations.md#no-closures-in-hot-paths)              |
| List/HashSet/Dict    | [use-pooling](../skills/use-pooling.md)                                                   |
| Arrays               | [use-array-pool](../skills/use-array-pool.md)                                             |
| String concatenation | [use-pooling](../skills/use-pooling.md#stringbuilder-pooling)                             |
| Boxing (IEquatable)  | [avoid-allocations](../skills/avoid-allocations.md#implement-iequatablet-to-avoid-boxing) |
| Enum dictionary keys | [avoid-allocations](../skills/avoid-allocations.md#enum-dictionary-keys-cause-boxing)     |
| Interface iteration  | [avoid-allocations](../skills/avoid-allocations.md#foreach-boxing-on-collections)         |

---

## Complete Refactoring Example

### Before: Multiple Allocation Issues

```csharp
public class EnemyManager
{
    private List<Enemy> _enemies = new List<Enemy>();

    // Multiple allocations per call
    public string GetStatusReport()
    {
        // LINQ allocations
        var activeEnemies = _enemies.Where(e => e.IsActive).ToList();
        var totalHealth = activeEnemies.Sum(e => e.Health);

        // String allocation
        string report = $"Active: {activeEnemies.Count}, Total HP: {totalHealth}";

        // More LINQ
        var lowHealth = activeEnemies.Where(e => e.Health < 50).ToList();
        foreach (var enemy in lowHealth)
        {
            report += $"\n  Warning: {enemy.Name} at {enemy.Health} HP";
        }

        return report;
    }
}
```

**Allocation Analysis:**

1. `.Where(e => e.IsActive)` - Iterator + delegate + closure
2. `.ToList()` - New List allocation
3. `.Sum(e => e.Health)` - Delegate allocation
4. String interpolation - String allocation
5. `.Where(e => e.Health < 50)` - Iterator + delegate + closure
6. `.ToList()` - Another List allocation
7. `foreach` on List - Enumerator boxing
8. `+=` in loop - Multiple string allocations
