# Integer bit lookup tables

`BitOps.Log2(uint)` and `Log2(ulong)` use bit smearing and de Bruijn indexing. Zero still
returns zero, and signed overloads still interpret the unsigned bit pattern. Lookup data lives
in constant read-only spans. Mono reads fixed array caches initialized once from those spans:
span construction was slower in the native editor even after getter inlining. The caches hold
160 table bytes plus array overhead. Warm lookups create no arrays.

Three alternating fresh process pairs on Windows x64 Unity 6000.6.7f1 Editor Mono compare main
`af80061e` with this implementation. Inputs contain 4,096 values in a seeded shuffled order,
with varied bit widths or values 1–40. Each capture uses 64 warm calls, batches calibrated to
at least 20 ms and 21 raw rounds. Independent reference loops calculate the expected checksum.
Every measured batch checks its result.

| Operation                  | Input         | Before median | After median | Paired after/before range |
| -------------------------- | ------------- | ------------: | -----------: | ------------------------: |
| `Log2(uint)`               | Varied widths |      2.496 ns |     1.276 ns |               0.502–0.571 |
| `Log2(uint)`               | 1–40          |      2.175 ns |     1.275 ns |               0.569–0.607 |
| `Log2(ulong)`              | Varied widths |      9.062 ns |     1.427 ns |               0.156–0.619 |
| `Log2(ulong)`              | 1–40          |      2.223 ns |     1.436 ns |               0.625–0.648 |
| `TrailingZeroCount(ulong)` | Varied widths |      0.926 ns |     0.926 ns |               0.983–1.022 |
| `TrailingZeroCount(ulong)` | 1–40          |      0.854 ns |     0.846 ns |               0.988–1.025 |

The table uses the median of three process medians. The varied 64-bit baseline is unstable:
its three medians are 2.306, 9.062 and 9.335 ns. All three pairs improve, but the largest ratio
is not a general speed guarantee. These loops include traversal and checksum costs. They do
not establish isolated instruction latency, IL2CPP performance, another runtime's behavior,
or a game workload speedup.

All six processes pass 619 cases with no skips or failures, including the existing cases and
four exhaustive 16-bit-window oracle cases. Those functional cases use scalar checks without a per-value assertion allocation and remain
in the normal suite. The timing fixture is
in the `Performance` category and stays out of normal iteration.

Each process exits zero, reaches both output stream ends without a drain stop and retains
unchanged input hashes. Loaded module identities are stable within each variant and differ
between variants. Offline inspection of the captured DLL confirms the Mono cache fields,
one array load per lookup and constant-data span getters without array creation. Initialization
creates the caches once; the IL check describes warm lookup paths.

The [replay archive](./bit-lookup-tables.zip) contains source variants, fixtures, all six NUnit
results, raw rounds, process receipts, source and assembly identities, IL records and replay steps.
