# Dictionary Key Performance

## TL;DR — Do Not Hand-Write an Enum Comparer

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
interface the JIT cannot devirtualize or inline in this runtime — one interface call per probe,
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
