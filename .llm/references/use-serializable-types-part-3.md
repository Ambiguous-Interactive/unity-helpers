# use-serializable-types - Part 3

## Split Content

## SerializableType

Stores a `System.Type` reference that survives serialization. Displays a searchable dropdown in the Inspector.

### Basic Usage

```csharp
using System;
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

public sealed class SpawnRule : MonoBehaviour
{
    [SerializeField]
    private SerializableType _behaviourType = new(typeof(EnemyController));

    private void SpawnEnemy(GameObject prefab)
    {
        // Resolve the stored type
        Type type = _behaviourType.Value;
        if (type != null)
        {
            prefab.AddComponent(type);
        }
    }
}
```

### Type Resolution

```csharp
SerializableType enemyType = new(typeof(EnemyController));

// Check if type is set
if (!enemyType.IsEmpty)
{
    // Get resolved type (null if type no longer exists)
    Type type = enemyType.Value;

    // Try pattern for safer access
    if (enemyType.TryGetValue(out Type resolved))
    {
        Debug.Log($"Type: {resolved.Name}");
    }

    // Display name for UI
    string displayName = enemyType.DisplayName;
}

// Create from type
SerializableType fromType = SerializableType.FromType(typeof(PlayerController));

// Assignment
enemyType.SetType(typeof(BossController));
```

### Inspector Features

- Searchable dropdown showing all available types
- Grouped by namespace
- Shows friendly display names
- Handles type renames/refactors gracefully

---

## WGuid

A Unity-serializable wrapper for `System.Guid`. Stores as two longs for efficient serialization.

### Basic Usage

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

public sealed class Entity : MonoBehaviour
{
    [SerializeField]
    private WGuid _id = WGuid.NewGuid();

    public WGuid Id => _id;

    private void Awake()
    {
        if (_id == WGuid.Empty)
        {
            _id = WGuid.NewGuid();
        }
        Debug.Log($"Entity ID: {_id}");
    }
}
```

### Conversion and Parsing

```csharp
// Generate new GUID
WGuid newId = WGuid.NewGuid();

// Convert to/from System.Guid (implicit)
Guid systemGuid = newId;
WGuid fromSystem = systemGuid;

// Parse from string
WGuid parsed = WGuid.Parse("2f3a9b4c-8d1f-4cba-8df7-2af00f5c6c1e");

// Safe parsing
if (WGuid.TryParse(userInput, out WGuid guid))
{
    Debug.Log($"Valid GUID: {guid}");
}

// From byte array
byte[] bytes = Guid.NewGuid().ToByteArray();
WGuid fromBytes = new WGuid(bytes);

// String output
string str = newId.ToString();
```

### Comparison and Collections

```csharp
WGuid id1 = WGuid.NewGuid();
WGuid id2 = WGuid.NewGuid();

// Equality
bool same = id1 == id2;
bool different = id1 != id2;

// Empty check
bool isEmpty = id1 == WGuid.Empty;

// Use in collections
HashSet<WGuid> pending = new();
pending.Add(id1);

Dictionary<WGuid, string> names = new();
names[id1] = "Player";
```

---

## Quick Reference

| Type                      | Create                  | Check Value        | Get Value                      |
| ------------------------- | ----------------------- | ------------------ | ------------------------------ |
| `SerializableDictionary`  | `new()`                 | `ContainsKey(key)` | `TryGetValue(key, out value)`  |
| `SerializableSortedDict`  | `new()`                 | `ContainsKey(key)` | `TryGetValue(key, out value)`  |
| `SerializableHashSet`     | `new()`                 | `Contains(item)`   | N/A (set membership)           |
| `SerializableNullable<T>` | `new()` or `new(value)` | `HasValue`         | `Value` or `GetValueOrDefault` |
| `SerializableType`        | `new(typeof(T))`        | `!IsEmpty`         | `Value` or `TryGetValue`       |
| `WGuid`                   | `WGuid.NewGuid()`       | `!= WGuid.Empty`   | Implicit conversion to `Guid`  |

---

## Related Skills

- [Serializable Types Patterns](../skills/use-serializable-types-patterns.md) - Common patterns and integration examples
- [Serialization](../skills/use-serialization.md) - JSON and Protobuf serialization details
- [Data Structures](../skills/use-data-structures.md) - Other available data structures
