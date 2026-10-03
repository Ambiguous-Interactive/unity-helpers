# Dictionary Key Performance

## TL;DR: Do Not Hand-Write an Enum Comparer

- `Dictionary<TEnum, TValue>` with the **default** comparer is the fastest form on Unity's Mono.
- Supplying a hand-written struct comparer costs about 11% on every lookup.
- This contradicts most Unity performance articles; they describe CoreCLR, where the advice was measured.

## The Measurement

Both sides call `TryGetValue` on an eight-member enum key with values that always hit, four million
lookups per slot. Measured on Unity **6000.4.6f1**, Editor Mono, counterbalanced `ABBABAAB` runs
with a settled heap per slot, per-cycle spread retained:

| Key                                           | Mops/s |   Ratio | Spread |
| --------------------------------------------- | -----: | ------: | -----: |
| `Dictionary<TEnum, int>`, default comparer    |  168.7 |       — |   3.1% |
| same dictionary, hand-written struct comparer |  150.2 | 0.8902x |   1.3% |

Two independent runs agreed (0.8940x, then 0.8902x).

## Why the Folk Advice Fails Here

Unity's Mono BCL already ships a specialized enum comparer for enum keys, so
there are no boxes to remove: `EqualityComparer<ProbeKey>.Default.GetType().Name` reports
`EnumEqualityComparer'1`. What a _supplied_ comparer does instead is move hashing and equality onto an
interface the JIT cannot devirtualize or inline in this runtime: one interface call per probe,
which is exactly the 11%.

The advice exists because on CoreCLR (desktop .NET), generic specialization makes supplied struct
comparers fully inlined while enums sometimes fall to a slower shared path. Unity's Mono and IL2CPP
are not that runtime.

## Practical Rules

- **Enum keys:** declare nothing. `new Dictionary<TEnum, TValue>()`.
- **Dense id-like keys (0..N):** consider an array indexed by the key first; an `int[]` was measured
  about 5x faster than any hash table at eight members.
- **Struct keys other than primitives/enums:** measure before supplying a comparer; on this runtime
  the default is usually already specialized.
- This package's own `IntMap<TValue>` exists for int keys precisely by removing all comparer
  indirection from its probe loop.

## Caveats

All three numbers above are Editor Mono measurements; IL2CPP shares much of Mono's code-generation
behavior for constrained calls but has not been re-measured here. If you re-measure on device, use a
counterbalanced protocol so the machine's temperature does not answer instead of the code.

## Integer Map Migration Constraints

A type replacement needs the caller's key domain, mutation pattern, and API contract. The
[#905 audit](https://github.com/Ambiguous-Interactive/unity-helpers/issues/905) found these
constraints in the current production owners:

| Owner                                                                                                         | Constraint to preserve before replacement                                                                                                                                                         |
| ------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `CircleLineRenderer._cachedSegments`                                                                          | Segment counts are bounded to 3–4096. Measure this small cache against an indexed table as well as a hash map.                                                                                    |
| `PoolBucketMap<T>._buckets`                                                                                   | The threaded configuration uses `ConcurrentDictionary` and atomic bucket publication. `IntMap` supplies neither; a type substitution would lose the synchronization contract.                     |
| `SpriteSheetExtractor` boundary-gap counts                                                                    | The pooled `DictionaryBuffer` lease owns cleanup. Equal-frequency gaps are selected in enumeration order; changing table order can change the inferred sprite grid. Preserve both contracts.      |
| `UnityLogTagFormatter` decoration leases                                                                      | Caller priorities accept the full signed integer domain. Keep lease cleanup synchronized with the separate sorted decoration map and preserve its priority order.                                 |
| WGroup foldout state dictionaries                                                                             | `WGroupGUI` accepts concrete `Dictionary<int, bool>` in several shared call paths. Keys are hash products; the full signed integer domain is possible. Preserve parameter interoperability.       |
| Dropdown display caches                                                                                       | `IntDropDownDrawer`, its Odin counterpart and `DropDownShared` key caches by computed hashes or option indices. Classify those domains separately; a hash may equal either reserved `IntMap` key. |
| `AnimationCreatorWindow._cachedElementProperties` and `TextureSettingsApplierWindow._replaceSelectionByIndex` | Keys are element indices. Measure realistic cache size, invalidation and miss rates before changing the backing store.                                                                            |
| Serializable dictionary/set drawer lookup and animation state                                                 | Several related maps share row indices and invalidation paths. Preserve missing-key behavior and coordinated cleanup; measure the complete drawer workload.                                       |
| WButton label/content caches                                                                                  | Count/index domains can be dense and tiny. Existing `GetOrAdd` helpers require mutable `IDictionary`. Compare indexed storage and include cold-cache misses.                                      |

`IntMap<T>` refuses `int.MinValue` and `int.MinValue + 1`, has a read-only dictionary interface
rather than the mutable `IDictionary` contract, and may change enumeration order when it rebuilds.
Its integer key alone cannot establish a safe conversion. The new collision and churn regressions
in [#926](https://github.com/Ambiguous-Interactive/unity-helpers/issues/926) also show why dense-key
microbenchmarks cannot justify every caller.

No production dictionary conversion or conversion analyzer is selected by this audit. Corrected-map
Mono measurements and floor/latest Release IL2CPP timing remain required by
[#578](https://github.com/Ambiguous-Interactive/unity-helpers/issues/578). Any eventual analyzer must
be opt-in and advisory, identify unsupported keys and interfaces, and avoid promising a faster
replacement without evidence for the consumer's workload.
