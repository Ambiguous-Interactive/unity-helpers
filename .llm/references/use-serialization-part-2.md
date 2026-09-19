# use-serialization - Part 2

## Split Content

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
