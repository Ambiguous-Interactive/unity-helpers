# WallstopProto vs. Public Unity protobuf-net Projects

## TL;DR

- **2,166 serialized member declarations** were catalogued across four public Unity codebases that
  annotate protobuf-net contracts. **2,151 of them (99.31%) are shapes WallstopProto already
  encodes.**
- The **15 remaining members (0.69%) all produce a `WPROTO###` build error** naming the type, the
  member and the fix. None of them fails silently.
- Only **5 members (0.23%)** need the contract restructured rather than merely annotated: four
  interface-typed members and one `Dictionary<K, Nullable<V>>`.
- The real risk was **not** at the member level. It was that **`WPROTO030`, the migration diagnostic,
  never fired for 80 of the 485 contracts surveyed (16.5%)**, because it matched
  `ProtoBuf.ProtoContractAttribute` by exact name and those contracts are declared with
  `[DataContract]` or with a protobuf-net build whose root namespace was renamed. Those types fell
  through to the reflection path with nothing said.
  [#597](https://github.com/Ambiguous-Interactive/unity-helpers/issues/597) closed that gap: both
  shapes are announced now, and the diagnostic names the discriminator that matched.

## Method

Surveyed **2026-08-29**. No repository was cloned. Every file was read through the GitHub REST API
(`git/trees?recursive=1` for enumeration) and `raw.githubusercontent.com` (for content), pinned to
the commit named in each section, then catalogued locally with a regular-expression pass that pairs
each `[ProtoMember]` / `[DataMember]` with the member declaration that follows it, and each
`[ProtoContract(ImplicitFields = ...)]` with every public member of its type.

Type kinds (is `ResourceType` an enum or a contract?) were resolved from the corpus itself where the
declaration was in scope; by fetching the declaring file for the 35 types whose declaration was
outside the scanned subset; and, for the remaining names, all of which are UnityEngine or TextMeshPro
enums reached from RTSL, by checking each name against those APIs. A type
counts as a contract only when a contract attribute is in the attribute block immediately preceding
its declaration, so a type carrying only `[DataMember]`s is scored as **not** a contract, which is
what both serializers see.

Coverage of each repository:

| Repository                                                            | Commit     | `.cs` files read | Scope                                    |
| --------------------------------------------------------------------- | ---------- | ---------------: | ---------------------------------------- |
| [SubnauticaNitrox/Nitrox](https://github.com/SubnauticaNitrox/Nitrox) | `80003b1b` |            1,784 | complete tree                            |
| [OpenHellion/Client](https://github.com/OpenHellion/Client)           | `6caf4223` |            1,839 | complete tree                            |
| [WheatDew/WheatCabin](https://github.com/WheatDew/WheatCabin)         | `b9a33385` |              252 | vendored Battlehub RTSL directories only |
| [QQ1273459693/EmpireOnlineclient][empire]                             | `642ad82b` |                7 | generated `GameProtocol` directory only  |
| [Ailtop/RustDocuments](https://github.com/Ailtop/RustDocuments)       | `842a0445` |            3,114 | complete tree                            |
| [egametang/ET](https://github.com/egametang/ET)                       | `743c635d` |            4,261 | complete tree                            |

[empire]: https://github.com/QQ1273459693/EmpireOnlineclient

**How the corpora were found.** Nitrox and Battlehub RTSL were already named as the two dominant
public Unity protobuf-net corpora. The other four came from GitHub code search:
`"using ProtoBuf" "using UnityEngine" extension:cs` surfaced OpenHellion and RustDocuments;
`"[ProtoContract]" path:Assets extension:cs NOT Battlehub` surfaced EmpireOnlineclient; ET was added
as the largest Unity C# game framework on GitHub (9.9k stars) to test whether a framework of that
size still uses protobuf-net at all. Two searches were run specifically to price the features
WallstopProto does not have: `"ProtoMember" "AsReference" extension:cs path:Assets` returns exactly
one hit, a vendored copy of protobuf-net's own attribute source, and
`"DataFormat.ZigZag" OR "DataFormat.FixedSize" "UnityEngine" extension:cs` returns **zero**.

RTSL is redistributed verbatim: the same generated `Persistent*` files appear in
[ETdoFresh/UDevelop](https://github.com/ETdoFresh/UDevelop) and
[llhswwha/RuntimeEditor_HDRP](https://github.com/llhswwha/RuntimeEditor_HDRP), so one copy was
catalogued and it stands for all three.

## Step 1: what WallstopProto supports, derived from this repository

The authoritative sources are the `WPROTO###` descriptors (40 on the day of the survey) in
`Generator~/WallstopStudios.UnityHelpers.Proto.Generator/WProtoDiagnostics.cs`, the runtime under
`Runtime/Core/Serialization/WallstopProto/`, and the
[serialization guide](./serialization.md#wallstopproto-the-reflection-free-wire-layer-preview).

| Shape                                                                                                                                      | Supported                                                         | Proof                                                                         |
| ------------------------------------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| Integer / floating-point scalars, `bool`, `char`, `string`, `byte[]`                                                                       | Yes                                                               | `OracleDifferentialTests.cs`                                                  |
| Enums, including enum dictionary keys                                                                                                      | Yes                                                               | `MapDifferentialTests.cs`                                                     |
| `Nullable<T>` as a **member**                                                                                                              | Yes                                                               | `OracleDifferentialTests.cs`                                                  |
| `Nullable<T>` as a collection element or map value                                                                                         | **No — `WPROTO003`**                                              | `DiagnosticTests.cs` pins `int?[]`, `int?[][]`, `int?[,]`                     |
| `DateTime`, `TimeSpan`, `Guid`, `decimal`, `Uri`                                                                                           | Yes                                                               | `WProtoBclFormatters.cs` registers five                                       |
| `DateTimeOffset`, `IntPtr`, `UIntPtr`, `Type`                                                                                              | **No — `WPROTO003`**, by design                                   | `DiagnosticTests.cs`                                                          |
| 1-D arrays, `List<T>`, `HashSet<T>`, any `ICollection<T>` + `Add`                                                                          | Yes                                                               | `RepeatedFormatterTests.cs`                                                   |
| `LinkedList`, `Queue`, `Stack`, `ReadOnlyCollection`, the interfaces                                                                       | Yes                                                               | `RepeatedFormatterTests.cs`                                                   |
| Nested / jagged (`int[][]`, `List<List<int>>`, `List<Dictionary<…>>`)                                                                      | Yes — **superset of protobuf-net**                                | `NestedCollectionTests.cs`                                                    |
| Rectangular arrays of any rank, in any collection position                                                                                 | Yes — **superset of protobuf-net**                                | `RectangularArrayTests.cs`                                                    |
| Collection nesting deeper than 64                                                                                                          | **No — `WPROTO032`**                                              | descriptor                                                                    |
| Dictionaries and the dictionary interfaces                                                                                                 | Yes                                                               | `MapDifferentialTests.cs`                                                     |
| `byte[]` or message dictionary key                                                                                                         | **No — `WPROTO003`**                                              | `MapMember.cs`                                                                |
| Two- and three-component `ValueTuple`                                                                                                      | Yes, via shipped surrogates                                       | `WProtoValueTupleMarshalRegistrations.cs`                                     |
| Inheritance via `[WProtoInclude]`, or `[WProtoSubtype]` on the subtype                                                                     | Yes                                                               | `IncludeDifferentialTests.cs`, `SubtypeDeclarationTests.cs`                   |
| Include on a non-direct subtype                                                                                                            | **No — `WPROTO013`**                                              | descriptors                                                                   |
| Surrogates, including open generic pairs                                                                                                   | Yes                                                               | `SurrogateDifferentialTests.cs`                                               |
| `SkipConstructor`                                                                                                                          | Yes (`WPROTO033` warns on lost initializers)                      | `SkipConstructorTests.cs`                                                     |
| Four lifecycle hooks                                                                                                                       | Yes (`WPROTO034` warns on a subtype hook)                         | `HookDifferentialTests.cs`                                                    |
| Generic contracts, closed over constructions found in source                                                                               | Yes                                                               | `GenericDifferentialTests.cs`                                                 |
| A contract nested **inside** a generic type                                                                                                | **No — `WPROTO009`**                                              | descriptor                                                                    |
| Immutable contracts (`readonly` fields, get-only properties)                                                                               | Yes                                                               | `ImmutableDifferentialTests.cs`                                               |
| Immutable members **plus** includes                                                                                                        | **No — `WPROTO015`**                                              | descriptor                                                                    |
| Interfaces served at the **root**                                                                                                          | Yes, via `[WProtoDeclaredRoot]`                                   | `DeclaredRootTests.cs`                                                        |
| An interface-typed **member**                                                                                                              | **No — `WPROTO003`**                                              | stated in [the guide](./serialization.md#declared-roots-serving-an-interface) |
| `IsRequired`                                                                                                                               | Yes                                                               | `WProtoMemberAttribute.cs`                                                    |
| `OverwriteList`                                                                                                                            | Yes                                                               | `WProtoMemberAttribute.cs`                                                    |
| `DataFormat = ZigZag`                                                                                                                      | Yes (`WPROTO037` on a type without that encoding)                 | `ZigZagDifferentialTests.cs`                                                  |
| `DataFormat = FixedSize` or `Group`                                                                                                        | **No — the enum has two members**                                 | `WProtoDataFormat.cs`                                                         |
| `AsReference`, `DynamicType`                                                                                                               | **No, and deliberately unnamed**                                  | `WProtoMemberAttribute.cs` has no such property                               |
| `ImplicitFields`                                                                                                                           | **No — every member needs `[WProtoMember]`**                      | `WProtoContractAttribute.cs`                                                  |
| `Vector2`, `Vector3`, `Vector2Int`, `Vector3Int`, `Quaternion`, `Color`, `Color32`, `Rect`, `RectInt`, `Bounds`, `BoundsInt`, `Resolution` | Yes — exactly 12 shipped surrogates                               | `WProtoUnitySurrogateRegistrations.cs`                                        |
| `Vector4`, `Matrix4x4`, `LayerMask`, `AnimationCurve`, `Keyframe`, `Gradient`, `BoneWeight`                                                | **No shipped surrogate — `WPROTO003`** (a consumer can write one) | same file; `Vector4` has a JSON converter only                                |
| `UnityEngine.Object` references                                                                                                            | **No** — an asset reference has no wire identity                  | no formatter, no surrogate                                                    |
| A `[ProtoContract]` with no `[WProtoContract]`                                                                                             | Announced by `WPROTO030` (Info)                                   | `WProtoGenerator.cs`                                                          |
| Runtime `RuntimeTypeModel` configuration (`Add`, `AddSubType`, `SetSurrogate`)                                                             | **No equivalent, and no diagnostic**                              | compile-time attributes only                                                  |

## Step 2: what the public corpora actually use

### Corpus A — Nitrox (Subnautica multiplayer)

Nitrox is the largest active public Unity project that still writes protobuf-net payloads: it reads
and writes Subnautica's own save format through the game-shipped `protobuf-net.dll`, referenced by
`Nitrox.Server.Subnautica.csproj`.

| Measure                                                                                            |                         Count |
| -------------------------------------------------------------------------------------------------- | ----------------------------: |
| `.cs` files scanned                                                                                |                         1,784 |
| files carrying any serialization annotation                                                        |                           120 |
| files declaring a contract                                                                         |                            72 |
| `[DataContract]`                                                                                   |                            71 |
| `[DataMember]`                                                                                     |                           224 |
| `[ProtoContract]`                                                                                  |                             9 |
| `[ProtoMember]`                                                                                    |                            25 |
| `[ProtoInclude]`                                                                                   | 78 (across 8 declaring types) |
| deepest single include list                                                                        |         41 (`EntityMetadata`) |
| runtime `SetSurrogate` registrations                                                               |                             6 |
| `AsReference` / `DynamicType` / `DataFormat` / `SkipConstructor` / `ImplicitFields` / `IsRequired` |                             0 |
| member declarations catalogued                                                                     |                           234 |

Two findings matter more than the counts.

**Nitrox's contracts are `[DataContract]`, not `[ProtoContract]`, by 71 to 9.** protobuf-net accepts
the WCF-style attributes as an alternative contract source, and Nitrox uses them because the same
types are also serialized by its own `Nitrox.BinaryPack`.

**Nitrox's protobuf-net is namespaced `ProtoBufNet`, not `ProtoBuf`.** `GlobalUsings.cs` carries
`global using ProtoBufNet;` — a rebuilt protobuf-net whose root namespace was renamed so it does not
collide with the game's own `ProtoBuf`. Their server serializer explicitly handles three attribute
families: `System.Runtime.Serialization.DataContractAttribute`, `ProtoBufNet.ProtoContractAttribute`,
and the game's `ProtoBuf.ProtoContractAttribute` (matched by string, in `HasUweProtoContract`).

Five generic contracts appear (`ThreadSafeList<T>`, `ThreadSafeSet<T>`, `ThreadSafeQueue<T>`,
`ThreadSafeDictionary<K,V>`, `Optional<T>`), and at least two of them are simultaneously a contract
and an `ICollection<T>` — the exact shape `WPROTO012` exists to refuse until the author says which
encoding they meant.

### Corpus B — Battlehub RTSL (Runtime Save Load)

RTSL's generated `Persistent*` classes are the single largest body of `[ProtoContract]` code in
public Unity repositories, and they are redistributed verbatim across at least three repositories.

| Measure                                      | Count |
| -------------------------------------------- | ----: |
| `.cs` files scanned (three RTSL directories) |   252 |
| files declaring a contract                   |   179 |
| `[ProtoContract]`                            |   205 |
| `[ProtoMember]`                              | 1,169 |
| `[ProtoInclude]`                             | **0** |
| `[ProtoAfterDeserialization]`                |     2 |
| `[ProtoBeforeSerialization]`                 |     1 |
| `[ProtoIgnore]`                              |     1 |
| `IsRequired = true`                          |     4 |
| member declarations catalogued               | 1,168 |

Every one of the 205 contracts is spelled `[ProtoContract]` with no arguments. RTSL is **generic-
contract-dominated**: 238 of its members are a closed `Persistent*<TID>`, 87 are the bare type
parameter `TID`, and 41 more are arrays of one or the other.

RTSL declares **no** polymorphism in attributes. It configures the model at run time instead, in one
322-line file: 40 `RuntimeTypeModel.Add` calls, 14 `AddSubType` calls, 5 `SetSurrogate` calls, 12
`MakeGenericType` closures over a settings-driven `IDTypes` array, one `AutoAddMissingTypes`, one
`CompileInPlace`, and a `DynamicTypeFormatting` handler that maps an unresolvable formatted type name
onto a `NilContainer`. On a player build it loads a **precompiled `RTSLTypeModel.dll`**, which is the
classic protobuf-net answer to IL2CPP.

The 14 runtime `AddSubType` calls are the part with no attribute equivalent in protobuf-net's own
model, because RTSL generates each `Persistent*` file independently and a base cannot list subtypes
it has not seen. The subtype-side `[WProtoSubtype]` declaration fits that layout exactly: each
generated file would name its own base and field number, and no generator pass would have to revisit
the base.

### Corpus C — OpenHellion (Hellion game client)

| Measure                                                      |                         Count |
| ------------------------------------------------------------ | ----------------------------: |
| `.cs` files scanned                                          |                         1,839 |
| files declaring a contract                                   |                           151 |
| `[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]` |      **162 — every contract** |
| `[ProtoMember]`                                              |                         **0** |
| `[ProtoInclude]`                                             | 96 (across 3 declaring types) |
| deepest single include list                                  |            72 (`NetworkData`) |
| public members carried implicitly                            |                           666 |
| `Nullable<T>` members                                        |                            61 |

OpenHellion assigns **no field numbers at all**: the entire wire contract is protobuf-net's implicit
declaration-order numbering. It is also the only corpus that puts `[ProtoContract]` **on an
interface**: `IAuxDetails` is an empty interface carrying eight `[ProtoInclude]`s, and four members
are typed as it.

### Corpus D — EmpireOnlineclient (TEngine / Fantasy generated messages)

| Measure                        | Count |
| ------------------------------ | ----: |
| `.cs` files scanned            |     7 |
| files declaring a contract     |     6 |
| `[ProtoContract]`              |    38 |
| `[ProtoMember]`                |    98 |
| `[ProtoIgnore]`                |    10 |
| `IsRequired = true`            |    10 |
| `[ProtoInclude]`               |     0 |
| distinct member type spellings |     3 |

This is what code generated from a `.proto`-shaped schema looks like in a Unity client: scalars,
nested contracts, and `List<T>` of nested contracts. Nothing else.

### Corpora E and F — two informative negatives

**Rust** (`Ailtop/RustDocuments`, a decompilation of the shipped Unity game) references 98 distinct
`ProtoBuf.*` message types across 290 of its 3,114 files, and declares **zero** contracts: its DTOs
are generated into a separate assembly with static `Serialize`/`Deserialize` methods. A shipped Unity
game at Rust's scale does not use the attribute-and-reflection path at all.

**ET** (`egametang/ET`, 9.9k stars, the largest public Unity C# client-and-server framework) has
**zero** protobuf-net references across 4,261 files. It has migrated to MemoryPack. Nitrox has
migrated most of its own traffic to `Nitrox.BinaryPack` and now uses protobuf-net only where the game
format demands it.

## Step 3: the verdict

### Supported

2,151 of 2,166 catalogued members. Aggregated by shape:

| Shape                                           | Members |
| ----------------------------------------------- | ------: |
| Scalars (`int`, `float`, `bool`, `string`, …)   |   1,197 |
| Closed generic contract constructions           |     239 |
| Arrays (of scalars, enums, contracts, generics) |     175 |
| Enums                                           |     173 |
| Nested contracts                                |     114 |
| Bare generic type parameters (`T`, `TID`)       |      88 |
| `List<T>` / `HashSet<T>` and the interfaces     |      64 |
| `Nullable<T>` members                           |      62 |
| `DateTime` / `Guid`                             |      12 |
| Dictionaries                                    |      10 |
| `byte[]`                                        |       9 |
| Unity structs with a shipped surrogate, direct  |       8 |

The rows are disjoint and sum to 2,151. A `Vector3[]` is counted once, in the array row; 17 of the
members inside the array and collection rows hold a surrogated Unity struct.

Every category above is pinned by a test in
`Generator~/WallstopStudios.UnityHelpers.Proto.Generator.Tests/`. Two of them —
`List<Dictionary<K,V>>`-style nesting and rectangular arrays — WallstopProto encodes and protobuf-net
refuses outright, so this corpus understates the gap in WallstopProto's favour.

### Refused with a diagnostic

All 15 remaining members. This is the intended outcome: the developer is told at build time.

| Shape                                                            | Members | Diagnostic  | Fix                                                     |
| ---------------------------------------------------------------- | ------: | ----------- | ------------------------------------------------------- |
| `NitroxTechType` (has `[DataMember]`s but no contract attribute) |       6 | `WPROTO003` | add `[WProtoContract]`                                  |
| `IAuxDetails` — an interface-typed member                        |       4 | `WPROTO003` | restructure to an abstract base + declared root         |
| `Matrix4x4[]`, `Keyframe[]`, `BoneWeight[]`                      |       3 | `WPROTO003` | write a surrogate (none is shipped)                     |
| `List<PeerId>` (a `readonly record struct`)                      |       1 | `WPROTO003` | write a surrogate                                       |
| `Dictionary<int, float?>`                                        |       1 | `WPROTO003` | hold the value in a contract, or drop the `Nullable<T>` |

Of these, 10 are fixed by adding an attribute or a surrogate — the ordinary port step. **5 (0.23%)
require the contract's shape to change**, and both of those shapes come from the same repository:
OpenHellion's interface members and its single `Nullable<T>` map value.

Two contract-level features are also refused rather than supported, though neither costs a member:
`ImplicitFields` (the 666 members OpenHellion's 162 contracts carry implicitly would each need a
`[WProtoMember]` number written by hand, reproducing protobuf-net's declaration-order numbering
exactly or every existing save is misread), and
`[ProtoContract]` on an interface (`IAuxDetails`), which has to become
`[assembly: WProtoDeclaredRoot(typeof(IAuxDetails), typeof(AuxDetailsBase))]` over an abstract base.

### Unsupported and silent

These are the dangerous ones. Nothing in the build says a word, and the type falls through to
protobuf-net's reflection path, which does not work under IL2CPP.

> **Findings (1) and (2) below were fixed by
> [#597](https://github.com/Ambiguous-Interactive/unity-helpers/issues/597).** `WPROTO030` now
> recognizes both shapes; the numbers here record the survey as it was measured. See
> [Serialization diagnostics](./serialization.md#the-generator) for the detection rule that replaced
> the exact-name match.

**1. `WPROTO030` does not fire for a `[DataContract]` protobuf-net contract.** The generator matches
`ProtoBuf.ProtoContractAttribute` by exact display name in `WProtoGenerator.cs`. Nitrox declares 71
contracts with `[DataContract]` / `[DataMember(Order = n)]`, which protobuf-net honours and this
diagnostic ignores.

**2. `WPROTO030` does not fire for a renamed-namespace protobuf-net build.** Nitrox's remaining 9
`[ProtoContract]` types resolve to `ProtoBufNet.ProtoContractAttribute`, so the exact-name match
misses them as well.

Together those were **80 of the 485 contracts surveyed (16.5%)** that got no migration signal at all:

| Corpus             | Contracts | Announced by `WPROTO030` |
| ------------------ | --------: | -----------------------: |
| Nitrox             |        80 |                    **0** |
| Battlehub RTSL     |       205 |                      205 |
| OpenHellion        |       162 |                      162 |
| EmpireOnlineclient |        38 |                       38 |
| **Total**          |   **485** |          **405 (83.5%)** |

**3. A type registered only through `RuntimeTypeModel` is invisible to every compile-time
diagnostic.** RTSL's 40 `Add`, 14 `AddSubType` and 5 `SetSurrogate` calls, and Nitrox's
assembly-scanning `RegisterAssemblyClasses` loop, describe contracts that no attribute names.
WallstopProto has no runtime registration surface to migrate them onto and no way to see them, so a
project built this way ports by rewriting its model configuration as attributes — with nothing
telling it which types it missed.

Note that (1) and (2) are diagnostic-coverage gaps, not encoding gaps: once such a type is annotated
with `[WProtoContract]`, its members are ordinary members and land in the 99.31%. (3) is a genuine
absence of a feature.

## The honest answer

> "do we support the majority, if not all of them?"

**Yes — 99.31% of them, measured over 2,166 serialized member declarations in four public Unity
codebases (Nitrox, Battlehub RTSL, OpenHellion, EmpireOnlineclient) at the commits above.** That is
the denominator: individual `[ProtoMember]` / `[DataMember]` / implicit-field declarations, not
types, not repositories.

Restated over the other two denominators, so the number cannot be read as flattering:

- **By member: 2,151 / 2,166 = 99.31% encode today.** 15 refuse, all with a `WPROTO###` error.
  5 (0.23%) need the contract's shape changed rather than merely annotated.
- **By contract: 485 surveyed.** All 485 can be ported. 405 (83.5%) are told to port by `WPROTO030`;
  **80 (16.5%) are told nothing.**
- **By repository: 4 of 6.** Two of the six repositories that were expected to use protobuf-net no
  longer do (ET moved to MemoryPack; Rust never used the attribute path). The trend in this corpus is
  away from protobuf-net, not toward it.

The three protobuf-net features WallstopProto deliberately does not have — `AsReference`,
`DynamicType` and non-ZigZag `DataFormat` — appear **zero times** across all four attribute-bearing
corpora, and a targeted GitHub code search for each of them alongside `UnityEngine` returns one
vendored copy of protobuf-net's own source and nothing else. They were the right things to leave out.

The one feature that would move the number is `ImplicitFields`: it costs no members today because
OpenHellion's 666 implicit members are individually supported, but it makes that repository's port a
666-line hand transcription in which one wrong number silently breaks every save.

## What this means for the issue

The public-project survey is complete and its answer is unambiguous. Two follow-ups are worth
tracking separately, because neither is a shape question:

1. ~~**Widen `WPROTO030` to `[DataContract]` and to any `ProtoContractAttribute` regardless of
   namespace.**~~ **Done** in
   [#597](https://github.com/Ambiguous-Interactive/unity-helpers/issues/597). It was not a bare
   predicate change: `[DataContract]` is also WCF's attribute, so it counts only alongside
   `[DataMember(Order = n)]` and a protobuf-net reference, and a renamed `ProtoContractAttribute`
   counts only when its namespace also declares `ProtoMemberAttribute`.
2. **Decide explicitly whether `ImplicitFields` is in scope**, or document that it is not and that a
   port must number the members by hand in declaration order.
3. **Consider shipping the missing Unity struct surrogates.** `Matrix4x4`, `Keyframe` and
   `BoneWeight` each cost a member in RTSL, and `Vector4` has a JSON converter but no protobuf
   surrogate on **either** path — so this is existing protobuf-net parity rather than a WallstopProto
   regression, and four more surrogate structs would close it.

## See Also

- [Serialization Overview](./serialization.md) — the WallstopProto guide and the full
  `WPROTO###` list
- [Serializable Types](./serialization-types.md) — the Unity-side serializable containers
- [Performance Analyzers](../../performance/analyzers.md) — the separate `WUH###` family
