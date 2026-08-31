# Changelog

<!-- cspell:ignore Prd -->

All notable changes to Unity Helpers will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Add `Objects.StableHash32V1(bytes, seed)`, an FNV-1a hash whose answer depends on nothing but its arguments, so it is safe to persist or compare across machines. See [Stable Hashing](./docs/features/utilities/helper-utilities.md#stable-hashing-for-saves-and-networking) ([#639](https://github.com/Ambiguous-Interactive/unity-helpers/issues/639)).
- Add `ApproximatelyEquals(other, tolerance)` to `Circle`, `Sphere`, `Line2D`, `Line3D` and `PoolFrequencyStatistics`, so a computed shape can still be compared loosely now that `Equals` is exact. The tolerance is the whole permitted per-component difference at every magnitude ([#639](https://github.com/Ambiguous-Interactive/unity-helpers/issues/639)).
- A negative, infinite or NaN tolerance makes `ApproximatelyEquals` return false, and a non-finite component compares exactly, so a shape with an infinite radius is still approximately equal to itself ([#639](https://github.com/Ambiguous-Interactive/unity-helpers/issues/639)).
- Add `FastVector2Int.HasSameXY` and `FastVector3Int.HasSameXY`, which say what the cross-dimensional `Equals` overloads did. Those overloads are now `[Obsolete]` and go away in 4.0 ([#639](https://github.com/Ambiguous-Interactive/unity-helpers/issues/639)).
- Add `Helpers.NameHashCode`, the hash code that agrees with `Helpers.NameEquals` ([#639](https://github.com/Ambiguous-Interactive/unity-helpers/issues/639)).
- Add `TryNextUint`, `TryNextUlong`, `TryNextDouble` and `TryNextGaussian` to `AbstractRandom`, which report a stalled entropy source instead of returning the degraded answer the existing methods give. A generator still never throws for its own internal state ([#638](https://github.com/Ambiguous-Interactive/unity-helpers/issues/638)).
- Add `WUH010`, which warns when a dictionary is read through its key indexer -- it throws for an absent key where `TryGetValue` reports, and `match.Groups["name"]` quietly hands back a group that never matched. **Off by default**, unlike the rest of the family. See [Analyzers](./docs/performance/analyzers.md#wuh010-a-dictionary-indexer-read-opt-in) ([#652](https://github.com/Ambiguous-Interactive/unity-helpers/issues/652)).
- Add `WUH003` and `WUH004`, which warn when `?.`, `?[]`, `??` or `??=` is applied to a `UnityEngine.Object` -- those operators test CLR null, so a destroyed object walks straight through the guard -- and when an NUnit null assertion passes over one. See [Analyzers](./docs/performance/analyzers.md) ([#621](https://github.com/Ambiguous-Interactive/unity-helpers/issues/621)).
- Add `WUH005`, which warns on `UnityEngine.Random`, whose state no test can set or read without disturbing every other caller. It points at `PRNG.Instance` and the seedable generators behind `IRandom`. See [Analyzers](./docs/performance/analyzers.md) ([#622](https://github.com/Ambiguous-Interactive/unity-helpers/issues/622)).
- Add `WUH006` and `WUH007`, which warn when an `EffectHandle` or a coroutine handle is discarded -- each is the only thing that can undo the call it came from. The coroutine rule matches the return type, so this package's delay helpers count too. See [Analyzers](./docs/performance/analyzers.md) ([#623](https://github.com/Ambiguous-Interactive/unity-helpers/issues/623), [#626](https://github.com/Ambiguous-Interactive/unity-helpers/issues/626)).
- Add `WUH008`, which warns when a `TryXxx` `out` value is read on a path where the call's result was never tested. Only the BCL promises to write `default` on a miss. See [Analyzers](./docs/performance/analyzers.md) ([#629](https://github.com/Ambiguous-Interactive/unity-helpers/issues/629)).
- Add `WUH009`, which warns when a teardown override calls `base` before statements that still need what it released. Setup chains base-first; teardown chains base-last. See [Analyzers](./docs/performance/analyzers.md) ([#630](https://github.com/Ambiguous-Interactive/unity-helpers/issues/630)).
- Add `Span<T>` overloads of the `IList` helpers -- shuffling, `Shift`/rotate, `TrySwap`, `Fill`, predicate search, `TryFindAll` and `TryPartition` -- so a `stackalloc` buffer reaches them with no allocation. They share the `IList` bodies, so seeded output is unchanged. See [Span Operations](./docs/features/utilities/math-and-extensions.md#span-operations) ([#632](https://github.com/Ambiguous-Interactive/unity-helpers/issues/632), [#651](https://github.com/Ambiguous-Interactive/unity-helpers/issues/651)).
- Add `WPROTO047`, a warning when a type inherits its WallstopProto contract and declares `[WProtoMember]` of its own without carrying `[WProtoContract]`. The code works; the attribute is what tells a reader those members are on the wire ([#613](https://github.com/Ambiguous-Interactive/unity-helpers/issues/613)).
- Add an editor validation engine: implement `IValidationRule` and run it across the whole project a few milliseconds per tick, not one thirty-second freeze. Only claimed assets load, and one `-executeMethod` runs it in CI with a JSON report and a reviewable suppression file. See [Asset Validation](./docs/features/editor-tools/asset-validation.md) ([#288](https://github.com/Ambiguous-Interactive/unity-helpers/issues/288)).
- Add the **Asset Validation** window -- **Tools > Wallstop Studios > Unity Helpers** -- a dockable results view with search, a severity floor, click-to-ping, and one-click suppression. Searching keeps the selection, and a project with no rules is refused at once rather than after walking every asset ([#288](https://github.com/Ambiguous-Interactive/unity-helpers/issues/288), [#634](https://github.com/Ambiguous-Interactive/unity-helpers/issues/634)).
- Add `[WProtoSubtype(typeof(Base))]`, so a subtype joins a WallstopProto hierarchy without picking a field number. The editor assigns and commits the number on the next reload, and **Assign WallstopProto Subtype Tags** retires a removed one -- hand-numbered or not -- so it is never reused. See [Polymorphism](./docs/features/serialization/serialization.md#polymorphism) ([#587](https://github.com/Ambiguous-Interactive/unity-helpers/issues/587), [#601](https://github.com/Ambiguous-Interactive/unity-helpers/issues/601), [#606](https://github.com/Ambiguous-Interactive/unity-helpers/issues/606)).
- Add `Sfc64Random`, the Small Fast Chaotic generator: a published-pedigree 64-bit generator with a very small hot path that answers `NextUlong` in one state advance. See [Random Generators](./docs/features/utilities/random-generators.md) ([#516](https://github.com/Ambiguous-Interactive/unity-helpers/issues/516)).
- Add `[WProtoReserved]`, which records a field number or name a removed `[WProtoMember]` held -- or, on an enum, a removed member's value. Taking one again is a build error, not a save that reads back as the wrong thing, and the exported schema carries the proto3 `reserved` lines. See [Retiring a member](./docs/features/serialization/serialization.md#retiring-a-member) ([#608](https://github.com/Ambiguous-Interactive/unity-helpers/issues/608), [#609](https://github.com/Ambiguous-Interactive/unity-helpers/issues/609)).
- Subclass a `[WProtoContract]` and it serializes -- no attribute needed, the editor commits its field number. Write `[WProtoNotSerialized]` on one that never reaches the serializer. See [Polymorphism](./docs/features/serialization/serialization.md#polymorphism) ([#613](https://github.com/Ambiguous-Interactive/unity-helpers/issues/613)).
- Add a proto schema exporter: **Tools > Wallstop Studios > Unity Helpers > Proto Schema Exporter** writes `proto3` for your `[WProtoContract]` types, so anything downstream can read your saves. Search and tick the exact types, name a package, and write one file or one per assembly, namespace or type ([#424](https://github.com/Ambiguous-Interactive/unity-helpers/issues/424), [#595](https://github.com/Ambiguous-Interactive/unity-helpers/issues/595)).
- Add strict UTF-8 validation to WallstopProto strings and Uri: wire bytes that are not valid UTF-8 refuse the payload as malformed instead of decoding to replacement characters, as proto3 requires ([#580](https://github.com/Ambiguous-Interactive/unity-helpers/issues/580)).
- Add `IntMap<TValue>`, an int-keyed open-addressing map measured at 1.26x–2.19x `Dictionary<int,int>` on hit-heavy lookups, with no comparer indirection on the lookup path. See [Data Structures](./docs/features/utilities/data-structures.md#intmap-int-keyed-open-addressing-map) ([#578](https://github.com/Ambiguous-Interactive/unity-helpers/issues/578)).
- Add `char` and `Uri` support to WallstopProto, byte-compatible with both protobuf-net majors, so links and code units inside a save no longer need a surrogate. `DateTimeOffset`, `IntPtr`, `UIntPtr` and `Type` stay refused, with the reasons in the serialization guide ([#399](https://github.com/Ambiguous-Interactive/unity-helpers/issues/399)).
- Add `stackTrace: false` to `Log`, `LogWarn` and `LogError`, for a diagnostic that repeats once per object or once per frame. Unity captures a stack trace for every log by default, measured at 178.4 us against 13.3 us without one. See [Logging Extensions](./docs/features/logging/logging-extensions.md) ([#564](https://github.com/Ambiguous-Interactive/unity-helpers/issues/564)).
- Add `RandomGeneratorMetadata.Period`, so every generator states its period where a caller can read it, and the [Random Generators](./docs/features/utilities/random-generators.md) table now carries a Period column that cannot drift from it. A published period is quoted; where none exists the value states the measured live state width instead ([#516](https://github.com/Ambiguous-Interactive/unity-helpers/issues/516), [#285](https://github.com/Ambiguous-Interactive/unity-helpers/issues/285)).
- Add `SerializedMemberNames`, which converts between a property's source name and the `<Name>k__BackingField` Unity serializes it under, for anyone writing a drawer that resolves a member by name ([#550](https://github.com/Ambiguous-Interactive/unity-helpers/issues/550)).
- Add `WUH002`, which warns when a Unity-serialized field resolves onto a collection of collections -- Unity drops every inner value and reports nothing. Covers public fields of nested `[Serializable]` types too. A warning at most, and switchable off. See [Performance Analyzers](./docs/performance/analyzers.md) ([#548](https://github.com/Ambiguous-Interactive/unity-helpers/issues/548)).
- Add a non-throwing sibling for all eight ranged draws, `NextIntInRange` through `NextDoubleInRange`. They answer the low bound where the strict overloads reject an empty range, which is what an authored min/max pair set equal produces. See [Ranges a Designer Authored](./docs/features/utilities/random-generators.md#ranges-a-designer-authored) ([#546](https://github.com/Ambiguous-Interactive/unity-helpers/issues/546)).
- Add `WUH001`, which warns when a lookup factory is passed as a method group -- C# rebuilds that delegate on every call, hits included. Covers `ConcurrentDictionary`, `ConditionalWeakTable` and this package's `IDictionary` extensions. A warning at most, and switchable off. See [Performance Analyzers](./docs/performance/analyzers.md) ([#538](https://github.com/Ambiguous-Interactive/unity-helpers/issues/538)).
- Add `Serializer.ProtobufSurrogatesReady()`, which reports whether protobuf will write the byte layout this package documents, and names the types it will not. A game can check it before its first save instead of reading a startup log. See [Checking the surrogates took effect](./docs/features/serialization/serialization.md#checking-the-surrogates-took-effect) ([#531](https://github.com/Ambiguous-Interactive/unity-helpers/issues/531)).
- Add `ReflectionHelpers.CreateTypedArray()`, which builds an array whose element type is known only at run time about twice as fast as `Array.CreateInstance` plus `Array.SetValue` ([#529](https://github.com/Ambiguous-Interactive/unity-helpers/issues/529)).
- Add `string.Slugify()`, which produces a URL- and filename-safe key: lowercase ASCII, single hyphens, accents folded so `"Café"` is `"cafe"`. `ToKebabCase` keeps punctuation and accents and is not a substitute. See [Slugs](./docs/features/utilities/math-and-extensions.md#slugs) ([#386](https://github.com/Ambiguous-Interactive/unity-helpers/issues/386)).
- Add `TrackedObjectPool<T>`, a pool for `UnityEngine.Object` items whose lifetime ends in a callback rather than a scope. `Dispose` destroys what is still checked out instead of stranding it in the scene, and a destroyed item is never pooled or handed out. See [Pooling Unity Objects](./docs/features/utilities/helper-utilities.md#pooling-unity-objects-that-outlive-their-scope) ([#523](https://github.com/Ambiguous-Interactive/unity-helpers/issues/523)).
- Add `VisualElement.IsShown()`, `IsShownResolved()`, `IsWithin()`, `FocusedElement()` and `TryFocus()`. `Focus()` is silent when it does nothing, and Unity's own `Contains` excludes the element itself, so "did focus move" and "is the focused element mine" both had to be written by hand. See [UI Toolkit Extensions](./docs/features/utilities/math-and-extensions.md#ui-toolkit-extensions) ([#513](https://github.com/Ambiguous-Interactive/unity-helpers/issues/513)).
- Add `Xoshiro128StarStar` and `Xoshiro256StarStar`, the first generators here whose every output bit is strong enough for `NextBool` and power-of-two masks. `Xoshiro256StarStar` also answers `NextUlong` in one state advance instead of two. See [Random Generators](./docs/features/utilities/random-generators.md) ([#510](https://github.com/Ambiguous-Interactive/unity-helpers/issues/510), [#285](https://github.com/Ambiguous-Interactive/unity-helpers/issues/285)).
- Add release notes to the Unity Package Manager: the package now carries its changelog section and a Changelog link, so Version History shows what changed instead of nothing ([#421](https://github.com/Ambiguous-Interactive/unity-helpers/issues/421)).
- Add AOT protobuf support for `ValueTuple`: `Serializer.ProtoSerialize((7, 1.5f))` threw `ExecutionEngineException` on an IL2CPP player and now goes through a generated formatter. JSON of a tuple stays editor-only. Define `WALLSTOP_DISABLE_VALUE_TUPLE_SERIALIZATION` to opt out. See [Tuples serialize on IL2CPP](./docs/features/serialization/serialization-types.md#tuples-serialize-on-il2cpp) ([#289](https://github.com/Ambiguous-Interactive/unity-helpers/issues/289)).
- Add `SerializableValueTuple<T1, T2>` and `SerializableValueTuple<T1, T2, T3>`, so a tuple survives Unity serialization -- a `(int, float)` field is silently dropped, taking any authored contents with it. Byte-identical to `ValueTuple` in protobuf and JSON. See [SerializableValueTuple](./docs/features/serialization/serialization-types.md#serializablevaluetuple) ([#289](https://github.com/Ambiguous-Interactive/unity-helpers/issues/289)).
- Add a comparer constructor to the `Dictionary<TKey, TValue>` nested in `SerializableDictionaryBase`, so `base(new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase))` -- the documented way to seed a comparer -- compiles inside a subclass, where that type shadows the framework one ([#476](https://github.com/Ambiguous-Interactive/unity-helpers/issues/476)).
- Add `long`, `float` and `double` overloads of `WrappedAdd`, and a `long` `WrappedIncrement`, so an angle or a normalized phase wraps with the same call a ring-buffer cursor does. See [Numeric Helpers](./docs/features/utilities/math-and-extensions.md#numeric-helpers).
- Add `WDoomRandom`, an index-into-array generator inspired by id's original DOOM technique: one array read per draw, and one index of state, so a save file records the whole generator. It repeats every 1024 draws -- for retro feel and replays, never sampling. See [Random Generators](./docs/features/utilities/random-generators.md) ([#281](https://github.com/Ambiguous-Interactive/unity-helpers/issues/281), [#516](https://github.com/Ambiguous-Interactive/unity-helpers/issues/516), [#524](https://github.com/Ambiguous-Interactive/unity-helpers/issues/524)).
- Add `SortAlgorithm.Yam` and `IList<T>.YamSort()`, a stable sort that approaches O(n) on sequential and reverse-sequential data. Adapted from [YamSort by Gary Gende](https://github.com/gendeg/YamSort). See [IList Sorting Performance](./docs/performance/ilist-sorting-performance.md) ([#461](https://github.com/Ambiguous-Interactive/unity-helpers/issues/461)).
- Add `[SingletonCreation(SingletonCreationPolicy.NeverCreate)]`, which stops `RuntimeSingleton<T>.Instance` creating a bare stand-in when no instance exists. It returns `null` and names the type instead. See [Controlling on-demand creation](./docs/features/utilities/singletons.md#controlling-on-demand-creation) ([#321](https://github.com/Ambiguous-Interactive/unity-helpers/issues/321)).
- Add `AnimationClip.GetSpriteFramesFromClip()`, which pairs every sprite a clip references with the binding that supplies it, a `GetSpritesFromClip(path, propertyName, type)` overload that filters on that binding, and `UnityExtensions.SpriteBindingProperty`. Editor-only. See [Sprites from an AnimationClip](./docs/features/utilities/math-and-extensions.md#sprites-from-an-animationclip) ([#451](https://github.com/Ambiguous-Interactive/unity-helpers/issues/451)).
- Add `AssetChangeDetectionUtility.Enabled`, `ResetEnabledToDefault()` and `EnabledScope(bool)` to turn the `[DetectAssetChanged]` watcher on and off. The returned `AssetChangeDetectionEnabledScope` restores the previous setting on dispose ([#327](https://github.com/Ambiguous-Interactive/unity-helpers/issues/327)).
- Add `SingleThreadedThreadPool.DrainAsync()`, which closes the pool and waits for queued work to finish instead of dropping it, plus `IsAcceptingWork` ([#318](https://github.com/Ambiguous-Interactive/unity-helpers/issues/318)).
- Add `SerializableList<T>`, a list that survives Unity serialization inside another serialized collection ([#314](https://github.com/Ambiguous-Interactive/unity-helpers/issues/314)).
- Add the Inspector drawer for `SerializableDictionary<TKey, TValue, TValueCache>`, which previously had none ([#314](https://github.com/Ambiguous-Interactive/unity-helpers/issues/314)).
- Add `WGuid.TryCreate(Guid, out WGuid)`, which wraps an existing GUID without throwing when it is not version 4 ([#437](https://github.com/Ambiguous-Interactive/unity-helpers/issues/437)).
- Add `Enum.TryConvertToUInt64()` and `TryConvertToInt64()`, which convert any enum value to its 64-bit bit pattern without overflowing on negative or very large members.
- Add `ColorQuantization`, the one place an 8-bit channel becomes a normalized float and back. `ToNormalized` divides by 255, the same bits Unity's `Color32` conversion gives; `ToByte` rounds to the nearest channel; `ToThresholdByte` bounds a float cutoff. See [8-bit Channels and Normalized Floats](./docs/features/utilities/math-and-extensions.md#8-bit-channels-and-normalized-floats) ([#466](https://github.com/Ambiguous-Interactive/unity-helpers/issues/466), [#565](https://github.com/Ambiguous-Interactive/unity-helpers/issues/565)).
- Add `ColorQuantization.AreSameColor()`, the one definition of "the same color" in the package: two colors match when they encode to the same 8-bit channels, which is the only rule an equality comparer can hash ([#472](https://github.com/Ambiguous-Interactive/unity-helpers/issues/472)).
- Add `ColorContrast`, which answers whether text is readable on a background the way WCAG defines it: `RelativeLuminance`, `ContrastRatio` and `ReadableTextColor`. See [Readable Text on Any Background](./docs/features/utilities/math-and-extensions.md#readable-text-on-any-background) ([#471](https://github.com/Ambiguous-Interactive/unity-helpers/issues/471)).
- Add `TextureResampling`, the one place a resampler picks a source pixel and mixes colors of unequal opacity: `BilinearSourceCoordinate`, `NearestSourceIndex`, `Premultiply` and `Unpremultiply`. See [Resampling a Texture](./docs/features/utilities/math-and-extensions.md#resampling-a-texture) ([#470](https://github.com/Ambiguous-Interactive/unity-helpers/issues/470)).
- Add `SemaphoreSlim.Acquire()`, `TryAcquire()` and `AcquireAsync()`, so a permit can be taken with `using` instead of a `finally`. The lease releases its permit exactly once, however many copies of it exist. See [Semaphore Leases](./docs/features/utilities/helper-utilities.md#semaphore-leases) ([#358](https://github.com/Ambiguous-Interactive/unity-helpers/issues/358)).
- Add `DurableFile`, whose writes cannot leave a truncated file behind: `TryWriteAllText`, `TryAppendAllText`, `TryCopy`, `TryDelete` and async equivalents. See [Durable Writes for Player Data](./docs/features/utilities/helper-utilities.md#durable-writes-for-player-data) ([#319](https://github.com/Ambiguous-Interactive/unity-helpers/issues/319)).
- Add `WallstopProto` (preview), a reflection-free protobuf reader and writer that AOT-compiles under IL2CPP and is byte-compatible with protobuf-net. See [WallstopProto](./docs/features/serialization/serialization.md#wallstopproto-the-reflection-free-wire-layer-preview) ([#343](https://github.com/Ambiguous-Interactive/unity-helpers/issues/343)).
- Add facade-owned nested-message size plans to WallstopProto. Measured prefix widths avoid repeated large-payload moves, direct writers keep canonical back-patching, and a formatter that underwrites its measurement is refused ([#383](https://github.com/Ambiguous-Interactive/unity-helpers/issues/383), [#504](https://github.com/Ambiguous-Interactive/unity-helpers/issues/504)).
- Add the WallstopProto source generator (preview): annotate a type with `[WProtoContract]` and its formatter is generated into your assembly and registered for you. A contract it cannot serialize is a build error naming the fix. See [The generator](./docs/features/serialization/serialization.md#the-generator) ([#343](https://github.com/Ambiguous-Interactive/unity-helpers/issues/343)).
- Add `WProtoFormatterProvider`, which resolves `IWProtoFormatter<T>` without reflection. Built-ins cover `FastVector2Int`, `FastVector3Int`, `WGuid`, `RandomState`, `DateTime`, `TimeSpan`, `Guid` and `decimal`; later registrations replace them ([#343](https://github.com/Ambiguous-Interactive/unity-helpers/issues/343), [#379](https://github.com/Ambiguous-Interactive/unity-helpers/issues/379), [#399](https://github.com/Ambiguous-Interactive/unity-helpers/issues/399)).
- Add support for a `[WProtoMember]` that is another `[WProtoContract]`, including the same type again, so a tree or linked list serializes without a hand-written formatter. A sub-message merges into the value your constructor gave the member. See [Contracts that hold other contracts](./docs/features/serialization/serialization.md#contracts-that-hold-other-contracts) ([#380](https://github.com/Ambiguous-Interactive/unity-helpers/issues/380), [#475](https://github.com/Ambiguous-Interactive/unity-helpers/issues/475), [#485](https://github.com/Ambiguous-Interactive/unity-helpers/issues/485)).
- Add WallstopProto support for arrays and any `ICollection<T>` with a public parameterless constructor and `Add`, including `struct` collections, which are not boxed on the write path. `OverwriteList = true` replaces the constructor's collection instead of appending. See [Collections](./docs/features/serialization/serialization.md#collections) ([#343](https://github.com/Ambiguous-Interactive/unity-helpers/issues/343)).
- Add WallstopProto support for `LinkedList<T>`, `Queue<T>`, `Stack<T>`, `ReadOnlyCollection<T>`, `ReadOnlyDictionary<K,V>` and members declared as the matching interfaces. A `Stack<T>` round-trips in its original order. See [Collections](./docs/features/serialization/serialization.md#collections) ([#395](https://github.com/Ambiguous-Interactive/unity-helpers/issues/395)).
- Add WallstopProto support for nested and jagged collections — `int[][]`, `List<List<int>>`, `Dictionary<string, List<int>>` and deeper — which protobuf-net refuses. See [Collections](./docs/features/serialization/serialization.md#collections) ([#399](https://github.com/Ambiguous-Interactive/unity-helpers/issues/399)).
- Add WallstopProto support for rectangular arrays — `int[,]`, `int[,,]`, and one anywhere a collection can go — which protobuf-net refuses. Dimensions travel with the elements, so `new int[0, 5]` keeps its shape. See [Collections](./docs/features/serialization/serialization.md#collections) ([#434](https://github.com/Ambiguous-Interactive/unity-helpers/issues/434)).
- Add WallstopProto maps: a `Dictionary<,>`, `SortedDictionary<,>` or your own `IDictionary<,>` is written as a protobuf map, byte-compatible with protobuf-net. See [Maps](./docs/features/serialization/serialization.md#maps) ([#387](https://github.com/Ambiguous-Interactive/unity-helpers/issues/387)).
- Add WallstopProto polymorphism: `[WProtoInclude(tag, typeof(Subtype))]` round-trips a member typed as a base as its concrete subtype, with no reflection. An unrecognized include tag is skipped, so saves from newer builds still load. See [Polymorphism](./docs/features/serialization/serialization.md#polymorphism) ([#390](https://github.com/Ambiguous-Interactive/unity-helpers/issues/390)).
- Add `[assembly: WProtoSurrogate(typeof(Real), typeof(Surrogate))]` for wire shapes on types you cannot annotate, plus open generic pairs that cover each closed construction. Package pairs serve two- and three-component `ValueTuple` members and map keys. See [Surrogates](./docs/features/serialization/serialization.md#surrogates) ([#391](https://github.com/Ambiguous-Interactive/unity-helpers/issues/391), [#399](https://github.com/Ambiguous-Interactive/unity-helpers/issues/399)).
- Add `WPROTO038`, a build error when an open surrogate pair has mismatched arity, openness, or stricter generic constraints than its real type ([#399](https://github.com/Ambiguous-Interactive/unity-helpers/issues/399)).
- Add `[assembly: WProtoRootMarshal(typeof(Real), typeof(Formatter))]`, which gives a type a different wire shape as the root of a serialization than as a member of a contract. See [Root marshals](./docs/features/serialization/serialization.md#root-marshals-the-collections-with-two-encodings) ([#402](https://github.com/Ambiguous-Interactive/unity-helpers/issues/402)).
- Add `[assembly: WProtoDeclaredRoot(typeof(IRandom), typeof(AbstractRandom))]`, which names the contract serving a value held as an interface. It applies at the root only. See [Declared roots](./docs/features/serialization/serialization.md#declared-roots-serving-an-interface) ([#403](https://github.com/Ambiguous-Interactive/unity-helpers/issues/403)).
- Add generic `[WProtoContract]` support: each closed construction gets its own encoding and is registered automatically, so your own `Box<YourStruct>` needs no manual registration. A member typed as the contract's own parameter merges a sub-message a payload carries twice. See [Generic contracts](./docs/features/serialization/serialization.md#generic-contracts) ([#385](https://github.com/Ambiguous-Interactive/unity-helpers/issues/385), [#484](https://github.com/Ambiguous-Interactive/unity-helpers/issues/484)).
- Add `Tools > Wallstop Studios > Unity Helpers > Validate Serialized Fields In Selection`, which reports a `public` or `[SerializeField]` field Unity silently drops -- a `Dictionary`, a `(int, float)`, a `Nullable<T>` -- and names the package stand-in to use instead. See [Serialized Field Validator](./docs/features/editor-tools/editor-tools-guide.md#serialized-field-validator) ([#497](https://github.com/Ambiguous-Interactive/unity-helpers/issues/497)).
- Add generated JSON converters, so `Deque<int>` and `SerializableDictionary<string, int>` serialize in an IL2CPP player instead of throwing `ExecutionEngineException`. Define `WALLSTOP_DISABLE_GENERATED_JSON_CONVERTERS` to opt out. See [Generic containers get their converters generated](./docs/features/serialization/serialization.md#generic-containers-get-their-converters-generated) ([#501](https://github.com/Ambiguous-Interactive/unity-helpers/issues/501)).
- Add bounded pooled scratch lists to generated JSON array converters, so a warm read only allocates its returned array and an untrusted large array is not retained by the pool ([#504](https://github.com/Ambiguous-Interactive/unity-helpers/issues/504)).
- Add a metamorphic battery over WallstopProto: payloads rewritten into other legal spellings -- fields reordered, an undeclared field injected, a sub-message split into two that merge -- must decode unchanged ([#462](https://github.com/Ambiguous-Interactive/unity-helpers/issues/462)).
- Add a differential fuzz over WallstopProto: random valid values for repeated, map and polymorphic contracts, checked in both directions against protobuf-net 2.4.9 and 3.2.56 ([#437](https://github.com/Ambiguous-Interactive/unity-helpers/issues/437)).
- Add `WProtoMemberAttribute.DataFormat`, which selects a signed integer member's encoding. `WProtoDataFormat.ZigZag` is protobuf's `sint32`/`sint64`, where a small negative value costs one byte instead of ten. See [DataFormat](./docs/features/serialization/serialization.md#dataformat-what-a-negative-number-costs) ([#527](https://github.com/Ambiguous-Interactive/unity-helpers/issues/527)).
- Add `WPROTO037`, a build error for `DataFormat = ZigZag` on a member with no such encoding. Only `sbyte`, `short`, `int` and `long` have one, so anywhere else the annotation would have been dropped and the member written as the `int32` it declined ([#527](https://github.com/Ambiguous-Interactive/unity-helpers/issues/527)).
- Add `WPROTO035` and `WPROTO036`, warnings for an `[assembly: WJsonConverter]` pairing that cannot be closed, and for two pairings claiming one type ([#501](https://github.com/Ambiguous-Interactive/unity-helpers/issues/501)).
- Add `WPROTO034`, a warning for a lifecycle hook declared on a subtype of a `[WProtoInclude]` chain. protobuf-net 3 never runs one, and protobuf-net 2 runs it in the opposite order to WallstopProto, so declare it on the root instead. See [The generator](./docs/features/serialization/serialization.md#the-generator) ([#500](https://github.com/Ambiguous-Interactive/unity-helpers/issues/500)).
- Add `WPROTO033`, a warning for a `SkipConstructor` contract holding a field that is initialized where it is declared and is not a `[WProtoMember]`. That instance is allocated uninitialized, so the field arrives at its type's default. See [The generator](./docs/features/serialization/serialization.md#the-generator) ([#494](https://github.com/Ambiguous-Interactive/unity-helpers/issues/494)).
- Add WallstopProto support for `readonly` fields and get-only properties, previously a build error. The generator emits a private constructor into your `partial` type, keeps the parameterless one it would otherwise have had, and a member the payload omits keeps the value your constructor gave it ([#394](https://github.com/Ambiguous-Interactive/unity-helpers/issues/394), [#491](https://github.com/Ambiguous-Interactive/unity-helpers/issues/491)).
- Add `WProtoContractAttribute.SkipConstructor`, which reads a contract without running any constructor its author wrote, matching protobuf-net and still working under IL2CPP. A collection member holds exactly the elements the payload carried, unless a parent's constructor supplied the instance ([#394](https://github.com/Ambiguous-Interactive/unity-helpers/issues/394)).
- Add `WProtoContractAttribute.IgnoreListHandling`, which writes a contract that also implements `ICollection<T>` as a message. Without it such a member is a build error naming both readings ([#343](https://github.com/Ambiguous-Interactive/unity-helpers/issues/343)).
- Add `IWProtoScalarFormatter<T>` and `WProtoGeneric<T>`, which make a value's wire type expressible so a generic contract can encode a member whose type it cannot see. `IWProtoFormatter<T>` is unchanged.
- Add `IWProtoPolymorphicFormatter`, which lets a formatter report the runtime types its `[WProtoInclude]` chain writes, so `Serializer` can serve a value held as its base ([#403](https://github.com/Ambiguous-Interactive/unity-helpers/issues/403)).
- Add `IWProtoConditionalFormatter`, so a formatter reports whether it can encode a closure before anything is written. An element WallstopProto cannot encode now falls back to protobuf-net instead of throwing mid-serialization ([#402](https://github.com/Ambiguous-Interactive/unity-helpers/issues/402), [#416](https://github.com/Ambiguous-Interactive/unity-helpers/issues/416)).
- Add `WProtoFacade.TryDeserializeAs()`, the read that names a concrete type. `Serializer.ProtoDeserialize<T>(byte[], Type)` routes through it ([#403](https://github.com/Ambiguous-Interactive/unity-helpers/issues/403)).
- Add `SerializationCapacityLimits`, the bound a deserializer applies to a capacity a payload claims, with `MaximumRestoredCapacity` (default 1,048,576) for games whose saves are legitimately larger. An empty rectangular array whose axis exceeds it does not deserialize; one carrying elements is unaffected ([#437](https://github.com/Ambiguous-Interactive/unity-helpers/issues/437)).
- Add `WProtoReader.CountPackedElements()`, `WProtoArrayBuilder<T>` and `WProtoRepeated.Reserve()`, so a hand-written formatter can size a repeated field once instead of growing it. Generated formatters already size theirs from the packed run ([#398](https://github.com/Ambiguous-Interactive/unity-helpers/issues/398)).
- Add `WProtoReader.TryReadPackedRun()`, which reads a packed repeated field's payload as its own reader without spending a nesting level.
- Add `WProtoMessageAccumulator` and the matching `WProtoReader.TryReadMessage(payload, formatter, out value)` overload, so a hand-written formatter can merge a sub-message field a payload carries more than once ([#475](https://github.com/Ambiguous-Interactive/unity-helpers/issues/475)).
- Add `IWProtoMergeFormatter<T>` and a `WProtoReader.TryReadMessage(payload, formatter, seed, out value)` overload, so a hand-written formatter can decode into a value the caller already holds. `IWProtoFormatter<T>` is unchanged; a formatter that skips this reads exactly as before ([#485](https://github.com/Ambiguous-Interactive/unity-helpers/issues/485)).
- Add `WProtoReader.MaxNestingDepth` (64), the deepest message nesting a payload may use, so a few kilobytes cannot ask a formatter for thousands of stack frames. A hand-written formatter descends with `TryReadMessage(formatter, out value)` or `new WProtoReader(payload, in parent)`, which carry the depth ([#343](https://github.com/Ambiguous-Interactive/unity-helpers/issues/343), [#377](https://github.com/Ambiguous-Interactive/unity-helpers/issues/377)).
- Add `WProtoGeneric<T>.CanEncode`, which reports whether a closed type argument can be encoded at all, `IsMessage`, which reports whether it is encoded as a sub-message, and a `TryReadValue(ref reader, payload, out value)` overload that decodes accumulated occurrences ([#484](https://github.com/Ambiguous-Interactive/unity-helpers/issues/484)).
- Add `WProtoRectangular`, the shape check and refusal message a hand-written rectangular-array formatter needs.
- Add `WProtoRepeated.NullElement()` and `NullNestedElement()`, which build the exceptions a generated formatter throws for a `null` repeated element or inner collection.
- Add `WProtoFormatterProvider.UnexpectedSubtype()`, which builds the exception thrown for a value whose runtime type its contract does not declare.
- Add `WPROTO018`, a build error when a `[WProtoContract]`'s base is one too but is not declared with `[WProtoInclude]` ([#394](https://github.com/Ambiguous-Interactive/unity-helpers/issues/394)).
- Add `WPROTO028`, a warning when a closed construction cannot be registered because it closes over a `private` nested type. It is skipped rather than failing the build ([#414](https://github.com/Ambiguous-Interactive/unity-helpers/issues/414)).
- Add `WPROTO030`, an informational diagnostic when a protobuf-net contract has no `[WProtoContract]`, so migration is an opt-in worklist. It reads `[ProtoContract]`, a vendored protobuf-net under a renamed namespace, and a `[DataContract]` with ordered `[DataMember]`s, and names which matched ([#407](https://github.com/Ambiguous-Interactive/unity-helpers/issues/407), [#597](https://github.com/Ambiguous-Interactive/unity-helpers/issues/597)).
- Add `WPROTO031`, a warning when assemblies declare different roots for the same type ([#419](https://github.com/Ambiguous-Interactive/unity-helpers/issues/419)).
- Add `WPROTO032`, a build error when a member's collections nest more than 64 deep, which is deeper than the reader can read back ([#399](https://github.com/Ambiguous-Interactive/unity-helpers/issues/399)).

### Security

- Clamp or refuse a capacity a payload claims in `Deque`, `SparseSet`, `BitSet` and `ImmutableBitSet`. Six bytes claiming `int.MaxValue` previously allocated 8-16 GB and crashed the player. Raise `SerializationCapacityLimits.MaximumRestoredCapacity` if your saves exceed 1,048,576 elements ([#429](https://github.com/Ambiguous-Interactive/unity-helpers/pull/429)).

### Changed

- Restrict `Equals(object)` on `Attribute`, `WGuid`, `FastVector2Int`, `FastVector3Int`, `SerializableType`, `SerializableNullable<T>` and both `SerializableValueTuple` arities to its own type. A boxed `float`, `Guid`, `Type`, `Vector2Int` or `ValueTuple` never answered true in return; the typed overloads are unchanged ([#639](https://github.com/Ambiguous-Interactive/unity-helpers/issues/639)).
- Change `Attribute` to hash its `CurrentValue`, so an attribute already sitting in a `Dictionary` or `HashSet` becomes unreachable the moment a modification changes that value. Re-add it after applying one ([#639](https://github.com/Ambiguous-Interactive/unity-helpers/issues/639)).
- Restrict `CompareTo(object)` on `FastVector2Int` and `FastVector3Int` to its own type, so it can no longer answer 0 for a pair `Equals` refuses. The cross-dimensional `CompareTo` overloads are now `[Obsolete]` and go away in 4.0 ([#639](https://github.com/Ambiguous-Interactive/unity-helpers/issues/639)).
- Say what `Objects.HashCode` actually promises: the mixing is fixed, but each argument contributes an ordinary `GetHashCode`, which is process-local for strings, Unity objects and your own types. It was documented as deterministic and offered for save files ([#639](https://github.com/Ambiguous-Interactive/unity-helpers/issues/639)).
- Change `PRNG.Instance` to return `Xoshiro256StarStar`: an `Excellent`-rated generator with a published 2^256-1 period and reference, measured even with the previous default. Streams drawn from `PRNG.Instance` differ; construct a generator directly to keep one ([#516](https://github.com/Ambiguous-Interactive/unity-helpers/issues/516)).
- An attribute with only additive modifications recalculates 2.85x faster: 0.4563 us before, 0.1600 us now, measured on `6000.4.6f1`. Addition, Multiplication and Override each got a full pass over every modification whether or not any carried that action ([#529](https://github.com/Ambiguous-Interactive/unity-helpers/issues/529)).
- A relational field that finds nothing is ~15x cheaper to assign: 366-431 us before, 25.1 us now, measured on `6000.4.6f1` against a control that did not move. Unity captured a stack trace for the error log on every assignment, and for a collection field finding nothing is a normal state ([#564](https://github.com/Ambiguous-Interactive/unity-helpers/issues/564), [#529](https://github.com/Ambiguous-Interactive/unity-helpers/issues/529)).
- **`RomuDuo` now implements published romuDuo, so a given seed produces a different sequence than in 3.5.1.** No saved seed or `RandomState` carries a sequence across this change; pin a generator whose stream is unchanged if you need one to ([#509](https://github.com/Ambiguous-Interactive/unity-helpers/issues/509)).
- `DisjointSet.TryGetAllSets()` is 2.4x-3.2x faster and allocates no temporary lists. It gathered every element into a per-root scratch list and then copied all of them a second time; elements now go straight into their result list, found through a dense index rather than a hash lookup ([#309](https://github.com/Ambiguous-Interactive/unity-helpers/issues/309)).
- `Serializer.ProtoSerialize(value, ref buffer)` no longer allocates a second full payload for the `Serializable` collection types. The overload exists so a per-frame serialize allocates nothing ([#504](https://github.com/Ambiguous-Interactive/unity-helpers/issues/504)).
- Serializing a `SerializableDictionary` or `SerializableHashSet` no longer resolves its protobuf wrapper type by reflection on every call; the type and its constructor are resolved once per element type ([#504](https://github.com/Ambiguous-Interactive/unity-helpers/issues/504)).
- Writing a `WGuid` to JSON no longer allocates a 36-character string per value, and reading an `AnimationCurve` keyframe no longer boxes its `weightedMode` ([#504](https://github.com/Ambiguous-Interactive/unity-helpers/issues/504)).
- Relational fields typed as an interface no longer fetch every component on the object and type-test each one. Unity's own query resolves an interface, so the query is 1.35x-2.49x faster depending on how many components the object carries -- and the child shape pays it once per descendant ([#529](https://github.com/Ambiguous-Interactive/unity-helpers/issues/529)).
- Relational collection fields typed as a base component -- `Collider2D[]`, `Renderer[]` -- no longer fetch every component on the object and type-test each one. Unity's own query already resolves a base class, so assignment of a component with three such fields is 9% faster ([#529](https://github.com/Ambiguous-Interactive/unity-helpers/issues/529)).
- `Animator.ResetTriggers()` no longer allocates. It read `Animator.parameters`, which builds a new array and new element objects on every read -- 53.5 bytes per call for a three-parameter controller. The trigger hashes are now read once per controller ([#549](https://github.com/Ambiguous-Interactive/unity-helpers/issues/549)).
- `GameObject.IsDontDestroyOnLoad()` no longer allocates. It read `Scene.name`, which marshals a fresh managed string every call, and the signature reads cheap enough to end up in `Update`. The answer now comes from the scene's handle ([#549](https://github.com/Ambiguous-Interactive/unity-helpers/issues/549)).
- `WGroup` and `WGroupEnd` now accept fields only. They advertised properties, which nothing lays out, so the attribute compiled and drew nothing. Use `[field: WGroup(...)]` on an auto-property ([#550](https://github.com/Ambiguous-Interactive/unity-helpers/issues/550)).
- `WShowIf` now reads a condition that names a `[field: SerializeField]` auto-property from the serialized state rather than the live object, so a pending Inspector edit is what it reacts to ([#550](https://github.com/Ambiguous-Interactive/unity-helpers/issues/550)).
- Draw 64-bit values 2.49x faster from `BlastCircuitRandom`, `RomuDuo`, `SplitMix64`, `WyRandom` and `Xoshiro256StarStar` -- one state advance instead of two. `NextUlong`, `NextLong` and `NextDouble` return a different sequence for a given seed; 32-bit draws are unchanged. See [Seeded streams that moved](./docs/features/utilities/random-generators.md#seeded-streams-that-moved) ([#509](https://github.com/Ambiguous-Interactive/unity-helpers/issues/509)).
- Return the strong half of `XoroShiroRandom`, raising it from `Fair` to `Good`: its `NextBool()` was predictable from 128 draws, and PractRand now runs clean through 8GB where it used to fail at 16MB. Every draw changes for a given seed ([#509](https://github.com/Ambiguous-Interactive/unity-helpers/issues/509), [#285](https://github.com/Ambiguous-Interactive/unity-helpers/issues/285)).
- Stop the serializer allocating a delegate on every collection serialize. Four cache lookups built their factory per call rather than reusing one, costing 106-116 bytes each time, cache hit included ([#504](https://github.com/Ambiguous-Interactive/unity-helpers/issues/504)).
- Assign a hierarchy 30% faster when most of its components have no relational fields: `AssignHierarchy` took a lock per component just to answer "does this type have any", costing 59.7 ns per component against 41.5 ns now ([#529](https://github.com/Ambiguous-Interactive/unity-helpers/issues/529)).
- Deserialize a `SerializableDictionary`, `SerializableSortedDictionary`, `SerializableHashSet` or `SerializableSortedSet` from JSON without reflection: the converters looked their backing fields up on every read, costing 1.5 us of a 17.6 us 16-entry read ([#504](https://github.com/Ambiguous-Interactive/unity-helpers/issues/504)).
- Stop `[ChildComponent]` and `[ParentComponent]` collection fields allocating a `Component[]` on every assignment, so a scene full of components no longer builds garbage during `Awake` ([#534](https://github.com/Ambiguous-Interactive/unity-helpers/issues/534), [#529](https://github.com/Ambiguous-Interactive/unity-helpers/issues/529)).
- Assign relational array fields faster: a `[SiblingComponent]` array field costs 22% less, and sibling collection fields no longer allocate per call. `List` and `HashSet` fields are unchanged ([#529](https://github.com/Ambiguous-Interactive/unity-helpers/issues/529)).
- Write `FastVector2Int` and `FastVector3Int` components as `sint32`, so a negative coordinate costs one byte instead of ten. A 1,000-cell tilemap centred on the origin falls from 14,690 to 3,870 bytes. Payloads written by 3.5.1 still read ([#527](https://github.com/Ambiguous-Interactive/unity-helpers/issues/527)).
- Stop `FastVector2Int` and `FastVector3Int` writing their cached hash, which every reader already recomputed: a 1,000-cell tilemap falls from 14,167 to 5,870 bytes. Payloads written by 3.5.1 still read; a 3.5.1 build cannot read new ones ([#519](https://github.com/Ambiguous-Interactive/unity-helpers/issues/519)).
- Remove an item from a `SpatialHash2D`/`SpatialHash3D` bucket by swapping the last entry into its place rather than shifting the tail. Query results were never ordered and are not now; the order two items in one cell come back in can differ from before.
- Say what has actually been measured about `IllusionFlow`, `StormDropRandom`, `PhotonSpinRandom`, `FlurryBurstRandom` and `BlastCircuitRandom`. All five are now verified clean through 8GB of PractRand 0.95 here, rather than described by an author's claim on a repository that is offline. Ratings are unchanged ([#286](https://github.com/Ambiguous-Interactive/unity-helpers/issues/286), [#516](https://github.com/Ambiguous-Interactive/unity-helpers/issues/516)).
- Take every `Bounds`, `BoundsInt`, `Rect` and `Color` extension receiver by `in`. Calling one no longer copies the struct at your call site, which `ErrorProne.NET.Structs` reported as an `EPS06` warning you could not fix without giving up extension syntax ([#512](https://github.com/Ambiguous-Interactive/unity-helpers/issues/512)).
- Make `PooledArray<T>` and `PooledResource<T>` `readonly struct`, so reading `array`, `length` or `resource` and calling `Dispose()` no longer copies the wrapper first ([#512](https://github.com/Ambiguous-Interactive/unity-helpers/issues/512)).
- Lower `XoroShiroRandom` to `Fair` and `RomuDuo` to `Good`. Bit 0 of `XoroShiroRandom` follows a linear recurrence of order 128, so its `NextBool()` is predictable from 128 draws; prefer `Xoshiro128StarStar` where single bits matter ([#509](https://github.com/Ambiguous-Interactive/unity-helpers/issues/509), [#286](https://github.com/Ambiguous-Interactive/unity-helpers/issues/286)).
- Lower `SquirrelRandom` to `Fair`. It fails PractRand at 1GB, reproducibly across four seeds; it stays a good fit for the table lookups it was designed for ([#286](https://github.com/Ambiguous-Interactive/unity-helpers/issues/286)).
- Read cancellation-aware JSON files through a pooled scratch buffer and deserialize the pooled stream's valid segment directly. Large save files no longer allocate an extra full-payload copy before decoding ([#504](https://github.com/Ambiguous-Interactive/unity-helpers/issues/504)).
- Speed up every pooled-buffer operation on blittable elements -- the sorts, `Shuffle`, `Fill` and the geometry helpers -- by clearing a returned array only when its element type can hold a reference. A reference element is still never left rooted in the pool ([#482](https://github.com/Ambiguous-Interactive/unity-helpers/issues/482)).
- Speed up `ProtoSerialize`, `ProtoDeserialize`, `ProtoEquals` and `NextEnum` by resolving each one's type questions once per closed generic instead of on every call. Measured at 8.5x for that check alone on reference types ([#346](https://github.com/Ambiguous-Interactive/unity-helpers/issues/346)).
- Every `IList<T>` sort now runs over an array rather than the list's indexer, so sorting a `T[]` is 2.5x to 5.4x faster and needs no copy. Any other list is copied through a pooled buffer and back. See [Where the Time Actually Goes](./docs/performance/ilist-sorting-performance.md#where-the-time-actually-goes) ([#463](https://github.com/Ambiguous-Interactive/unity-helpers/issues/463)).
- Speed up `IList<T>.Reverse` (26x-37x), `Shift`/`RotateLeft`/`RotateRight` (2.7x-36x), `Fill` (3.6x-38x), `Shuffle` (1.5x-2x) and predicate `IndexOf`/`LastIndexOf` (2.4x-3.1x) with bulk array operations. See [Bulk Operations on a List](./docs/performance/ilist-sorting-performance.md#bulk-operations-on-a-list) ([#480](https://github.com/Ambiguous-Interactive/unity-helpers/issues/480)).
- Enable WallstopProto by default for the runtime assembly: every `Serializer.ProtoSerialize` and `ProtoDeserialize` overload, including `ref buffer` and `forceRuntimeType: true`, uses generated formatters when available and falls back to protobuf-net ([#343](https://github.com/Ambiguous-Interactive/unity-helpers/issues/343), [#403](https://github.com/Ambiguous-Interactive/unity-helpers/issues/403)).
- Serialize thirty of this package's own contracts through WallstopProto when `WALLSTOP_PROTO` is defined, including `AbstractRandom` and all seventeen generators. Saved data is unchanged ([#394](https://github.com/Ambiguous-Interactive/unity-helpers/issues/394)).
- Serialize `SerializableHashSet`, `SerializableSortedSet`, `SerializableDictionary`, `SerializableSortedDictionary`, `Deque`, `CyclicBuffer` and `SparseSet` through WallstopProto when `WALLSTOP_PROTO` is defined. Saved data is unchanged ([#402](https://github.com/Ambiguous-Interactive/unity-helpers/issues/402)).
- Allow a `struct` backing set in `SerializableSetBase<T, TSet>`: the `where TSet : class` constraint is gone ([#388](https://github.com/Ambiguous-Interactive/unity-helpers/issues/388)).
- Report a read of the write-only `GameObject` and `Touch` JSON converters as `NotSupportedException` instead of `NotImplementedException` ([#437](https://github.com/Ambiguous-Interactive/unity-helpers/issues/437)).
- Ship the bundled `System.Text.Json` and friends only on editors that do not provide them; Unity supplies its own from 6000.5. See [Bundled Assembly Conflicts](./docs/guides/bundled-assembly-conflicts.md) ([#331](https://github.com/Ambiguous-Interactive/unity-helpers/issues/331)).

### Fixed

- Fix `Circle`, `Sphere`, `Line2D`, `Line3D` and `PoolFrequencyStatistics` comparing equal while hashing apart, so a value could vanish from the set it had just been added to. Equality is exact now; use `ApproximatelyEquals` for a tolerance ([#639](https://github.com/Ambiguous-Interactive/unity-helpers/issues/639)).
- Fix `Attribute` hashing by reference identity while `Equals` compared `CurrentValue`, which put two equal attributes in different dictionary buckets. It hashes `CurrentValue` now ([#639](https://github.com/Ambiguous-Interactive/unity-helpers/issues/639)).
- Fix `AttributeEffect.Equals` ignoring `periodicEffects`, `behaviors` and all four stacking fields, and its hash ignoring the name it compares. Equality reads every authored field now, and the hash reads the managed ones ([#639](https://github.com/Ambiguous-Interactive/unity-helpers/issues/639)).
- Fix `CosmeticEffectData` hashing its component count while `Equals` compared a deduplicated type set, so one copy of a component and two compared equal and hashed apart ([#639](https://github.com/Ambiguous-Interactive/unity-helpers/issues/639)).
- Fix an effect's stack key becoming unreachable once the effect asset was destroyed. The key compared managed identity and hashed through Unity's own `GetHashCode`, which collapses on destruction ([#639](https://github.com/Ambiguous-Interactive/unity-helpers/issues/639)).
- Fix `default(RandomState)` hashing zero while comparing equal to `new RandomState(0)`. The hash comes from the members `Equals` compares, never from the value a payload carried, and skips a gaussian the state says it does not hold ([#639](https://github.com/Ambiguous-Interactive/unity-helpers/issues/639)).
- Fix `AnimationEventEqualityComparer.GetHashCode(null)` throwing while its `Equals` reported two nulls equal, which made `HashSet<AnimationEvent>.Add(null)` fail. Null hashes to zero ([#639](https://github.com/Ambiguous-Interactive/unity-helpers/issues/639)).
- Fix `SerializableType.Equals(null)` and `SerializableNullable<T>.Equals(null)` answering true. A boxed value type is a real object, so `Object.Equals` requires false; test emptiness with `IsEmpty` or `HasValue` ([#639](https://github.com/Ambiguous-Interactive/unity-helpers/issues/639)).
- Fix effect teardown that ran your callbacks before the handle was detached: one that removed, re-applied or queried its own effect saw it still active, recursed, or corrupted a pooled buffer mid-tick. See [Removal Is Two-Phase](./docs/features/effects/effects-system.md#removal-is-two-phase) ([#640](https://github.com/Ambiguous-Interactive/unity-helpers/issues/640)).
- Fix `IRandom.Next()` and `NextLong()` returning `int.MaxValue` and `long.MaxValue`, one value outside their documented `[0, max)` range. Values from those two methods change for a given seed; raw `NextUint`/`NextUlong` streams and saved generator state do not ([#638](https://github.com/Ambiguous-Interactive/unity-helpers/issues/638)).
- Fix guards a destroyed object walked straight through: `?.` and `??` compare against CLR null, so `TagHandler`, `EffectHandler`, `SingletonAutoLoader`, two property drawers and the settings asset acted on effects, windows and assets that were already gone ([#621](https://github.com/Ambiguous-Interactive/unity-helpers/issues/621)).
- Fix `RuntimeSingleton<T>.Instance` handing back a live-but-inert `GameObject` when the singleton component could not be attached. The half-built object is destroyed and `Instance` returns null instead. ([#629](https://github.com/Ambiguous-Interactive/unity-helpers/issues/629)).
- Fix relational component assignment falling back to reflection after its metadata cache was destroyed: a destroyed `_metadataCache` field skipped the live `AttributeMetadataCache.Instance` instead of using it. ([#629](https://github.com/Ambiguous-Interactive/unity-helpers/issues/629)).
- Fix the Animation Creator treating a frame index too large for an `int` as index `0`, where it collided with a real frame `0`. Such a name is no longer read as an indexed frame. ([#629](https://github.com/Ambiguous-Interactive/unity-helpers/issues/629)).
- Fix reads of `TryXxx` `out` values whose call was never tested, including a `RollingHighWaterMark` cleanup that could subtract a sample nobody wrote and then loop forever ([#629](https://github.com/Ambiguous-Interactive/unity-helpers/issues/629)).
- Fix two documentation examples that could not work as printed: the enum display-name sample imported `Core.Attribute` rather than `Core.Attributes`, and the `link.xml` sample preserved an assembly name that does not exist, which strips silently ([#441](https://github.com/Ambiguous-Interactive/unity-helpers/issues/441)).
- Fix fifteen broken documentation links on `AssetDatabaseBatchScope`: its `<see cref>` references to `AssetDatabase.Refresh`, `CreateAsset` and `ImportAsset` named an ambiguous overload or an unresolvable type, so an IDE linked the wrong overload or nothing ([#594](https://github.com/Ambiguous-Interactive/unity-helpers/issues/594)).
- Fix the IntelliSense tooltip on ten public `ReflectionHelpers` delegate factories, which carried two `<summary>` tags and showed the vaguer one ([#441](https://github.com/Ambiguous-Interactive/unity-helpers/issues/441)).
- Fix zero-valued `ValueTuple` components and fixed-width map keys being omitted by WallstopProto where protobuf-net writes them explicitly, including enum tuple map keys ([#399](https://github.com/Ambiguous-Interactive/unity-helpers/issues/399)).
- Fix `string.FromBase64()` inventing replacement-character text when the decoded bytes are not valid UTF-8; corrupt payloads now return an empty string instead ([#580](https://github.com/Ambiguous-Interactive/unity-helpers/issues/580)).
- Fix entering Play Mode destroying scene-authored `RuntimeSingleton` components before their scene starts ([#582](https://github.com/Ambiguous-Interactive/unity-helpers/issues/582)).
- Fix double allocation when JSON-deserializing arrays: array growth rents from the shared pool and collection property names match without a throwaway string ([#504](https://github.com/Ambiguous-Interactive/unity-helpers/issues/504)).
- An `AttributeEffect` authoring mistake -- `Instant` with periodic or behaviour data, or an unassigned cosmetic entry -- is now reported once per effect and in the Inspector, not on every application. Each report rendered the whole effect to JSON, measured at 20.5 us ([#567](https://github.com/Ambiguous-Interactive/unity-helpers/issues/567)).
- Fix `Attribute.CurrentValue` reporting the wrong number in the editor: outside play mode it discarded the cached value, so an attribute deserialized while buffed lost the buff, and in play mode an Inspector edit to the base value left the cache stale ([#569](https://github.com/Ambiguous-Interactive/unity-helpers/issues/569)).
- Fix a single `[SiblingComponent]` or `[ChildComponent]` field binding a disabled component when `IncludeInactive = false`. With two candidates on one object and the first disabled, the disabled one was assigned instead of the enabled one behind it ([#529](https://github.com/Ambiguous-Interactive/unity-helpers/issues/529)).
- Fix a second `await` of the same `AsyncOperation` stopping the first one resuming. Two coroutines or tasks awaiting one `SceneManager.LoadSceneAsync` handle now both continue; previously only the last to register did.
- Fix one refused protobuf surrogate registration disabling every registration after it, so `Vector3`, `Color` and `Bounds` silently encoded with different bytes. Each is now independent and names the type it could not register.
- Fix `UnityRandom` losing its position when saved: its snapshot now carries `UnityEngine.Random`'s state, so a restored generator resumes the exact sequence instead of continuing from wherever the engine is. Restoring writes that global back. See [Random Generators](./docs/features/utilities/random-generators.md#saving-and-restoring-a-generator) ([#521](https://github.com/Ambiguous-Interactive/unity-helpers/issues/521)).
- Fix protobuf-net writing a different payload than WallstopProto for a zero-initialized `FastVector2Int` or `FastVector3Int`. The surrogate mirrored the cached hash through `GetHashCode()` rather than the stored field, so the two encoders disagreed on the origin ([#309](https://github.com/Ambiguous-Interactive/unity-helpers/issues/309)).
- Fix `default(FastVector2Int)` and `default(FastVector3Int)` comparing unequal to the origin they describe. An array element, an unset field or a dictionary miss did not match `new FastVector2Int(0, 0)`, so a set held the origin cell twice. Wire format is unchanged ([#309](https://github.com/Ambiguous-Interactive/unity-helpers/issues/309)).
- Fix `default(CacheStatistics)` and `default(PoolStatistics)` comparing unequal to an all-zero snapshot, for the same reason ([#309](https://github.com/Ambiguous-Interactive/unity-helpers/issues/309)).
- Fix `PoolStatistics` hashing the three rates it compares with a tolerance, so two snapshots that were equal could hash differently and a set held both ([#309](https://github.com/Ambiguous-Interactive/unity-helpers/issues/309)).
- Fix generator metadata that named the wrong algorithm: `LinearCongruentialGenerator` is the Numerical Recipes `ranqd1` LCG, not Park-Miller; `RomuDuo` matches neither published ROMU duo variant; `IllusionFlow` is not a PCG or xorshift hybrid ([#509](https://github.com/Ambiguous-Interactive/unity-helpers/issues/509)).
- Fix the license recorded for the ROMU algorithm behind `RomuDuo`: it is Apache 2.0, not CC0 ([#509](https://github.com/Ambiguous-Interactive/unity-helpers/issues/509)).
- Fix `SortByName()` and `ScriptableObjectSingleton<T>.Instance` throwing on a name whose trailing digits do not fit an `int`, such as a timestamp, or are not ASCII digits. Such names now order correctly, and a suffix of any length is compared without being parsed ([#386](https://github.com/Ambiguous-Interactive/unity-helpers/issues/386)).
- Fix `string.Reverse()` destroying emoji and other non-BMP characters: it split every surrogate pair, so the result encoded as replacement characters. Reversing twice now returns the original ([#386](https://github.com/Ambiguous-Interactive/unity-helpers/issues/386)).
- Fix `string.Truncate()` returning a result longer than the limit it was given when the ellipsis did not fit, and cutting characters in half. The result now always fits and is always valid text ([#386](https://github.com/Ambiguous-Interactive/unity-helpers/issues/386)).
- Fix `SerializedStringComparer` throwing from `Equals` and `GetHashCode` when its serialized mode held an unrecognized value. It falls back to ordinal comparison, and hashes null instead of throwing ([#386](https://github.com/Ambiguous-Interactive/unity-helpers/issues/386)).
- Fix fourteen comparables ordering null last instead of first, disagreeing with every `IComparable` in the framework and with each other. `StringWrapper` also ordered by hash code rather than by its string ([#386](https://github.com/Ambiguous-Interactive/unity-helpers/issues/386)).
- Fix `XoroShiroRandom` being documented as xoshiro128**. It is xoroshiro128+, whose lowest bits are the weak ones and the half this returns, so its rating is now Good ([#285](https://github.com/Ambiguous-Interactive/unity-helpers/issues/285)).
- Fix `NextGuid()` and `NextWGuid()` throwing on any generator restored from a protobuf payload read by protobuf-net. Twelve generators were affected, so the first GUID drawn after loading a save crashed ([#492](https://github.com/Ambiguous-Interactive/unity-helpers/issues/492)).
- Fix `IllusionFlow`, `PcgRandom`, `RomuDuo`, `XorShiftRandom` and `XoroShiroRandom` replaying a different sequence than the one saved when their whole serialized state was at its default ([#492](https://github.com/Ambiguous-Interactive/unity-helpers/issues/492)).
- Fix dead or crashing streams when restored shared reservoir, PCG, StormDrop, PhotonSpin or SystemRandom state is invalid. Repair and PhotonSpin warmup now run after construction or deserialization, not on every draw ([#492](https://github.com/Ambiguous-Interactive/unity-helpers/issues/492), [#503](https://github.com/Ambiguous-Interactive/unity-helpers/issues/503)).
- Fix `Helpers.EnumeratePrefabs` and `EnumerateScriptableObjects` searching folders the caller never named when the asset paths were passed as anything other than a `string[]` ([#482](https://github.com/Ambiguous-Interactive/unity-helpers/issues/482)).
- Fix `[WShowIf]` never matching a member of a `ulong`-backed enum above `long.MaxValue`, which hid the field it was meant to reveal ([#346](https://github.com/Ambiguous-Interactive/unity-helpers/issues/346)).
- Fix a chosen text colour on a `[WButton]` or `[WEnumToggleButtons]` palette entry being overwritten whenever its red, green and blue were all zero, so opaque black was replaced by the auto-computed colour and any transparency was discarded. Existing entries keep the colour they were showing ([#476](https://github.com/Ambiguous-Interactive/unity-helpers/issues/476)).
- Fix settings color keys being stored case-sensitively while every reader matched them without regard to case, so `"Save"` and `"save"` were two palette entries on disk and one entry to every reader ([#476](https://github.com/Ambiguous-Interactive/unity-helpers/issues/476)).
- Fix `PositiveMod` returning a negative result -- the one thing it promises never to do -- for any maximum above 2^30 (`int`) or 2^62 (`long`), and rounding `float` and `double` values that were already inside the range. `WrappedAdd` now wraps the real sum when adding overflows.
- Fix `float` and `double` `PositiveMod` returning `0` for every input when the maximum is 1, which made the normalized-phase case it documents useless: `5.5f.PositiveMod(1f)` is `0.5f`. It could also return the maximum itself when the remainder was too small to survive being added to it.
- Fix `SerializableSortedDictionary` skipping the rebuild of its serialized arrays when two stored keys compare equal under its own comparer, which could leave a newly added key unsaved ([#476](https://github.com/Ambiguous-Interactive/unity-helpers/issues/476)).
- Fix `SerializableDictionary`, `SerializableSortedDictionary` and `SerializableHashSet` writing back entries their own comparer holds as one, so a case-insensitive dictionary could save two entries that merged into one on the next load ([#476](https://github.com/Ambiguous-Interactive/unity-helpers/issues/476)).
- Fix `SortAlgorithm.Power` and `SortAlgorithm.PowerPlus` reordering elements that compare equal, which both are documented as never doing. Any list whose equal elements sit inside a descending stretch was affected ([#461](https://github.com/Ambiguous-Interactive/unity-helpers/issues/461)).
- Fix `SerializableDictionary` and `SerializableSortedDictionary` discarding a comparer they were constructed with, so seeding one with `StringComparer.OrdinalIgnoreCase` — the documented way to make it case-insensitive — did nothing ([#472](https://github.com/Ambiguous-Interactive/unity-helpers/issues/472)).
- Fix the settings window treating any color within about two and a half 8-bit steps of the factory default as untouched, so a color you had deliberately changed could be overwritten by the suggested palette ([#472](https://github.com/Ambiguous-Interactive/unity-helpers/issues/472)).
- Fix the inspector repainting on every settings check once a custom color held a `NaN` channel, and missing the removal of a color key present in any snapshot but the first ([#472](https://github.com/Ambiguous-Interactive/unity-helpers/issues/472)).
- Fix a log format naming a color Unity cannot parse — `$"{value:#notacolor}"` — emitting a rich text tag the console shows as literal markup instead of the value. The value is now logged undecorated ([#473](https://github.com/Ambiguous-Interactive/unity-helpers/issues/473)).
- Fix `LayeredImage` discarding one alpha level more than its `pixelCutoff` asks for at some cutoffs and not others, so it kept different pixels than the sprite tools at the same cutoff ([#473](https://github.com/Ambiguous-Interactive/unity-helpers/issues/473)).
- Fix the multi-file selector's list background being white at 15% opacity rather than the opaque dark panel it was written as, and the sprite animation creator's selected-thumbnail border coming back out of gamut and more opaque than its fill. `Color * float` scales alpha too ([#473](https://github.com/Ambiguous-Interactive/unity-helpers/issues/473)).
- Fix `[WButton]` and the serialized collection drawers labelling buttons with the less readable of black and white on 22.9% of colors. The shipped green "Add" button was at 2.84:1, below the 3:1 large-text floor; it is now 7.40:1 ([#471](https://github.com/Ambiguous-Interactive/unity-helpers/issues/471)).
- Fix a dark `[WButton]` showing no hover or press feedback: darkening a color already near black clamped all three states to the same color ([#471](https://github.com/Ambiguous-Interactive/unity-helpers/issues/471)).
- Fix `Serializer.ProtoDeserialize()` refusing to read back what `ProtoSerialize()` wrote for a value whose fields are all at their defaults — `Vector3.zero`, `Color.clear`, `Quaternion(0,0,0,0)` and any such contract encode to zero bytes, which was rejected as empty input. A `null` payload is still refused ([#474](https://github.com/Ambiguous-Interactive/unity-helpers/issues/474)).
- Fix `TextureScale.Bilinear()` and `Point()` shifting a scaled texture half a texel toward the origin: an upscale never reached the source's brightest pixel and a symmetric image downscaled asymmetrically ([#470](https://github.com/Ambiguous-Interactive/unity-helpers/issues/470)).
- Fix `TextureScale.Bilinear()` and the Image Blur tool pulling a fully transparent pixel's color into its visible neighbors, so red beside transparent green produced a yellow edge. Opaque textures are unchanged ([#470](https://github.com/Ambiguous-Interactive/unity-helpers/issues/470)).
- Fix the sprite sheet extractor's preview thumbnails sampling half a texel toward the origin ([#470](https://github.com/Ambiguous-Interactive/unity-helpers/issues/470)).
- Fix the `WButton` and `WEnumToggleButtons` style and texture caches growing without bound as inspector colours change: two colours the caches called equal were stored under different keys, so every repaint could add another entry ([#466](https://github.com/Ambiguous-Interactive/unity-helpers/issues/466)).
- Fix a `WButton` hover or pressed colour derived from an out-of-gamut palette colour coming back with a negative channel ([#466](https://github.com/Ambiguous-Interactive/unity-helpers/issues/466)).
- Fix `IRandom.NextColorInRange()` returning the varied colour fully opaque, discarding the alpha of the base colour ([#466](https://github.com/Ambiguous-Interactive/unity-helpers/issues/466)).
- Fix `IRandom.NextColorInRange()` throwing `ArgumentException` for a variance of zero or a negative variance, and returning black for one that is not a finite number ([#466](https://github.com/Ambiguous-Interactive/unity-helpers/issues/466)).
- Fix `Color.ChangeColorBrightness()` returning a colour whose channels are all `NaN` when the correction factor is `NaN` ([#466](https://github.com/Ambiguous-Interactive/unity-helpers/issues/466)).
- Fix a save holding an unset `WGuid` failing to load: JSON wrote the empty GUID and then refused to read it back ([#437](https://github.com/Ambiguous-Interactive/unity-helpers/issues/437)).
- Fix a JSON payload with `min` greater than `max`, or with `max` missing, crashing a `Range<T>` load with `ArgumentException` instead of reporting corrupt data ([#437](https://github.com/Ambiguous-Interactive/unity-helpers/issues/437)).
- Fix a `Gradient` with more than eight colour or alpha keys silently losing the extras and filling the player log with errors. The payload is now refused ([#437](https://github.com/Ambiguous-Interactive/unity-helpers/issues/437)).
- Fix `Serializer.JsonStringify` and `JsonSerialize` failing on a `Type`, at the root of a graph or behind an `object` member, despite the shipped `Type` converter ([#437](https://github.com/Ambiguous-Interactive/unity-helpers/issues/437)).
- Fix an unattributed field joining the wrong group when a type declares more than one `[WGroup]`, and a bare `[WGroupEnd]` closing a group other than the one it follows. Auto-include now targets the most recently declared group, and a bare end closes every open group ([#455](https://github.com/Ambiguous-Interactive/unity-helpers/issues/455)).
- Fix a `SerializableDictionary`'s "Add entry" Value field being drawn 8.5px left of its Key field wherever the inspector indents it ([#284](https://github.com/Ambiguous-Interactive/unity-helpers/issues/284)).
- Fix a watcher on a non-`GameObject` type loading every imported prefab, which is where `SendMessage cannot be called during Awake, CheckConsistency, or OnValidate` came from. A sub-asset nested inside a `.prefab` no longer matches a watcher on its type ([#280](https://github.com/Ambiguous-Interactive/unity-helpers/issues/280)).
- Fix the asset-change watcher deserializing assets during import to decide whether a path holds a watched type, which produced the same warning. The decision is now made from asset metadata ([#439](https://github.com/Ambiguous-Interactive/unity-helpers/issues/439)).
- Fix `[DetectAssetChanged]` crashing headless editors; it no longer initializes in batch mode ([#327](https://github.com/Ambiguous-Interactive/unity-helpers/issues/327)).
- Fix `SerializableDictionary<TKey, List<TValue>>` and its sorted counterpart saving their keys and none of their values. Dictionaries with other value types keep exactly the same saved data ([#314](https://github.com/Ambiguous-Interactive/unity-helpers/issues/314), [#348](https://github.com/Ambiguous-Interactive/unity-helpers/issues/348)).
- Report `SerializableHashSet<List<TValue>>` in the Inspector instead of drawing a column that persists nothing: a list compares by reference, so such a set already treats equal contents as distinct elements ([#314](https://github.com/Ambiguous-Interactive/unity-helpers/issues/314), [#354](https://github.com/Ambiguous-Interactive/unity-helpers/issues/354)).
- Fix the Inspector drawing an empty value column instead of an error for dictionary values no wrapper repairs, such as `List<List<T>>` and jagged arrays. Sorted dictionaries are covered too ([#357](https://github.com/Ambiguous-Interactive/unity-helpers/issues/357)).
- Fix `StartFunctionAsCoroutine()` and `ExecuteOverTime()` stopping forever the first time their action threw. Both now report the failure against the owning object and keep running ([#359](https://github.com/Ambiguous-Interactive/unity-helpers/issues/359)).
- Fix disabled logging still evaluating its receiver and building its message, so `MySingleton.Instance.Log($"…")` created the singleton and allocated in a release build. Define `ENABLE_UBERLOGGING` to keep logging — see [Logging Extensions](./docs/features/logging/logging-extensions.md) ([#350](https://github.com/Ambiguous-Interactive/unity-helpers/issues/350)).
- Fix `GetRandomPointInCircle()` and `GetRandomPointInSphere()` returning points outside the shape far from the origin: at world coordinate 1,000,000 with radius 0.05, half of them were outside.
- Fix `NativePcgRandom` producing worse randomness than every other generator: `NextFloat()` could return exactly 1, `NextLong()` could return a negative, `NextUint(0)` threw, and most seeds built a shortened-period stream. Sequences for a given seed have changed ([#282](https://github.com/Ambiguous-Interactive/unity-helpers/issues/282)).
- Fix `ToCachedName()` and `ToDisplayName()` falling back to a dictionary for any signed enum with a negative member, and `sbyte` enums allocating a 256-entry cache ([#339](https://github.com/Ambiguous-Interactive/unity-helpers/issues/339)).
- Fix `EditorCacheHelper.GetEnumDisplayName()` rebuilding the enum's value array on every call. Display names are unchanged.
- Fix `[WEnumToggleButtons]` throwing `OverflowException` on an enum with a negative member and taking the Inspector down. The Odin drawer rendered such members as "None" and wrote that back ([#339](https://github.com/Ambiguous-Interactive/unity-helpers/issues/339)).
- Fix an interrupted `Serializer.WriteToJsonFile()` destroying the previous save. Every JSON file write now stages and swaps, and creates missing directories ([#319](https://github.com/Ambiguous-Interactive/unity-helpers/issues/319)).
- Fix a copied `PooledResource<T>` or `PooledArray<T>` returning the same instance to the pool twice, so two live rentals shared one buffer. Every disposable scope in the package is now disposed at most once, at no allocation cost ([#358](https://github.com/Ambiguous-Interactive/unity-helpers/issues/358)).
- Fix a double-disposed `AssetDatabaseBatchScope` ending an outer scope's batch early, leaving the rest of its asset writes unbatched ([#358](https://github.com/Ambiguous-Interactive/unity-helpers/issues/358)).
- Fix `WallstopArrayPool<T>` and `WallstopFastArrayPool<T>` allocating 32 bytes on every rent ([#367](https://github.com/Ambiguous-Interactive/unity-helpers/issues/367)).
- Fix `Color.ToHex()` truncating each channel instead of rounding it, so it disagreed with Unity's own `ColorUtility.ToHtmlStringRGBA()` on half of all colors and could not return `FF` for any channel below exactly 1 ([#466](https://github.com/Ambiguous-Interactive/unity-helpers/issues/466)).
- Fix the sprite cropper and pivot adjuster selecting different pixels than the sheet extractor at the same alpha cutoff, and two transparency scorers disagreeing with their twenty siblings by one alpha level ([#466](https://github.com/Ambiguous-Interactive/unity-helpers/issues/466)).
- Fix the Inspector's solid-texture cache holding two entries for one color: colors a hair either side of a channel boundary compared equal and hashed apart ([#466](https://github.com/Ambiguous-Interactive/unity-helpers/issues/466)).
- Fix `GetAverageColor(ColorAveragingMethod.Dominant)` returning a channel above 1 for a saturated color - a dominant white came back as 1.0039 ([#466](https://github.com/Ambiguous-Interactive/unity-helpers/issues/466)).
- Fix `GetAverageColor(ColorAveragingMethod.Weighted)` returning a fully transparent color for opaque black pixels, which weigh nothing under luminance weighting ([#466](https://github.com/Ambiguous-Interactive/unity-helpers/issues/466)).

## [3.5.1] - 2026-07-12

### Fixed

- **Unity license lock handoff after CI cleanup**: final Unity license returns now emit fail-closed cleanup proof to the organization build lock. Cleanup is bounded and non-masking, failed or cancelled acquisitions still release queued/owned lock state, and only a successful return command or exact allowlisted Unity responses permit the activation slot to enter cooldown instead of quarantine.

## [3.5.0] - 2026-07-07

### Added

- **Asset change detection loop reset utility**: Added `AssetChangeDetectionUtility.ResetLoopProtection()` so editor tooling can resume `[DetectAssetChanged]` dispatch after a recursive callback has tripped loop protection, without requiring a domain reload.
- **Feel-good randomness helpers**: Added `ExactAveragePrd`, `BadLuckProtection`, and `WeightedShuffleBag<T>` for player-facing randomness that reduces streak frustration while keeping deterministic, testable behavior. `ExactAveragePrd` solves the pseudo-random distribution coefficient for a configured long-run average, `BadLuckProtection` implements pity-timer chance ramps and optional hard guarantees, and `WeightedShuffleBag<T>` emits exact weighted ticket counts before repeating. The helpers expose explicit restore APIs so save/load systems can persist pity/deck state.

### Fixed

- **Optional Odin Inspector integration without unguarded Odin assemblies**: `RuntimeSingleton<T>`, `ScriptableObjectSingleton<T>`, and `AttributeEffect` now use Odin serialized base classes only when the `odininspector` package is installed and the package-owned `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR` define is active; otherwise they fall back to Unity base classes. Projects that still define the global `ODIN_INSPECTOR` symbol without shipping Sirenix assemblies no longer fail package compilation, while Odin projects keep the serialized base behavior automatically.

## [3.4.0] - 2026-07-01

See [the roadmap](./docs/overview/roadmap.md) for details

### Added

- **`AssetDatabaseBatchHelper.EnsureAssetFolder` / `EnsureAssetParentFolder`**: batch-safe helpers that register every missing folder segment with the `AssetDatabase` (never via raw disk, which can leave the database out of sync and spawn numbered duplicate folders) and pause any open `StartAssetEditing` batch first, so a subsequent `AssetDatabase.CreateAsset` cannot fail with "Parent directory must exist".

### Changed

- **`[WValueDropDown]` provider-misconfiguration is now logged as a Warning, not an Error**: a null/empty/missing/throwing or wrong-return-type dropdown value provider is a handled condition — the dropdown falls back to an empty option list — so `WValueDropDownAttribute` / `DropDownValueProvider` now log these diagnostics at `Warning` severity instead of `Error`. The message text is unchanged. This better reflects that the feature degrades gracefully, and (as a side effect) stops these editor-tooling diagnostics from failing PlayMode tests that incidentally exercise the dropdown path.
- **`DirectoryHelper.EnsureDirectoryExists` no longer logs an error before throwing**: passing a path outside `Assets/` in the editor previously emitted a `Debug.LogError` **and** threw an `ArgumentException` (a redundant double signal that polluted the console and could fail unrelated tests that did not expect the log). It now throws the `ArgumentException` only, and the exception message names the offending path (for example, `Cannot create directory 'SomeFolder/SubFolder' outside the Assets folder: AssetDatabase only manages paths under 'Assets/'.`). Callers that already catch the exception are unaffected; callers that relied on the console log should read the thrown exception instead.

### Fixed

- **Unity 6000.3 compatibility for runtime helpers**: `Object.GetUnityObjectId`, `UnityObjectExtensions.FindObjectsOfTypeShim`, `SceneHelper.EnsureOneComponent`, and the `Rigidbody2D` velocity helpers now gate Unity 6 APIs by the specific version that introduced them instead of treating every Unity 6000.x editor as equivalent. Unity 6000.3 and earlier supported matrix versions compile through the older overloads while Unity 6000.4+ continues to use the newer no-sort object lookup and `EntityId.ToULong` paths.
- **`EffectHandler` periodic catch-up after a long frame**: missed periodic ticks now advance on their scheduled interval instead of sliding from the current frame, and unlimited periodic effects are capped per update so a large frame hitch cannot monopolize the player loop. Remaining due ticks continue catching up on subsequent updates.
- **Protobuf abstract-root deserialization reporting corrupt data instead of a registration error**: abstract `[ProtoContract]` roots without `[ProtoInclude]` metadata now require an explicit `RegisterProtobufRoot` mapping before deserialize, so ambiguous abstract roots fail with the documented type-resolution exception instead of falling through to protobuf-net's version-specific corrupt-data / constructor errors. Explicit registrations also take precedence over abstract root heuristics.
- **Background logging using a stale Unity main-thread capture**: `WallstopStudiosLogger` now shares `UnityMainThreadGuard`'s captured main-thread state, preventing background log calls from being misclassified after test/player lifecycle resets.
- **Protobuf serialization of an EMPTY `SerializableHashSet` / `SerializableSortedSet` / `SerializableDictionary` / `SerializableSortedDictionary` throwing `SerializationInputException` ("data is empty") on deserialize**: an empty collection serializes to a zero-byte payload (the wrapper carries only repeated fields), but `ProtoDeserialize` ran its generic empty-payload guard before the collection-wrapper interception, so a valid empty collection could not round-trip. The collection interception now runs first, so an empty collection round-trips to an empty collection; the empty-payload guard still rejects empty input for ordinary (non-collection) message types. Wire format for non-empty payloads is unchanged.
- **Serialization crashing or silently corrupting data on IL2CPP/AOT standalone players**: `Serializer` now serializes the affected types through AOT-safe paths instead of protobuf-net's reflection / `Reflection.Emit` model, which under IL2CPP hit the unsupported `GetTypeModifiers` icall or bound immutable structs to empty values. `Deque<T>`, `CyclicBuffer<T>`, and `SparseSet` route through plain array/scalar protobuf wrappers (also fixing a spurious `[ProtoAfterDeserialization]` `ArgumentOutOfRangeException`); `FastVector2Int`, `FastVector3Int`, `ImmutableBitSet`, and `Parabola` route through mutable protobuf surrogates. `Serializer.JsonStringify` / `JsonSerialize` of anonymous types (and other types lacking a public parameterless constructor) now use a reflection-light writer instead of System.Text.Json's parameterized-constructor converter, which has no AOT code under IL2CPP. A package `link.xml` preserves the serialization assemblies. JSON output and protobuf round-trip values are unchanged on mono / editor. The package pre-registers the common element specializations (`int`, `long`, `float`, `double`, `bool`, `string`, `Vector2`, `Vector3`, `Vector2Int`, `Vector3Int`, `FastVector2Int`, `FastVector3Int`); consumers that store a **custom value-type** element in a `Deque<T>` or `CyclicBuffer<T>` must register that `T` for AOT themselves (reference-type elements share a single generic specialization and need no registration).
- **Relational component attributes (`[SiblingComponent]` / `[ChildComponent]` / `[ParentComponent]`) crashing or resolving the wrong components on IL2CPP/AOT players**: the collection fast-path built a runtime generic accessor via `Expression.Compile()`, which IL2CPP cannot service (it threw at runtime). It now uses the AOT-safe non-generic `GetComponents` / `GetComponentsInChildren` / `GetComponentsInParent(Type, …)` overloads. Include-inactive filtering also wrongly treated every component as enabled on IL2CPP (a reflection delegate path that silently failed), so disabled components were not filtered out; the enabled state is now read AOT-safely. Behavior on mono / editor is unchanged.
- **`[WNotNull]` validation (`this.CheckForNulls()`) silently doing nothing in player builds**: the null check was compiled only into the editor, so it never threw `ArgumentNullException` for a null `[WNotNull]` field in a built player. It now runs in every build (AOT-safe) exactly as the API documents, so missing required references fail fast at runtime instead of being silently ignored.
- **`[ValidateAssignment]` validation (`this.ValidateAssignments()`) silently doing nothing in player builds**: like `CheckForNulls`, the warning pass was compiled only into the editor (while the sibling `AreAnyAssignmentsInvalid` already ran everywhere), so unassigned fields were never reported in a built player. It now runs in every build (AOT-safe), consistent with `AreAnyAssignmentsInvalid`.
- **`ScriptableObjectSingleton` assets not regenerating on Unity 6 after the asset file was deleted**: when a singleton's `.asset` was removed but its `.meta` remained, Unity 6000.3+ retains the path-to-GUID mapping, which `ScriptableObjectSingletonCreator` treated as "path already occupied" and skipped recreation. Creation decisions now key on the asset body file, so the singleton is recreated on every Unity version.
- **`AssetDatabase.CreateAsset` failing with "Parent directory must exist" / "Creating asset at path … failed"**: editor utilities that create assets — `ScriptableObjectSingletonCreator`, `ScriptableObjectSingletonMetadataUtility`, `AttributeMetadataCacheGenerator`, `PersistentDirectorySettings`, and the sprite/atlas/animation creator windows — now ensure the parent folder is registered with the `AssetDatabase` before creating the asset, including while an `AssetDatabase` batch is open (common under `-batchmode`). Previously, creating an asset in a not-yet-registered folder could fail or be skipped.
- **Unity 6 deprecation warnings from `Object.FindObjectsOfType` / `FindObjectOfType`**: runtime DI-integration code (Reflex / VContainer / Zenject) now routes object lookups through a version-gated shim that uses `FindObjectsByType` / `FindFirstObjectByType` on Unity 2022.2+ (including Unity 6) and the legacy API below it, eliminating the deprecation warnings while preserving behavior.
- **`MonoBehaviour.ExecuteFunctionAfterFrame` callback never firing in headless/batch mode**: the helper yielded `WaitForEndOfFrame`, which never resumes under `-batchmode -nographics` (no end-of-frame render signal), so the queued callback was silently dropped on headless players, dedicated servers, and CI. It now advances a single frame in batch mode (the headless-safe equivalent) and continues to use `WaitForEndOfFrame` in interactive/graphical sessions, so the callback runs in every environment.

## [3.3.0]

### Added

- **Failed Tests Exporter**: New editor utility that hooks into the Unity Test Runner to automatically capture test failures and export them to timestamped text files
  - Automatically records test name, failure message, and stack trace for each failed test
  - Configurable output directory with a visual folder picker — defaults to the project root if not set
  - Path validation on every use: falls back to the project root if the configured directory is missing, invalid, or outside the project
  - Menu items under `Tools > Wallstop Studios > Unity Helpers` to export or clear captured failures
  - Disabled by default — enable via `Project Settings > Wallstop Studios > Unity Helpers`

### Fixed

- **Editor asset import-loop pressure from unchanged metadata saves**: `ScriptableObjectSingletonMetadata` and `AttributeMetadataCache` now skip `SetDirty` / `SaveAssets` work when regenerated metadata is unchanged. This prevents initialize-on-load and explicit singleton ensure passes from repeatedly re-saving identical assets during Unity test runs or editor refreshes, reducing the chance of Unity's "infinite import loop" detector tripping on native runs.
- **`AssetPostprocessorDeferral` dedup regressing structurally-equal-but-distinct drains**: `AssetPostprocessorDeferral.Schedule` now dedups pending drains via `ReferenceEquals` instead of `List<Action>.Contains` (which invokes `Delegate.Equals`). The prior structural-equality behavior silently coalesced two distinct delegates that shared the same `Method`+`Target` -- common when a local helper returns a lambda that captures only outer-method variables -- so a self-rescheduling drain could be dropped entirely. Matches the documented intent in the [AssetPostprocessor safety skill](./.llm/skills/asset-postprocessor-safety.md) and is pinned by the new `ScheduleStructurallyEqualButDistinctDelegatesAreNotDeduplicated` regression test
- **WButton conflict warnings hidden in collapsed foldouts**: Fixed group conflict warnings (placement, priority, and draw order) not rendering when foldout groups are collapsed. Conflict warnings now render in the group header area regardless of foldout expansion state, and warning cache population is validated across collapsed, expanded, and always-open foldout behavior.
- **Spurious "SendMessage cannot be called..." warnings from asset processors**: Eliminated spurious "SendMessage cannot be called during Awake, CheckConsistency, or OnValidate" warnings from `DetectAssetChangeProcessor` and related editor processors (`LlmArtifactCleaner`, `SpriteLabelProcessor`) by deferring callback invocation out of Unity's asset-import phase via a shared `AssetPostprocessorDeferral` helper backed by `EditorApplication.delayCall`. The prior synchronous path called `AssetDatabase.LoadAllAssetsAtPath` / `GetComponentsInChildren` / user callbacks during the import phase, which triggered Unity's internal sprite/renderer lifecycle relays and produced per-import warning storms. A new opt-out setting (`Project Settings > Wallstop Studios > Unity Helpers > Detect Asset Changes`, option **Defer Post-process Callbacks**, default on) restores the old synchronous behavior for users who require it. ([#234](https://github.com/wallstop/unity-helpers/pull/234))
- **Pool auto-purge throttle dropping real purges**: Fixed `WallstopGenericPool<T>.PurgeInternalCore` letting the 1-second `MinAutoPurgeIntervalSeconds` throttle block `MaxPoolSize` enforcement. A Rent/Return that advanced `_lastAutoPurgeTime` on a healthy-pool scan would block a subsequent same-tick call that pushed the pool past `MaxPoolSize`, silently skipping the `CapacityExceeded` purge and dropping the matching `OnPurge` notification. The throttle now has two orthogonal rules: (1) when the pool is observably over `MaxPoolSize`, the throttle is bypassed entirely so a burst of returns cannot accumulate beyond capacity within a single clock tick; (2) otherwise the throttle still rate-limits scans (preserving the O(1) amortized fast path for healthy pools under contention). The multithreaded variant also guards the timestamp advance with CAS max-semantics so concurrent out-of-order writes cannot regress the throttle clock. Applies to both the multithreaded and SINGLE_THREADED pool variants
- **Pool performance regression from O(n) usage tracking**: Fixed `RollingHighWaterMark` (used by pool purge system) performing O(n) operations on every pool rent/return, causing 100x slowdowns under sustained load. Replaced `List<Sample>` with `CyclicBuffer<Sample>` for O(1) add/remove, added incremental running sum for O(1) average computation, and added a monotonic deque for O(1) amortized peak tracking. Previously, 100K rent/return cycles took 20+ seconds; now completes within 200ms budget
- **TexturePlatformOverrideEntryDrawer render-phase mutations**: Fixed `OnGUI` writing to `SerializedProperty` values every frame without `BeginChangeCheck`/`EndChangeCheck` guards. Direct assignments like `apply.boolValue = EditorGUI.ToggleLeft(...)` and `nameProp.stringValue = EditorGUI.TextField(...)` dirtied the `SerializedObject` on every repaint, corrupting undo history. Also removed a redundant render-phase writeback of the computed display label to the platform name property
- **TexturePlatformOverrideEntryDrawer GenericMenu undo and Custom handling**: Fixed `GenericMenu` callback not calling `Undo.RecordObjects` before mutation and not handling the "Custom" menu option. Selecting "Custom" from the dropdown now correctly sets the platform name to `string.Empty` (triggering custom mode), and all selections are undoable
- **SourceFolderEntryDrawer render-phase mutation**: Fixed `EnumFlagsField` for selection mode writing to `modeProp.intValue` every frame without a `BeginChangeCheck`/`EndChangeCheck` guard
- **AttributeMetadataCache generator path mismatch**: Fixed `AttributeMetadataCacheGenerator.GetOrCreateCache()` using hardcoded asset paths that did not match the `[ScriptableSingletonPath("Wallstop Studios/Unity Helpers")]` attribute on `AttributeMetadataCache`. The generator was creating and loading the cache asset from `Assets/Resources/Wallstop Studios/` instead of the correct `Assets/Resources/Wallstop Studios/Unity Helpers/` path, causing cache generation to silently fail when the singleton was already loaded at the correct path
- **IntDropDown invalid value handling**: Fixed `IntDropDownDrawer` to properly handle property values that fall outside the configured options. Invalid values (values not in the options array) are now preserved without modification during render and clearly displayed with an "(Invalid)" suffix. Previously, invalid values were displayed as-is without any visual indication. The UI Toolkit `IntDropDownSelector.GetDefaultValue()` now returns the first option instead of 0
- **Linux dropdown rendering phantom rows**: Replaced all `EditorGUI.Popup` usage with `GenericMenu`-based dropdowns to eliminate phantom empty rows when selected index is -1 on Linux. Affected drawers: `WValueDropDownDrawer`, `IntDropDownDrawer`, `StringInListDrawer`, `TexturePlatformOverrideEntryDrawer` (standard variants), and `WValueDropDownOdinDrawer`, `IntDropDownOdinDrawer`, `StringInListOdinDrawer` (Odin Inspector variants). Odin drawers now always use `GenericMenu` regardless of the page limit setting, since `GenericMenu` handles all list sizes correctly without the rendering issues that required the threshold ([#209](https://github.com/wallstop/unity-helpers/pull/209))
- **Multi-object editing for WValueDropDown**: Added typed `SerializedProperty` setters for Unity-native types (`Vector2`, `Vector3`, `Vector4`, `Color`, `Rect`, `Bounds`, `Quaternion`, `AnimationCurve`, `Hash128`, and their `Int` variants) in `WValueDropDownDrawer.ApplyOption` to avoid the reflection fallback for known property types. The generic reflection path now iterates over all `serializedObject.targetObjects` for proper multi-object editing support instead of only updating the first selected object
- **SerializableSet undo not working for add, clear, sort, and commit operations**: Fixed `TryClearSet`, `TryAddNewElement`, `TryCommitPendingEntry`, `AppendNullPlaceholderEntry`, and `TrySortElements` in `SerializableSetPropertyDrawer` not calling `Undo.FlushUndoRecordObjects()` after direct object mutation. These methods used `Undo.RecordObjects` to snapshot pre-change state but never finalized the undo record, causing `Undo.PerformUndo()` to silently do nothing
- **GUIContent GC pressure in drawer OnGUI**: Fixed per-frame `GUIContent` allocations in `IntDropDownDrawer.DrawGenericMenuDropDown` and `PoolTypeConfigurationDrawer.OnGUI` that created avoidable garbage collection pressure in the Inspector. Both drawers now reuse static `GUIContent` instances (consistent with `WValueDropDownDrawer` and `StringInListDrawer` which already followed this pattern)

## [3.2.1]

### Fixed

- **WValueDropDown empty rows in dropdown**: Fixed `WValueDropDown` and `StringInList` dropdowns showing empty/blank rows at the top of the dropdown list, particularly on Linux. Clamped invalid `-1` selected indices to `0` in `WValueDropDownDrawer` and `StringInListDrawer`, and hardened dropdown display logic to replace empty labels with descriptive fallback text ([#209](https://github.com/wallstop/unity-helpers/pull/209))
- **Dropdown display label normalization**: Fixed search, filter, suggestion, and selected-value display in `WValueDropDown`, `StringInList`, and popup dropdown windows not applying the `(Option N)` fallback label consistently, causing items with empty display labels to be unsearchable and the wrong option to appear selected ([#213](https://github.com/wallstop/unity-helpers/pull/213))

## [3.1.9]

### Fixes

- "Destroying assets is not permitted to avoid data loss" on Asset Domain Reload issue

## [3.1.8]

### Fixed

- **Unity 6.3 unsigned package warning**: Added `"signature": "unsigned"` field to package.json to explicitly mark the package as unsigned for Unity 6.3+. This prevents Unity from showing a warning that the package is missing a signature. The change is backwards compatible with older Unity versions and works with OpenUPM, npm, and git URL installations.
- **WGroup not working in Unity 6000.x**: Fixed WGroup attributes not rendering in Unity 6 by using named parameter syntax for `CustomEditor` attribute's `editorForChildClasses` parameter. This change is backward compatible with Unity 2022 and earlier versions.

## [3.1.7]

- **DetectAssetChanged scene file crash**: Fixed Unity crash ("Do not use ReadObjectThreaded on scene objects!") when `.unity` or `.scenetemplate` files were processed by the asset change detection system

## [3.1.6]

### Fixed

- **Banner SVG Issues**: Various issues relating to Unity Helpers banner SVG rendering
- **Documentation nested list rendering**: Fixed GitHub Pages rendering where nested bullet lists appeared flat without proper indentation ([#175](https://github.com/wallstop/unity-helpers/pull/175))
- **Documentation too self-congratulatory**: Toned down the documentation to be more realistic and less LLM-speak

## [3.1.5]

### Changed

- **Breaking:** Relational component attributes (`[SiblingComponent]`, `[ParentComponent]`, `[ChildComponent]`) now assign `null` to single-component fields when no matching component is found and `SkipIfAssigned=false` (the default). Previously, fields retained their existing values when no component was found. This change makes single-field behavior consistent with collection-field behavior, which already assigned empty collections.

## [3.1.4]

### Added

- **ScriptableObjectSingletonMetadata Sync Button**: Added a `Sync` button to `ScriptableObjectSingletonMetadata` inspector that re-scans all assemblies for `ScriptableObjectSingleton<T>` types and updates their metadata entries. This allows manually refreshing singleton metadata when assets are added, moved, or renamed.

### Fixed

- **ScriptableObjectSingletonCreator race condition**: Fixed issue where newly created singleton assets were immediately deleted because `LoadAssetAtPath` returned null before Unity's AssetDatabase had indexed the file. The fix adds a synchronous import after `CreateAsset` and avoids deleting on-disk files when the file exists but isn't visible to the AssetDatabase yet.

## [3.1.3]

### Added

- **AnimationCreator Configuration Persistence**: Save and load AnimationCreator settings to JSON files alongside sprite source folders.
  - Configurations are automatically saved as `.animation-creator.json` in sprite source directories
  - Auto-loads existing configurations when source folders are selected
  - Save individual or all configurations with dedicated UI buttons
  - Reset to defaults with optional config file deletion
  - Preserves all settings including animation data, framerate curves, regex patterns, and grouping options
- **AnimationCreator Pagination**: Animation data list now uses pagination (20 items per page) for better performance with large animation sets

### Fixed

- **AnimationCreator editor performance**: Significantly improved scrolling and editing responsiveness when working with animation data.
- **ScriptableObjectSingletonCreator retry exhaustion**: Fixed issue where new singleton assets would fail to create with "Maximum automatic retry attempts reached" even when specifying paths.
- **Animation Copier diff detection**: Fixed issue where copied animations were incorrectly detected as "changed" instead of "unchanged" after copy operations.

## [3.1.2]

### Fixed

- Updated npmignore to align more closely with gitignore. The `scripts/tests` meta file error when sourcing from npm should be gone.

## [3.1.1]

### Fixed

- WInLineEditorOdinDrawer now compiles.

## [3.1.0]

### Added

- **Pool Access Frequency Tracking**: Intelligent purge decisions based on pool usage patterns
- **Memory Pressure Detection**: Proactive memory monitoring for intelligent pool purging
- **Cross-Pool Global Memory Budget**: Prevents aggregate memory bloat across all pools
- **Size-Aware Purge Policies**: Large objects (above LOH threshold) get stricter purge policies
  - `WallstopGenericPool<T>` automatically uses size-aware options during construction
- **SpriteSheetExtractor**: New editor tool for extracting individual sprites from sprite sheet textures.
  - This is an ALPHA feature, much functionality is currently broken.
- **Cache Data Structure**: New high-performance, configurable `Cache<TKey, TValue>` with fluent builder API
  - Multiple eviction policies: LRU, Segmented LRU (SLRU), LFU, FIFO, and Random
  - Time-based expiration with `ExpireAfterWrite` and `ExpireAfterAccess`
  - Weight-based sizing for entries of varying cost
  - Dynamic growth with configurable thrash detection
  - Loading cache support with `GetOrAdd` and custom loader functions
  - Thread-safe by default (single-threaded mode via `SINGLE_THREADED` define)
  - Eviction, get, and set callbacks for monitoring cache behavior
  - Statistics tracking with hit/miss counts
- **CachePresets**: Factory methods for creating pre-configured caches optimized for common gamedev scenarios
- **AnimationCreator Variable Framerate**: AnimationCreatorWindow now supports variable framerate animations using AnimationCurve
  - New `FramerateMode` enum (`Constant` or `Curve`) for choosing timing mode
  - Per-animation `framesPerSecondCurve` allows custom timing across animation progress
  - Curve presets: Flat, Ease In, Ease Out, and Sync with constant FPS
  - Frame timing preview shows per-frame durations before generation
- **AnimationCreator Live Preview**: Real-time animation preview panel
  - Play/pause/stop transport controls for preview playback
  - Frame scrubber for manual frame navigation
  - Respects variable framerate curves during preview
  - Shows current frame index and FPS in preview panel
- **AnimationData Cycle Offset**: New `cycleOffset` property (0-1) sets animation loop start point
- **Pool Auto-Purging**: `WallstopGenericPool<T>` now supports configurable auto-purging and eviction
  - New `PoolOptions<T>` class for configuring pool behavior at construction
  - `MaxPoolSize` limits pool capacity with automatic eviction of excess items
  - `IdleTimeoutSeconds` purges items that have been idle too long
  - `PurgeTrigger` flags control when purging occurs: `OnRent`, `OnReturn`, `Periodic`, or `Explicit`
  - `OnPurge` callback with `PurgeReason` (IdleTimeout, CapacityExceeded, Explicit) for monitoring
  - Intelligent purging mode tracks usage patterns to avoid purge-allocate cycles
  - `MinRetainCount` ensures a minimum number of items are always kept
- **Application Lifecycle Hooks for Pool Purging**: Automatic pool purging in response to system events
  - `Application.lowMemory` triggers emergency purge (ignores hysteresis, purges to `MinRetainCount`)
  - `Application.focusChanged` triggers purge when app backgrounds (mobile platforms)
  - New `PurgeReason` values: `MemoryPressure`, `AppBackgrounded`, `SceneUnloaded` (reserved)
  - Configurable via `PoolPurgeSettings.PurgeOnLowMemory` and `PoolPurgeSettings.PurgeOnAppBackground`
  - `GlobalPoolRegistry` tracks all pool instances for cross-pool operations
  - `PoolPurgeSettings.PurgeAllPools()` method for manual global purge
  - Lifecycle hooks automatically registered via `RuntimeInitializeOnLoadMethod`
- **RandomExtensions `NextOfExcept`**: New extension methods for selecting random elements with exclusions
  - `NextOfExcept(values)` - no exclusions (convenience overload)
  - `NextOfExcept(values, exception1...)` - exclude values
  - Zero-allocation using pooled collections internally

### Changed

- **BREAKING:** Pool purging now enabled by default with conservative settings
  - `GlobalEnabled` defaults to `true` (was `false`)
  - `DefaultBufferMultiplier` defaults to `2.0` (was `1.5`)
  - `DefaultHysteresisSeconds` defaults to `120` (was `60`)
  - `DefaultSpikeThresholdMultiplier` defaults to `2.5` (was `2.0`)
  - Use `PoolPurgeSettings.DisableGlobally()` to restore previous behavior
  - `UnityMainThreadDispatcher` auto-load behavior has changed from auto-loading to not auto-loading.
  - `UnityMainThreadDispatcher` hide flags have been changed to `None`.

- **DictionaryExtensions `ToDictionary`**: Now uses last-wins semantics for duplicate keys instead of throwing `ArgumentException`
  - Aligns with common dictionary initialization patterns
  - Applies to both `KeyValuePair<K,V>` and tuple `(K, V)` overloads

- **IEnumerableExtensions return types**: `OrderBy`, `Ordered`, and `Shuffled` methods now return `List<T>` instead of `IEnumerable<T>` for improved usability (indexable, known count)
  - **Note**: These methods now use eager evaluation (execute immediately) instead of deferred evaluation
  - Source code remains compatible—`List<T>` is assignable to `IEnumerable<T>`

### Improved

- **LRU cache eviction**: Bounded editor caches now use LRU (Least Recently Used) eviction instead of FIFO
  - Frequently-accessed cache entries are retained longer, improving cache hit rates
  - Both reads and writes update an item's "recency", preventing hot items from being evicted
  - Affects `EditorCacheHelper.AddToBoundedCache` and new `TryGetFromBoundedLRUCache` method
  - Applied to `InLineEditorShared`, `WShowIfPropertyDrawer`, and other bounded editor caches
- **Shuffled performance**: `IEnumerableExtensions.Shuffled` now uses O(n) Fisher-Yates shuffle instead of O(n log n) sort-based approach
- **LINQ elimination**: Removed LINQ usage across runtime code for reduced allocations and improved performance
  - Affects `Trie`, `Geometry`, `Serializer`, `ValidateAssignmentAttribute`, `WShowIfAttribute`, relational component attributes, and more
  - Uses pooled collections and explicit loops instead of LINQ methods
  - Zero-allocation patterns applied throughout
- **GlobalPoolRegistry.EnforceBudget() zero-allocation**: Replaced per-call `List<IPoolStatistics>` allocation with static reusable list protected by existing lock

### Fixed

- **Cache pre-allocation OutOfMemoryException**: Fixed production bug where `Cache<TKey, TValue>` would pre-allocate internal storage to `MaximumSize` instead of using a small initial capacity
  - Creating a cache with `MaximumSize = int.MaxValue` now works correctly instead of throwing `OutOfMemoryException`
  - New `InitialCapacity` option allows explicit control over starting allocation size (default 16)
  - Cache grows dynamically from `InitialCapacity` toward `MaximumSize` as items are added
  - `CacheBuilder<TKey, TValue>.InitialCapacity(int)` method for fluent configuration
  - `Cache<TKey, TValue>.MaximumSize` property added to expose configured maximum (distinct from `Capacity`)
  - Large `InitialCapacity` values are clamped to `MaxReasonableInitialCapacity` (65536) to prevent excessive allocations
- **Pool MinRetainCount not respected during gradual explicit purges**: Fixed `MinRetainCount` being ignored when using `MaxPurgesPerOperation` with explicit purges
  - Gradual purges now correctly stop when pool size reaches `MinRetainCount`
  - Added `_pool.Count > effectiveMinRetain` check to the purge loop condition in both thread-safe and non-thread-safe pool implementations
- **Pool idle timeout purges blocked by comfortable size**: Fixed idle timeout purges not occurring when pool size was at or below comfortable size
  - Idle timeout purges now proceed regardless of comfortable size, as they represent essential pool hygiene
  - Added `hasIdleTimeout` to loop entry condition to allow idle timeout evaluation independent of size
- **Pool hysteresis incorrectly blocking idle timeout purges**: Fixed hysteresis protection blocking all purge types including idle timeout
  - Idle timeout purges now proceed during hysteresis since they only remove items unused for extended periods
  - Capacity and explicit purges remain blocked during hysteresis to prevent thrashing
- **ScriptableObjectSingletonCreator race condition creating numbered duplicate folders**: Fixed race condition where parallel operations could cause Unity to create numbered duplicate folders like "Resources 1", "Resources 2", etc.
  - Added detection for Unity's numbered duplicate folder creation pattern
  - Automatically deletes duplicate folders and uses the intended folder path
  - Logs warning if duplicate folder deletion fails, alerting user to manual cleanup needed

## [3.0.5]

### Added

- **GitHub Pages Support**: All documentation is now available via a pretty [GitHub Pages](https://ambiguous-interactive.github.io/unity-helpers/)
- **GitHub Wiki Support**: All documentation is now available via a less pretty [GitHub Wiki](https://github.com/wallstop/unity-helpers/wiki)
- **Comprehensive Odin Inspector Attribute Support**: All Unity Helpers inspector attributes now work seamlessly with Odin Inspector's `SerializedMonoBehaviour` and `SerializedScriptableObject` types
  - **`[WButton]`**: Full support including grouping, placement, history, async methods, and parameters
  - **`[WShowIf]`**: Conditional property display based on field values, methods, or comparisons
  - **`[WReadOnly]`**: Disables editing while preserving display in Odin inspectors
  - **`[WEnumToggleButtons]`**: Toggle button UI for enum selection with flags support
  - **`[WValueDropDown]`**: Dropdown selection from custom value lists
  - **`[WInLineEditor]`**: Inline editing of referenced ScriptableObjects and components
  - **`[WNotNull]`**: Null reference validation with HelpBox warnings/errors
  - **`[ValidateAssignment]`**: Field validation for null, empty strings, and empty collections
  - **`[StringInList]`**: String selection from predefined lists or method providers
  - **`[IntDropDown]`**: Integer selection from predefined value lists
  - No setup required — attributes work identically whether Odin Inspector is installed or not
  - Custom Odin drawers registered when `ODIN_INSPECTOR` symbol is defined
- **WButton Custom Editor Integration**: New `WButtonEditorHelper` class for integrating WButton functionality into custom editors
  - Only needed when creating custom `OdinEditor` subclasses for specific types
  - Provides simple API for any custom editor to draw WButton methods
  - Methods: `DrawButtonsAtTop()`, `DrawButtonsAtBottom()`, `ProcessInvocations()`, and convenience methods
  - Documented integration patterns for both Odin Inspector and standard Unity custom editors

### Fixed

- **Sprite Sheet Auto-Detection Preferring Non-Transparent Boundaries**: Fixed an issue where the "Auto Best" algorithm and other detection methods could select grid boundaries that pass through non-transparent pixels when transparent alternatives existed
  - Changed scoring system from linear to non-linear, heavily favoring fully transparent grid lines (10x higher score) over partially transparent ones
  - Adjusted boundary comparison to only prefer alternatives when transparency score differs by more than 5%, preventing minor variations from overriding better transparent boundaries
  - When scores are similar, the algorithm now prefers divisors closer to the originally detected cell size
  - This fix affects `ScoreDivisorByTransparency`, `ScoreCellSizeForDimension`, and `FindBestTransparencyAlignedDivisor` methods
- **Manual Recompile Silent Failure After Build**: Fixed an issue where the "Request Script Recompilation" menu item and shortcut would stop responding after building a project (particularly on Linux)
  - Added defensive null check in compilation pending evaluator to prevent silent `NullReferenceException`
  - The null evaluator scenario could occur when static field initialization failed or was corrupted during build operations without a domain reload

## [3.0.4]

### Fixed

- Documentation only (`WGroupEnd` examples)

## [3.0.3]

### Fixed

- Fix packaging issue related to rsp files

## [3.0.2]

### Fixed

- Fix packaging issue related to Styles/Elements/Progress.meta file

## [3.0.1]

### Fixed

- Updated `package.json` to be OpenUPM-compatible

## [3.0.0]

### Added

- **llms.txt**: Added `llms.txt` file following the [llmstxt.org](https://llmstxt.org/) specification for LLM-friendly documentation
  - Provides a structured overview of package features, APIs, and documentation links for AI assistants
  - Enables third-party LLMs to quickly understand and work with the Unity Helpers codebase
- **Auto-Load Singleton System**: New singleton pattern with configurable lifetimes and thread-safe execution
  - `UnityMainThreadGuard` for ensuring operations run on the main thread
  - `UnityMainThreadDispatcher` with configurable lifecycle management
  - `AutoLoadSingletonAttribute` for automatic singleton instantiation during Unity start-up phases
  - Reworked the autoload singleton architecture for better scene persistence
- **Asset Change Detection**: Monitor asset changes with `DetectAssetChangedAttribute`
  - Annotate methods to automatically execute when specific asset types are created or deleted
  - Support for inheritance with `IncludeAssignableTypes` option
  - Automatic registration and callback execution via asset processor
- **Inspector Attributes & Drawers**: Comprehensive custom inspector tooling
  - `WGroup` attribute for visual grouping of inspector properties, including collapsible sections and palette-driven styling
  - `WButton` attribute with support for async/Task methods and custom styling
  - `WEnumToggleButtons` attribute for toggle-based enum selection in inspector
  - `WShowIf` conditional display attribute improvements
  - Enhanced dropdown attributes for better property selection
- **Inspector Validation Attributes**: Enhanced inspector feedback for null/invalid field detection
  - `WNotNullAttribute` now displays a warning or error HelpBox in the inspector when the field is null
  - `WNotNullAttribute` new properties: `MessageType` (Warning/Error enum) and `CustomMessage` (string) for customizable feedback
  - `WNotNullAttribute` new constructor overloads for easy customization of message type and custom messages
  - New `WNotNullPropertyDrawer` for rendering validation feedback in the inspector
  - `ValidateAssignmentAttribute` now displays a warning or error HelpBox in the inspector when the field is invalid (null, empty string, or empty collection)
  - `ValidateAssignmentAttribute` new properties: `MessageType` (Warning/Error enum) and `CustomMessage` (string) for customizable feedback
  - `ValidateAssignmentAttribute` new constructor overloads for easy customization of message type and custom messages
  - New `ValidateAssignmentPropertyDrawer` for rendering validation feedback in the inspector
  - Both attributes maintain full backward compatibility—existing code works unchanged with default warning messages
  - `StringInListAttribute` now supports `[StringInList(nameof(Method))]` to call parameterless instance or static methods on the decorated object, and the drawer exposes the same experience in both IMGUI and UI Toolkit inspectors
  - `WButton` now supports `groupPriority` and `groupPlacement` parameters for fine-grained control over button group ordering and positioning
- **Serialization Data Structures**: Production-ready serializable collections
  - `SerializableDictionary<TKey, TValue>` with custom inspector drawer
  - `SerializableSortedDictionary<TKey, TValue>` with ordered iteration
  - `SerializableHashSet<T>` with custom set drawer and duplicate detection
  - `SerializableSortedSet<T>` for sorted sets with `IComparable<T>` elements
  - `SerializableNullable<T>` for nullable value types in inspector
  - `SerializableType` for type references in inspector
  - Pagination support for large collections in the Editor
  - Inline nested editor support for complex types
  - Undo/Redo support for all serializable collection modifications
  - Confirmation dialog when clearing collections to prevent accidental data loss
- **Editor Tooling Enhancements**:
  - Enhanced `StringInListDrawer` for validated string input with suggestions
  - UI Toolkit-based editors for modern Unity editor integration
  - Configurable settings windows with improved layout and styling
  - Move up/down buttons for reordering collection elements
  - Add/remove buttons with improved visual styling
  - Added **Request Script Recompilation** menu item (`Tools ▸ Wallstop Studios ▸ Unity Helpers`) to manually trigger script recompilation
  - The "Request Script Compilation" utility includes a Unity Shortcut Manager binding (default **Ctrl/Cmd + Alt + R**) for quick access. The shortcut appears under _Wallstop Studios / Request Script Compilation_ and can be remapped like any other Unity shortcut.
  - Coroutine wait buffer defaults can now be configured under **Project Settings ▸ Wallstop Studios ▸ Unity Helpers**. The generated `Resources/WallstopStudios/UnityHelpers/UnityHelpersBufferSettings.asset` applies the selected quantization, entry caps, and LRU mode automatically on domain reload or when the player starts (unless your code overrides the values at runtime).
  - Added **Unity Method Analyzer** (`Tools ▸ Wallstop Studios ▸ Unity Helpers ▸ Unity Method Analyzer`) for detecting inheritance issues and Unity lifecycle method errors across C# codebases
- **Random Number Generation**: Extended PRNG capabilities
  - Added `BlastCircuitRandom` and `WaveSplatRandom` generators with improved performance characteristics
  - New `RandomGeneratorMetadata` system for inspecting generator properties
  - Extended random sampling methods with improved statistical distribution
- **Array Pooling**: New `SystemArrayPool<T>` and unified `PooledArray<T>` return type
  - Added `SystemArrayPool<T>` wrapping `System.Buffers.ArrayPool<T>.Shared` for variable-sized allocations
  - Added `PooledArray<T>` struct as unified return type for all array pools with proper `Length` tracking
  - `WallstopArrayPool<T>` and `WallstopFastArrayPool<T>` now return `PooledArray<T>` instead of `PooledResource<T[]>`
  - Critical for `SystemArrayPool<T>`: returned arrays may be larger than requested; always use `pooled.Length`, not `array.Length`
- **Grid Concave Hull Reliability**:
  - Edge-split and grid KNN hull builders now insert missing axis-aligned corners after the initial pass, guaranteeing concave stair, horseshoe, and serpentine inputs retain their interior vertices even when only sparse samples exist.
  - Improved handling of staircase patterns, axis-corner preservation, and diagonal-only rejection for more robust hull generation.

### Fixed

- **Random Number Generation**: Critical edge case handling
  - Fixed poor handling of `NextFloat()` and `NextDouble()` potentially returning exactly `0.0` or `1.0` in extensions and helpers
  - Fixed sampling bias in `NextUlong()` for more uniform distribution
  - Ensured proper range handling for all random generation methods
- **IllusionFlow Random**: Serialization and performance issues
  - Fixed deserialization bugs in `IllusionFlow` components
  - Optimized to reduce GC churn during effect processing
- **Editor & Inspector**: Multiple rendering and caching bugs
  - Fixed stale label caching causing incorrect inspector display
  - Fixed scene loading edge cases in editor workflows
- **Component System**: Runtime component query issues
  - Fixed `GetComponents` returning null arrays in some cases
  - Fixed jitter-related bugs in component updates
- **Extension Methods**: Mathematical edge cases
  - Fixed calculations with zero or negative areas (bounds, rectangles, circles)
  - Fixed color averaging bugs in color extension methods
- **Geometry & Spatial**: Convex hull computation
  - Fixed convex hull behavior for edge cases (collinear points, degenerate cases)
  - Improved hull computation accuracy and performance
- **GUID Generation**: Specification compliance
  - Fixed GUID v4 generation to properly set version and variant bits per RFC 4122
- **Editor Settings**: Project settings and drawer issues
  - Fixed obsolete API usage in editor code
  - Fixed project settings panel rendering issues
  - Fixed reflection-based property access for better performance
- **Scriptable Object Singletons**: Duplicate folders should no longer be created
  - Fixed a "should-never-happen" bug where, if a singleton was accessed for the first time off the main thread, it would never be able to be accessed for the lifetime of the process
  - Fixed a bug where auto-creation would happen concurrently with AssetDatabase importing, resulting in Unity crashing with no error message

### Improved

- **Performance Optimizations**:
  - Reduced reflection usage in custom property drawers (10-100x faster in some cases)
  - Optimized list navigation and caching for large collections
  - Faster indexing and lookup in serializable data structures
  - Improved drawer update performance for complex inspector hierarchies
  - Data structure conversion optimizations
  - Minor relational component performance improvements, specifically for children components
  - Reduced GC allocations across property drawers, editor tools, and various helpers
- **EnhancedImage Visual Component**:
  - Improved material instance management with proper cleanup OnDestroy
  - Better domain reload handling for HDR color and material state persistence
  - Enhanced editor inspector with automatic material fix suggestions
- **Animation Editor Tools**:
  - Fixed FPS field handling in Animation Viewer and Sprite Sheet Animation Creator
  - Improved frame reordering and preview responsiveness
- **Documentation**:
  - Major documentation refactor for clarity
  - Added GUID generation documentation
  - Improved inline code documentation
  - Better attribute usage examples

### Changed

- **Breaking Changes**:
  - Removed `KVector2` (deprecated, use Unity's built-in Vector2)
  - Renamed `KGuid` -> `WGuid`, changed data layout
  - Forced `WallstopFastArrayPool` to force `unmanaged` types. This pool does not clear arrays and can leak references.
  - `WallstopArrayPool<T>` and `WallstopFastArrayPool<T>` now return `PooledArray<T>` instead of `PooledResource<T[]>`. Update usages from `pooled.resource` to `pooled.Array` and consider using `pooled.Length` for iteration bounds.
  - The legacy line-division concave hull overload `BuildConcaveHull(IEnumerable<FastVector3Int>, Grid, float scaleFactor, float concavity)` has been marked `[Obsolete]` and now throws `NotSupportedException`. Use `ConcaveHullStrategy.Knn` or `ConcaveHullStrategy.EdgeSplit` (and their dedicated helpers) instead; the docs now call out this retirement explicitly.
  - `StringInList` inspectors now keep the property row single-line and open a dedicated popup that contains search, pagination, and keyboard navigation for large catalogs (applies to both IMGUI and UI Toolkit drawers, including `SerializableType`).
- **API Improvements**:
  - Simplified `TryAdd` methods for collections
  - Enforced `IComparable` constraint where appropriate for sorting
  - Better handling of null additions in collections
  - Updated editor tooling for better integration with Unity 2021.3+
  - Default IList.Sort to Grail sort for stability and improved performance
- **Documentation**:
  - Updated documentation to reflect new features and API changes
  - Re-organized documentation into a more logical structure
  - Consolidated documentation naming around kebab-case

---

## [2.0.0]

- Deprecate BinaryFormatter with `[Obsolete]`, keep functional for trusted/legacy scenarios.
- Make GameObject JSON converter output structured JSON with `name`, `type`, and `instanceId`.
- Fix stray `UnityEditor` imports in Runtime to ensure clean player builds.

---

## [1.x]

- See commit history for incremental features (random engines, spatial trees, serialization converters, editor tools).
