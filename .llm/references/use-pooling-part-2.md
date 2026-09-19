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

When materializing an `IEnumerable<T>` into a pooled list, **always use `AddRange`** instead of `foreach` + `Add`:

```csharp
using WallstopStudios.UnityHelpers.Utils;

// ❌ BAD: foreach allocates an enumerator, no capacity pre-allocation
using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> result);
foreach (T item in source)
{
    result.Add(item);  // May trigger multiple resizes
}

// ✅ GOOD: AddRange is optimized for performance
using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> result);
result.AddRange(source);
```

**Why AddRange is better:**

1. **Capacity pre-allocation**: If source is `ICollection<T>`, AddRange queries `Count` first
2. **Bulk copy**: For arrays and `List<T>`, uses `Array.Copy` which is much faster
3. **Potential zero-allocation**: May avoid enumerator allocation entirely
4. **Fewer resizes**: Pre-allocated capacity means fewer or no list resizes

### When to Use for Loop Instead

Use indexed `for` loops only when you must **transform** or **filter** each element:

```csharp
// ✅ TRANSFORMATION: Must use for loop
for (int i = 0; i < guids.Length; i++)
{
    paths.Add(ConvertGuidToPath(guids[i]));  // Transforming - can't use AddRange
}

// ✅ FILTERING: Must use for loop
for (int i = 0; i < items.Length; i++)
{
    if (items[i].IsValid)
    {
        filtered.Add(items[i]);  // Filtering - can't use AddRange
    }
}
```

| Scenario                | Use                                             |
| ----------------------- | ----------------------------------------------- |
| Copy all elements as-is | `AddRange(source)`                              |
| Transform each element  | `for` loop + `Add(Transform(item))`             |
| Filter elements         | `for` loop + conditional `Add`                  |
| Transform AND filter    | `for` loop + conditional `Add(Transform(item))` |

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
