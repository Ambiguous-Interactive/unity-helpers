# gc-architecture-unity - Part 2

## Split Content

## Memory Types and GC Tracking

| Type              | Location       | GC Tracked | Notes                    |
| ----------------- | -------------- | ---------- | ------------------------ |
| Local value types | Stack          | No         | Automatic cleanup        |
| Reference types   | Managed heap   | Yes        | Subject to GC            |
| Boxed value types | Managed heap   | Yes        | Hidden allocations       |
| `NativeArray<T>`  | Unmanaged heap | No         | Manual disposal required |
| `stackalloc`      | Stack          | No         | Limited size, unsafe     |
| Static fields     | Managed heap   | Yes        | Persist until app exit   |

### Using Unmanaged Memory to Avoid GC

```csharp
using Unity.Collections;

// Allocates outside managed heap — GC ignores it
NativeArray<Vector3> positions = new NativeArray<Vector3>(
    128, Allocator.Persistent);

// CRITICAL: Must dispose manually or memory leaks!
void OnDestroy()
{
    if (positions.IsCreated)
    {
        positions.Dispose();
    }
}
```

---

## Platform Considerations

### Mobile

- Smaller heap limits
- GC pauses more noticeable
- Battery impact from GC work
- **Recommendation**: Even stricter zero-allocation policy

### WebGL

- Single-threaded, GC blocks everything
- No incremental GC available
- **Recommendation**: Pre-allocate everything at startup

### IL2CPP vs Mono

Both use the same Boehm GC behavior, but:

- IL2CPP generates better native code
- IL2CPP has AOT-only limitation (no runtime code generation)
- Same GC characteristics apply to both

---

## Quick Reference: GC-Safe Patterns

| ❌ Avoid                     | ✅ Use Instead                   |
| ---------------------------- | -------------------------------- |
| `new List<T>()` in methods   | `Buffers<T>.List.Get()`          |
| `new T[]` in methods         | Array pools                      |
| String concatenation         | `StringBuilder` pooling          |
| LINQ in Update/FixedUpdate   | Explicit `for` loops             |
| Boxing value types           | Generic methods                  |
| `foreach` on `List<T>`       | `for` with indexer               |
| Per-frame allocations        | Cache and reuse                  |
| Closures capturing variables | Static lambdas or explicit loops |

---

## Related Skills

- [high-performance-csharp](../skills/high-performance-csharp.md) — Core zero-allocation patterns
- [unity-performance-patterns](../skills/unity-performance-patterns.md) — Unity-specific optimizations
- [memory-allocation-traps](../skills/memory-allocation-traps.md) — Hidden allocation sources
- [refactor-to-zero-alloc](../skills/refactor-to-zero-alloc.md) — Migration patterns
- [use-pooling](../skills/use-pooling.md) — Collection pooling
