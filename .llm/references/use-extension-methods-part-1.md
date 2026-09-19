# use-extension-methods - Part 1

## Split Content

**Trigger**: When manipulating collections, strings, or colors and need convenient, performant utilities beyond built-in methods.

---

## Dictionary Extensions

### GetOrAdd - Add If Missing

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;

// Get existing or create new value with factory
Dictionary<string, List<Item>> itemsByCategory = new();
List<Item> weapons = itemsByCategory.GetOrAdd("weapons", () => new List<Item>());
weapons.Add(sword);

// With key-based factory
Dictionary<int, PlayerData> playerCache = new();
PlayerData data = playerCache.GetOrAdd(playerId, id => LoadPlayerData(id));

// With parameterless constructor constraint
Dictionary<string, List<int>> scores = new();
List<int> playerScores = scores.GetOrAdd<string, List<int>>("player1");  // Creates new List<int>()
```

### GetOrElse - Default Without Modification

```csharp
// Get value or return default without modifying dictionary
IReadOnlyDictionary<string, int> config = GetConfig();
int maxPlayers = config.GetOrElse("maxPlayers", () => 4);
int timeout = config.GetOrElse("timeout", 30);  // Direct value overload
```

### TryRemove - Safe Removal With Value

```csharp
// Remove and get removed value in one operation
Dictionary<int, Enemy> enemies = new();
if (enemies.TryRemove(enemyId, out Enemy removed))
{
    removed.OnDespawn();
}
```

### AddOrUpdate - Upsert Pattern

```csharp
// Add new or update existing
Dictionary<string, int> scoreboard = new();
scoreboard.AddOrUpdate(
    playerName,
    key => 1,                           // Creator: first kill
    (key, existing) => existing + 1     // Updater: increment kills
);
```

### Merge - Combine Dictionaries

```csharp
// Merge two dictionaries (right overwrites left)
var defaults = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
var overrides = new Dictionary<string, int> { ["b"] = 5, ["c"] = 3 };
Dictionary<string, int> merged = defaults.Merge(overrides);
// Result: { ["a"] = 1, ["b"] = 5, ["c"] = 3 }
```

---

## List Extensions

### Shuffle - Fisher-Yates In-Place

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;

List<Card> deck = GetAllCards();

// Shuffle using default PRNG
deck.Shuffle();

// Shuffle with custom random
deck.Shuffle(myRandom);
```

**Performance**: O(n), **Allocations**: None

### GetRandomElement - Random Selection

```csharp
List<Enemy> enemies = GetAllEnemies();
Enemy target = enemies.GetRandomElement();           // Uses PRNG.Instance
Enemy target2 = enemies.GetRandomElement(myRandom);  // Custom random
```

**Performance**: O(1), **Allocations**: None

### RemoveAtSwapBack - Fast Unordered Removal

```csharp
// ❌ Standard removal: O(n) - shifts all elements after index
enemies.RemoveAt(index);

// ✅ Swap-back removal: O(1) - swaps with last element, then removes last
enemies.RemoveAtSwapBack(index);  // Does not preserve order!
```

**Performance**: O(1), **Allocations**: None

### IndexOf / LastIndexOf with Predicate

```csharp
List<Enemy> enemies = GetEnemies();

// Find first enemy with low health
int index = enemies.IndexOf(e => e.Health < 10);

// Find last enemy that can attack
int lastIndex = enemies.LastIndexOf(e => e.CanAttack);
```

**Performance**: O(n), **Allocations**: None

### Shift - Rotate Elements

```csharp
List<int> numbers = new() { 1, 2, 3, 4, 5 };

numbers.Shift(2);   // Result: { 4, 5, 1, 2, 3 } (shift right)
numbers.Shift(-1);  // Result: { 2, 3, 4, 5, 1 } (shift left)
```

**Performance**: O(n), **Allocations**: None

### Reverse Range

```csharp
List<int> numbers = new() { 1, 2, 3, 4, 5 };
numbers.Reverse(1, 3);  // Result: { 1, 4, 3, 2, 5 }
```

**Performance**: O(n), **Allocations**: None

### Pooled Sorting

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;

List<Enemy> enemies = GetEnemies();

// Sort with custom comparer and algorithm
enemies.Sort(
    Comparer<Enemy>.Create((a, b) => a.Distance.CompareTo(b.Distance)),
    SortAlgorithm.Grail  // Stable, allocation-free
);

// Available algorithms:
// - Grail (default): Stable, O(n log n), allocation-free
// - Tim: Stable, fast for partially sorted data
// - PatternDefeatingQuickSort: Fast unstable sort
// - Insertion: Best for small or nearly-sorted lists
// - Ghost, Meteor, Power, Ska, Ipn, Smooth, Block, Ips4o, Glide, Flux
```

---
