# use-pooling - Part 1

## Split Content

**Trigger**: When working with frequently allocated collections to avoid GC pressure.

---

## API Quick Reference

**Namespace**: `WallstopStudios.UnityHelpers.Utils`

| API Call                                                    | Returns                         | Purpose                             |
| ----------------------------------------------------------- | ------------------------------- | ----------------------------------- |
| `Buffers<T>.List.Get(out List<T>)`                          | `PooledResource<List<T>>`       | Pooled list                         |
| `Buffers<T>.HashSet.Get(out HashSet<T>)`                    | `PooledResource<HashSet<T>>`    | Pooled hash set                     |
| `Buffers<T>.Queue.Get(out Queue<T>)`                        | `PooledResource<Queue<T>>`      | Pooled queue                        |
| `Buffers<T>.Stack.Get(out Stack<T>)`                        | `PooledResource<Stack<T>>`      | Pooled stack                        |
| `Buffers.StringBuilder.Get(out StringBuilder)`              | `PooledResource<StringBuilder>` | Pooled string builder               |
| `Buffers<T>.GetList(int capacity, out List<T>)`             | `PooledResource<List<T>>`       | Pooled list with initial capacity   |
| `Buffers.GetStringBuilder(int capacity, out StringBuilder)` | `PooledResource<StringBuilder>` | Pooled string builder with capacity |

---

## Collection Pooling with Buffers&lt;T&gt;

### List Pooling

```csharp
using WallstopStudios.UnityHelpers.Utils;

// Get a pooled list - returns PooledResource<List<T>>
using PooledResource<List<Enemy>> lease = Buffers<Enemy>.List.Get(out List<Enemy> enemies);

// Use the list normally
foreach (Enemy e in GetAllEnemies())
{
    if (e.IsActive)
    {
        enemies.Add(e);
    }
}

ProcessEnemies(enemies);

// List is automatically returned to pool when lease is disposed
```

### HashSet Pooling

```csharp
using WallstopStudios.UnityHelpers.Utils;

using PooledResource<HashSet<int>> lease = Buffers<int>.HashSet.Get(out HashSet<int> visited);

// Use the hashset normally
visited.Add(startNode);
while (queue.Count > 0)
{
    int node = queue.Dequeue();
    foreach (int neighbor in GetNeighbors(node))
    {
        if (visited.Add(neighbor))
        {
            queue.Enqueue(neighbor);
        }
    }
}

// HashSet is automatically returned to pool when lease is disposed
```

---

## Pattern: Zero-Allocation Method

### Before (Allocating)

```csharp
// ❌ Creates new list every call
public List<Enemy> GetEnemiesInRange(Vector2 center, float radius)
{
    List<Enemy> result = new List<Enemy>();
    foreach (Enemy enemy in allEnemies)
    {
        if (Vector2.Distance(center, enemy.Position) < radius)
        {
            result.Add(enemy);
        }
    }
    return result;
}
```

### After (Zero-Allocation)

```csharp
// ✅ Uses caller-provided list
public void GetEnemiesInRange(Vector2 center, float radius, List<Enemy> result)
{
    result.Clear();
    foreach (Enemy enemy in allEnemies)
    {
        if (Vector2.Distance(center, enemy.Position) < radius)
        {
            result.Add(enemy);
        }
    }
}

// Usage with pooling
using PooledResource<List<Enemy>> lease = Buffers<Enemy>.List.Get(out List<Enemy> nearbyEnemies);
GetEnemiesInRange(playerPos, 10f, nearbyEnemies);
foreach (Enemy enemy in nearbyEnemies)
{
    // Process enemy
}
```

---

## Pattern: Temporary Processing

```csharp
using WallstopStudios.UnityHelpers.Utils;

public void ProcessItems()
{
    // Get pooled list for temporary use
    using PooledResource<List<Item>> lease = Buffers<Item>.List.Get(out List<Item> validItems);

    // Filter items
    foreach (Item item in allItems)
    {
        if (item.IsValid)
        {
            validItems.Add(item);
        }
    }

    // Sort
    validItems.Sort((a, b) => a.Priority.CompareTo(b.Priority));

    // Process
    foreach (Item item in validItems)
    {
        ProcessItem(item);
    }

    // List automatically returned to pool
}
```

---
