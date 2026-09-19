# refactor-to-zero-alloc - Part 3

## Split Content

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

- [linq-elimination-patterns](../skills/linq-elimination-patterns.md) - LINQ to loop conversions
- [avoid-allocations](../skills/avoid-allocations.md) - Closure and boxing patterns
- [use-pooling](../skills/use-pooling.md) - Collection pooling API
- [use-array-pool](../skills/use-array-pool.md) - Array pool selection
- [memory-allocation-traps](../skills/memory-allocation-traps.md) - Hidden allocation sources
- [high-performance-csharp](../skills/high-performance-csharp.md) - Core performance philosophy
- [unity-performance-patterns](../skills/unity-performance-patterns.md) - Unity-specific patterns
- [profile-debug-performance](../skills/profile-debug-performance.md) - Profiling guide
- [performance-audit](../skills/performance-audit.md) - Performance review checklist
