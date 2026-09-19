# use-serializable-types - Part 2

## Split Content

## SerializableHashSet&lt;T&gt;

A Unity-serializable hash set for storing unique elements.

### Basic Usage

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

public sealed class AchievementTracker : MonoBehaviour
{
    [SerializeField]
    private SerializableHashSet<string> _unlockedAchievements = new();

    public void Unlock(string achievementId)
    {
        if (_unlockedAchievements.Add(achievementId))
        {
            Debug.Log($"Achievement unlocked: {achievementId}");
        }
    }

    public bool IsUnlocked(string achievementId)
    {
        return _unlockedAchievements.Contains(achievementId);
    }
}
```

### With Custom Comparer

```csharp
// Case-insensitive string set
SerializableHashSet<string> tags = new(StringComparer.OrdinalIgnoreCase);
tags.Add("Player");
tags.Contains("PLAYER");  // true

// From existing collection
string[] initialTags = { "Enemy", "Boss", "Flying" };
SerializableHashSet<string> enemies = new(initialTags);
```

### Set Operations

```csharp
SerializableHashSet<string> setA = new() { "A", "B", "C" };
SerializableHashSet<string> setB = new() { "B", "C", "D" };

// Union
setA.UnionWith(setB);  // A, B, C, D

// Intersection
setA.IntersectWith(setB);  // B, C

// Difference
setA.ExceptWith(setB);  // A

// Convert to standard HashSet
HashSet<string> copy = setA.ToHashSet();
```

---

## SerializableNullable&lt;T&gt;

A Unity-serializable alternative to `Nullable<T>` for value types. Shows a checkbox in the Inspector to toggle whether a value is set.

### Basic Usage

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

public sealed class SpawnSettings : MonoBehaviour
{
    [SerializeField]
    private SerializableNullable<float> _respawnDelay = new(5f);  // Has value

    [SerializeField]
    private SerializableNullable<int> _maxSpawns = new();  // No value (null)

    private void Start()
    {
        // Check if value exists
        if (_respawnDelay.HasValue)
        {
            Debug.Log($"Respawn delay: {_respawnDelay.Value}");
        }

        // TryGetValue pattern
        if (_maxSpawns.TryGetValue(out int max))
        {
            Debug.Log($"Max spawns: {max}");
        }

        // Get with default
        float delay = _respawnDelay.GetValueOrDefault(3f);
        int spawns = _maxSpawns.GetValueOrDefault(10);
    }
}
```

### Value Management

```csharp
SerializableNullable<int> score = new();

// Set value
score.SetValue(100);
Debug.Log(score.HasValue);  // true
Debug.Log(score.Value);     // 100

// Clear value
score.Clear();
Debug.Log(score.HasValue);  // false

// Implicit conversions
SerializableNullable<int> fromValue = 42;
SerializableNullable<int> fromNullable = (int?)null;
int? toNullable = score;  // Works both ways
```

### Use Cases

- Optional configuration values
- Fields that may or may not be set in Inspector
- Nullable value types in save data
- Optional parameters with clear "not set" state

---
