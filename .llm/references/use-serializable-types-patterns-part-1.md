# use-serializable-types-patterns - Part 1

## Split Content

**Trigger**: When implementing common patterns with Unity-serializable collections, integrating with JSON/Protobuf, or needing advanced usage examples.

---

## When to Use This Skill

Use this skill when you need:

- Common patterns for configuration, registries, and tracking systems
- Integration with JSON and Protobuf serialization
- Advanced usage examples for serializable collections
- Best practices for working with SerializableDictionary, SerializableHashSet, and related types

For basic type definitions and API reference, see [Serializable Types](../skills/use-serializable-types.md).
For serialization system details, see [Serialization](../skills/use-serialization.md).

---

## JSON/Protobuf Compatibility

All serializable types work automatically with the package's serialization system.

### JSON Example

```csharp
using WallstopStudios.UnityHelpers.Core.Serialization;

[Serializable]
public class GameState
{
    public SerializableDictionary<string, int> Scores = new();
    public SerializableHashSet<string> UnlockedLevels = new();
    public SerializableNullable<float> BestTime = new();
    public WGuid PlayerId = WGuid.NewGuid();
}

// Serialize to JSON
GameState state = new();
state.Scores["Level1"] = 1500;
state.UnlockedLevels.Add("Level1");
state.BestTime.SetValue(45.3f);

string json = Serializer.JsonSerialize(state, prettyPrint: true);

// Deserialize from JSON
GameState loaded = Serializer.JsonDeserialize<GameState>(json);
```

### Protobuf Example

```csharp
using ProtoBuf;
using WallstopStudios.UnityHelpers.Core.Serialization;

[ProtoContract]
public class SaveData
{
    [ProtoMember(1)]
    public SerializableDictionary<string, int> Inventory { get; set; } = new();

    [ProtoMember(2)]
    public SerializableHashSet<string> Achievements { get; set; } = new();

    [ProtoMember(3)]
    public SerializableNullable<int> HighScore { get; set; } = new();

    [ProtoMember(4)]
    public WGuid SessionId { get; set; } = WGuid.NewGuid();
}

// Serialize to bytes
SaveData data = new();
byte[] bytes = Serializer.ProtoSerialize(data);

// Deserialize from bytes
SaveData loaded = Serializer.ProtoDeserialize<SaveData>(bytes);
```

---

## Common Patterns

### Configuration with Defaults

```csharp
public sealed class EnemyConfig : MonoBehaviour
{
    [SerializeField]
    private SerializableDictionary<string, float> _statMultipliers = new()
    {
        { "Health", 1.0f },
        { "Damage", 1.0f },
        { "Speed", 1.0f }
    };

    [SerializeField]
    private SerializableNullable<float> _bossMultiplier = new();

    public float GetMultiplier(string stat)
    {
        float baseMultiplier = _statMultipliers.GetValueOrDefault(stat, 1.0f);
        float bossBonus = _bossMultiplier.GetValueOrDefault(1.0f);
        return baseMultiplier * bossBonus;
    }
}
```

### Entity Registry with GUIDs

```csharp
public sealed class EntityRegistry : MonoBehaviour
{
    [SerializeField]
    private SerializableDictionary<WGuid, string> _entityNames = new();

    public void Register(WGuid id, string name)
    {
        _entityNames[id] = name;
    }

    public string GetName(WGuid id)
    {
        return _entityNames.GetValueOrDefault(id, "Unknown");
    }
}
```

### Type-Based Factory

```csharp
public sealed class EnemyFactory : MonoBehaviour
{
    [SerializeField]
    private SerializableDictionary<string, SerializableType> _enemyTypes = new();

    public Component SpawnEnemy(string enemyId, GameObject prefab)
    {
        if (_enemyTypes.TryGetValue(enemyId, out SerializableType typeRef))
        {
            Type type = typeRef.Value;
            if (type != null)
            {
                return prefab.AddComponent(type);
            }
        }
        return null;
    }
}
```

### Unlockables Tracking

```csharp
public sealed class ProgressTracker : MonoBehaviour
{
    [SerializeField]
    private SerializableHashSet<string> _unlockedItems = new();

    [SerializeField]
    private SerializableSortedDictionary<string, int> _itemCounts = new();

    public void UnlockItem(string itemId)
    {
        _unlockedItems.Add(itemId);
    }

    public void AddItem(string itemId, int count)
    {
        if (_itemCounts.TryGetValue(itemId, out int current))
        {
            _itemCounts[itemId] = current + count;
        }
        else
        {
            _itemCounts[itemId] = count;
        }
    }
}
```
