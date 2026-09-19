# gc-architecture-unity - Part 1

## Split Content

**Trigger**: When you need to understand how Unity's garbage collector differs from .NET, when to use incremental GC, or when to manually trigger garbage collection.

---

## Unity GC vs .NET GC Comparison

Unity uses the **Boehm-Demers-Weiser (BDW)** garbage collector, which differs significantly from .NET's generational GC:

| Aspect                  | Unity (Boehm GC)                | .NET CLR                                |
| ----------------------- | ------------------------------- | --------------------------------------- |
| **Algorithm**           | Conservative, non-generational  | Generational (Gen 0, 1, 2)              |
| **Generations**         | None — processes all objects    | 3 generations (short/medium/long-lived) |
| **Memory Compaction**   | ❌ No                           | ✅ Yes (except Large Object Heap)       |
| **Root Scanning**       | Conservative (less precise)     | Precise root scanning                   |
| **Fragmentation**       | Prone to fragmentation          | Reduced via compaction                  |
| **Short-lived Objects** | No optimization                 | Gen0 efficiently handles                |
| **Heap Shrinking**      | ❌ Never returns memory to OS   | ✅ Can release memory                   |
| **Stop-the-World**      | Full heap scan every collection | Per-generation (faster Gen0)            |

---

## Why This Matters

### The .NET Assumption That Fails in Unity

Standard .NET development assumes frequent small allocations are cheap because:

- Gen0 collections are fast (only scans newest objects)
- Short-lived objects are quickly collected
- Memory compaction prevents fragmentation

**In Unity, ALL of these assumptions are wrong:**

- Every collection scans the entire heap
- No optimization for short-lived objects
- Fragmentation accumulates over time
- Memory is never returned to the OS

### The Impact

```csharp
// In .NET: Acceptable pattern
// In Unity: Creates GC pressure every frame!
void Update()
{
    var enemies = new List<Enemy>();  // .NET: Gen0, cheap
                                       // Unity: Full heap allocation
}
```

---

## Unity's Incremental GC Mode

Unity 2019.1+ offers **Incremental Garbage Collection** to reduce frame spikes.

### How It Works

Instead of one long pause, incremental GC:

1. Distributes marking phase across multiple frames
2. Uses "write barriers" to track reference changes between frames
3. Reduces individual pause durations

### When Incremental GC Works Well

- Most object references remain stable between frames
- Objects don't frequently change relationships
- Steady-state gameplay with predictable allocation patterns

### When Incremental GC Fails

```csharp
// ❌ BAD: Too many reference changes per frame
void Update()
{
    // Each of these changes triggers write barrier overhead
    _target = FindNearestEnemy();
    _weapon.Target = _target;
    _ui.UpdateTarget(_target);
    // ...100 more reference changes
}
```

**Threshold Warning**: If reference changes exceed what incremental GC can process, Unity falls back to a **full stop-the-world collection** — potentially worse than non-incremental!

### Enabling Incremental GC

```text
Edit → Project Settings → Player → Other Settings → Use Incremental GC
```

**Default**: Enabled in Unity 2019.1+

---

## When to Manually Trigger GC

### Safe Times to Call GC.Collect()

```csharp
// ✅ During loading screens
public IEnumerator LoadLevel()
{
    ShowLoadingScreen();
    yield return LoadAssetsAsync();

    // Clean up before gameplay
    Resources.UnloadUnusedAssets();
    System.GC.Collect();

    HideLoadingScreen();
}

// ✅ During scene transitions
void OnSceneUnloaded(Scene scene)
{
    Resources.UnloadUnusedAssets();
    System.GC.Collect();
}

// ✅ During pause menus (if acceptable pause)
void OnGamePaused()
{
    System.GC.Collect();
}
```

### Never Call GC.Collect() During

- Active gameplay
- Animation playback
- Physics simulation
- Audio playback
- Any time frame timing matters

---

## Memory Lifecycle in Unity

### The Managed Heap Never Shrinks

```text
Start: [    Heap: 10 MB    ]
Allocate: [    Heap: 50 MB    ]
GC Run: [    Heap: 50 MB    ]  ← Still 50 MB!
              ↑ Free space exists but heap size unchanged
```

**Implication**: Memory high-water mark persists until application restart.

### Fragmentation Accumulates

Without compaction, freed memory creates "holes":

```text
Before: [A][B][C][D][E][F][G][H]
After:  [A][ ][C][ ][E][ ][G][ ]  ← Freed B, D, F, H
New:    [A][ ][C][ ][E][ ][G][ ]  ← Can't fit [IIII] (4 slots)
                                    Even though 4 slots are free!
```

**Solution**: Avoid the problem — don't allocate in the first place.

---
