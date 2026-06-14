# Skill: Serialization Safety

<!-- trigger: serialization, serialize, deserialize, proto, json, binaryformatter, memorystream | Serializer exception contract — every entry point throws SerializationFailureException or has a Try sibling | Core -->

**Trigger**: When writing ANY code that calls or extends the `Serializer` class, or when reviewing PRs that touch `Runtime/Core/Serialization/**`.

---

## Why This Skill Exists

`Serializer` is the **single, documented carve-out** from this repository's "never throw, handle gracefully" rule (see `.llm/skills/defensive-programming.md`). Save files, network packets, and persisted state are too load-bearing to silently return `default(T)` — a swallowed corruption looks identical to a missing field and produces ghost data hours later in production.

A real production crash (`ArgumentNullException: Buffer cannot be null`) leaked out of `new MemoryStream(byte[])` deep inside a ZLinq pipeline. The fix is structural: **every deserialize path is wrapped, every failure has a typed exception, and every throwing method has a `Try*` sibling for callers who want flow control.**

---

## The Exception Contract

Every public `Serializer.*Deserialize*` and `Serializer.Deserialize`/`Serializer.Serialize` method MUST throw exactly one exception type: `SerializationFailureException` (or one of its sealed subclasses). Framework exceptions (`ProtoBuf.ProtoException`, `System.Text.Json.JsonException`, `ArgumentNullException`, `InvalidOperationException`, ...) are **wrapped at the format boundary** and surfaced as `InnerException`.

| Subclass                              | When                                           | `InnerException`          | Swallowed by `TryXxx`?   |
| ------------------------------------- | ---------------------------------------------- | ------------------------- | ------------------------ |
| `SerializationInputException`         | Null/empty/malformed argument                  | `null`                    | ✅ Yes                   |
| `SerializationCorruptDataException`   | Codec rejected a non-null payload              | The framework exception   | ✅ Yes                   |
| `SerializationTypeException`          | Polymorphic root unresolved / no registration  | `null` or codec exception | ❌ No (programmer error) |
| `SerializationConfigurationException` | Invalid `SerializationType`, null `Type`, etc. | `null`                    | ❌ No (programmer error) |

All subclasses expose the same immutable properties: `Format`, `Operation`, `DeclaredType`, `ResolvedType`, `InputDescriptor`, `Stage`, `Reason`. `Message` is composed lazily on first access — callers that never log pay no string-formatting cost.

---

## How to Catch Failures

### When you want to know _something_ went wrong but don't care which format

```csharp
using WallstopStudios.UnityHelpers.Core.Serialization;

try
{
    PlayerData data = Serializer.ProtoDeserialize<PlayerData>(bytes);
    Apply(data);
}
catch (SerializationFailureException ex)
{
    // ex.Format, ex.Stage, ex.DeclaredType, ex.InnerException — all populated
    Debug.LogWarning($"Save load failed: {ex.Message}");
    LoadDefaults();
}
```

### When you want flow control without throwing — use `TryXxx`

```csharp
// Try* swallows Input + CorruptData failures (caller can recover).
// Type + Configuration failures still throw — those are programmer errors.
if (Serializer.TryProtoDeserialize(bytes, out PlayerData data))
{
    Apply(data);
}
else
{
    LoadDefaults();
}
```

| Throwing method                                             | `Try` sibling                                              |
| ----------------------------------------------------------- | ---------------------------------------------------------- |
| `Deserialize<T>(byte[], SerializationType)`                 | `TryDeserialize<T>(byte[], SerializationType, out T)`      |
| `ProtoDeserialize<T>(byte[])`                               | `TryProtoDeserialize<T>(byte[], out T)`                    |
| `ProtoDeserialize<T>(byte[], Type)`                         | `TryProtoDeserialize<T>(byte[], Type, out T)`              |
| `JsonDeserialize<T>(string)` / `JsonDeserialize<T>(byte[])` | `TryJsonDeserialize<T>(string, out T)` / `(byte[], out T)` |
| `JsonDeserializeFast<T>(byte[])`                            | `TryJsonDeserializeFast<T>(byte[], out T)`                 |
| `BinaryDeserialize<T>(byte[])`                              | `TryBinaryDeserialize<T>(byte[], out T)`                   |

A `SerializerApiContractTests` reflection test fails the build if any of these pairs go missing.

---

## When Adding a New Deserialize Method

You MUST follow this checklist (otherwise the build will fail):

1. **Guard null/empty** — use `SerializationFailureException.ThrowNullInput<T>` / `ThrowEmptyInput<T>`. Never write `if (data == null) throw new ArgumentNullException(...)`.
2. **Wrap codec failures** — `try { ... } catch (Exception inner) { SerializationFailureException.ThrowCorrupt<T>(format, op, data.Length, stage, inner); }`. Never let a `ProtoException`, `JsonException`, or any other framework exception escape.
3. **Add the `Try*` sibling** — must return `bool`, set `out T = default` on failure, and **catch only `SerializationInputException` and `SerializationCorruptDataException`**. `SerializationTypeException` and `SerializationConfigurationException` must propagate (they signal programmer errors).
4. **Add contract tests** in `Tests/Runtime/Serialization/` covering: null, empty, corrupt bytes, valid roundtrip, and the matrix in `SerializerExceptionContractTests`.
5. **`SerializerApiContractTests` will refuse to build** if a `*Deserialize*` method exists without a matching `Try*` overload — do not suppress this test.

---

## Forbidden Patterns

```csharp
// ❌ NEVER — silent default on failure for save/load data
try { return Serializer.ProtoDeserialize<T>(bytes); }
catch { return default; }

// ❌ NEVER — leaking framework exception
public static T DeserializeFancy<T>(byte[] data) => ProtoBuf.Serializer.Deserialize<T>(new MemoryStream(data));
//                                                                                              ^^^^^^^^^^^^^^^^ unguarded null!

// ❌ NEVER — catching too broad in a Try* sibling (would hide programmer errors)
public static bool TryDeserializeFancy<T>(byte[] data, out T value) {
    try { value = DeserializeFancy<T>(data); return true; }
    catch { value = default; return false; }   // SWALLOWS SerializationTypeException!
}

// ❌ NEVER — `throw new ProtoException(...)` / `throw new JsonException(...)` from Serializer.cs
//   Use SerializationFailureException + an InnerException instead.
```

```csharp
// ✅ CORRECT — guard + wrap + Try sibling
public static T DeserializeFancy<T>(byte[] data) {
    if (data == null) {
        SerializationFailureException.ThrowNullInput<T>(SerializationFormat.Protobuf, SerializationOperation.Deserialize);
    }
    if (data.Length == 0) {
        SerializationFailureException.ThrowEmptyInput<T>(SerializationFormat.Protobuf, SerializationOperation.Deserialize);
    }
    try {
        using MemoryStream ms = new(data);
        return ProtoBuf.Serializer.Deserialize<T>(ms);
    }
    catch (Exception inner) when (inner is not SerializationFailureException) {
        SerializationFailureException.ThrowCorrupt<T>(
            SerializationFormat.Protobuf,
            SerializationOperation.Deserialize,
            data.Length,
            SerializationStage.Decode,
            inner);
        return default; // unreachable; ThrowCorrupt is [DoesNotReturn]
    }
}

public static bool TryDeserializeFancy<T>(byte[] data, out T value) {
    try { value = DeserializeFancy<T>(data); return true; }
    catch (SerializationInputException) { value = default; return false; }
    catch (SerializationCorruptDataException) { value = default; return false; }
    // Type/Configuration exceptions still propagate — programmer errors.
}
```

---

## Caller-Side Patterns

### Save/load with graceful fallback

```csharp
public SaveData Load() {
    try {
        byte[] bytes = File.ReadAllBytes(_path);
        return Serializer.ProtoDeserialize<SaveData>(bytes);
    }
    catch (SerializationFailureException ex) {
        Debug.LogWarning($"Save corrupt ({ex.Format}/{ex.Stage}): {ex.Message}");
        return SaveData.CreateDefault();
    }
    catch (IOException ex) {
        Debug.LogWarning($"Save unreadable: {ex.Message}");
        return SaveData.CreateDefault();
    }
}
```

### Streaming pipeline that may see null/missing entries (the screenshot bug)

```csharp
// ✅ Use the Try sibling so the pipeline never throws on a poisoned record.
foreach (byte[] blob in records) {
    if (Serializer.TryProtoDeserialize(blob, out PlayerState state)) {
        yield return state;
    }
}
```

---

## Related Skills

- `.llm/skills/use-serialization.md` — overall serializer reference (formats, schema evolution, Unity types).
- `.llm/skills/defensive-programming.md` — the "never throw" rule and its serialization carve-out.
- `.llm/references/forbidden-patterns.md` — concrete anti-patterns to flag in PR review.
