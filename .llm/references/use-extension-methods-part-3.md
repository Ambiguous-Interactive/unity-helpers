# use-extension-methods - Part 3

## Split Content

## Common Patterns

### Pattern: Safe Dictionary Access

```csharp
// ❌ Verbose null checking
if (!cache.TryGetValue(key, out var value))
{
    value = CreateValue();
    cache[key] = value;
}

// ✅ Concise with GetOrAdd
var value = cache.GetOrAdd(key, () => CreateValue());
```

### Pattern: Fast Collection Processing

```csharp
// ❌ LINQ with allocations
var randomEnemy = enemies.OrderBy(_ => random.Next()).First();

// ✅ Zero-allocation random selection
var randomEnemy = enemies.GetRandomElement();
```

### Pattern: Unordered Fast Removal

```csharp
// ❌ O(n) removal in hot path
for (int i = activeProjectiles.Count - 1; i >= 0; i--)
{
    if (activeProjectiles[i].IsExpired)
    {
        activeProjectiles.RemoveAt(i);  // Shifts all elements!
    }
}

// ✅ O(1) removal when order doesn't matter
for (int i = activeProjectiles.Count - 1; i >= 0; i--)
{
    if (activeProjectiles[i].IsExpired)
    {
        activeProjectiles.RemoveAtSwapBack(i);  // Just swaps with last
    }
}
```

### Pattern: Thread-Safe Dictionary Operations

```csharp
// Works with ConcurrentDictionary automatically
ConcurrentDictionary<int, Player> players = new();

// These use ConcurrentDictionary's native thread-safe methods
players.GetOrAdd(playerId, id => new Player(id));
players.TryRemove(playerId, out var removed);
players.AddOrUpdate(playerId, _ => new Player(playerId), (_, p) => p.Update());
```

---

## Namespace

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;
```

All extension methods are in this namespace and available on their respective types once imported.
