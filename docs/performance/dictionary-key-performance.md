# Dictionary Key Performance

<span id="tldr-do-not-hand-write-an-enum-comparer"></span>

## Choose a Key and Collection

Start with `Dictionary<TKey, TValue>` and its default comparer. Choose another collection when
your key domain or access contract gives you a reason. The measurements below describe one small,
hit-only workload on Editor Mono; they do not rank every dictionary on every backend.

| Your workload                     | Starting choice                                                               | Check before changing it                                              |
| --------------------------------- | ----------------------------------------------------------------------------- | --------------------------------------------------------------------- |
| Enum keys                         | `Dictionary<TEnum, TValue>` with the default comparer                         | A custom comparer must preserve equality and justify its cost.        |
| Small, dense integer indices      | Array or list                                                                 | Validate indices and account for unused slots.                        |
| Sparse integer keys, mostly reads | `Dictionary<int, TValue>`; consider `IntMap<TValue>` after measuring          | Reserved keys, miss rate, capacity, interfaces and enumeration order. |
| Multiple threads publish entries  | A synchronized collection or explicit locking                                 | `IntMap<TValue>` does not provide concurrent publication.             |
| Sorted results                    | A sorted collection or an explicit sort                                       | Hash-table enumeration is not a priority or tie-breaking contract.    |
| Unity-serialized inspector data   | A [serializable collection](../features/serialization/serialization-types.md) | Runtime dictionary speed does not supply Unity serialization.         |

## The Measurement

Both sides call `TryGetValue` on an eight-member enum key with values that always hit, four million
lookups per slot. Measured on Unity **6000.4.6f1**, Editor Mono, counterbalanced `ABBABAAB` runs
with a settled heap per slot, per-cycle spread retained:

| Key                                           | Mops/s |   Ratio | Spread |
| --------------------------------------------- | -----: | ------: | -----: |
| `Dictionary<TEnum, int>`, default comparer    |  168.7 |       — |   3.1% |
| same dictionary, hand-written struct comparer |  150.2 | 0.8902x |   1.3% |

Two independent runs agreed (0.8940x, then 0.8902x).

<span id="why-the-folk-advice-fails-here"></span>

## Interpreting the Enum Result

The measured runtime reported `EnumEqualityComparer'1` for
`EqualityComparer<ProbeKey>.Default.GetType().Name`. It already had an enum-specific default
comparer. Supplying a custom comparer was slower in this workload; the timings alone do not
isolate JIT behavior or interface dispatch as the cause.

This result applies to the measured Unity runtime. Different runtimes, comparer implementations,
key distributions and player backends can produce different results. Measure your actual target
before applying advice from a different environment.

## Practical Rules

- **Enum keys:** declare nothing. `new Dictionary<TEnum, TValue>()`.
- **Dense id-like keys (0..N):** consider an array indexed by the key first; an `int[]` was measured
  about 5x faster than any hash table at eight members.
- **Struct keys other than primitives/enums:** measure before supplying a comparer; on this runtime
  implement consistent equality and hashing, then compare the real alternatives.
- This package's own `IntMap<TValue>` exists for int keys precisely by removing all comparer
  indirection from its probe loop.

## Caveats

These are historical Editor Mono measurements. This page provides no corresponding IL2CPP result. If you re-measure on device, use a
counterbalanced protocol so the machine's temperature does not answer instead of the code.

## Integer Map Migration Constraints

Before replacing a dictionary, answer these questions for your own caller:

1. **Can every key be represented?** `IntMap<TValue>` rejects `int.MinValue` and
   `int.MinValue + 1`. Computed hashes can produce either value; an integer key is not proof
   that the domain is safe.
2. **Which interfaces do callers require?** `IntMap<TValue>` implements a read-only dictionary
   interface, with its own mutation methods. It does not implement mutable `IDictionary<int, TValue>`.
   A method that accepts the latter needs a deliberate API change or a different collection.
3. **Does order affect the result?** A rebuild can change `IntMap` enumeration order. Sort explicitly
   if iteration resolves equal scores, chooses a winner, or controls display order.
4. **Who owns cleanup?** Replacing a dictionary rented through `DictionaryBuffer` also changes its
   lifetime. Keep the lease until every consumer has finished, and return only the rented collection.
5. **Who can mutate it?** Do not replace `ConcurrentDictionary` with an unsynchronized map. A lock
   must cover the complete shared operation, including lookup, insertion and dependent state.
6. **What does the complete operation cost?** Include initialization, growth, misses, churn and
   retained capacity. Compare an indexed array as well when keys are dense and bounded.

See [IntMap usage and limits](../features/utilities/data-structures.md#intmap-int-keyed-open-addressing-map) before adoption.
Use the [performance measurement guide](./baseline-tests-performance.md) to interpret a result;
a dense, hit-only microbenchmark cannot establish a win for a large, miss-heavy cache.

Keep `Dictionary` when it meets your needs and you have no representative player measurement
supporting a replacement. This page does not promise a faster replacement or an automatic conversion.
