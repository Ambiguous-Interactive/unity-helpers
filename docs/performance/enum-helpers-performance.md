# Enum Helper Performance

These measurements support the `[WEnumHelpers]` generation path and the generalized enum
helpers. They are **.NET 9 desktop measurements**, not Unity Editor Mono or IL2CPP measurements.
Use them to choose a strategy and to reproduce the experiment; re-measure on the target player
before making a platform-specific claim.

## Workload

The harness compiled the repository's real `EnumExtensions`, `EnumValues`, `EnumMembershipCache`,
and `EnumNameComparer` sources and compared:

- the generalized helper path;
- a hand-written generated-equivalent path using the same literal table shape the generator emits;
- the BCL `ToString()` where relevant.

Each operation ran a retained loop over every declared value. Timings are medians of five
counterbalanced rounds after warmup. Cold numbers are separate processes that allocate and
touch one table for the first time. The environment was .NET 9.0.306 on Linux x64.

| Shape       | Values                                       |
| ----------- | -------------------------------------------- |
| `small`     | 4-member int32 enum                          |
| `dense`     | 8-bit int32 flags enum                       |
| `wide`      | 8-bit uint64 flags enum with high bits       |
| `signed`    | sbyte flags enum with negative members       |
| `sparse100` | 100-member int32 enum spanning about 100,000 |
| `display`   | int32 enum with `EnumDisplayName` attributes |

## Results

| Operation   | Shape     | Generalized |     Generated | Ratio |
| ----------- | --------- | ----------: | ------------: | ----: |
| Flag check  | small     |    0.505 ns |      0.405 ns | 1.25x |
| Flag check  | dense     |    0.540 ns |      0.420 ns | 1.29x |
| Flag check  | wide      |    0.420 ns |      0.420 ns | 1.00x |
| Flag check  | signed    |    0.550 ns |      0.480 ns | 1.15x |
| Name lookup | small     |     1.07 ns |       0.35 ns | 3.06x |
| Name lookup | dense     |     1.20 ns |       0.35 ns | 3.43x |
| Name lookup | wide      |     2.60 ns |       1.90 ns | 1.37x |
| Name lookup | sparse100 |     2.50 ns |       2.00 ns | 1.25x |
| Name lookup | display   |     1.00 ns |       0.33 ns | 3.03x |
| Parse       | small     |      9.0 ns | not generated |     — |
| Parse       | sparse100 |      180 ns | not generated |     — |

Cold start, first use of the lookup table:

| Shape     | Generalized allocation | Generated allocation | Generalized first call | Generated first call |
| --------- | ---------------------: | -------------------: | ---------------------: | -------------------: |
| small     |                2,712 B |                 56 B |                4.08 ms |              0.09 ms |
| dense     |                5,024 B |              2,072 B |                3.76 ms |              0.09 ms |
| sparse100 |               33,896 B |             11,896 B |                4.49 ms |              2.38 ms |
| wide      |                3,840 B |                  0 B |                4.67 ms |              0.09 ms |

## Interpretation

- Dense literal arrays are the right generated shape for names and display names: the existing
  generalized path is already an array index, so the win comes from replacing the numeric
  conversion and reflection initialization, not from a different lookup algorithm. A `switch`
  tree was slower than the array on these shapes and was rejected.
- A generated numeric `switch` is the right shape for wide enums with few members: it is faster
  than the generalized dictionary and retains no table.
- A generated `Dictionary<ulong, string>` is the right shape for large sparse enums. It is a
  hot-path win here and removes the generalized cache's reflection build. The 11,896 B cold
  figure is an upper bound measured with a harness that built its keys and names at runtime; the
  shipped generator emits literals.
- Generated flag checks are a modest win. The wide 64-bit case is already one bitwise test.
- The generalized cold numbers already include reusing `Enum.GetNames` output for alias-free
  sparse caches and dropping the unused dictionary on the array path; both reduce first-use
  allocation without changing a name.
- `typeof(T) == typeof(int)` cannot specialize enum conversion: `typeof(T)` is the enum type,
  not its underlying type. That experiment was measured and discarded; the generalized
  conversion remains the `TypeCode` switch.
- Parse remains the generalized linear scan. The source generator deliberately does not change
  the `EnumNameCache<T>.TryParse` API because a generic static class cannot receive a per-enum
  overload.

## Allocation contract

- `ToCachedName()` returns a `string`; it is zero-allocation after warmup for values the cache
  retains.
- A generated array or dictionary table is a one-time static initialization: the first call for
  that enum allocates the table, and later calls allocate nothing. A generated numeric `switch`
  table allocates nothing at all.
- `ToDisplayName()` behaves the same way; generated display names are literals.
- `TryFormatCachedName()` writes into a caller-owned `Span<char>`. After its enum's table is
  initialized, a declared value reuses that text without allocating. An uncached flag
  combination or undefined value may allocate its first formatted string, exactly like
  `ToCachedName()`.

The full raw table is in [enum-helpers-benchmarks.csv](./enum-helpers-benchmarks.csv).
