# Changelog

<!-- cspell:ignore Prd -->

All notable changes to Unity Helpers will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- **Unity CLI install-path probes hanging under service accounts**: CI-managed editor provisioning now preserves an exact install-path confirmation captured before the watchdog stops a lingering Unity CLI process tree, so NetworkService runners can continue provisioning instead of discarding the confirmed path as a failed probe.
- **Unity repair installs timing out after materializing a usable editor**: atomic repair provisioning now continues to required-module verification when the Unity CLI exits nonzero or reaches its watchdog deadline after `Unity.exe` is resolvable, matching the base-install recovery path while still failing closed when any required CI module is absent.
- **Locked corrupt editors blocking native-startup recovery**: when a failed native startup probe cannot quarantine the existing editor because another process still holds its tree, provisioning now installs and verifies the editor in the alternate CI-managed root instead of leaving the runner permanently unable to self-repair.

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
