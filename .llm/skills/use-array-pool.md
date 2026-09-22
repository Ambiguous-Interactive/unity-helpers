# Skill: Use Array Pool

<!-- trigger: array, buffer, pool, temporary | Working with temporary arrays | Performance -->

**Trigger**: When working with temporary arrays or buffers that should be pooled for performance.

---

## ⚠️ CRITICAL: Pool Selection Directly Impacts Memory

Using the wrong array pool for your use case **will cause memory leaks**. Read carefully.

---

## Decision Flowchart

```text
Is the size a compile-time constant or from a small fixed set?
├─ YES → Use WallstopArrayPool<T> (or WallstopFastArrayPool<T> for unmanaged types)
└─ NO  → Is the size derived from user input, collection sizes, or runtime calculations?
         ├─ YES → Use SystemArrayPool<T>
         └─ NO  → When in doubt, use SystemArrayPool<T> (safer default)
```

---

## WallstopArrayPool&lt;T&gt; / WallstopFastArrayPool&lt;T&gt;

**Use ONLY for constant or tightly bounded sizes.**

Returns arrays of EXACT requested size. Pools arrays by exact size in a dictionary keyed by size.

### ⚠️ MEMORY LEAK WARNING

These pools create a separate bucket for EVERY unique size requested. Variable sizes = unbounded memory growth.

### ✅ SAFE Usage

```csharp
// Compile-time constants
using PooledArray<byte> pooled = WallstopArrayPool<byte>.Get(16, out byte[] buffer);
using PooledArray<int> pooled = WallstopArrayPool<int>.Get(64, out int[] buffer);
using PooledArray<float> pooled = WallstopArrayPool<float>.Get(256, out float[] buffer);

// Algorithm-bounded sizes with small fixed upper limits
using PooledArray<int> pooled = WallstopArrayPool<int>.Get(bucketCount, out int[] buffer);
// where bucketCount is capped at 32

// PRNG internal state buffers (fixed sizes)
using PooledArray<ulong> pooled = WallstopArrayPool<ulong>.Get(4, out ulong[] state);

// Sizes from a small, known set
int size = enumValue switch
{
    Size.Small => 8,
    Size.Medium => 16,
    Size.Large => 32,
    _ => 8,
};
using PooledArray<int> pooled = WallstopArrayPool<int>.Get(size, out int[] buffer);
```

### ❌ MEMORY LEAK Usage

```csharp
// User input - every unique value creates permanent bucket
using PooledArray<int> pooled = WallstopArrayPool<int>.Get(userInput, out int[] buffer);

// Collection size - every unique size leaks memory
using PooledArray<T> pooled = WallstopArrayPool<T>.Get(collection.Count, out T[] buffer);

// Random sizes - creates up to N permanent buckets
using PooledArray<int> pooled = WallstopArrayPool<int>.Get(random.Next(1, 1000), out int[] buffer);

// Dynamic calculation - unbounded sizes = unbounded memory
using PooledArray<int> pooled = WallstopArrayPool<int>.Get(width * height, out int[] buffer);
```

### WallstopFastArrayPool&lt;T&gt;

Identical to `WallstopArrayPool<T>` but does NOT clear arrays on return. Use for `unmanaged` types where you'll overwrite all values before reading.

---

## SystemArrayPool&lt;T&gt;

**Use for variable or unpredictable sizes.**

Returns arrays of AT LEAST requested size (may be larger due to power-of-2 bucketing). Wraps .NET's `ArrayPool<T>.Shared`.

### ✅ Appropriate Usage

```csharp
// Sorting algorithm buffers (scale with input)
using PooledArray<T> pooled = SystemArrayPool<T>.Get(count / 2, out T[] buffer);

// Collection copies of unknown size
using PooledArray<T> pooled = SystemArrayPool<T>.Get(list.Count, out T[] buffer);

// Any size derived from user input or external data
using PooledArray<byte> pooled = SystemArrayPool<byte>.Get(fileSize, out byte[] buffer);

// Sizes computed at runtime
using PooledArray<int> pooled = SystemArrayPool<int>.Get(width * height, out int[] buffer);

// Large arrays (1KB+)
using PooledArray<float> pooled = SystemArrayPool<float>.Get(10000, out float[] buffer);
```

### ⚠️ CRITICAL: Use `pooled.Length`, NOT `buffer.Length`

The returned array may be LARGER than requested:

```csharp
// ✅ CORRECT - use pooled.Length
using PooledArray<int> pooled = SystemArrayPool<int>.Get(count, out int[] buffer);
for (int i = 0; i < pooled.Length; i++)  // Use pooled.Length!
{
    buffer[i] = ProcessItem(i);
}

// ❌ WRONG - buffer.Length may be larger than requested
for (int i = 0; i < buffer.Length; i++)  // May iterate past valid data!
{
    // ...
}
```

---

## Quick Reference

| Pool                       | Size Type                  | Returns                 | Memory Safety                |
| -------------------------- | -------------------------- | ----------------------- | ---------------------------- |
| `WallstopArrayPool<T>`     | Fixed/constant             | Exact size              | ⚠️ Leaks with variable sizes |
| `WallstopFastArrayPool<T>` | Fixed/constant (unmanaged) | Exact size, not cleared | ⚠️ Leaks with variable sizes |
| `SystemArrayPool<T>`       | Variable/dynamic           | At least requested      | ✅ Safe for any size         |

---

## Pattern Examples

### APIs That Consume the Whole Array

When an API reads the entire physical array and accepts no logical length (for example,
`AssetDatabase.FindAssets` with `searchInFolders`), do not pass a possibly oversized
`SystemArrayPool<T>` rental. Do not switch to `WallstopArrayPool<T>.Get(collection.Count)` for
an unbounded count: each distinct exact size retains a pool bucket. Use an exact owned array
unless the possible sizes form a small, fixed set. Measure small, infrequent editor calls before
adding pooling; rent/return can cost more than the allocation.

### Fixed-Size Buffer

```csharp
// PRNG state - always exactly 4 ulongs
using PooledArray<ulong> pooled = WallstopArrayPool<ulong>.Get(4, out ulong[] state);
InitializeState(state);
// state is automatically returned to pool when pooled is disposed
```

### Variable-Size Buffer

```csharp
// Sorting - size depends on input
public void Sort<T>(IList<T> list)
{
    using PooledArray<T> pooled = SystemArrayPool<T>.Get(list.Count, out T[] temp);
    for (int i = 0; i < pooled.Length; i++)
    {
        temp[i] = list[i];
    }
    // ... sorting logic using temp ...
}
```

### Nested Pooling

```csharp
using PooledArray<int> outer = SystemArrayPool<int>.Get(outerSize, out int[] outerBuffer);
for (int i = 0; i < outer.Length; i++)
{
    using PooledArray<int> inner = SystemArrayPool<int>.Get(innerSize, out int[] innerBuffer);
    // Process with innerBuffer
}
// Both are properly returned to pools
```

---

## Related Skills

- [use-pooling](./use-pooling.md) - Collection pooling (List, HashSet, StringBuilder)
- [avoid-allocations](./avoid-allocations.md) - Avoiding heap allocations and boxing
- [high-performance-csharp](./high-performance-csharp.md) - Core zero-allocation patterns
- [refactor-to-zero-alloc](./refactor-to-zero-alloc.md) - Migration from allocating to pooled code
