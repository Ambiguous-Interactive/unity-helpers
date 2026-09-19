# use-pooling - Part 3

## Split Content

### ❌ Returning Pooled List

```csharp
using WallstopStudios.UnityHelpers.Utils;

// ❌ Returns list that will be returned to pool
public List<Item> GetItems()
{
    using PooledResource<List<Item>> lease = Buffers<Item>.List.Get(out List<Item> items);
    // ... populate items ...
    return items;  // ❌ List will be pooled when method exits!
}

// ✅ Return a copy or use out parameter
public List<Item> GetItems()
{
    using PooledResource<List<Item>> lease = Buffers<Item>.List.Get(out List<Item> items);
    // ... populate items ...
    return new List<Item>(items);  // Return copy
}

// ✅ Or use out parameter pattern
public void GetItems(List<Item> result)
{
    result.Clear();
    // ... populate result ...
}
```

---

## StringBuilder Pooling

```csharp
using WallstopStudios.UnityHelpers.Utils;

// StringBuilder pooling - returns PooledResource<StringBuilder>
using PooledResource<StringBuilder> sbLease = Buffers.StringBuilder.Get(out StringBuilder sb);
sb.Append("Hello ");
sb.Append("World");
string result = sb.ToString();

// With initial capacity
using PooledResource<StringBuilder> sbLease = Buffers.GetStringBuilder(256, out StringBuilder sb);
```

---

## Array Pooling

For array pooling, see the [Array Pooling Guide](../skills/use-array-pool.md).

**Choose the Right Pool:**

| Pool                       | Use Case                                  | Returns            |
| -------------------------- | ----------------------------------------- | ------------------ |
| `WallstopArrayPool<T>`     | Fixed/constant sizes only                 | Exact size         |
| `WallstopFastArrayPool<T>` | Fixed sizes, unmanaged types, no clearing | Exact size         |
| `SystemArrayPool<T>`       | Variable/dynamic sizes                    | At least requested |

```csharp
// Fixed size (e.g., PRNG state buffers)
using PooledArray<ulong> pooled = WallstopArrayPool<ulong>.Get(4, out ulong[] state);

// Variable size (e.g., sorting buffers)
using PooledArray<T> pooled = SystemArrayPool<T>.Get(list.Count, out T[] temp);
for (int i = 0; i < pooled.Length; i++)  // Use pooled.Length, NOT buffer.Length!
{
    temp[i] = list[i];
}
```

Quick summary:

- **Fixed sizes** -> `WallstopArrayPool<T>.Get()`
- **Variable sizes** -> `SystemArrayPool<T>.Get()`

---

## Unity GameObject Pooling

For GameObjects (bullets, enemies, effects), use Unity's `ObjectPool<T>`:

```csharp
using UnityEngine.Pool;

private ObjectPool<GameObject> _bulletPool;

void Awake()
{
    _bulletPool = new ObjectPool<GameObject>(
        createFunc: () => Instantiate(_bulletPrefab),
        actionOnGet: obj => obj.SetActive(true),
        actionOnRelease: obj => obj.SetActive(false),
        actionOnDestroy: obj => Destroy(obj),
        defaultCapacity: 50,
        maxSize: 200
    );
}

public GameObject SpawnBullet() => _bulletPool.Get();
public void ReturnBullet(GameObject bullet) => _bulletPool.Release(bullet);
```

See [unity-performance-patterns](../skills/unity-performance-patterns.md) for more Unity-specific pooling.

---

## Performance Comparison

```csharp
using WallstopStudios.UnityHelpers.Utils;

// ❌ Allocates new list (GC pressure)
void ProcessAllocating()
{
    List<Item> items = new List<Item>();  // Allocation!
    // ... use items ...
}

// ✅ Zero allocation (uses pool)
void ProcessPooled()
{
    using PooledResource<List<Item>> lease = Buffers<Item>.List.Get(out List<Item> items);
    // ... use items ...
}
```

| Approach          | First Call | Subsequent Calls |
| ----------------- | ---------- | ---------------- |
| `new List<T>()`   | Allocates  | Allocates        |
| `Buffers<T>.List` | Allocates  | Zero allocation  |

---

## When to Use Pooling

✅ **Use pooling for:**

- Hot paths (Update, OnGUI, tight loops)
- Temporary processing collections
- Methods called frequently
- Collections with short lifetimes

❌ **Don't use pooling for:**

- Long-lived collections (class fields)
- Collections returned to callers (unless copied)
- Small, infrequent allocations

---

## Related Skills

- [high-performance-csharp](../skills/high-performance-csharp.md) - Core performance patterns
- [avoid-allocations](../skills/avoid-allocations.md) - Avoiding heap allocations and boxing
- [unity-performance-patterns](../skills/unity-performance-patterns.md) - Unity GameObject pooling
- [use-array-pool](../skills/use-array-pool.md) - Array pooling guide
- [refactor-to-zero-alloc](../skills/refactor-to-zero-alloc.md) - Migration patterns
