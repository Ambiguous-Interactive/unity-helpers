# performance-audit - Part 2

## Split Content

## Stack Allocation

```csharp
// ✅ Stack-allocated array for small fixed sizes
Span<int> buffer = stackalloc int[16];

// ✅ Value tuples instead of Tuple<>
(int x, int y) point = (10, 20);
```

---

## Editor Code Is NOT Exempt

Editor tools must also be performant:

| Context              | Concern                          |
| -------------------- | -------------------------------- |
| Inspector drawing    | Called every frame when visible  |
| Asset processing     | May handle thousands of assets   |
| Scene view callbacks | Called frequently during editing |

---

## Unity-Specific Audit Points

### Component Access

```csharp
// ❌ AUDIT FLAG: GetComponent in Update
void Update()
{
    GetComponent<Rigidbody>().AddForce(Vector3.up);  // Cache this!
}
```

### Array-Valued APIs

```csharp
// ❌ AUDIT FLAG: Array property accessed multiple times
for (int i = 0; i < mesh.vertices.Length; i++)  // Creates array!
{
    Process(mesh.vertices[i]);  // Creates another array!
}
```

### Tag/Name Comparisons

```csharp
// ❌ AUDIT FLAG: String allocation
if (gameObject.tag == "Player") { }  // Use CompareTag!
if (gameObject.name == "Enemy") { }  // Cache name!
```

### Material Access

```csharp
// ❌ AUDIT FLAG: Material cloning
renderer.material.color = Color.red;  // Clones material!
```

---

## Profiling Checklist

1. **Identify hot paths**: Code called frequently (Update, OnGUI, loops)
2. **Check allocations**: Use Unity Profiler's GC Alloc column
3. **Look for boxing**: Value types passed to `object` parameters
4. **Find LINQ usage**: Search for `.Where`, `.Select`, `.Any`, `.First`
5. **Check string operations**: `+` operator, `Format`, interpolation in loops
6. **Review collection creation**: `new List<>`, `new Dictionary<>` in methods
7. **Audit Unity API calls**: GetComponent, Camera.main, Physics casts
8. **Check coroutines**: new WaitForSeconds, closures in yields
9. **Find foreach on List**: Replace with `for` loops (Mono boxing)
10. **Check enum dictionary keys**: Need custom `IEqualityComparer`
11. **Verify struct equality**: Must implement `IEquatable<T>`

---

## Performance Metrics & Thresholds

### Target Values

| Metric                    | Target                | Notes                     |
| ------------------------- | --------------------- | ------------------------- |
| **Per-frame allocations** | **0 bytes**           | Zero-allocation goal      |
| **GC.Collect calls**      | Only during loading   | Never during gameplay     |
| **Frame time (60 FPS)**   | < 16.67ms             | Budget for all systems    |
| **Frame time (90 FPS)**   | < 11.1ms              | VR/AR requirement         |
| **SetPass calls**         | Minimize for hardware | Reduce draw calls         |
| **Batch count**           | Maximize batching     | Static + dynamic batching |

### Unity Profiler Workflow

1. Open **Window → Analysis → Profiler**
2. Enable **Deep Profile** for detailed allocation tracking
3. Select **CPU Usage** module
4. Sort by **GC Alloc** column to find allocation sources
5. Click entries to see call stack
6. Profile on target device (not just editor)

### Frame Debugger Workflow

1. Open **Window → Analysis → Frame Debugger**
2. Step through rendering commands
3. Identify redundant draw calls
4. Check shader property bindings
5. Verify batching is working

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

---

## Allocation Verification Tests

### NUnit Allocation Test Pattern

```csharp
[Test]
public void Method_ShouldNotAllocate_InSteadyState()
{
    // Warm up - first call may allocate (JIT, caches)
    _instance.Method();

    // Measure steady state
    long before = GC.GetAllocatedBytesForCurrentThread();

    for (int i = 0; i < 1000; i++)
    {
        _instance.Method();
    }

    long after = GC.GetAllocatedBytesForCurrentThread();
    long allocated = after - before;

    Assert.AreEqual(0, allocated, $"Method allocated {allocated} bytes");
}
```

---

## Quick Wins

| Pattern                          | Replacement                      |
| -------------------------------- | -------------------------------- |
| `items.Where(x => ...).ToList()` | `for` loop with pooled list      |
| `items.Any(x => ...)`            | `for` loop with early return     |
| `items.FirstOrDefault(x => ...)` | `for` loop with `break`          |
| `new List<T>()` in method        | `Buffers<T>.List.Get()`          |
| `string.Format()` in loop        | `StringBuilder`                  |
| `items.Count()` on IEnumerable   | Cache count or use `ICollection` |
| `GetComponent<T>()` in Update    | Cache in Awake                   |
| `Physics.RaycastAll`             | `Physics.RaycastNonAlloc`        |
| `gameObject.tag == "X"`          | `gameObject.CompareTag("X")`     |
| `new WaitForSeconds(n)`          | Cache as field                   |

---
