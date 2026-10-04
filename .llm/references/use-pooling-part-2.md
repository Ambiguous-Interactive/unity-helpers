# use-pooling - Part 2

## Split Content

## Pattern: Nested Pooling

```csharp
using WallstopStudios.UnityHelpers.Utils;

public void ProcessGroups()
{
    using PooledResource<List<ItemGroup>> groupLease = Buffers<ItemGroup>.List.Get(out List<ItemGroup> groups);

    foreach (ItemGroup group in GetGroups())
    {
        groups.Add(group);
    }

    foreach (ItemGroup group in groups)
    {
        // Nested pooled list
        using PooledResource<List<Item>> itemLease = Buffers<Item>.List.Get(out List<Item> items);

        GetItemsInGroup(group, items);
        ProcessItems(items);

        // Inner list returned to pool
    }

    // Outer list returned to pool
}
```

---

## Scratch Buffers Inside Pool Infrastructure

Do not rent purge or disposal snapshots from `Buffers<T>` or `WallstopGenericPool<T>` inside
those pools' own implementation: acquiring or returning the scratch lease can trigger another
purge. Use an independent scratch cache with no lifecycle callbacks or registry participation.

- A `[ThreadStatic]` field isolates threads, not nested calls. Detach a free buffer on checkout
  so a callback that reenters the same pool, or another pool with the same element type, receives
  a distinct buffer until the outer call returns it.
- Keep the checkout alive through the last callback. Return it in `finally`, and clear all item
  references even when the buffer will be discarded.
- Prefer a concrete internal struct `IDisposable` lease and `using` for lexical scratch ownership.
  Reuse `DisposalLease` so copied or stale leases cannot return a buffer twice; a per-struct flag
  cannot protect copies. Avoid boxing the lease or allocating a return delegate.
- Preserve lazy acquisition: when a snapshot is needed only after selecting an item, keep a default
  local lease, acquire it at that point and dispose it in `finally`. Do not force eager rentals or
  mutate a readonly `using` local to acquire later. Scoped leases follow the package's threading
  contract, including single-thread use under `SINGLE_THREADED`.
- Bound both retained buffer count and capacity per thread and closed generic type. Discard
  oversized or excess returns; a low-memory purge must not permanently retain its largest batch.
- Preserve retirement ordering: detach every selected item and update bookkeeping before invoking
  callbacks. Buffer reuse must not turn a stable snapshot back into live pool enumeration.
- Verify nested ownership, repeated reuse, reference clearing, capacity/depth bounds and thread
  isolation. Warmed allocation measurements need a working known-allocation positive control;
  cold growth and overflow still allocate, and host results do not establish player throughput.

## Pattern: Conditional Pooling

```csharp
using WallstopStudios.UnityHelpers.Utils;

public void MaybeProcessItems(bool shouldProcess)
{
    if (!shouldProcess)
    {
        return;
    }

    // Pool lease is only acquired when needed
    using PooledResource<List<Item>> lease = Buffers<Item>.List.Get(out List<Item> items);

    // ... use items ...
}
```

---

## Pattern: Populating from IEnumerable

Prefer `AddRange` when the source implements `ICollection<T>` for the pooled destination's exact
element type. An arbitrary `IEnumerable<T>` does not guarantee the bulk-copy path.

```csharp
using WallstopStudios.UnityHelpers.Utils;

// With an ICollection<T> source, AddRange can reserve capacity and copy directly.
using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> result);
foreach (T item in source)
{
    result.Add(item);  // May trigger multiple resizes
}

// Preferred when source implements ICollection<T> for this result's T.
using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> result);
result.AddRange(source);
```

**Why the `ICollection<T>` path helps:**

1. **Capacity pre-allocation**: If source is `ICollection<T>`, AddRange queries `Count` first
2. **Bulk copy**: For arrays and `List<T>`, copies directly instead of calling `Add` for each element
3. **Potential zero-allocation**: May avoid enumerator allocation entirely
4. **Fewer resizes**: Pre-allocated capacity means fewer or no list resizes

`List<Derived>` does not implement `ICollection<Base>`. Passing it to `List<Base>.AddRange` uses
the covariant enumerable fallback and boxes its struct enumerator for nonempty transfers. Keep `foreach` over the
concrete typed source for that transfer. Reserve destination capacity when its required size is known.

### When to Use for Loop Instead

Use a per-element loop when you must **transform** or **filter** elements. Prefer typed `foreach` unless the body needs the index:

```csharp
// ✅ TRANSFORMATION: Process each element
foreach (string guid in guids)
{
    paths.Add(ConvertGuidToPath(guid));  // Transforming - can't use AddRange
}

// ✅ FILTERING: Process each element
foreach (T item in items)
{
    if (item.IsValid)
    {
        filtered.Add(item);  // Filtering - can't use AddRange
    }
}
```

| Scenario                | Use                                                                               |
| ----------------------- | --------------------------------------------------------------------------------- |
| Copy all elements as-is | `AddRange` for `ICollection<T>`; typed `foreach` for differing list element types |
| Transform each element  | `foreach` + `Add(Transform(item))`                                                |
| Filter elements         | `foreach` + conditional `Add`                                                     |
| Transform AND filter    | `foreach` + conditional `Add(Transform(item))`                                    |

---

## Common Mistakes

### Non-Existent APIs (Common LLM Mistakes)

The following APIs do NOT exist. Use the correct alternatives:

| Does NOT Exist           | Correct Alternative                     |
| ------------------------ | --------------------------------------- |
| `Buffers.Lease<T>`       | `Buffers<T>.List.Get(out List<T>)`      |
| `Buffers<T>.Lease`       | `Buffers<T>.List.Get(out List<T>)`      |
| `Buffers.Get<T>()`       | `Buffers<T>.List.Get(out List<T>)`      |
| `Buffers<T>.Get()`       | `Buffers<T>.List.Get(out List<T>)`      |
| `Buffers<T>.Rent()`      | `Buffers<T>.List.Get(out List<T>)`      |
| `BufferPool<T>`          | `Buffers<T>` (static class)             |
| `ListPool<T>.Get()`      | `Buffers<T>.List.Get(out List<T>)`      |
| `Buffers.Lease<List<T>>` | `PooledResource<List<T>>` (return type) |

### ❌ Forgetting `using`

```csharp
using WallstopStudios.UnityHelpers.Utils;

// ❌ Memory leak - list never returned to pool
PooledResource<List<Item>> lease = Buffers<Item>.List.Get(out List<Item> items);
// ... use items ...
// lease never disposed!

// ✅ Always use 'using'
using PooledResource<List<Item>> lease = Buffers<Item>.List.Get(out List<Item> items);
```

### ❌ Using List After Dispose

```csharp
using WallstopStudios.UnityHelpers.Utils;

List<Item> storedItems;

void Bad()
{
    using PooledResource<List<Item>> lease = Buffers<Item>.List.Get(out List<Item> items);
    items.Add(new Item());
    storedItems = items;  // ❌ Storing reference to pooled list!
}

void UseLater()
{
    foreach (Item item in storedItems)  // ❌ List may be in use elsewhere!
    {
        // Undefined behavior
    }
}
```
