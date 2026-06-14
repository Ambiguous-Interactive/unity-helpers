# Skill: Use Serialization

<!-- trigger: serialize, json, protobuf, save, persist | Save files, network, persistence | Feature -->

**Trigger**: When serializing/deserializing data for save files, network, or persistence.

---

## Available Formats

| Format   | Use Case                            | Method                        |
| -------- | ----------------------------------- | ----------------------------- |
| JSON     | Human-readable, debugging, config   | `Serializer.JsonSerialize()`  |
| Protobuf | Compact binary, network, large data | `Serializer.ProtoSerialize()` |

---

## Error Handling

`Serializer` is the **single documented exception** to this repo's "never throw" rule (see [Defensive Programming](./defensive-programming.md)). Save/network data is too load-bearing for silent `default(T)`. **Every** deserialize entry point either throws `SerializationFailureException` or returns `false` via a `TryXxx` sibling. Full details: [Serialization Safety](./serialization-safety.md).

```csharp
// Throwing — catch SerializationFailureException for any format/stage.
try
{
    PlayerData data = Serializer.ProtoDeserialize<PlayerData>(bytes);
}
catch (SerializationFailureException ex)
{
    Debug.LogWarning($"Load failed: {ex.Format}/{ex.Stage} — {ex.Message}");
}

// Non-throwing — Try* returns false on null/empty/corrupt input.
if (Serializer.TryProtoDeserialize(bytes, out PlayerData data))
{
    Apply(data);
}
```

`Try*` swallows `SerializationInputException` and `SerializationCorruptDataException`. `SerializationTypeException` (unresolved polymorphic root) and `SerializationConfigurationException` (invalid `SerializationType`) still propagate — they are programmer errors.

---

## JSON Serialization

### Basic Usage

```csharp
using WallstopStudios.UnityHelpers.Core.Serialization;

// Serialize to string
PlayerData data = new PlayerData { Name = "Hero", Level = 42 };
string json = Serializer.JsonSerialize(data);

// Deserialize from string
PlayerData loaded = Serializer.JsonDeserialize<PlayerData>(json);
```

### Serialize to Bytes

```csharp
// For file/network use
byte[] bytes = Serializer.JsonSerializeToBytes(data);
PlayerData loaded = Serializer.JsonDeserializeFromBytes<PlayerData>(bytes);
```

### Pretty Print

```csharp
// Human-readable output
string prettyJson = Serializer.JsonSerialize(data, prettyPrint: true);
```

---

## Protobuf Serialization

### Setup

Add `[ProtoContract]` and `[ProtoMember]` attributes:

```csharp
using ProtoBuf;

[ProtoContract]
public class PlayerData
{
    [ProtoMember(1)]
    public string Name { get; set; }

    [ProtoMember(2)]
    public int Level { get; set; }

    [ProtoMember(3)]
    public List<Item> Inventory { get; set; }
}
```

### Basic Usage

```csharp
using WallstopStudios.UnityHelpers.Core.Serialization;

// Serialize to bytes
PlayerData data = new PlayerData { Name = "Hero", Level = 42 };
byte[] bytes = Serializer.ProtoSerialize(data);

// Deserialize from bytes
PlayerData loaded = Serializer.ProtoDeserialize<PlayerData>(bytes);
```

### Stream-Based

```csharp
// Write to stream
using (FileStream fs = File.Create("save.dat"))
{
    Serializer.ProtoSerialize(fs, data);
}

// Read from stream
using (FileStream fs = File.OpenRead("save.dat"))
{
    PlayerData loaded = Serializer.ProtoDeserialize<PlayerData>(fs);
}
```

---

## Supported Unity Types

Both JSON and Protobuf support these Unity types out of the box:

| Type                            | Notes                 |
| ------------------------------- | --------------------- |
| `Vector2`, `Vector3`, `Vector4` | All components        |
| `Vector2Int`, `Vector3Int`      | Integer vectors       |
| `Quaternion`                    | x, y, z, w components |
| `Color`, `Color32`              | RGBA                  |
| `Rect`, `RectInt`               | Position and size     |
| `Bounds`                        | Center and size       |
| `Matrix4x4`                     | All 16 values         |

---

## Serializable Collections

Use Unity Helpers serializable types for collections:

```csharp
using WallstopStudios.UnityHelpers.Core.Model;

[ProtoContract]
public class GameState
{
    // Dictionary support
    [ProtoMember(1)]
    public SerializableDictionary<string, int> Scores { get; set; }

    // HashSet support
    [ProtoMember(2)]
    public SerializableHashSet<string> UnlockedAchievements { get; set; }

    // Nullable value types
    [ProtoMember(3)]
    public SerializableNullable<int> HighScore { get; set; }
}
```

---

## Schema Evolution (Protobuf)

### Adding Fields

```csharp
[ProtoContract]
public class PlayerData
{
    [ProtoMember(1)]
    public string Name { get; set; }

    [ProtoMember(2)]
    public int Level { get; set; }

    // New field - old data will have default value
    [ProtoMember(3)]
    public int Gold { get; set; }
}
```

### Removing Fields

```csharp
[ProtoContract]
public class PlayerData
{
    [ProtoMember(1)]
    public string Name { get; set; }

    // Don't reuse member number 2!
    // [ProtoMember(2)] was OldField

    [ProtoMember(3)]
    public int Gold { get; set; }
}
```

### Reserved Numbers

```csharp
[ProtoContract]
[ProtoReserved(2, 5, 6)]  // Don't reuse these numbers
public class PlayerData
{
    [ProtoMember(1)]
    public string Name { get; set; }

    [ProtoMember(3)]
    public int Level { get; set; }
}
```

---

## Complete Example

### Data Classes

```csharp
using ProtoBuf;
using WallstopStudios.UnityHelpers.Core.Model;

[ProtoContract]
public class SaveData
{
    [ProtoMember(1)]
    public PlayerData Player { get; set; }

    [ProtoMember(2)]
    public WorldData World { get; set; }

    [ProtoMember(3)]
    public SerializableDictionary<string, QuestProgress> Quests { get; set; }
}

[ProtoContract]
public class PlayerData
{
    [ProtoMember(1)]
    public string Name { get; set; }

    [ProtoMember(2)]
    public int Level { get; set; }

    [ProtoMember(3)]
    public Vector3 Position { get; set; }

    [ProtoMember(4)]
    public Quaternion Rotation { get; set; }

    [ProtoMember(5)]
    public List<InventoryItem> Inventory { get; set; }
}
```

### Save/Load Manager

```csharp
using WallstopStudios.UnityHelpers.Core.Serialization;

public class SaveManager : MonoBehaviour
{
    private const string SaveFileName = "save.dat";

    public void Save(SaveData data)
    {
        string path = Path.Combine(Application.persistentDataPath, SaveFileName);
        byte[] bytes = Serializer.ProtoSerialize(data);
        File.WriteAllBytes(path, bytes);
    }

    public SaveData Load()
    {
        string path = Path.Combine(Application.persistentDataPath, SaveFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] bytes = File.ReadAllBytes(path);
        return Serializer.ProtoDeserialize<SaveData>(bytes);
    }

    // JSON for debugging
    public void SaveDebug(SaveData data)
    {
        string path = Path.Combine(Application.persistentDataPath, "save_debug.json");
        string json = Serializer.JsonSerialize(data, prettyPrint: true);
        File.WriteAllText(path, json);
    }
}
```

---

## Performance Comparison

| Operation         | JSON       | Protobuf              |
| ----------------- | ---------- | --------------------- |
| Serialize Speed   | ★★★        | ★★★★★                 |
| Deserialize Speed | ★★★        | ★★★★★                 |
| Output Size       | Large      | Small (2-10x smaller) |
| Human Readable    | ✅ Yes     | ❌ No                 |
| Schema Evolution  | ⚠️ Fragile | ✅ Robust             |

### When to Use JSON

- Config files edited by humans
- Debugging and logging
- Web API compatibility
- Small data volumes

### When to Use Protobuf

- Save files
- Network packets
- Large data volumes
- Performance-critical paths
- Schema versioning needed

---

## Common Pitfalls

### Missing ProtoContract

```csharp
// ❌ Will fail - missing attribute
public class MyData
{
    public int Value { get; set; }
}

// ✅ Correct
[ProtoContract]
public class MyData
{
    [ProtoMember(1)]
    public int Value { get; set; }
}
```

### Reusing ProtoMember Numbers

```csharp
// ❌ Data corruption when loading old saves
[ProtoMember(2)]  // Was previously "OldField"
public int NewField { get; set; }

// ✅ Use new number, reserve old
[ProtoReserved(2)]
[ProtoMember(3)]
public int NewField { get; set; }
```

### Circular References

```csharp
// ❌ Stack overflow
public class Node
{
    public Node Parent { get; set; }  // Circular!
}

// ✅ Use AsReference
[ProtoContract]
public class Node
{
    [ProtoMember(1, AsReference = true)]
    public Node Parent { get; set; }
}
```
