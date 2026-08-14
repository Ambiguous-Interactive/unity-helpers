# Serialization Guide

## TL;DR — What Problem This Solves

- Save/load data and configs reliably with JSON or Protobuf using one unified API.
- Unity‑aware converters handle common engine types; pooled buffers keep GC low.
- Pick Pretty/Normal for human‑readable; Fast/FastPOCO for hot paths.

Visuals

![Serialization Flow](../../images/serialization/serialization-flow.svg)

This package provides fast, compact serialization for save systems, configuration, and networking with a unified API.

- Json — System.Text.Json with Unity-aware converters
- Protobuf — protobuf-net for compact, schema-evolvable binary
- SystemBinary — .NET BinaryFormatter for legacy/trusted-only scenarios

All formats are exposed via `WallstopStudios.UnityHelpers.Core.Serialization.Serializer` and selected with `SerializationType`.

## Formats Provided

### Json

Human-readable; ideal for settings, debug, modding, and Git diffs.

- Includes converters for Unity types (ignores cycles, includes fields by default, case-insensitive by default; enums as strings in Normal/Pretty):
  - Vector2, Vector3, Vector4, Vector2Int, Vector3Int
  - Color, Color32, ColorBlock
  - Quaternion, Matrix4x4, Pose, Plane, SphericalHarmonicsL2
  - Bounds, BoundsInt, Rect, RectInt, RectOffset, RangeInt
  - Ray, Ray2D, RaycastHit, BoundingSphere
  - Resolution, RenderTextureDescriptor, LayerMask, Hash128, Scene
  - AnimationCurve, Gradient, Touch, GameObject
  - ParticleSystem.MinMaxCurve, ParticleSystem.MinMaxGradient
  - System.Type (type metadata)
- Profiles: Normal, Pretty, Fast, FastPOCO (see below)

### Protobuf (protobuf-net)

**⭐ Killer Feature: Schema Evolution** — Players can load saves from older game versions without breaking! Add new fields, remove old ones, rename types—all while maintaining compatibility.

- Small and fast; best for networking and large save payloads.
- Forward/backward compatible message evolution (see the Schema Evolution guide below).

### SystemBinary (BinaryFormatter)

Only for legacy or trusted, same-version, local data. Avoid for long-term persistence or untrusted input.

- ⚠️ **Cannot handle version changes** - a single field addition breaks all existing saves.

## When To Use What

Use this decision flowchart to pick the right serialization format:

```text
START: What are you serializing?
  │
  ├─ Game settings / Config files
  │   │
  │   ├─ Need human-readable / Git-friendly?
  │   │   → JSON (Normal or Pretty) ✓
  │   │
  │   └─ Performance critical (large files)?
  │       → JSON (Fast or FastPOCO) ✓
  │
  ├─ Save game data
  │   │
  │   ├─ First save system / Need debugging?
  │   │   → JSON (Pretty) ✓
  │   │
  │   ├─ Mobile / Size matters?
  │   │   → Protobuf ✓
  │   │
  │   └─ Need cross-version compatibility?
  │       → Protobuf ✓
  │
  ├─ Network messages (multiplayer)
  │   │
  │   └─ Bandwidth is critical
  │       → Protobuf ✓
  │
  ├─ Editor-only / Temporary cache (trusted environment)
  │   │
  │   └─ Same Unity version, local only
  │       → SystemBinary (⚠️ legacy, consider JSON Fast)
  │
  └─ Hot path / Per-frame serialization
      │
      ├─ Pure C# objects (no Unity types)?
      │   → JSON (FastPOCO) ✓
      │
      └─ Mixed with Unity types?
          → JSON (Fast) ✓
```

### Quick Reference

- **Use JSON for:**
  - Player/tool settings, human-readable saves, serverless workflows, text diffs
  - Quick iteration and debugging
  - First-time save system implementation

- **Use Protobuf for:**
  - Network payloads and large, bandwidth-sensitive saves
  - Cases where schema evolves across versions
  - Mobile games where save file size matters

- **Use SystemBinary only for:**
  - Transient caches in trusted environments with exact version match
  - ⚠️ Consider JSON Fast instead - SystemBinary is legacy

## JSON Examples (Unity-aware)

- Serialize/deserialize and write/read files

```csharp
using System.Collections.Generic;
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Serialization;

public class SaveData
{
    public Vector3 position;
    public Color playerColor;
    public List<GameObject> inventory;
}

var data = new SaveData
{
    position = new Vector3(1, 2, 3),
    playerColor = Color.cyan,
    inventory = new List<GameObject>()
};

// Serialize to UTF-8 JSON bytes (Unity types supported)
byte[] jsonBytes = Serializer.JsonSerialize(data);

// Pretty stringify for human readability
string jsonText = Serializer.JsonStringify(data, pretty: true);

// Parse from string (convert to bytes first)
byte[] textBytes = System.Text.Encoding.UTF8.GetBytes(jsonText);
SaveData fromText = Serializer.JsonDeserialize<SaveData>(textBytes);

// File helpers
Serializer.WriteToJsonFile(data, path: "save.json", pretty: true);
SaveData fromFile = Serializer.ReadFromJsonFile<SaveData>("save.json");

// Generic entry points (choose format at runtime)
byte[] bytes = Serializer.Serialize(data, SerializationType.Json);
SaveData loaded = Serializer.Deserialize<SaveData>(bytes, SerializationType.Json);
```

## Advanced JSON APIs

Unity Helpers provides several advanced APIs for high-performance and robust file operations.

### Writes Do Not Destroy the Previous File

Every `WriteToJsonFile` / `WriteToJsonFileAsync` overload writes through
[`DurableFile`](../utilities/helper-utilities.md#durable-writes-for-player-data): the document is staged in
a sibling file, flushed to disk, and swapped over the destination. A write interrupted by a crash, a power
loss, or a full disk therefore leaves the previous save readable instead of truncating it. Missing
directories are created for you. Failures still throw, exactly as before.

### Async File Operations

For non-blocking file I/O (useful in loading screens or background saves):

```csharp
using WallstopStudios.UnityHelpers.Core.Serialization;

// Async read from file
SaveData data = await Serializer.ReadFromJsonFileAsync<SaveData>("save.json");

// Async write to file
await Serializer.WriteToJsonFileAsync(data, "save.json", pretty: true);

// With cancellation token (for interruptible operations)
var cts = new CancellationTokenSource();
SaveData data = await Serializer.ReadFromJsonFileAsync<SaveData>("save.json", cts.Token);
await Serializer.WriteToJsonFileAsync(data, "save.json", pretty: true, cts.Token);
```

**When to use async:**

- Loading screens where you don't want to block the main thread
- Auto-save systems running in the background
- Large save files that may take noticeable time

### Safe Try-Pattern APIs

For graceful error handling without try-catch blocks:

```csharp
using WallstopStudios.UnityHelpers.Core.Serialization;

// TryRead - returns false if file missing or invalid JSON
if (Serializer.TryReadFromJsonFile<SaveData>("save.json", out SaveData data))
{
    // File exists and parsed successfully
    LoadGame(data);
}
else
{
    // File missing or corrupted - start new game
    StartNewGame();
}

// TryWrite - returns false if write failed
if (!Serializer.TryWriteToJsonFile(data, "save.json"))
{
    Debug.LogError("Failed to save game!");
    ShowSaveErrorDialog();
}
```

**When to use Try-pattern:**

- Loading saves that may not exist (new players)
- Handling corrupted save files gracefully
- Writing to paths that may not be writable

### Fast Serialization (Hot Paths)

For performance-critical scenarios where you serialize/deserialize frequently:

```csharp
using WallstopStudios.UnityHelpers.Core.Serialization;

// Fast serialize - stricter options, Unity converters, minimal validation
byte[] fastBytes = Serializer.JsonSerializeFast(networkMessage);

// Fast deserialize
NetworkMessage msg = Serializer.JsonDeserializeFast<NetworkMessage>(fastBytes);

// Fast serialize with buffer reuse (zero-allocation after warmup)
byte[] buffer = null;
int length = Serializer.JsonSerializeFast(networkMessage, ref buffer);
// Use buffer[0..length], buffer is reused on subsequent calls
```

**Fast options differences:**

| Setting               | Normal/Pretty | Fast     |
| --------------------- | ------------- | -------- |
| Case-insensitive      | ✅            | ❌       |
| Comments allowed      | ✅            | ❌       |
| Trailing commas       | ✅            | ❌       |
| Include fields        | ✅            | ❌       |
| Reference handling    | Safe          | Disabled |
| Unity type converters | ✅            | ✅       |

### Creating Custom Options

Create your own options based on the Fast presets:

```csharp
using WallstopStudios.UnityHelpers.Core.Serialization;
using System.Text.Json;

// Get a copy of Fast options to customize
JsonSerializerOptions myOptions = Serializer.CreateFastJsonOptions();
myOptions.WriteIndented = true;  // Add pretty-printing

// FastPOCO - for pure C# objects with NO Unity types (fastest)
JsonSerializerOptions pocoOptions = Serializer.CreateFastPocoJsonOptions();

// Use with any serialize method
byte[] bytes = Serializer.JsonSerialize(data, myOptions);
Serializer.WriteToJsonFile(data, "file.json", myOptions);
```

**Option profiles:**

- `CreateFastJsonOptions()` — Fast parsing + Unity type converters (Vector3, Color, etc.)
- `CreateFastPocoJsonOptions()` — Fastest, no converters, pure C# objects only

### Performance Comparison

```csharp
// 🐌 Normal (most compatible, slightly slower)
byte[] normal = Serializer.JsonSerialize(data);

// 🚀 Fast (stricter, faster parsing/writing)
byte[] fast = Serializer.JsonSerializeFast(data);

// 🚀🚀 Fast + buffer reuse (zero-allocation after first call)
byte[] buffer = null;
int len = Serializer.JsonSerializeFast(data, ref buffer);

// 🚀🚀🚀 Fast POCO (pure C# objects, no Unity types)
JsonSerializerOptions pocoOpts = Serializer.CreateFastPocoJsonOptions();
byte[] fastest = Serializer.JsonSerialize(pureCSharpData, pocoOpts);
```

## Protobuf Examples (Compact + Evolvable)

- Basic usage

```csharp
using ProtoBuf; // protobuf-net
using WallstopStudios.UnityHelpers.Core.Serialization;

[ProtoContract]
public class PlayerInfo
{
    [ProtoMember(1)] public int id;
    [ProtoMember(2)] public string name;
}

var info = new PlayerInfo { id = 1, name = "Hero" };
byte[] buf = Serializer.ProtoSerialize(info);
PlayerInfo again = Serializer.ProtoDeserialize<PlayerInfo>(buf);

// Generic entry points
byte[] buf2 = Serializer.Serialize(info, SerializationType.Protobuf);
PlayerInfo again2 = Serializer.Deserialize<PlayerInfo>(buf2, SerializationType.Protobuf);

// Buffer reuse (reduce GC in hot paths)
byte[] buffer = null;
int len = Serializer.Serialize(info, SerializationType.Protobuf, ref buffer);
PlayerInfo sliced = Serializer.Deserialize<PlayerInfo>(buffer.AsSpan(0, len).ToArray(), SerializationType.Protobuf);
```

- Unity types with Protobuf: built-in surrogates

```csharp
// This package registers protobuf-net surrogates at startup so Unity structs just work in protobuf models.
// The following Unity types are protobuf-compatible out of the box:
// - Vector2, Vector3, Vector2Int, Vector3Int
// - Quaternion
// - Color, Color32
// - Rect, RectInt
// - Bounds, BoundsInt
// - Resolution
// Example: use Vector3 directly in a protobuf-annotated model
using ProtoBuf;              // protobuf-net
using UnityEngine;           // Unity types
using WallstopStudios.UnityHelpers.Core.Serialization;

[ProtoContract]
public class NetworkMessage
{
    [ProtoMember(1)] public int playerId;
    [ProtoMember(2)] public Vector3 position;   // Works via registered surrogates
    [ProtoMember(3)] public Quaternion facing;  // Works via registered surrogates
}

// Serialize/deserialize as usual
var msg = new NetworkMessage { playerId = 7, position = new Vector3(1,2,3), facing = Quaternion.identity };
byte[] bytes = Serializer.ProtoSerialize(msg);
NetworkMessage again = Serializer.ProtoDeserialize<NetworkMessage>(bytes);
```

Notes

- Surrogates are registered in the Serializer static initializer; you don't need to call anything.
- If you define your own DTOs, they will continue to work; surrogates simply make Unity structs first-class.
- Keep using [ProtoContract]/[ProtoMember] and stable field numbers for your own types.

### ⚠️ IL2CPP and Code Stripping Warning

**Critical for IL2CPP builds (WebGL, iOS, Android, Consoles):**

Protobuf uses reflection internally to serialize/deserialize types. Unity's IL2CPP managed code stripping may remove types or fields that are only accessed via reflection, causing **silent data loss or runtime crashes** in release builds.

**Common symptoms:**

- `NullReferenceException` or `TypeLoadException` during Protobuf deserialization
- Fields mysteriously have default values after loading (data appears to be lost)
- Works perfectly in Editor/Development builds, fails in Release/IL2CPP builds
- "Type not found" or "Method not found" errors at runtime

### Solution: Create a link.xml file

In your `Assets` folder (or any subfolder), create `link.xml` to preserve your Protobuf types:

```xml
<linker>
  <!-- Preserve all your Protobuf-serialized types -->
  <assembly fullname="Assembly-CSharp">
    <!-- Preserve specific types -->
    <type fullname="MyGame.PlayerSave" preserve="all"/>
    <type fullname="MyGame.InventoryData" preserve="all"/>
    <type fullname="MyGame.NetworkMessage" preserve="all"/>

    <!-- Or preserve entire namespace -->
    <namespace fullname="MyGame.SaveData" preserve="all"/>
  </assembly>

  <!-- If using Protobuf types across assemblies -->
  <assembly fullname="MyGame.Shared">
    <namespace fullname="MyGame.Shared.Protocol" preserve="all"/>
  </assembly>

  <!-- Preserve Unity Helpers if needed -->
  <assembly fullname="WallstopStudios.UnityHelpers.Runtime">
    <!-- Usually not needed, but if you see errors: -->
    <type fullname="WallstopStudios.UnityHelpers.Core.Serialization.Serializer" preserve="all"/>
  </assembly>
</linker>
```

**Testing checklist (CRITICAL):**

- ✅ **Test every IL2CPP build** - Development builds don't strip code, so issues only appear in Release
- ✅ **Test on actual devices** - WebGL/Mobile stripping can differ from standalone builds
- ✅ **Test full save/load cycle** - Save in one session, load in another to verify persistence
- ✅ **Update link.xml when adding new types** - Every `[ProtoContract]` type needs preservation
- ✅ **Check build logs for stripping warnings** - Unity logs which types/methods are stripped
- ✅ **Test after Unity upgrades** - Stripping behavior can change between Unity versions

**When you might not need link.xml:**

- Only using JSON serialization (source-generated, no reflection)
- Already preserving entire assembly with `preserve="all"`
- Using a custom IL2CPP link file that preserves everything

### Advanced: Preserve only what's needed

Instead of `preserve="all"`, you can be more selective:

```xml
<type fullname="MyGame.PlayerSave">
  <method signature="System.Void .ctor()" preserve="all"/>
  <field name="playerId" />
  <field name="level" />
  <field name="inventory" />
</type>
```

However, this is error-prone. **Start with `preserve="all"` and optimize later if build size is critical.**

**Related documentation:**

- [Unity Manual: Managed Code Stripping](https://docs.unity3d.com/Manual/managed-code-stripping.html)
- [protobuf-net documentation](https://protobuf-net.github.io/protobuf-net/)
- [Unity Discussions: link.xml best practices](https://discussions.unity.com/)

````text

<a id="protobuf-schema-evolution-the-killer-feature"></a>
## Protobuf Schema Evolution: The Killer Feature

**The Problem Protobuf Solves:**

You ship your game with this save format:
```csharp
[ProtoContract]
public class PlayerSave
{
    [ProtoMember(1)] public int level;
    [ProtoMember(2)] public string name;
}
````

A month later, you want to add a new feature and change the format:

```csharp
[ProtoContract]
public class PlayerSave
{
    [ProtoMember(1)] public int level;
    [ProtoMember(2)] public string name;
    [ProtoMember(3)] public int gold;        // NEW FIELD
    [ProtoMember(4)] public bool isPremium;  // NEW FIELD
}
```

**With JSON or BinaryFormatter:** Players' existing saves break. You must write migration code or wipe their progress.

**With Protobuf:** It just works! Old saves load perfectly with `gold = 0` and `isPremium = false` defaults.

### Real-World Save Game Evolution Example 🟡 Intermediate

**Version 1.0 (Launch):**

```csharp
[ProtoContract]
public class PlayerSave
{
    [ProtoMember(1)] public string playerId;
    [ProtoMember(2)] public int level;
    [ProtoMember(3)] public Vector3DTO position;
}
```

**Version 1.5 (Inventory System Added):**

```csharp
[ProtoContract]
public class PlayerSave
{
    [ProtoMember(1)] public string playerId;
    [ProtoMember(2)] public int level;
    [ProtoMember(3)] public Vector3DTO position;
    [ProtoMember(4)] public List<string> inventory = new();  // NEW: defaults to empty
}
```

**Version 2.0 (Stats Overhaul - level renamed to xp):**

```csharp
[ProtoContract]
public class PlayerSave
{
    [ProtoMember(1)] public string playerId;
    // [ProtoMember(2)] int level - REMOVED, but tag 2 is NEVER reused
    [ProtoMember(3)] public Vector3DTO position;
    [ProtoMember(4)] public List<string> inventory = new();
    [ProtoMember(5)] public int xp;              // NEW: experience points
    [ProtoMember(6)] public int skillPoints;     // NEW: unspent skill points
}
```

**Result:** Players who saved in v1.0 can load their save in v2.0:

- Old `level` value (tag 2) is ignored
- New `xp` and `skillPoints` default to 0
- All existing data (`playerId`, `position`, `inventory`) loads correctly
- **Zero migration code required!**

### Schema Evolution Rules

**✅ Safe Changes (Always Compatible):**

- Add new fields with new tag numbers
- Remove fields (but never reuse their tag numbers)
- Change field names (tags are what matter, not names)
- Add new message types
- Change default values (only affects new saves)

**⚠️ Requires Care:**

- Changing field types (e.g., `int` → `long` works, `int` → `string` doesn't)
- Changing `repeated` to singular or vice versa (usually breaks)
- Renumbering existing tags (breaks everything!)

**❌ Never Do This:**

- Reuse deleted field tag numbers
- Change the meaning of an existing tag
- Remove required fields (avoid `required` entirely - use validation instead)

### Multi-Version Compatibility Pattern 🔴 Advanced

Handle breaking changes across major versions gracefully:

```csharp
[ProtoContract]
public class SaveFile
{
    [ProtoMember(1)] public int version = 3;  // Track your save version

    // Version 1-3 fields
    [ProtoMember(2)] public string playerId;
    [ProtoMember(3)] public Vector3DTO position;

    // Version 2+ fields
    [ProtoMember(10)] public List<string> inventory;

    // Version 3+ fields
    [ProtoMember(20)] public PlayerStats stats;

    public void PostDeserialize()
    {
        if (version < 2)
        {
            // Migrate v1 saves: initialize empty inventory
            inventory ??= new List<string>();
        }

        if (version < 3)
        {
            // Migrate v2 saves: create default stats
            stats ??= new PlayerStats { xp = 0, level = 1 };
        }

        version = 3; // Update to current version
    }
}
```

> ⚠️ **Common Mistake:** Don't put migration logic in the constructor. Use `PostDeserialize()`
> or a dedicated method called after loading. Constructors don't run during deserialization.

### Testing Schema Evolution 🟢 Beginner

**Recommended Testing Pattern:**

```csharp
// 1. Save a file with version N:
var oldSave = new PlayerSave { level = 10, name = "Hero" };
byte[] bytes = Serializer.ProtoSerialize(oldSave);
File.WriteAllBytes("test_v1.save", bytes);

// 2. Update your schema (add new fields)

// 3. Load the old file with new schema:
byte[] oldBytes = File.ReadAllBytes("test_v1.save");
var loaded = Serializer.ProtoDeserialize<PlayerSave>(oldBytes);

// New fields have defaults, old fields are preserved
Assert.AreEqual(10, loaded.level);
Assert.AreEqual("Hero", loaded.name);
Assert.AreEqual(0, loaded.gold);  // New field defaults to 0
```

**Best Practice:** Keep regression test files — Store save files from each version in your test suite.

### Common Save System Patterns

**Pattern 1: Version-Aware Loading** 🟡 Intermediate

```csharp
public SaveFile LoadSave(string path)
{
    byte[] bytes = File.ReadAllBytes(path);
    SaveFile save = Serializer.ProtoDeserialize<SaveFile>(bytes);

    // Perform any version-specific migrations
    save.PostDeserialize();

    return save;
}
```

**Pattern 2: Gradual Migration (preserve old format for rollback)** 🔴 Advanced

```csharp
public class SaveManager
{
    public void SaveGame(PlayerData data)
    {
        var protobuf = ConvertToProtobuf(data);
        byte[] bytes = Serializer.ProtoSerialize(protobuf);

        // Write both formats during transition period
        File.WriteAllBytes("save.dat", bytes);
        Serializer.WriteToJsonFile(data, "save.json.backup");
    }
}
```

**Pattern 3: Automatic Backup Before Save** 🟡 Intermediate

```csharp
public void SaveGame(SaveFile save)
{
    string path = "player.save";
    string backup = $"player.save.backup_{DateTime.Now:yyyyMMdd_HHmmss}";

    // Backup existing save before overwriting
    if (File.Exists(path))
    {
        File.Copy(path, backup);
    }

    byte[] bytes = Serializer.ProtoSerialize(save);
    File.WriteAllBytes(path, bytes);

    // Keep only last 3 backups
    CleanupOldBackups("player.save.backup_*", keepCount: 3);
}
```

### Why This Matters for Live Games

**Without schema evolution (JSON/BinaryFormatter):**

- ❌ Every update risks breaking player saves
- ❌ Must write complex migration code for every version
- ❌ Players lose progress if migration fails
- ❌ Can't roll back broken updates (saves are corrupted)
- ❌ Hotfixes that change save format are terrifying

**With Protobuf schema evolution:**

- ✅ Add features freely without breaking existing saves
- ✅ Graceful degradation (old clients ignore new fields)
- ✅ Can roll back game versions without data loss
- ✅ Hotfixes are safe (just add new optional fields)
- ✅ Reduces QA burden (less migration testing needed)

### Protobuf Compatibility Tips

- Add fields with new numbers; old clients ignore unknown fields; new clients default missing fields.
- Never reuse or renumber existing field tags; reserve removed numbers if needed.
- Avoid changing scalar types on the same number.
- Prefer optional/repeated instead of required.
- Use sensible defaults to minimize payloads.
- **Group field numbers by version** (e.g., v1: 1-10, v2: 11-20, v3: 21-30) for clarity.

---

## Protobuf Polymorphism (Inheritance + Interfaces)

- Abstract base with [ProtoInclude] (recommended)
  - Protobuf-net does not infer subtype graphs unless you tell it. The recommended pattern is to put `[ProtoContract]` on an abstract base and list all concrete subtypes with `[ProtoInclude(tag, typeof(Subtype))]`.
  - Declare your fields/properties as the abstract base so protobuf can deserialize to the correct subtype.

```csharp
using ProtoBuf;

[ProtoContract]
public abstract class Message { }

[ProtoContract]
public sealed class Ping : Message { [ProtoMember(1)] public int id; }

[ProtoContract]
[ProtoInclude(100, typeof(Ping))]
public abstract class MessageBase : Message { }

[ProtoContract]
public sealed class Envelope { [ProtoMember(1)] public MessageBase payload; }

// round-trip works: Envelope.payload will be Ping at runtime
byte[] bytes = Serializer.ProtoSerialize(new Envelope { payload = new Ping { id = 7 } });
Envelope again = Serializer.ProtoDeserialize<Envelope>(bytes);
```

- **Interfaces require a root mapping** — Protobuf cannot deserialize directly to an interface because it needs a concrete root. You have three options:

1. Use an abstract base with `[ProtoInclude]` and declare fields as that base (preferred).

2. Register a mapping from the interface to a concrete root type at startup:

   ```csharp
   Serializer.RegisterProtobufRoot<IMsg, Ping>();
   IMsg msg = Serializer.ProtoDeserialize<IMsg>(bytes);
   ```

3. Specify the concrete type with the overload:

   ```csharp
   IMsg msg = Serializer.ProtoDeserialize<IMsg>(bytes, typeof(Ping));
   ```

### Random System Example

All PRNGs derive from `AbstractRandom`, which is `[ProtoContract]` and declares each implementation via `[ProtoInclude]`. Use this pattern in your models:

```csharp
[ProtoContract]
public class RNGHolder { [ProtoMember(1)] public AbstractRandom rng; }

// Serialize any implementation without surprises
RNGHolder holder = new RNGHolder { rng = new PcgRandom(seed: 123) };
byte[] buf = Serializer.ProtoSerialize(holder);
RNGHolder rt = Serializer.ProtoDeserialize<RNGHolder>(buf);
```

- If you truly need an `IRandom` field, register a root or pass the concrete type when deserializing:

```csharp
Serializer.RegisterProtobufRoot<IRandom, PcgRandom>();
IRandom r = Serializer.ProtoDeserialize<IRandom>(bytes);
// or
IRandom r2 = Serializer.ProtoDeserialize<IRandom>(bytes, typeof(PcgRandom));
```

### Tag Numbers Are API Surface

Tags in `[ProtoInclude(tag, ...)]` and `[ProtoMember(tag)]` are part of your schema. Add new numbers for new types/fields; never reuse or renumber existing tags once shipped.

## SystemBinary Examples (Legacy/Trusted Only)

```csharp
using WallstopStudios.UnityHelpers.Core.Serialization;

var obj = new SomeSerializableType();
byte[] bin = Serializer.BinarySerialize(obj);
SomeSerializableType roundtrip = Serializer.BinaryDeserialize<SomeSerializableType>(bin);

// Generic
byte[] bin2 = Serializer.Serialize(obj, SerializationType.SystemBinary);
var round2 = Serializer.Deserialize<SomeSerializableType>(bin2, SerializationType.SystemBinary);
```

Watch-outs

- BinaryFormatter is obsolete for modern .NET and unsafe for untrusted input.
- Version changes often break BinaryFormatter payloads; restrict to same-version caches.

Features

- Unity converters for JSON: Vector2/3/4, Color, Matrix4x4, GameObject, Type
- Protobuf (protobuf-net) integration
- LZMA compression utilities (`Runtime/Utils/LZMA.cs`)
- Pooled buffers/writers to reduce allocations

References

- API: `Runtime/Core/Serialization/Serializer.cs:1`
- LZMA: `Runtime/Utils/LZMA.cs:1`

## Migration

- Replace direct `System.Text.Json.JsonSerializer` calls in app code with `Serializer.JsonSerialize/JsonDeserialize/JsonStringify`, or with `Serializer.Serialize/Deserialize` + `SerializationType.Json` to centralize options and Unity converters.
- Replace any custom protobuf helpers with `Serializer.ProtoSerialize/ProtoDeserialize` or the generic `Serializer.Serialize/Deserialize` APIs. Ensure models are annotated with `[ProtoContract]` and stable `[ProtoMember(n)]` tags.
- For existing binary saves using BinaryFormatter, prefer migrating to Json or Protobuf. If you must keep BinaryFormatter, scope it to trusted, same-version caches only.

## 2.0 changes

- BinaryFormatter (`SerializationType.SystemBinary`) is deprecated but remains functional for trusted/legacy scenarios. Prefer:
  - `SerializationType.Json` (System.Text.Json with Unity-aware converters) for readable, diffable content.
  - `SerializationType.Protobuf` (protobuf-net) for compact, high-performance binary payloads.

## IL2CPP / AOT guidance

System.Text.Json can require extra care under AOT (e.g., IL2CPP):

- Prefer explicit `JsonSerializerOptions` and concrete generic APIs over `object`-based serialization to reduce reflection.
- For hot POCO models, consider adding a source-generated context (JsonSerializerContext) in your game assembly and pass it to `JsonSerializer` calls.
- If you rely on many custom converters, ensure they are referenced by code so the linker doesn't strip them. The UnityHelpers converters are referenced via options by default.
- Avoid deserializing `System.Type` from untrusted input (see `TypeConverter`); this is intended for trusted configs/tools.

## WallstopProto: the reflection-free wire layer (preview)

`WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto` is the beginning of an in-tree
protobuf implementation that does no runtime reflection, so it AOT-compiles cleanly under IL2CPP where
protobuf-net's model builder cannot. `Serializer` routes through it **per annotated type** when
`WALLSTOP_PROTO` is defined — see [Serving through `Serializer`](#serving-through-serializer) — and the
wire layer is public and usable today either way. It is public **for your code, not just this
package's**: a game annotates its own types and gets the same treatment.

The reader and writer are `ref struct`s over spans that allocate nothing and never throw. Every
operation reports success, and a failure latches so a truncated write cannot look complete and a corrupt
payload cannot decode as data:

```csharp
using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

byte[] buffer = new byte[64];
WProtoWriter writer = new(buffer);
writer.TryWriteTag(1, WProtoWireType.Varint);
writer.TryWriteInt32(health);
writer.TryWriteTag(2, WProtoWireType.LengthDelimited);
writer.TryWriteString(playerName);

if (writer.Faulted)
{
    // A write was refused -- out of room, or a bad field number. Nothing partial was
    // emitted, and every later write is refused too.
}

WProtoReader reader = new(writer.Written);
while (reader.TryReadTag(out int fieldNumber, out int wireType))
{
    switch (fieldNumber)
    {
        case 1:
            reader.TryReadInt32(out health);
            break;
        case 2:
            reader.TryReadString(out playerName);
            break;
        default:
            // Fields a newer build wrote are stepped over exactly, not guessed at.
            reader.TrySkipField(fieldNumber, wireType);
            break;
    }
}
```

Sub-messages are length-prefixed, so their size has to be known before the payload is written.
`WProtoSizes` measures without allocating a scratch buffer:

```csharp
int payloadSize =
    WProtoSizes.TagSize(1) + WProtoSizes.Int32Size(health) + WProtoSizes.StringSize(playerName);
```

### Annotating your own contracts

> **`[ProtoContract]` is not read.** WallstopProto has its own attributes and only its own attributes.
> A type annotated for protobuf-net keeps being served by protobuf-net; it does **not** get a
> generated formatter, and it will not be AOT-safe under IL2CPP. Moving a contract across means
> adding `[WProtoContract]` beside `[ProtoContract]` and `[WProtoMember(n)]` beside each
> `[ProtoMember(n)]`, with the same field numbers.
>
> This is deliberate. Reusing protobuf-net's attributes would have made the two serializers
> indistinguishable at the declaration, so a feature protobuf-net supports and this one does not —
> `AsReference`, `DynamicType`, `DataFormat` — would read as supported and silently mean something
> else. Separate attributes make the set of things that round-trip explicit.

Annotate a type and a formatter is generated for it, in your assembly, at your build. Field numbers
are the wire contract; `Name` is not written to the wire at all, and exists so a schema, a
diagnostic, or a payload dump does not change meaning when you rename a C# member:

```csharp
[WProtoContract(Name = "player_state")]
public sealed partial class PlayerState
{
    [WProtoMember(1, Name = "health")]
    private int _health;

    [WProtoMember(2, Name = "display_name")]
    private string _displayName;

    [WProtoIgnore]
    private Dictionary<string, int> _index;

    [WProtoAfterDeserialization]
    private void RebuildIndex() { /* restore what was not serialized */ }
}
```

Four lifecycle hooks are supported — `[WProtoBeforeSerialization]`, `[WProtoAfterSerialization]`,
`[WProtoBeforeDeserialization]` and `[WProtoAfterDeserialization]`. They may be private: generated
formatters are emitted as a nested type of the contract, which is why the contract must be `partial`.
`[WProtoAfterDeserialization]` runs only after a **successful** read, so a corrupt payload reports
failure instead of handing back an object whose derived state was rebuilt from half-written members.

### The generator

The package ships a Roslyn source generator as a `RoslynAnalyzer`-labelled asset, so it runs on
**your** assemblies as well as its own — including `Assembly-CSharp`. Nothing needs installing and
nothing needs registering: a `[WProtoContract]` in your code gets a nested `WProtoFormatter` and an
entry in a generated registrar that runs at `RuntimeInitializeLoadType.BeforeSceneLoad`.

Supported member types include scalar values, enums, nested contracts, nullable values, collections,
maps, surrogates, and a generic contract's closed type parameters. An unsupported shape is a **build
error naming the type, the member and the remedy**, never a silent skip — a contract that quietly got
no formatter would surface as an exception from the first save in a shipped player:

| Code        | Meaning                                                                              |
| ----------- | ------------------------------------------------------------------------------------ |
| `WPROTO001` | The contract, or a type enclosing it, is not `partial`                               |
| `WPROTO002` | Two members claim the same field number                                              |
| `WPROTO003` | A member's type is not supported yet                                                 |
| `WPROTO004` | A field number is outside 1-536,870,911, or inside the reserved 19000-19999          |
| `WPROTO005` | A lifecycle hook sits on a type with no `[WProtoContract]`, so nothing calls it      |
| `WPROTO006` | Two methods carry the same lifecycle attribute                                       |
| `WPROTO007` | A member is read-only, so a decoded value cannot be assigned to it                   |
| `WPROTO008` | A lifecycle hook is static, or takes parameters                                      |
| `WPROTO009` | A contract is nested inside a generic type and cannot be registered                  |
| `WPROTO010` | A hook sits on a struct contract, where `in` copies the value and discards mutations |
| `WPROTO011` | A class contract has no parameterless constructor to read into                       |

`WPROTO028` is a **warning** that reports a skip rather than a refusal. It fires when a
closed construction found in your source cannot be named by the generated registrar — most often a
generic contract or a marshalled collection closed over a `private` nested type. Naming one from the
registrar would be `CS0122` in your own build, so it is skipped instead; the warning is there because
the skip is otherwise invisible until that type is serialized in a shipped player. Widen the offending
type to `internal`, or register the formatter yourself from code that can name it.

`WPROTO031` warns when two assemblies declare different roots for the same type. It reports both
roots and both assemblies, including conflicts that exist entirely between referenced packages.
Generated registrars run in Unity's unordered startup phase, so leaving the conflict unresolved
makes assembly load order choose the adapter and wire shape. Remove one declaration. A
`Serializer.RegisterProtobufRoot` claim fixes protobuf-net's root choice but cannot repair which
WallstopProto adapter an unordered registrar replaced.

There is also one **informational migration diagnostic**. `WPROTO030` marks a protobuf-net
`[ProtoContract]` that has no `[WProtoContract]`, because that type has no generated formatter and,
unless served another way, follows the reflective fallback path that does not work under IL2CPP. It
is informational so upgrading the package does not break an existing consumer or a
warnings-as-errors build. In Unity, promote it to a warning in `Assets/Default.ruleset` when you want
a migration worklist:

```xml
<?xml version="1.0" encoding="utf-8"?>
<RuleSet Name="Project analyzer rules" ToolsVersion="15.0">
  <Rules AnalyzerId="WallstopStudios.UnityHelpers.Proto.Generator"
    RuleNamespace="WallstopStudios.UnityHelpers.Proto.Generator">
    <Rule Id="WPROTO030" Action="Warning" />
  </Rules>
</RuleSet>
```

An IDE or standalone .NET build can set
`dotnet_diagnostic.WPROTO030.severity = warning` in `.editorconfig` instead. Add `[WProtoContract]`
and matching `[WProtoMember]` field numbers to port the type. If a contract is deliberately served
through a surrogate, root marshal, or hand-written formatter, suppress `WPROTO030` around its
`[ProtoContract]` declaration.

#### Contracts that hold other contracts

A `[WProtoMember]` whose type is another contract is written as a nested message, and a contract may
refer to itself, so a linked list or a tree serializes without a hand-written formatter:

```csharp
[WProtoContract]
public sealed partial class Inventory
{
    [WProtoMember(1)]
    public int Gold;

    [WProtoMember(2)]
    public Loadout Equipped;   // another [WProtoContract]
}
```

Four behaviors are worth knowing, because two of them are the opposite of the rule for scalars:

- **A null sub-message is omitted; a present-but-empty one is written** as a key and a zero length —
  the same distinction an empty `string` draws.
- **A struct sub-message is always written**, even when every member equals its default. protobuf-net
  does the same, and matching it is what keeps saved data readable.
- **Every lifecycle hook still runs exactly once per serialization**, however deep the value sits, so
  a `[WProtoBeforeSerialization]` hook that rents pooled scratch releases it exactly once.
- **`IsRequired` does not make a null appear.** It forces a value equal to its default onto the wire —
  a `0` int, a `default` struct sub-message — but a `null` string, `byte[]` or message reference is
  still absent, which is what protobuf-net does.

Nesting is bounded at `WProtoReader.MaxNestingDepth` (64) when writing and measuring as well as when
reading. A graph deeper than that — in practice, one containing a cycle — throws an
`InvalidOperationException` naming the type, because a cyclic message has no finite encoded size and
the alternative is a stack overflow, which cannot be caught.

#### Collections

A `[WProtoMember]` may be an array or a collection, and it becomes a **repeated field** — a run of
same-numbered fields on the wire rather than one value:

```csharp
[WProtoContract]
public sealed partial class Inventory
{
    [WProtoMember(1)]
    public int[] ItemIds;

    [WProtoMember(2)]
    public List<string> Tags;

    [WProtoMember(3)]
    public HashSet<int> UnlockedRecipes;

    [WProtoMember(4, OverwriteList = true)]
    public List<Loadout> Loadouts;   // replaced on read instead of appended to
}
```

**What is accepted.** A single-dimension array; the standard-library collections in the table below;
or any type that implements `ICollection<T>` exactly once, has a public parameterless constructor,
and has a public `Add(T)` — `List<T>`, `HashSet<T>`, `SortedSet<T>`, `Collection<T>`,
`ObservableCollection<T>` and your own types. The element may be any scalar shape, an enum, a
`byte[]`, or another `[WProtoContract]`.

| Declared member type                                                                         | Notes                                                                     |
| -------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------- |
| `T[]`, `List<T>`, `HashSet<T>`, `SortedSet<T>`, `Collection<T>`, your own                    | Filled in place through `Add`                                             |
| `LinkedList<T>`                                                                              | Filled through `AddLast`; its `ICollection<T>.Add` is explicit            |
| `Queue<T>`                                                                                   | Filled through `Enqueue`; front-to-back order round-trips                 |
| `Stack<T>`                                                                                   | Written top-first and pushed back in reverse, so a round trip is faithful |
| `ReadOnlyCollection<T>`                                                                      | Accumulated into a list and constructed once                              |
| `IList<T>`, `ICollection<T>`, `IEnumerable<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>` | Read back as a `List<T>`                                                  |
| `ISet<T>`, `IReadOnlySet<T>`                                                                 | Read back as a `HashSet<T>`                                               |

**Which type an interface member holds afterwards is part of the contract**, not an implementation
detail — your code runs against whatever is there after a load. The choices above are protobuf-net's,
so a contract migrating from it keeps working unchanged.

**Your own collection interface is refused**, with a build error naming the member. There is no
implementation the generator could pick; protobuf-net guesses `List<T>` and throws
`InvalidCastException` when it hands the result back. Declare the member as a concrete type.

**Nested and jagged collections work** — `int[][]`, `List<int[]>`, `List<List<int>>`, `int[][][]`,
`HashSet<int>[]`, `List<Dictionary<string, int>>` and
`Dictionary<string, List<int>>` all serialize, nested as deeply as the reader can read them back
(64 levels; see below). `repeated repeated` has no protobuf spelling, so each
inner collection is encoded as a wrapper message holding it at field 1 — exactly the
`message Wrapper { repeated T values = 1; }` idiom `protoc` generates for the equivalent schema.
Nothing is asked of you: declare the member and it works.

> **These members do not round-trip through protobuf-net.** It refuses every nested shape at write,
> on both 2.4.9 and 3.2.56 (`Nested or jagged lists, arrays and maps are not supported`), so no
> payload of this shape exists anywhere for either side to read. This is the one place WallstopProto
> is deliberately a superset rather than a match. If a contract must stay readable by protobuf-net,
> wrap the inner collection in a `[WProtoContract]` of its own and make the member a collection of
> that instead.

`byte[][]` and `List<byte[]>` are not nested collections and never were: a `byte[]` is a single
length-delimited value rather than a repeated field, so they are ordinary repeated members and their
bytes are unchanged.

Two refusals remain, and neither is a nested-collection gap. A **rectangular** array (`int[,]`) has
no per-row structure to wrap — reconstructing one needs its dimensions carried in the payload, which
is a wire-format decision rather than a missing case. And a chain of collections nested more than
**64** deep is a build error (`WPROTO032`): each level is a real sub-message, and the reader refuses
to read past 64 levels of nesting, so a deeper member could be written and never read back.

**An empty inner collection survives a round trip; an empty outer one does not.** That looks like an
inconsistency and is the opposite. A top-level repeated field that is absent and one that is empty
are the same bytes, so an empty outer collection cannot be told from a missing one — but an inner
collection has a wrapper message on the wire saying it was there, so `{{1}, {}, {2}}` comes back
with its empty middle intact.

A `null` inner collection follows the rule for its position, which differs. As a repeated **element**
it is refused — a run has no encoding for an absent value, exactly as for any null element. As a map
**value** it is omitted and reads back as `null`, exactly as a null message value does, because a map
entry has a field to leave out.

**A collection may be a `struct`.** Nothing about `ICollection<T>` requires a class, and an inline or
pooled buffer is a good reason to make one a value type. A struct collection is never null-checked
and is assigned back to its member after reading, because everything in between operated on a copy.
Iteration binds to your concrete enumerator, so a struct collection is not boxed on the write path.

**Five behaviors are worth knowing.** All five are protobuf-net's, measured rather than assumed, and
three of them are the opposite of the rule for a plain member:

- **Every element is written**, including one equal to its type's default. A member holding `0` is
  omitted; an element holding `0` is not, because dropping it would shorten the collection.
- **Null and empty are the same bytes** — both write nothing. So an empty collection with no
  constructor value behind it **reads back as `null`**. This is a silent data change and it is
  reproduced deliberately, because the alternative is data protobuf-net cannot read.
- **A null element is refused**, with an `InvalidOperationException` naming the contract and the
  member. There is no encoding for an absent value inside a run; writing one would either invent an
  empty value or silently shorten the collection. protobuf-net raises on the same input.
- **Reading appends** to whatever the constructor left in the member. `OverwriteList = true` replaces
  it instead. An **absent** field leaves the constructor's value alone either way — there is nothing
  for an overwrite to be triggered by. For a `Stack<T>` "appends" means the first decoded element
  ends up on top, which is what makes writing top-first and pushing back in reverse round-trip.
- **A run of packable scalars is written packed** — one key and one length for the whole run instead
  of a key per element, which roughly halves a repeated `int`. protobuf-net writes unpacked and reads
  either form, so this is a size win rather than a compatibility break, and payloads in both forms
  are accepted here.

**Reading a packed run allocates the collection once.** The run's length prefix already says how
many elements follow, so the generated reader sizes its destination up front instead of doubling it
and leaving each previous buffer to the collector. Decoding 128 `int`s into an `int[]` allocates 560
bytes — the array and the contract, and nothing else — against 1,744 before, and against
protobuf-net's 560 for the same graph. An **unpacked** run, which is what protobuf-net writes, is a
sequence of separate fields whose length is not knowable until it ends, and still grows as it did.

**A capacity is not sized from the payload at all.** The wrappers for `Deque`, `SparseSet` and the
bit sets carry a capacity, and a capacity -- unlike a length prefix -- has nothing behind it: six
bytes claiming `int.MaxValue` used to allocate 8 GB. `SerializationCapacityLimits` bounds it now,
clamping where the structure grows on demand and refusing where the capacity decides behavior. If
your saves genuinely hold more than 1,048,576 elements, raise
`SerializationCapacityLimits.MaximumRestoredCapacity` once at startup -- that is a decision your game
makes about its own data, not one a payload makes.

The same three pieces are public, for a formatter you write yourself:
`WProtoReader.CountPackedElements(wireType)` returns the exact element count of a packed run without
consuming it, `WProtoArrayBuilder<T>` collects into an exactly-sized array, and
`WProtoRepeated.Reserve(list, additional)` sizes a `List<T>` in one allocation. All three are hints:
a run with no count behaves exactly as it did.

#### Maps

A dictionary member is written as a protobuf **map**: a repeated _entry message_ with the key at
field 1 and the value at field 2. That is a different shape from a repeated value, which is why a
dictionary does not simply ride the collection path.

| Declared member type                                                                                 | Notes                            |
| ---------------------------------------------------------------------------------------------------- | -------------------------------- |
| `Dictionary<K,V>`, `SortedDictionary<K,V>`, `SortedList<K,V>`, `ConcurrentDictionary<K,V>`, your own | Filled in place                  |
| `IDictionary<K,V>`, `IReadOnlyDictionary<K,V>`                                                       | Read back as a `Dictionary<K,V>` |
| `ReadOnlyDictionary<K,V>`                                                                            | Accumulated and constructed once |

A key may be any integral type, `bool`, `string`, a floating-point type or an enum — the same set
protobuf-net accepts, which is wider than the protobuf specification's. A `byte[]` or message key is
refused, because neither has a stable identity to key on once round-tripped.

**Three behaviors were measured rather than assumed.** The entry obeys the ordinary omission rules,
so `{"a": 0}` encodes as key only. A missing key or value decodes to that type's protobuf default,
and for a string that is `""` rather than a `null` that would throw inside the dictionary. And a
repeated key is last-wins, applied through the indexer rather than `Add`, which would throw on the
second occurrence of a key a hostile payload repeated.

**A dictionary may be a `struct`**, on the same terms a collection may: it is never null-checked, and
it is assigned back to its member after reading because everything in between operated on a copy.

#### Polymorphism

`[WProtoInclude(tag, typeof(Subtype))]` on a contract lets a member typed as the base round-trip as
the concrete subtype:

```csharp
[WProtoContract]
[WProtoInclude(100, typeof(Melee))]
[WProtoInclude(101, typeof(Ranged))]
public abstract partial class Weapon
{
    [WProtoMember(1)]
    public int Durability;
}

[WProtoContract]
public partial class Melee : Weapon
{
    [WProtoMember(1)]     // the subtype has its own tag space
    public int Reach;
}
```

Dispatch is a chain of type tests over the declared subtypes — static code IL2CPP compiles like any
other, with no reflection and no `MakeGenericType`.

**Four things are worth knowing, and the first is the one that surprises:**

- **The include is written first, before the base's own members**, whatever its tag number. Every
  other member obeys ascending field order; includes do not. Measured, and confirmed with an include
  at tag 3 emitted ahead of members at tags 1 and 5.
- **An include names a _direct_ subtype.** A grandchild is declared on the type it actually derives
  from, not on the root — protobuf-net refuses the other arrangement outright. Each level writes its
  own include and then its own members, so a three-level hierarchy nests naturally.
- **An all-default subtype still writes its include** (a tag and a zero length). Dropping it because
  the payload is empty would read the value back as its base type.
- **A subtype nothing declares is refused**, naming the type and the fix, rather than written under
  its nearest declared ancestor's tag and silently downgraded on read. An unrecognized include tag in
  a _payload_ is the opposite case and is skipped as an ordinary unknown field, so a save from a
  newer build still loads.

An abstract contract must declare at least one include — reading it could otherwise never produce an
instance — and a payload for one that names no subtype is malformed rather than an empty base.

**A subtype is written as its base writes it**, whichever type you name at the call site.
`ProtoSerialize<Melee>(melee)` and `ProtoSerialize<Weapon>(melee)` produce the same bytes: the
include holding `Melee`'s members, then `Weapon`'s. That is what protobuf-net does, so payloads move
between the two serializers unchanged.

The consequence is that annotating a subtype whose base is a contract, without the base declaring it,
is a build error (`WPROTO018`) — there would be no tag to write it under.

#### Surrogates

Unity's `Vector3`, `Color` and `Bounds` cannot carry `[WProtoContract]` — they are not yours to
annotate. A **surrogate** gives them a wire shape:

```csharp
[assembly: WProtoSurrogate(typeof(Vector3), typeof(Vector3Surrogate))]

[WProtoContract]
public partial struct Vector3Surrogate
{
    [WProtoMember(1)] public float x;
    [WProtoMember(2)] public float y;
    [WProtoMember(3)] public float z;

    public static implicit operator Vector3(Vector3Surrogate v) => new(v.x, v.y, v.z);
    public static implicit operator Vector3Surrogate(Vector3 v) => new() { x = v.x, y = v.y, z = v.z };
}
```

Any member of the real type — plain, repeated, or a map value — is then written as the surrogate,
byte-for-byte, and converted back on read. The surrogate's field numbers alone define the bytes.

**The attribute goes on the assembly**, not on either type. The real type usually lives somewhere
that cannot reference this package, and an assembly attribute is the one thing the generator can
enumerate cheaply across every reference — which is what lets a **consumer's** build find the
surrogates this package ships. The compilation's own declarations are searched first, so you can
override a surrogate for a type you also use.

Both conversions must exist, implicit or explicit. A default surrogated **struct** is still written
(a tag and a zero length), following the same rule as any struct sub-message.

#### Generic contracts

A `[WProtoContract]` may be generic, and its members may be typed as its own parameters:

```csharp
[WProtoContract]
public partial class Box<T>
{
    [WProtoMember(1)] public T Value;
    [WProtoMember(2)] public T[] Many;
}
```

**Each closure gets its own encoding, because it must.** The field key itself changes with `T` —
`Box<int>.Value` is `08 01` (varint), `Box<double>` is `09 …` (fixed64), `Box<string>` is `0A …`
(length-delimited). The generated code asks `WProtoGeneric<T>` rather than carrying a constant, and
that is a closed generic IL2CPP compiles ahead of time like any other.

**The closures you use must appear in source.** A registrar cannot register an open generic, and
constructing one at runtime would need `MakeGenericType` — the exact call IL2CPP cannot compile. The
generator registers every closed construction it can see in the compilation, which is what makes a
consumer's own `Box<TheirStruct>` work without any manual registration. A construction that appears
in no source could not have been reached at runtime either.

If you need a closure that no code names directly, name it — a `static` field of that type is
enough.

A contract **nested inside** a generic type is still refused (`WPROTO009`): it is not itself generic,
so there is no construction of it to discover, and its formatter would be emitted and never
registered. Move it out, or make it generic itself.

#### Immutable contracts

A contract may keep its `readonly` fields and get-only properties:

```csharp
[WProtoContract]
public readonly partial struct Coordinate
{
    [WProtoMember(1)] public readonly int X;
    [WProtoMember(2)] public readonly int Y;
}
```

C# permits a `readonly` field to be assigned only by a constructor of its declaring type — a nested
formatter is not enough. But the generator reopens the contract as `partial`, so it emits a **private
constructor there**, and the formatter builds the value once every member has been read. Your type
keeps the immutability you chose and gains no public surface.

The generated constructor takes a `WProtoConstruct` marker as its first parameter purely so it cannot
collide with one you wrote yourself — a two-field type very plausibly has its own `(int, int)`
constructor, and both continue to exist.

Two consequences worth knowing:

- **A `[WProtoBeforeDeserialization]` hook runs after construction**, because for a type whose members
  _are_ its construction there is no earlier moment. Nothing is assigned after it, since nothing can
  be.
- **Immutable members and `[WProtoInclude]` cannot be combined** (`WPROTO015`). One needs the instance
  built once the last member is read; the other replaces the instance when an include tag arrives.
  The generator refuses rather than picking.

#### Reading without running your constructor

Some types cannot be read into a freshly constructed instance, because the constructor does work the
payload is meant to replace. A pseudo-random generator is the canonical case: its constructor seeds a
live generator, and the hook that rebuilds one from a saved seed sensibly does nothing when a
generator already exists — so constructing first hands back a generator on a **different stream** than
the one you saved, with nothing to report it.

`SkipConstructor` says not to:

```csharp
[WProtoContract(SkipConstructor = true)]
public sealed partial class Generator
{
    [WProtoMember(1)]
    private int _seed;

    private State _state;

    public Generator() => _state = State.From(Guid.NewGuid());   // never runs on read

    [WProtoAfterDeserialization]
    private void Rebuild() => _state ??= State.From(_seed);
}
```

This mirrors protobuf-net's flag of the same name, and produces the same bytes. It differs in **how**:
protobuf-net allocates the object uninitialized through reflection, which is exactly what does not
survive IL2CPP, so the generator emits a private constructor into your type's `partial` declaration
instead. The consequence is that C# field initializers and base constructors still run, where under
protobuf-net they do not — the object is more initialized, never less.

The flag is **inert on a type that declares no constructor of its own**. There is nothing to skip
there, and emitting a constructor would delete the implicit parameterless one and stop `new Yours()`
from compiling in your own code.

### Resolving a formatter

`WProtoFormatterProvider` maps a message type to its `IWProtoFormatter<T>`. The lookup is a static
field on a closed generic type, so it costs a field read and IL2CPP compiles it ahead of time like any
other generic call — there is no dictionary keyed by `Type` and no `MakeGenericType`:

Registration is automatic. The package registers its own formatters at
`RuntimeInitializeLoadType.SubsystemRegistration`, the earliest phase Unity runs, and generated
registrars run at `BeforeSceneLoad` -- so a formatter you register yourself, from any later phase,
always wins. `Register<T>` is last-wins, and that ordering is the guarantee that makes it useful.

```csharp
// Nothing to call: the package's formatters and every generated one are already registered.
// Outside a Unity runtime -- a plain dotnet test harness, say -- call WProtoBuiltInFormatters.RegisterAll().

WProtoFormatterProvider.Register(new MyHandWrittenFormatter());   // overrides whatever was there

IWProtoFormatter<PlayerState> formatter = WProtoFormatterProvider.Get<PlayerState>();
int size = formatter.Measure(state);
WProtoWriter writer = new(new byte[size]);
formatter.Write(ref writer, state);
```

`TryGet<T>()` reports a missing registration without throwing. `Get<T>()` throws an
`InvalidOperationException` that names the type and how to annotate it, which is the whole point: the
alternative under IL2CPP is an `ExecutionEngineException` from inside the runtime that names nothing.

Hand-written formatters ship for `FastVector2Int`, `FastVector3Int`, `WGuid` and `RandomState`;
everything else this package serializes through WallstopProto is generated from its annotations.

### Serving through `Serializer`

`Serializer.ProtoSerialize` / `ProtoDeserialize` ask WallstopProto first when the `WALLSTOP_PROTO`
define is set, and fall back to protobuf-net when it declines. That makes the swap **opt-in per
type**: annotating a contract moves it, and everything unannotated keeps working exactly as before,
so contracts can be ported and verified one at a time.

WallstopProto answers when a formatter is registered for the declared type, **and** the value's
runtime type is one that formatter writes:

```csharp
AbstractRandom rng = new PcgRandom(seed);

// Served: AbstractRandom has a formatter, and PcgRandom is one of the subtypes it declares
// with [WProtoInclude]. The bytes are the include holding PcgRandom's members followed by
// AbstractRandom's -- what protobuf-net writes for the same value.
byte[] bytes = Serializer.ProtoSerialize(rng);

// Comes back as PcgRandom. The payload's include tag names the subtype; the reader narrows to it.
AbstractRandom restored = Serializer.ProtoDeserialize<AbstractRandom>(bytes);
```

Three rules decide the rest:

- **A subtype nothing declares falls back to protobuf-net.** It has no encoding here — written under
  its nearest declared ancestor's tag it would read back _as_ that ancestor — so the request is
  declined rather than failed, and protobuf-net's runtime model answers it.
- **`forceRuntimeType` does not turn the swap off.** A generated formatter already dispatches on the
  runtime type, which is what that flag asks for.
- **An `interface`-typed declared type is served only when a root is declared for it.** An interface
  has no members, so nothing about it says which contract should answer. Say so once with
  [a declared root](#declared-roots-serving-an-interface); `IRandom` already has one.

Two consequences on the read side are worth knowing:

- **An empty payload is a value, not a failure.** A contract whose members all equal their defaults
  encodes to zero bytes, so `ProtoDeserialize` returns an all-defaults instance where an unported type
  would report empty input. Refusing it would mean refusing to read back something this serializer
  wrote. The `Serializable*` collections already behave this way, for the same reason.
- **A refused payload is reported as corrupt data, not as "not mine".** WallstopProto does not hand a
  payload its own formatter rejected on to protobuf-net for a second, differently-implemented decode,
  so a truncated or malformed buffer raises `SerializationCorruptDataException` — which means
  `TryProtoDeserialize` still returns `false` rather than throwing.

The runtime assembly enables `WALLSTOP_PROTO` for every supported Unity version, so this hybrid
dispatch is the default for UPM, `.unitypackage`, and source installs. A type WallstopProto cannot
serve still follows the existing protobuf-net path; `WPROTO030` identifies protobuf-net contracts
that have not gained a generated formatter.

### Root marshals: the collections with two encodings

`SerializableHashSet`, `SerializableSortedSet`, `SerializableDictionary`,
`SerializableSortedDictionary`, `Deque`, `CyclicBuffer` and `SparseSet` are never handed to
protobuf-net as themselves. Each is copied into a wrapper of items-plus-capacity — or parallel
key/value arrays — because protobuf-net's repeated provider ignores `IgnoreListHandling`. So these
types have **two** encodings, chosen by position: the wrapper's when the collection is the root of a
serialization, and an ordinary repeated field when it is a member of another contract. Both are in
save files that already exist, and WallstopProto reproduces both.

The root case is a **root marshal**, declared once at assembly level:

```csharp
[assembly: WProtoRootMarshal(typeof(Deque<>), typeof(DequeMarshalFormatter<>))]
```

It is deliberately not a surrogate. A surrogate substitutes a type _everywhere_; a marshal applies to
the root only, and lives in `WProtoRootMarshalProvider` rather than `WProtoFormatterProvider` so a
member-position lookup cannot reach it. Consumer types work the same way: name your own type and your
own `IWProtoFormatter<T>` implementation, and the generator registers one per closed construction it
finds — `Deque<YourStruct>` included, which is why the pair is an assembly attribute rather than
something this package hard-codes.

A marshal **declines** when its element type is one WallstopProto cannot encode — a type
protobuf-net reaches through a surrogate, or an enum — so the collection falls back to protobuf-net
exactly as it did before, rather than failing. A **generic contract** declines the same way, for the
same reason: `SerializableList<Vector2>` is registered for that closure, and `Vector2`'s wire shape
comes from a surrogate that is substituted while a contract is generated, when a closure's element is
not yet known. That decision propagates through nested closures, so an outer generic contract also
declines when its inner contract cannot serve its own type argument. And a `null` root of one of these collections now
encodes to an empty payload and reads back as an empty collection, where the reflection path threw.

Nothing about your own contracts changes. A member typed as one of these collections is written
exactly as it was before — a map for the dictionaries, a repeated field for the sets — and the three
that implement neither `ICollection<T>` nor `IDictionary<,>` are still refused as members, with the
same `WPROTO003` they always produced.

### Declared roots: serving an interface

A generator is almost never held as its concrete type. `IRandom` is the declared type this package's
own documentation recommends, and an interface has no members to encode — so nothing about it says
which contract should read a payload written for it. A **declared root** is that missing sentence,
written once at assembly level:

```csharp
[assembly: WProtoDeclaredRoot(typeof(IRandom), typeof(AbstractRandom))]
```

That pair ships, so `IRandom` needs nothing from you:

```csharp
IRandom rng = new PcgRandom(seed);

// Served through AbstractRandom's include chain -- byte-for-byte what protobuf-net writes, because
// its own root resolution already picks AbstractRandom for IRandom.
byte[] bytes = Serializer.ProtoSerialize(rng);
IRandom restored = Serializer.ProtoDeserialize<IRandom>(bytes); // comes back a PcgRandom
```

Declare your own the same way, naming any interface — or any abstract type that carries no
`[WProtoContract]` — and the `[WProtoContract]` that serves it. The generator emits the registration
into the declaring assembly and reports the pairs that cannot work: a root that is not assignable to
the declared type (`WPROTO023`), a type named as its own root (`WPROTO024`), a declared type that is
already a contract (`WPROTO025`), an open generic (`WPROTO026`), two roots for one declared type
inside one assembly (`WPROTO027`), a declared type that is neither an interface nor abstract
(`WPROTO029`), and conflicting roots across assemblies (`WPROTO031`).

Like a root marshal, a declared root applies **at the root only** — though for a different reason. A
marshal hides from the member path because its types have two encodings chosen by position; a
declared root hides because a member has no encoding for it at all. An interface-typed
`[WProtoMember]` is a `WPROTO003` build error, and the only member positions that could reach the
adapter are a generic contract's type argument and a marshalled collection's element
(`Deque<IRandom>`), where writing the root contract's message would be a shape protobuf-net has no
counterpart for. Those decline and fall back exactly as they did before the pair existed.

**Declaring a root asserts that this contract owns the declared type**, exactly as
`Serializer.RegisterProtobufRoot` does — and with the same consequence, because a payload does not
name the contract that wrote it. What the two serializers do about that is identical, and worth
stating precisely:

- **Writing** a value whose runtime type is outside the root's chain is declined, and protobuf-net
  writes it as its own type — the behaviour that shipped. Your own `IRandom` implementation keeps
  working.
- **Reading** into the declared type has no such information, so those bytes come back as the root.
  With `AbstractRandom` that fails loudly, because an abstract root's payload must carry an include
  tag; with a **concrete** root it is a plausible wrong object. That is what naming a root means, not
  a WallstopProto behaviour: `RegisterProtobufRoot<IEvent, PlayerJoined>()` decodes any `IEvent`
  payload as a `PlayerJoined` too. Name the concrete type explicitly —
  `ProtoDeserialize<IThing>(bytes, typeof(TheirThing))` — when more than one implementation writes.

Your own root still wins over a declaration. `Serializer.RegisterProtobufRoot<IRandom, YourRandom>()`
says this program has a different answer, and WallstopProto stops answering for that declared type on
both sides, whichever registration ran first; releasing it restores the declared pair. Declaring a
**second** `[assembly: WProtoDeclaredRoot]` for a type another package already declares is not the
way to override one: both registrars run in the same unordered Unity phase, so which wins is the load
order. `WPROTO031` reports that conflict even when it exists between two referenced packages. Remove
one declaration; a runtime claim alone cannot make the generated-adapter registration deterministic.

### Hostile payloads

`WProtoReader.MaxNestingDepth` (64) bounds how deep a payload may nest, counting sub-messages and
groups together. A formatter reads a sub-message by calling another formatter, so nesting depth is
stack depth — a few kilobytes can describe two thousand levels, and a stack overflow cannot be
caught. `TryReadMessage` refuses past the bound and reports it as malformed.

Failure propagates through return values, not through the outermost reader's `Malformed` flag: a
refused nested read is reported by the nested reader, and each caller's job is to stop.

A formatter reading a nested contract should call `reader.TryReadMessage(formatter, out T value)`,
which descends and decodes in one call and applies the bound for free. Reading the payload with
`TryReadBytes` and constructing a reader over it with the single-argument constructor restarts the
depth count at zero at every level, which removes the bound entirely for that subtree while
round-tripping perfectly well. A formatter that must build its own reader should pass the parent —
`new WProtoReader(payload, in reader)` — which is the only way to name a depth, and therefore cannot
understate one.

### Wire compatibility

The differential suite runs in two isolated processes: one loads protobuf-net 2.4.9 and the other
loads 3.2.56. Isolation matters because both assemblies have the same name; each run asserts the
physical version it loaded before testing. The writer is byte-for-byte identical to 3.2.56 across 90
wire cases covering varints, ZigZag, fixed32/64, strings, byte arrays, nested messages, unpacked
repeated fields, and the maximum field number, plus 644 whole-message cases across the four contracts
above. The shared v2/v3 domain is also byte-equal and cross-deserialized in both directions.

The majors themselves diverge at a few edges. protobuf-net 2.4.9 omits empty string map keys/values
and a default struct map value that 3.2.56 writes, and v2 silently drops a null repeated element that v3
rejects. WallstopProto follows the safer/current v3 behavior and separately proves it can read the v2
omissions using protobuf defaults (including `string.Empty`, never `null`) and the v2
map bytes, so an old save migrates without claiming an impossible three-way byte identity.

Three protobuf-net behaviors are worth knowing because they are easy to trip over:

- An **empty but non-null** `string` or `byte[]` is written as tag plus a zero length. Only `null` is
  omitted.
- **Negative zero is not preserved.** Default-value omission tests `value == 0`, and `-0.0 == 0.0`, so a
  `-0f` member is omitted and reads back as `+0f`.
- **Members go out in ascending field number, not declaration order.** `FastVector3Int` declares
  x, y, z, hash but tags them 1, 2, 4, 3, so its cached hash is written before z.
