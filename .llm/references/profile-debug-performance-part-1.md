# profile-debug-performance - Part 1

## Split Content

**Trigger**: When investigating performance issues, optimizing hot paths, or verifying zero-allocation code.

---

## Profiling First Principle

**Never optimize without profiling.** Performance intuition is often wrong.

> "Profile first, optimize observed hot spots. Each optimization reduces flexibility."

However, for patterns that cost nothing to implement correctly from the start (caching, avoiding LINQ, pooling), use them by default.

---

## Unity Profiler

### Setup

1. **Window → Analysis → Profiler**
2. Enable **Deep Profile** for detailed allocation tracking (slower but comprehensive)
3. Connect to **target device** for accurate metrics (Editor != runtime)

### Key Modules

| Module    | What It Shows                                |
| --------- | -------------------------------------------- |
| CPU Usage | Frame time, method execution, GC allocations |
| GPU Usage | Rendering time, draw calls                   |
| Memory    | Heap size, allocations, native memory        |
| Rendering | Batches, set pass calls, triangles           |
| Physics   | Contacts, rigidbodies, colliders             |

### Finding Allocations

1. Select **CPU Usage** module
2. Enable **Deep Profile** or use **Call Stacks** for GC Alloc
3. Look at **GC.Alloc** column in hierarchy view
4. Click on frame spikes to investigate

### GC Allocation Markers

```csharp
// Add markers to isolate specific code sections
using Unity.Profiling;

public class MyComponent : MonoBehaviour
{
    private static readonly ProfilerMarker s_UpdateMarker =
        new ProfilerMarker("MyComponent.Update");

    private static readonly ProfilerMarker s_ProcessEnemiesMarker =
        new ProfilerMarker("MyComponent.ProcessEnemies");

    void Update()
    {
        using (s_UpdateMarker.Auto())
        {
            using (s_ProcessEnemiesMarker.Auto())
            {
                ProcessEnemies();
            }
        }
    }
}
```

---

## Memory Profiler

### Setup

Install via **Package Manager** → Unity Registry → Memory Profiler

### Key Features

- **Snapshot comparison** — Compare memory between two points in time
- **Object references** — See what's keeping objects alive
- **Native memory** — Track textures, meshes, audio
- **Fragmentation view** — Visualize heap fragmentation

### Taking Snapshots

1. Play in Editor or connect to device
2. Click **Capture** in Memory Profiler window
3. Wait for snapshot to complete
4. Compare snapshots to find leaks

---

## Frame Debugger

### Window → Analysis → Frame Debugger

Use to analyze:

- Draw call count and batching effectiveness
- Shader passes
- Render target switches
- What's being rendered (and shouldn't be)

---

## Allocation Testing in Code

### Verify Zero Allocation

```csharp
using NUnit.Framework;

[Test]
public void Method_ShouldNotAllocate_InSteadyState()
{
    // Warm up - first call may allocate (caches, etc.)
    _target.MyMethod();

    // Measure steady state
    long before = GC.GetAllocatedBytesForCurrentThread();

    for (int i = 0; i < 1000; i++)
    {
        _target.MyMethod();
    }

    long after = GC.GetAllocatedBytesForCurrentThread();
    long allocated = after - before;

    Assert.AreEqual(0, allocated, $"Method allocated {allocated} bytes over 1000 calls");
}
```

### Allocation Test with Warmup Pattern

```csharp
[Test]
public void ProcessItems_ZeroAllocationAfterWarmup()
{
    List<Item> items = CreateTestItems(100);

    // Warmup phase - allow one-time allocations
    for (int warmup = 0; warmup < 10; warmup++)
    {
        _processor.Process(items);
    }

    // Measurement phase
    long baseline = GC.GetAllocatedBytesForCurrentThread();

    for (int i = 0; i < 100; i++)
    {
        _processor.Process(items);
    }

    long allocated = GC.GetAllocatedBytesForCurrentThread() - baseline;
    Assert.AreEqual(0, allocated, $"Allocated {allocated} bytes in steady state");
}
```

---
