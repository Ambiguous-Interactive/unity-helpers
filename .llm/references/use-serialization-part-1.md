# use-serialization - Part 1

## Split Content

**Trigger**: When serializing/deserializing data for save files, network, or persistence.

---

## Available Formats

| Format   | Use Case                            | Method                        |
| -------- | ----------------------------------- | ----------------------------- |
| JSON     | Human-readable, debugging, config   | `Serializer.JsonSerialize()`  |
| Protobuf | Compact binary, network, large data | `Serializer.ProtoSerialize()` |

---

## Error Handling

`Serializer` is the **single documented exception** to this repo's "never throw" rule (see [Defensive Programming](../skills/defensive-programming.md)). Save/network data is too load-bearing for silent `default(T)`. **Every** deserialize entry point either throws `SerializationFailureException` or returns `false` via a `TryXxx` sibling. Full details: [Serialization Safety](../skills/serialization-safety.md).

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
