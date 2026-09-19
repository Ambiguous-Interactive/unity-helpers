# refactor-to-zero-alloc - Part 2

## Split Content

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
