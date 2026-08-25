# Skill: Refactor to Zero-Allocation Patterns

<!-- trigger: refactor, zero, alloc, legacy, migrate | Converting allocating code to zero-allocation | Performance -->

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
2. **Eliminate LINQ** - Convert to explicit loops (see [linq-elimination-patterns](./linq-elimination-patterns.md))
3. **Eliminate closures** - Remove lambda captures (see [avoid-allocations](./avoid-allocations.md))
4. **Migrate to collection pooling** - Replace `new List<T>()` (see [use-pooling](./use-pooling.md))
5. **Apply StringBuilder patterns** - Replace string concatenation (see [use-pooling](./use-pooling.md#stringbuilder-pooling))
6. **Use array pools** - Replace `new T[]` (see [use-array-pool](./use-array-pool.md))
7. **Fix struct equality** - Implement `IEquatable<T>` (see [avoid-allocations](./avoid-allocations.md#implement-iequatablet-to-avoid-boxing))
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

For detailed trap descriptions, see [memory-allocation-traps](./memory-allocation-traps.md).

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
| LINQ methods         | [linq-elimination-patterns](./linq-elimination-patterns.md)                       |
| Closures/lambdas     | [avoid-allocations](./avoid-allocations.md#no-closures-in-hot-paths)              |
| List/HashSet/Dict    | [use-pooling](./use-pooling.md)                                                   |
| Arrays               | [use-array-pool](./use-array-pool.md)                                             |
| String concatenation | [use-pooling](./use-pooling.md#stringbuilder-pooling)                             |
| Boxing (IEquatable)  | [avoid-allocations](./avoid-allocations.md#implement-iequatablet-to-avoid-boxing) |
| Enum dictionary keys | [avoid-allocations](./avoid-allocations.md#enum-dictionary-keys-cause-boxing)     |
| Interface iteration  | [avoid-allocations](./avoid-allocations.md#foreach-boxing-on-collections)         |

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

### After: Zero-Allocation

```csharp
public class EnemyManager
{
    private List<Enemy> _enemies = new List<Enemy>();

    // Zero allocation in steady state
    public string GetStatusReport()
    {
        using var activeLease = Buffers<Enemy>.List.Get(out List<Enemy> activeEnemies);
        using var sbLease = Buffers.StringBuilder.Get(out StringBuilder sb);

        // Explicit loop instead of LINQ
        int totalHealth = 0;
        for (int i = 0; i < _enemies.Count; i++)
        {
            Enemy e = _enemies[i];
            if (e.IsActive)
            {
                activeEnemies.Add(e);
                totalHealth += e.Health;
            }
        }

        // StringBuilder instead of interpolation
        sb.Append("Active: ");
        sb.Append(activeEnemies.Count);
        sb.Append(", Total HP: ");
        sb.Append(totalHealth);

        // Single pass for low health warnings
        for (int i = 0; i < activeEnemies.Count; i++)
        {
            Enemy e = activeEnemies[i];
            if (e.Health < 50)
            {
                sb.AppendLine();
                sb.Append("  Warning: ");
                sb.Append(e.Name);
                sb.Append(" at ");
                sb.Append(e.Health);
                sb.Append(" HP");
            }
        }

        return sb.ToString();
    }
}
```

**Refactoring Applied:**

1. Replaced LINQ `.Where().ToList()` with explicit `for` loop
2. Replaced `.Sum()` with inline accumulation
3. Replaced string interpolation with StringBuilder
4. Used `Buffers<T>.List.Get()` for temporary list
5. Used `Buffers.StringBuilder.Get()` for string building
6. Combined filtering passes where possible

---

## Verification: Confirming Zero Allocations

### Unity Profiler Method

1. Open **Window > Analysis > Profiler**
2. Enable **Deep Profile** for detailed allocation tracking
3. Select **CPU Usage** module
4. Look for **GC.Alloc** column in the hierarchy
5. Run your code path and check for allocations

### Profiler Markers

```csharp
using Unity.Profiling;

private static readonly ProfilerMarker s_MyMethodMarker =
    new ProfilerMarker("MyClass.MyMethod");

public void MyMethod()
{
    using (s_MyMethodMarker.Auto())
    {
        // Code to profile
    }
}
```

### Allocation Test Pattern

```csharp
[Test]
public void Method_ShouldNotAllocate_InSteadyState()
{
    // Warm up - first call may allocate
    myObject.Method();

    // Measure steady state
    long before = GC.GetAllocatedBytesForCurrentThread();

    for (int i = 0; i < 1000; i++)
    {
        myObject.Method();
    }

    long after = GC.GetAllocatedBytesForCurrentThread();
    long allocated = after - before;

    Assert.AreEqual(0, allocated, $"Method allocated {allocated} bytes");
}
```

### Memory Profiler (Detailed Analysis)

For complex cases, use Unity Memory Profiler package:

1. Install via Package Manager
2. Take memory snapshots before/after operations
3. Compare snapshots to identify leaked allocations
4. Check "Managed Shell Objects" for unexpected references

---

## Quick Refactoring Checklist

When refactoring a method to zero-allocation:

- [ ] **Eliminate ALL LINQ** - Convert to explicit `for` loops
- [ ] Consider visitor pattern for complex iteration
- [ ] Find all closures - Use static lambdas or inline logic
- [ ] Find `new List<T>()` - Use `Buffers<T>.List.Get()`
- [ ] Find `new HashSet<T>()` - Use `Buffers<T>.HashSet.Get()`
- [ ] Find `new T[]` with variable size - Use `SystemArrayPool<T>.Get()`
- [ ] Find `new T[]` with constant size - Use `WallstopArrayPool<T>.Get()`
- [ ] Find string concatenation in loops - Use `Buffers.StringBuilder.Get()`
- [ ] Find string interpolation in hot paths - Use StringBuilder
- [ ] Verify all `using` statements are present for pooled resources
- [ ] Check method signatures - prefer out parameters over return values
- [ ] Run Unity Profiler to verify zero allocations

---

## Method Signature Refactoring

### Return Value to Out Parameter

```csharp
// BEFORE: Allocates new list
public List<Enemy> GetEnemiesInRange(float range)
{
    List<Enemy> result = new List<Enemy>();
    // ... populate ...
    return result;
}

// AFTER: Caller provides buffer
public void GetEnemiesInRange(float range, List<Enemy> result)
{
    result.Clear();
    for (int i = 0; i < _allEnemies.Count; i++)
    {
        Enemy e = _allEnemies[i];
        if (e.Distance < range)
        {
            result.Add(e);
        }
    }
}

// CALLER:
using var lease = Buffers<Enemy>.List.Get(out List<Enemy> nearbyEnemies);
GetEnemiesInRange(10f, nearbyEnemies);
```

### IEnumerable to Callback/Visitor

```csharp
// BEFORE: Returns IEnumerable (lazy but still allocates iterator)
public IEnumerable<T> GetItems()
{
    foreach (T item in source)
    {
        if (item.IsValid)
        {
            yield return item;
        }
    }
}

// AFTER: Visitor pattern - zero allocation
public void VisitItems(Action<T> visitor)
{
    for (int i = 0; i < source.Count; i++)
    {
        T item = source[i];
        if (item.IsValid)
        {
            visitor(item);
        }
    }
}

// BETTER: Stateful visitor with ref struct
public void VisitItems<TVisitor>(ref TVisitor visitor) where TVisitor : struct, IItemVisitor
{
    for (int i = 0; i < source.Count; i++)
    {
        T item = source[i];
        if (item.IsValid)
        {
            visitor.Visit(item);
        }
    }
}
```

---

## Common Refactoring Pitfalls

### Pitfall 1: Forgetting using Statement

```csharp
// WRONG: Memory leak - list never returned to pool
var lease = Buffers<Item>.List.Get(out List<Item> items);
// ... use items ...
// lease never disposed!

// CORRECT: Always use 'using'
using var lease = Buffers<Item>.List.Get(out List<Item> items);
```

### Pitfall 2: Storing Pooled Reference

```csharp
// WRONG: Storing reference to pooled collection
List<Item> _cachedItems;

void Bad()
{
    using var lease = Buffers<Item>.List.Get(out List<Item> items);
    _cachedItems = items;  // Storing pooled reference!
}

// CORRECT: Copy if you need to store
void Good()
{
    using var lease = Buffers<Item>.List.Get(out List<Item> items);
    _cachedItems = new List<Item>(items);  // Copy if needed
}
```

### Pitfall 3: Early Return Without Dispose

```csharp
// WRONG: Early return skips dispose
void Bad()
{
    var lease = Buffers<Item>.List.Get(out List<Item> items);
    if (condition)
    {
        return;  // Lease not disposed!
    }
    lease.Dispose();
}

// CORRECT: using statement handles all exit paths
void Good()
{
    using var lease = Buffers<Item>.List.Get(out List<Item> items);
    if (condition)
    {
        return;  // Lease disposed automatically
    }
}
```

---

## Related Skills

- [linq-elimination-patterns](./linq-elimination-patterns.md) - LINQ to loop conversions
- [avoid-allocations](./avoid-allocations.md) - Closure and boxing patterns
- [use-pooling](./use-pooling.md) - Collection pooling API
- [use-array-pool](./use-array-pool.md) - Array pool selection
- [memory-allocation-traps](./memory-allocation-traps.md) - Hidden allocation sources
- [high-performance-csharp](./high-performance-csharp.md) - Core performance philosophy
- [unity-performance-patterns](./unity-performance-patterns.md) - Unity-specific patterns
- [profile-debug-performance](./profile-debug-performance.md) - Profiling guide
- [performance-audit](./performance-audit.md) - Performance review checklist
