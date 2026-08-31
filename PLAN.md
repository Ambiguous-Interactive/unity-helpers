# Work Plan

Only in-progress and future work belongs here. Put completed work, measurements, and failed
hypotheses in `progress/session-*.md`; keep reusable guidance in `.llm/`; keep feature detail in its
issue, design, or documentation. Follow [Maintain Plan](./.llm/skills/maintain-plan.md).

## WallstopProto Closeout

**Status:** In progress; highest priority.

**Outcome:** Finish the IL2CPP-safe serializer, prove its release contract, and retire the runtime
protobuf-net fallback without breaking consumer contracts.

### Next tasks

1. Run release acceptance on the licensed IL2CPP matrix
   ([#602](https://github.com/Ambiguous-Interactive/unity-helpers/issues/602)): the enum-key fixture
   on a standalone player, both oracle suites (protobuf-net 2.4.9 and 3.2.56) beside the Unity
   editor fixtures, allocation and throughput gates preserved. Gated on the same runner capability
   as [#323](https://github.com/Ambiguous-Interactive/unity-helpers/issues/323).
1. Remove the runtime fallback and bundled protobuf-net assemblies only in a major release, after
   migration diagnostics cover shipped and representative consumer contracts.

A cross-assembly subtype **runtime registry** stays refused by `WPROTO040` (#603); emitting the
base's chain in the extending assembly is a different mechanism, open on
[#612](https://github.com/Ambiguous-Interactive/unity-helpers/issues/612).

**Complete when:** Supported and refused shapes are explicit (done for the base-class-library
sweep), both oracle suites and Unity/IL2CPP gates pass, consumer migration is documented, and no
runtime path requires protobuf-net. See the
[serialization guide](./docs/features/serialization/serialization.md).

## Local Gate Coverage

**Status:** In progress; one optional, unowned task remains.

**Outcome:** A new analyzer diagnostic is caught locally, in every tree the package ships. All four
check projects hold the package to `WUH010` (#653).

### Next tasks

1. Optional, unowned: a fifth check project compiling the fixture trees as a SEPARATE assembly
   against a prebuilt runtime, the only shape that reaches `WPROTO044` (#650 has the reasoning).
   Measured blocker: `TestCheck` calls itself `WallstopStudios.UnityHelpers`, so fixtures'
   `internal` access resolves by BEING the runtime; a split project must name itself an assembly
   `Runtime/AssemblyInfo.cs` grants `InternalsVisibleTo`.

**Complete when:** Reintroducing any locally reachable diagnostic into any of the four trees is
caught by a local command, proven by doing it.

## Documentation Sample Coverage

**Status:** In progress; the gate ships, the corpus is a third of what it could be.

**Outcome:** Every checkable documentation sample is checked, so an example naming a member that
moved fails a build rather than reading as correct forever.

### Next tasks

1. Raise the marked corpus above 103. Measured: 280 blocks are declaration-shaped, 103 stand alone;
   the rest are continuations, cheapest converted by making the sample self-contained.
2. Price the per-page preamble block. Measured: of 410 usage-shaped blocks 143 fail, because a
   snippet names locals the reader supplies; one shared declaration per page converts most.
3. Extend the gate to `Editor/**` samples once `EditorCheck`'s seven exclusions are understood.

**Complete when:** A sample naming a missing member cannot merge, the checked count is reported and
cannot silently fall, and the remainder is a stated decision. See `scripts/extract-doc-samples.js`.

## Correctness Sweep (#633)

**Status:** In progress; concrete safety defects are pinned, every measured optimization is blocked.

**Outcome:** The defects the [#633](https://github.com/Ambiguous-Interactive/unity-helpers/issues/633)
audit found are fixed and pinned, and the optimizations it proposes are chosen from data.

### Next tasks

1. Build the evidence gates
   ([#636](https://github.com/Ambiguous-Interactive/unity-helpers/issues/636)). Every measured half
   is blocked on them -- #637, #638, #640, #642, #643, #645, #646 -- and none may be chosen without
   paired player data.
2. Finish the remaining correctness surfaces in gameplay order: #644 relational-component and
   cache lifecycle beyond singletons; #647 serializer fuzzing beyond formatter completion; #648
   editor tooling at project scale beyond failure-atomic validation storage.
3. Finish #637 beyond the singleton identity slice: `Unsafe.As`/`Unsafe.AsRef` in enum conversion,
   WProto scalar formatting, reflection and singletons, plus input-sized `stackalloc` bounds.
4. Finish #645's measured algorithm work: exhaustive small-domain permutations, stability
   decoration, and interface-only `IList` backings. Fixed-size and invalid-range mutation safety is
   done.
5. The #635 comment audit, measured 2026-08-31 at **2,226** runs of two or more consecutive `//`
   lines (527 `Runtime/`, 434 `Editor/`, 1,265 `Tests/`) and enforced by nothing. Add the block-form
   linter first, dot-sourcing `scripts/comment-stripping.ps1`'s `Get-CommentRanges`, then sweep
   `Runtime/`, then `Editor/`, then decide explicitly about `Tests/`. Most sites want deletion, not
   conversion.

**Complete when:** Every child's definition of done is met, or its remainder is a recorded decision
rather than an omission.

## DxKit Rebrand

**Status:** Future; independently reviewable slices, after WallstopProto release work.

**Outcome:** Rebrand the display name and user-facing surfaces to DxKit without changing the package
id, root namespace, minimum Unity version, or version continuity.

### Next tasks

1. Normalize copy and facts in the five `design-system/DxKit *.dc.html` source canvases.
2. Land the brand foundation: vocabulary, canonical mark/icon, version-sync contract, package
   display name, README, docs copy, and canonical repository URLs.
3. Measure the RNG claim with the existing benchmark workflow; publish only a committed number.
4. Retheme MkDocs and build the landing page. Remove the orphaned Jekyll stack only after a
   consumer search proves nothing depends on it.
5. Build the shared UI Toolkit theme and Hub, then migrate windows, settings and drawers in small
   characterization-tested batches, keeping legacy menu aliases for one release. The Asset
   Validation window is the first consumer waiting on it
   ([#655](https://github.com/Ambiguous-Interactive/unity-helpers/issues/655), #634).
6. Produce the README/store assets and regenerate screenshots once the migrated UI ships, then add
   copy/link guardrails and run the documentation, packaging, Unity and visual checks per slice.

**Complete when:** Shipped surfaces use DxKit consistently, immutable identifiers are unchanged, all
numeric claims trace to committed measurements, strict gates pass, and migrated UI has behavior and
visual evidence. Sources in `design-system/`.

## Auto-Loading Cache

**Status:** Future; lower priority.

**Outcome:** Synchronous and asynchronous loading caches with per-key single-flight loading,
refresh-ahead, bulk loading, invalidation and refresh statistics.

### Next tasks

1. Characterize existing cache behavior and add `KeyedLock<TKey>` with a no-op `SINGLE_THREADED`
   path, then `RefreshAfterWrite` on cache options and builders.
2. Implement and test `LoadingCache<TKey, TValue>` before the asynchronous `ValueTask` API.
3. Add builder/preset integration, bulk loading, refresh counts, docs, changelog and metadata.

**Complete when:** Concurrent misses load once per key, refresh returns stale data while one runs,
failures retain the stale value, bulk loading falls back safely, and every behavior is tested in
both threading configurations.

## Not in Scope

- Completed sessions, shipped summaries, CI measurements, retrospectives.
- Full issue bodies, architectural essays, evidence tables, command catalogs.
- Organization-only capacity, billing, quota or repository-transfer operations.
- Work with no actionable next step and no observable completion signal.
