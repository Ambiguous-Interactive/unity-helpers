# performance-audit - Part 3

## Split Content

## Example Transformation

### Before (Allocating)

```csharp
public List<Enemy> GetActiveEnemiesInRange(float range)
{
    return enemies
        .Where(e => e.Health > 0 && e.Distance < range)
        .ToList();
}
```

### After (Zero-Allocation)

```csharp
public void GetActiveEnemiesInRange(float range, List<Enemy> results)
{
    results.Clear();
    for (int i = 0; i < enemies.Count; i++)
    {
        Enemy enemy = enemies[i];
        if (enemy.Health > 0 && enemy.Distance < range)
        {
            results.Add(enemy);
        }
    }
}

// Usage with pooling
using var lease = Buffers<Enemy>.List.Get(out List<Enemy> activeEnemies);
GetActiveEnemiesInRange(10f, activeEnemies);
```

---

## Related Skills

- [high-performance-csharp](../skills/high-performance-csharp.md) — Core performance patterns
- [unity-performance-patterns](../skills/unity-performance-patterns.md) — Unity-specific patterns
- [profile-debug-performance](../skills/profile-debug-performance.md) — Profiling guide
- [refactor-to-zero-alloc](../skills/refactor-to-zero-alloc.md) — Migration patterns
- [gc-architecture-unity](../skills/gc-architecture-unity.md) — Unity GC architecture
- [memory-allocation-traps](../skills/memory-allocation-traps.md) — Hidden allocation sources
