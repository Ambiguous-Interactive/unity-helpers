# use-serializable-types - Part 1

## Split Content

**Trigger**: When you need Unity-serializable collections (dictionaries, hash sets), nullable value types, type references, or GUIDs that work in the Inspector, JSON, and Protobuf.

---

## When to Use This Skill

Use this skill when you need:

- Dictionary or hash set collections that serialize in Unity Inspector
- Nullable value types (`int?`, `float?`) that Unity can serialize
- Type references (`System.Type`) stored in assets
- GUIDs that survive Unity serialization

For common patterns and integration examples, see [Serializable Types Patterns](../skills/use-serializable-types-patterns.md).
For serialization system details, see [Serialization](../skills/use-serialization.md).

---

## Overview

Unity's serialization system has limitations - it cannot serialize dictionaries, hash sets, nullable value types, `System.Type` references, or `System.Guid` directly. This package provides serializable wrappers that work seamlessly with:

- Unity Inspector
- Unity serialization (ScriptableObjects, MonoBehaviours)
- JSON serialization via `Serializer.JsonSerialize()`
- Protobuf serialization via `Serializer.ProtoSerialize()`

| Type                                         | Purpose                                  | Namespace                                                  |
| -------------------------------------------- | ---------------------------------------- | ---------------------------------------------------------- |
| `SerializableDictionary<TKey, TValue>`       | Dictionary with Inspector support        | `WallstopStudios.UnityHelpers.Core.DataStructure.Adapters` |
| `SerializableSortedDictionary<TKey, TValue>` | Sorted dictionary with Inspector support | `WallstopStudios.UnityHelpers.Core.DataStructure.Adapters` |
| `SerializableHashSet<T>`                     | HashSet with Inspector support           | `WallstopStudios.UnityHelpers.Core.DataStructure.Adapters` |
| `SerializableNullable<T>`                    | Nullable value types                     | `WallstopStudios.UnityHelpers.Core.DataStructure.Adapters` |
| `SerializableType`                           | `System.Type` reference                  | `WallstopStudios.UnityHelpers.Core.DataStructure.Adapters` |
| `WGuid`                                      | Unity-serializable GUID                  | `WallstopStudios.UnityHelpers.Core.DataStructure.Adapters` |

---

## SerializableDictionary&lt;TKey, TValue&gt;

A Unity-serializable dictionary that displays in the Inspector and supports JSON/Protobuf.

### Basic Usage

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

public sealed class LootTable : MonoBehaviour
{
    [SerializeField]
    private SerializableDictionary<string, int> _dropWeights = new();

    private void Awake()
    {
        // Use like a regular dictionary
        _dropWeights["Common"] = 80;
        _dropWeights["Rare"] = 15;
        _dropWeights["Legendary"] = 5;

        // All standard dictionary operations work
        if (_dropWeights.TryGetValue("Rare", out int weight))
        {
            Debug.Log($"Rare drop weight: {weight}");
        }

        foreach (KeyValuePair<string, int> entry in _dropWeights)
        {
            Debug.Log($"{entry.Key}: {entry.Value}");
        }
    }
}
```

### With Complex Values (Cache Pattern)

For complex value types that need special serialization handling, use the three-parameter variant with a cache class:

```csharp
using System;
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

[Serializable]
public sealed class WeaponDefinition
{
    public string DisplayName;
    public int Damage;
    public float AttackSpeed;
}

[Serializable]
public sealed class WeaponCache : SerializableDictionary.Cache<WeaponDefinition>
{
}

public sealed class WeaponRegistry : MonoBehaviour
{
    [SerializeField]
    private SerializableDictionary<string, WeaponDefinition, WeaponCache> _weapons = new();

    public WeaponDefinition GetWeapon(string id)
    {
        return _weapons.TryGetValue(id, out WeaponDefinition weapon) ? weapon : null;
    }
}
```

### Initialize From Existing Dictionary

```csharp
// Copy from standard dictionary
Dictionary<string, int> source = new()
{
    { "Gold", 100 },
    { "Silver", 50 }
};
SerializableDictionary<string, int> serializable = new(source);
```

---

## SerializableSortedDictionary&lt;TKey, TValue&gt;

A sorted dictionary that maintains key ordering. Keys must implement `IComparable<TKey>`.

### Basic Usage

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

public sealed class Leaderboard : MonoBehaviour
{
    [SerializeField]
    private SerializableSortedDictionary<string, int> _scores = new();

    private void Start()
    {
        _scores["Alice"] = 1200;
        _scores["Bob"] = 900;
        _scores["Charlie"] = 1500;

        // Iterates in sorted key order (Alice, Bob, Charlie)
        foreach (KeyValuePair<string, int> entry in _scores)
        {
            Debug.Log($"{entry.Key}: {entry.Value}");
        }
    }
}
```

### With Cache Pattern

```csharp
[Serializable]
public sealed class QuestDefinition
{
    public string Title;
    public int RequiredLevel;
}

[Serializable]
public sealed class QuestCache : SerializableDictionary.Cache<QuestDefinition>
{
}

// Keys sorted by quest ID
[Serializable]
public sealed class QuestDictionary
    : SerializableSortedDictionary<int, QuestDefinition, QuestCache>
{
}
```

---
