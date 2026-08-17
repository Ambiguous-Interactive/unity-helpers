# Changelog

<!-- cspell:ignore Prd -->

All notable changes to Unity Helpers will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Add `SortAlgorithm.Yam` and `IList<T>.YamSort()`, a stable sort that approaches O(n) on sequential and reverse-sequential data. Adapted from [YamSort by Gary Gende](https://github.com/gendeg/YamSort). See [IList Sorting Performance](./docs/performance/ilist-sorting-performance.md) ([#461](https://github.com/Ambiguous-Interactive/unity-helpers/issues/461)).

- Add `[SingletonCreation(SingletonCreationPolicy.NeverCreate)]`, which stops `RuntimeSingleton<T>.Instance` creating a bare stand-in when no instance exists. It returns `null` and names the type instead. See [Controlling on-demand creation](./docs/features/utilities/singletons.md#controlling-on-demand-creation) ([#321](https://github.com/Ambiguous-Interactive/unity-helpers/issues/321)).
- Add `AnimationClip.GetSpriteFramesFromClip()`, which pairs every sprite a clip references with the binding that supplies it, a `GetSpritesFromClip(path, propertyName, type)` overload that filters on that binding, and `UnityExtensions.SpriteBindingProperty`. Editor-only. See [Sprites from an AnimationClip](./docs/features/utilities/math-and-extensions.md#sprites-from-an-animationclip) ([#451](https://github.com/Ambiguous-Interactive/unity-helpers/issues/451)).
- Add `AssetChangeDetectionUtility.Enabled`, `ResetEnabledToDefault()` and `EnabledScope(bool)` to turn the `[DetectAssetChanged]` watcher on and off. The returned `AssetChangeDetectionEnabledScope` restores the previous setting on dispose ([#327](https://github.com/Ambiguous-Interactive/unity-helpers/issues/327)).
- Add `SingleThreadedThreadPool.DrainAsync()`, which closes the pool and waits for queued work to finish instead of dropping it, plus `IsAcceptingWork` ([#318](https://github.com/Ambiguous-Interactive/unity-helpers/issues/318)).
- Add `SerializableList<T>`, a list that survives Unity serialization inside another serialized collection ([#314](https://github.com/Ambiguous-Interactive/unity-helpers/issues/314)).
- Add the Inspector drawer for `SerializableDictionary<TKey, TValue, TValueCache>`, which previously had none ([#314](https://github.com/Ambiguous-Interactive/unity-helpers/issues/314)).
- Add `WGuid.TryCreate(Guid, out WGuid)`, which wraps an existing GUID without throwing when it is not version 4 ([#437](https://github.com/Ambiguous-Interactive/unity-helpers/issues/437)).
- Add `Enum.TryConvertToUInt64()` and `TryConvertToInt64()`, which convert any enum value to its 64-bit bit pattern without overflowing on negative or very large members.
- Add `ColorQuantization`, the one place an 8-bit color channel becomes a normalized float and back: `ToNormalized` decodes, `ToByte` encodes to the nearest channel, and `ToThresholdByte` gives the inclusive bound for comparing stored channels against a float cutoff. See [8-bit Channels and Normalized Floats](./docs/features/utilities/math-and-extensions.md#8-bit-channels-and-normalized-floats) ([#466](https://github.com/Ambiguous-Interactive/unity-helpers/issues/466)).
- Add `ColorQuantization.AreSameColor()`, the one definition of "the same color" in the package: two colors match when they encode to the same 8-bit channels, which is the only rule an equality comparer can hash ([#472](https://github.com/Ambiguous-Interactive/unity-helpers/issues/472)).
- Add `ColorContrast`, which answers whether text is readable on a background the way WCAG defines it: `RelativeLuminance`, `ContrastRatio` and `ReadableTextColor`. See [Readable Text on Any Background](./docs/features/utilities/math-and-extensions.md#readable-text-on-any-background) ([#471](https://github.com/Ambiguous-Interactive/unity-helpers/issues/471)).
- Add `TextureResampling`, the one place a resampler picks a source pixel and mixes colors of unequal opacity: `BilinearSourceCoordinate`, `NearestSourceIndex`, `Premultiply` and `Unpremultiply`. See [Resampling a Texture](./docs/features/utilities/math-and-extensions.md#resampling-a-texture) ([#470](https://github.com/Ambiguous-Interactive/unity-helpers/issues/470)).
- Add `SemaphoreSlim.Acquire()`, `TryAcquire()` and `AcquireAsync()`, so a permit can be taken with `using` instead of a `finally`. The lease releases its permit exactly once, however many copies of it exist. See [Semaphore Leases](./docs/features/utilities/helper-utilities.md#semaphore-leases) ([#358](https://github.com/Ambiguous-Interactive/unity-helpers/issues/358)).
- Add `DurableFile`, whose writes cannot leave a truncated file behind: `TryWriteAllText`, `TryAppendAllText`, `TryCopy`, `TryDelete` and async equivalents. See [Durable Writes for Player Data](./docs/features/utilities/helper-utilities.md#durable-writes-for-player-data) ([#319](https://github.com/Ambiguous-Interactive/unity-helpers/issues/319)).
- Add `WallstopProto` (preview), a reflection-free protobuf reader and writer that AOT-compiles under IL2CPP and is byte-compatible with protobuf-net. See [WallstopProto](./docs/features/serialization/serialization.md#wallstopproto-the-reflection-free-wire-layer-preview) ([#343](https://github.com/Ambiguous-Interactive/unity-helpers/issues/343)).
- Add the WallstopProto source generator (preview): annotate a type with `[WProtoContract]` and its formatter is generated into your assembly and registered for you. A contract it cannot serialize is a build error naming the fix. See [The generator](./docs/features/serialization/serialization.md#the-generator) ([#343](https://github.com/Ambiguous-Interactive/unity-helpers/issues/343)).
- Add `WProtoFormatterProvider`, which resolves a type's `IWProtoFormatter<T>` without reflection and names the type when nothing is registered. Formatters ship for `FastVector2Int`, `FastVector3Int`, `WGuid` and `RandomState`, registered as Unity starts; your own registration replaces one in use ([#343](https://github.com/Ambiguous-Interactive/unity-helpers/issues/343), [#379](https://github.com/Ambiguous-Interactive/unity-helpers/issues/379)).
- Add support for a `[WProtoMember]` that is another `[WProtoContract]`, including the same type again, so a tree or linked list serializes without a hand-written formatter. See [Contracts that hold other contracts](./docs/features/serialization/serialization.md#contracts-that-hold-other-contracts) ([#380](https://github.com/Ambiguous-Interactive/unity-helpers/issues/380)).
- Add WallstopProto support for arrays and any `ICollection<T>` with a public parameterless constructor and `Add`, including `struct` collections, which are not boxed on the write path. `OverwriteList = true` replaces the constructor's collection instead of appending. See [Collections](./docs/features/serialization/serialization.md#collections) ([#343](https://github.com/Ambiguous-Interactive/unity-helpers/issues/343)).
- Add WallstopProto support for `LinkedList<T>`, `Queue<T>`, `Stack<T>`, `ReadOnlyCollection<T>`, `ReadOnlyDictionary<K,V>` and members declared as the matching interfaces. A `Stack<T>` round-trips in its original order. See [Collections](./docs/features/serialization/serialization.md#collections) ([#395](https://github.com/Ambiguous-Interactive/unity-helpers/issues/395)).
- Add WallstopProto support for nested and jagged collections — `int[][]`, `List<List<int>>`, `Dictionary<string, List<int>>` and deeper — which protobuf-net refuses. See [Collections](./docs/features/serialization/serialization.md#collections) ([#399](https://github.com/Ambiguous-Interactive/unity-helpers/issues/399)).
- Add WallstopProto support for rectangular arrays — `int[,]`, `int[,,]`, and one anywhere a collection can go — which protobuf-net refuses. Dimensions travel with the elements, so `new int[0, 5]` keeps its shape. See [Collections](./docs/features/serialization/serialization.md#collections) ([#434](https://github.com/Ambiguous-Interactive/unity-helpers/issues/434)).
- Add WallstopProto maps: a `Dictionary<,>`, `SortedDictionary<,>` or your own `IDictionary<,>` is written as a protobuf map, byte-compatible with protobuf-net. See [Maps](./docs/features/serialization/serialization.md#maps) ([#387](https://github.com/Ambiguous-Interactive/unity-helpers/issues/387)).
- Add WallstopProto polymorphism: `[WProtoInclude(tag, typeof(Subtype))]` round-trips a member typed as a base as its concrete subtype, with no reflection. An unrecognized include tag is skipped, so saves from newer builds still load. See [Polymorphism](./docs/features/serialization/serialization.md#polymorphism) ([#390](https://github.com/Ambiguous-Interactive/unity-helpers/issues/390)).
- Add `[assembly: WProtoSurrogate(typeof(Real), typeof(Surrogate))]`, which gives a wire shape to a type you cannot annotate — Unity's `Vector3`, `Color`, `Bounds` and the rest. See [Surrogates](./docs/features/serialization/serialization.md#surrogates) ([#391](https://github.com/Ambiguous-Interactive/unity-helpers/issues/391)).
- Add `[assembly: WProtoRootMarshal(typeof(Real), typeof(Formatter))]`, which gives a type a different wire shape as the root of a serialization than as a member of a contract. See [Root marshals](./docs/features/serialization/serialization.md#root-marshals-the-collections-with-two-encodings) ([#402](https://github.com/Ambiguous-Interactive/unity-helpers/issues/402)).
- Add `[assembly: WProtoDeclaredRoot(typeof(IRandom), typeof(AbstractRandom))]`, which names the contract serving a value held as an interface. It applies at the root only. See [Declared roots](./docs/features/serialization/serialization.md#declared-roots-serving-an-interface) ([#403](https://github.com/Ambiguous-Interactive/unity-helpers/issues/403)).
- Add generic `[WProtoContract]` support: each closed construction gets its own encoding and is registered automatically, including a closure your assembly makes of a contract another assembly declares, so your own `Box<YourStruct>` needs no manual registration. See [Generic contracts](./docs/features/serialization/serialization.md#generic-contracts) ([#385](https://github.com/Ambiguous-Interactive/unity-helpers/issues/385)).
- Add WallstopProto support for `readonly` fields and get-only properties, previously a build error. The generator emits a private constructor into your `partial` type, so the type gains no public surface ([#394](https://github.com/Ambiguous-Interactive/unity-helpers/issues/394)).
- Add `WProtoContractAttribute.SkipConstructor`, which reads a contract without running any constructor its author wrote, matching protobuf-net's flag of the same name and still working under IL2CPP. A decoded collection member holds exactly the elements the payload carried ([#394](https://github.com/Ambiguous-Interactive/unity-helpers/issues/394)).
- Add `WProtoContractAttribute.IgnoreListHandling`, which writes a contract that also implements `ICollection<T>` as a message. Without it such a member is a build error naming both readings ([#343](https://github.com/Ambiguous-Interactive/unity-helpers/issues/343)).
- Add `IWProtoScalarFormatter<T>` and `WProtoGeneric<T>`, which make a value's wire type expressible so a generic contract can encode a member whose type it cannot see. `IWProtoFormatter<T>` is unchanged.
- Add `IWProtoPolymorphicFormatter`, which lets a formatter report the runtime types its `[WProtoInclude]` chain writes, so `Serializer` can serve a value held as its base ([#403](https://github.com/Ambiguous-Interactive/unity-helpers/issues/403)).
- Add `IWProtoConditionalFormatter`, so a formatter reports whether it can encode a closure before anything is written. An element WallstopProto cannot encode now falls back to protobuf-net instead of throwing mid-serialization ([#402](https://github.com/Ambiguous-Interactive/unity-helpers/issues/402), [#416](https://github.com/Ambiguous-Interactive/unity-helpers/issues/416)).
- Add `WProtoFacade.TryDeserializeAs()`, the read that names a concrete type. `Serializer.ProtoDeserialize<T>(byte[], Type)` routes through it ([#403](https://github.com/Ambiguous-Interactive/unity-helpers/issues/403)).
- Add `SerializationCapacityLimits`, the bound a deserializer applies to a capacity a payload claims, with `MaximumRestoredCapacity` (default 1,048,576) for games whose saves are legitimately larger. An empty rectangular array whose axis exceeds it does not deserialize; one carrying elements is unaffected ([#437](https://github.com/Ambiguous-Interactive/unity-helpers/issues/437)).
- Add `WProtoReader.CountPackedElements()`, `WProtoArrayBuilder<T>` and `WProtoRepeated.Reserve()`, so a hand-written formatter can size a repeated field once instead of growing it. Generated formatters already size theirs from the packed run ([#398](https://github.com/Ambiguous-Interactive/unity-helpers/issues/398)).
- Add `WProtoReader.TryReadPackedRun()`, which reads a packed repeated field's payload as its own reader without spending a nesting level.
- Add `WProtoReader.MaxNestingDepth` (64), the deepest message nesting a payload may use, so a few kilobytes cannot ask a formatter for thousands of stack frames. A hand-written formatter descends with `TryReadMessage(formatter, out value)` or `new WProtoReader(payload, in parent)`, which carry the depth ([#343](https://github.com/Ambiguous-Interactive/unity-helpers/issues/343), [#377](https://github.com/Ambiguous-Interactive/unity-helpers/issues/377)).
- Add `WProtoGeneric<T>.CanEncode`, which reports whether a closed type argument can be encoded at all.
- Add `WProtoRectangular`, the shape check and refusal message a hand-written rectangular-array formatter needs.
- Add `WProtoRepeated.NullElement()` and `NullNestedElement()`, which build the exceptions a generated formatter throws for a `null` repeated element or inner collection.
- Add `WProtoFormatterProvider.UnexpectedSubtype()`, which builds the exception thrown for a value whose runtime type its contract does not declare.
- Add `WPROTO018`, a build error when a `[WProtoContract]`'s base is one too but is not declared with `[WProtoInclude]` ([#394](https://github.com/Ambiguous-Interactive/unity-helpers/issues/394)).
- Add `WPROTO028`, a warning when a closed construction cannot be registered because it closes over a `private` nested type. It is skipped rather than failing the build ([#414](https://github.com/Ambiguous-Interactive/unity-helpers/issues/414)).
- Add `WPROTO030`, an informational diagnostic when a `[ProtoContract]` has no `[WProtoContract]`, so migration is an opt-in worklist ([#407](https://github.com/Ambiguous-Interactive/unity-helpers/issues/407)).
- Add `WPROTO031`, a warning when assemblies declare different roots for the same type ([#419](https://github.com/Ambiguous-Interactive/unity-helpers/issues/419)).
- Add `WPROTO032`, a build error when a member's collections nest more than 64 deep, which is deeper than the reader can read back ([#399](https://github.com/Ambiguous-Interactive/unity-helpers/issues/399)).

### Security

- Clamp or refuse a capacity a payload claims in `Deque`, `SparseSet`, `BitSet` and `ImmutableBitSet`. Six bytes claiming `int.MaxValue` previously allocated 8-16 GB and crashed the player. Raise `SerializationCapacityLimits.MaximumRestoredCapacity` if your saves exceed 1,048,576 elements ([#429](https://github.com/Ambiguous-Interactive/unity-helpers/pull/429)).

### Changed

- Enable WallstopProto by default for the runtime assembly: every `Serializer.ProtoSerialize` and `ProtoDeserialize` overload, including `ref buffer` and `forceRuntimeType: true`, uses generated formatters when available and falls back to protobuf-net ([#343](https://github.com/Ambiguous-Interactive/unity-helpers/issues/343), [#403](https://github.com/Ambiguous-Interactive/unity-helpers/issues/403)).
- Serialize thirty of this package's own contracts through WallstopProto when `WALLSTOP_PROTO` is defined, including `AbstractRandom` and all seventeen generators. Saved data is unchanged ([#394](https://github.com/Ambiguous-Interactive/unity-helpers/issues/394)).
- Serialize `SerializableHashSet`, `SerializableSortedSet`, `SerializableDictionary`, `SerializableSortedDictionary`, `Deque`, `CyclicBuffer` and `SparseSet` through WallstopProto when `WALLSTOP_PROTO` is defined. Saved data is unchanged ([#402](https://github.com/Ambiguous-Interactive/unity-helpers/issues/402)).
- Allow a `struct` backing set in `SerializableSetBase<T, TSet>`: the `where TSet : class` constraint is gone ([#388](https://github.com/Ambiguous-Interactive/unity-helpers/issues/388)).
- Report a read of the write-only `GameObject` and `Touch` JSON converters as `NotSupportedException` instead of `NotImplementedException` ([#437](https://github.com/Ambiguous-Interactive/unity-helpers/issues/437)).
- Ship the bundled `System.Text.Json` and friends only on editors that do not provide them; Unity supplies its own from 6000.5. See [Bundled Assembly Conflicts](./docs/guides/bundled-assembly-conflicts.md) ([#331](https://github.com/Ambiguous-Interactive/unity-helpers/issues/331)).

### Fixed

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
