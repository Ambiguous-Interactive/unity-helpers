# Choose and adopt WallstopProto

**Preview:** WallstopProto generates serializers for your annotated C# models. Use it when you
want compact binary saves or messages without runtime reflection, including in
IL2CPP players. Start with a small model, then verify your real save files before
moving an existing game to it.

For editable configuration and debugging, [JSON](./serialization.md#json-examples-unity-aware)
is often easier. For Inspector fields such as dictionaries and nullable values,
see [serializable wrappers](./serialization-types.md); an Inspector wrapper and a
binary save format solve different problems.

## Save and load your first model

Import the WallstopProto namespace, make the model `partial`, and give each saved
member a permanent field number. The generator creates and registers its formatter.

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

[WProtoContract]
public sealed partial class PlayerSave
{
    [WProtoMember(1)]
    public int Coins { get; set; }

    [WProtoMember(2)]
    public string PlayerName { get; set; }
}

public static class PlayerSaveExample
{
    public static PlayerSave RoundTrip(PlayerSave original)
    {
        byte[] payload = WProtoFacade.Serialize(original);
        return WProtoFacade.Deserialize<PlayerSave>(payload);
    }
}
```

`Serialize` returns an owned byte array. Store those bytes in your save file or send
them as your message payload. `Deserialize` returns a new value; keep the existing
game state until decoding succeeds. For persistent files, use
[DurableFile](../utilities/helper-utilities.md#durable-writes-for-player-data) to stage writes and preserve
the previous file if publication fails.

Do not reuse a field number after deleting a member. Renaming `PlayerName` can keep
number 2; changing that field from a string to an unrelated wire type needs a migration.
Adding a new optional member with a new number lets older saves omit it. Test the
resulting default value rather than assuming a constructor runs in every read path.

<a id="step-1-what-wallstopproto-supports-derived-from-this-repository"></a>
<a id="step-1-support-at-the-survey-revision"></a>

## Check your model before adopting it

| Your data                                                    | How to model it                                                                                                                                                      |
| ------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Numbers, booleans, strings, bytes and enums                  | Use fields or properties with `[WProtoMember]` numbers.                                                                                                              |
| Optional scalar values                                       | Use `Nullable<T>` members.                                                                                                                                           |
| Lists, arrays, sets and nested collections                   | Use supported collection types; see [collection behavior](./serialization.md#wallstopproto-the-reflection-free-wire-layer-preview).                                  |
| Dictionaries                                                 | Use supported key/value types. Check nullable key/value presence rules before choosing the container; see [nullable maps](./serialization.md#nullable-map-presence). |
| Nullable elements inside repeated collections                | Restructure the element as a contract; nullable repeated elements are unsupported.                                                                                   |
| Unity value types                                            | Use the [shipped surrogate layouts](./serialization.md#unity-surrogate-layouts).                                                                                     |
| Base classes with multiple concrete subtypes                 | Declare the allowed subtypes and their permanent tags; see [inheritance](./serialization.md#wallstopproto-the-reflection-free-wire-layer-preview).                   |
| Interfaces, runtime-discovered types or custom object graphs | Design explicit contracts or custom formatters before switching serializers.                                                                                         |
| Unity object references                                      | Save a stable identifier and resolve the object after loading.                                                                                                       |

A successful build checks that the generator can represent the declared shape.
It does not prove that a new model can read every save produced by your old serializer.
Read the `WPROTO` diagnostic at the model declaration and correct its named member
before testing player builds. Do not suppress a contract error to ship an unreadable save.

## Handle missing or damaged data

Use the value-returning methods when you want typed failure handling:

```csharp
using WallstopStudios.UnityHelpers.Core.Serialization;
using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

try
{
    PlayerSave loaded = WProtoFacade.Deserialize<PlayerSave>(payload);
    // Apply loaded only after the complete read succeeds.
}
catch (SerializationFailureException)
{
    // Offer a backup or a new save; preserve the original bytes for recovery.
}
```

The facade's `TryDeserialize` has a different purpose: `false` means no formatter
serves the requested type. A registered formatter rejecting malformed data still
throws. Do not treat that method as a general “bad file returns false” API.

For downloaded or user-supplied payloads, choose limits that fit your game's data:

```csharp
WProtoReadLimits limits = new WProtoReadLimits(
    maximumMessageBytes: 1_048_576,
    maximumLengthDelimitedBytes: 262_144,
    maximumFieldCount: 4_096,
    maximumNestingDepth: 32,
    maximumPackedElementCount: 100_000)
    .WithMessageMergeCopyBytes(4_194_304)
    .WithDecodedPayloadBytes(4_194_304)
    .WithTotalFieldCount(20_000)
    .WithCollectionElementCount(100_000)
    .WithContractConstructionCount(10_000);

PlayerSave loaded = WProtoFacade.Deserialize<PlayerSave>(payload, limits);
```

These are example policies, not suitable limits for every game. They bound specific
read operations, not all memory allocated by constructors, hooks or custom formatters.
Reject an oversized transport payload before handing it to the serializer as well.

## Move an existing protobuf-net save system

1. Keep copies of real saves from each released version, including unusual and empty
   models. Keep the old reader available during the migration.
2. Record the effective field numbers, subtype tags and defaults from the old model.
   Do not infer implicit field numbers from C# declaration order.
3. Add `[WProtoContract]` and explicit `[WProtoMember]` annotations to a small model.
   Match its old wire contract; attributes with similar names are not interchangeable.
4. Read retained old bytes with the new reader and compare meaningful values. Also
   test newly written bytes with the old reader if rollback or mixed clients require it.
5. Exercise missing fields, unknown fields, repeated values, inheritance, nullable
   values and malformed input. Test your actual Unity player targets, not only the Editor.
6. Roll out the new reader before removing the old backend. Preserve backups until
   you have tested the supported upgrade and rollback paths.

Runtime model registration needs an explicit migration; see
[migrating runtime registrations](./serialization.md#migrating-runtime-model-registrations).
A public project using protobuf-net, or a survey of its source, cannot establish
compatibility for your game's effective model and retained data.

## Remove the legacy backend when ready

After every required model uses generated formatters, you can enable
`WALLSTOP_PROTO_ONLY` for the relevant player targets. Remove or conditionally exclude
consumer `ProtoBuf` imports, attributes and assembly references first. This define
removes the package's legacy fallback; it does not convert saves or register missing models.
See [WProto-only builds](./serialization.md#wproto-only-builds) for configuration details.

For the complete API and advanced model options, continue with the
[serialization reference](./serialization.md#wallstopproto-the-reflection-free-wire-layer-preview).
