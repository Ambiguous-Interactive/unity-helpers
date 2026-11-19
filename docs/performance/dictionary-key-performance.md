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

## Insertion inlining qualification

A serial comparison tests only an `AggressiveInlining` hint on `SetInternal`, against current
main. Three fresh .NET 10 processes and three Mono processes cover 195 cells each, including
four key shapes and sizes 8, 64, 1024 and 16384. Each workload warms 64 calls, calibrates to
at least 20 ms, and retains 21 rotated rounds. Sources and compiled payload hashes stay fixed.

The hint is rejected. Insertion candidate/baseline geometric means are 1.0050 on .NET and
0.9612 on Mono; the equally weighted runtime aggregate is 0.9828. This misses the preregistered
0.95 aggregate threshold and the requirement that both runtimes improve. No hit/miss/iteration
cell has a median process ratio above 1.10 versus current main. The production map is unchanged.

### Recovered consumer harness on unchanged main

A separate serial replay uses the archived original-consumer program and its frozen 3.7.1
baseline, with restored current main as the candidate. All six fresh processes return zero,
reach both stream ends, report all 111 cells, and retain stable source/payload hashes. Values
are medians of three process medians. Hit/miss/insert ratios compare with 3.7.1; iteration
ratios compare with `Dictionary<int,int>`. All values are nanoseconds per operation or entry.

| Runtime | Keys      | Count | Hit ns (3.7.1 ratio) | Miss ns (3.7.1 ratio) | Insert ns (3.7.1 ratio) | Iterate ns/entry (Dictionary ratio) |
| ------- | --------- | ----: | -------------------: | --------------------: | ----------------------: | ----------------------------------: |
| .NET 10 | dense     |     8 |       0.718 (1.010×) |        1.099 (1.998×) |         14.096 (1.209×) |                      0.519 (0.966×) |
| .NET 10 | dense     |    64 |       0.823 (1.050×) |        0.755 (1.153×) |         12.802 (1.184×) |                      0.568 (0.974×) |
| .NET 10 | dense     |  1024 |       0.782 (1.061×) |        0.681 (1.018×) |         11.744 (1.156×) |                      0.489 (0.970×) |
| .NET 10 | sparse    |     8 |       0.895 (0.917×) |        0.681 (0.909×) |         11.853 (1.147×) |                      0.584 (1.076×) |
| .NET 10 | sparse    |    64 |       0.948 (1.004×) |        0.861 (0.898×) |         10.792 (1.048×) |                      0.509 (0.992×) |
| .NET 10 | sparse    |  1024 |       0.940 (0.959×) |        0.889 (0.871×) |         11.092 (1.081×) |                      0.411 (0.822×) |
| .NET 10 | shift1024 |     8 |       0.824 (0.863×) |        0.670 (0.975×) |         11.564 (1.143×) |                      0.507 (1.030×) |
| .NET 10 | shift1024 |    64 |       1.017 (0.978×) |        0.753 (0.996×) |         10.846 (1.064×) |                      0.497 (0.969×) |
| .NET 10 | shift1024 |  1024 |       0.910 (0.998×) |        0.850 (1.006×) |         12.045 (1.136×) |                      0.433 (0.815×) |
| Mono    | dense     |     8 |       2.205 (0.983×) |        2.667 (1.043×) |         12.273 (0.947×) |                      4.613 (1.197×) |
| Mono    | dense     |    64 |       2.128 (0.964×) |        1.593 (1.027×) |         12.341 (0.968×) |                      2.997 (1.079×) |
| Mono    | dense     |  1024 |       1.991 (1.002×) |        1.461 (1.010×) |         50.518 (1.188×) |                      2.712 (1.056×) |
| Mono    | sparse    |     8 |       2.179 (1.005×) |        2.694 (1.057×) |         12.297 (0.961×) |                      4.583 (1.192×) |
| Mono    | sparse    |    64 |       2.271 (0.973×) |        2.120 (1.043×) |         13.659 (1.062×) |                      3.002 (1.063×) |
| Mono    | sparse    |  1024 |       2.306 (1.012×) |        2.400 (1.167×) |         58.021 (1.262×) |                      2.679 (1.069×) |
| Mono    | shift1024 |     8 |       2.239 (1.039×) |        2.657 (1.041×) |         12.841 (1.006×) |                      4.651 (1.181×) |
| Mono    | shift1024 |    64 |       3.369 (1.192×) |        1.655 (1.055×) |         15.050 (1.037×) |                      3.088 (1.100×) |
| Mono    | shift1024 |  1024 |       2.081 (1.034×) |        1.568 (1.032×) |         48.563 (1.219×) |                      2.724 (1.037×) |

Iteration meets the 1.5× Dictionary threshold for every required 64/1024 cell. However,
13 hit/miss/insert cells exceed the original 10% allowance. Churn capacity remains bounded
at 64 slots in every current-main process. These results do not complete
[#1131](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1131). They do not qualify
Unity, IL2CPP, WebGL, arbitrary values or application workloads.

The [qualification archive](https://raw.githubusercontent.com/Ambiguous-Interactive/unity-helpers/587d3de6fde16c76a0ba256872cfe8c800bf1287/docs/performance/intmap-inlining-qualification.zip) contains all expanded and original
consumer source inputs, calibrated raw rounds, complete row sets, receipts, source/payload seals,
environment records, analysis and controllers. It preserves the rejected hint and a separate
unchanged-main replay. Earlier overlapping exploratory captures are excluded from the retention
decision and remain retained locally.

## November 19 original consumer replay

The archived consumer harness ran against main at `6ce056ca`, with its original 3.7.1
and Dictionary controls. Three .NET 10.0.14 processes and three Mono 6.14.1 processes
each completed 111 cells. Source and executable hashes stayed fixed. Values are medians
of three process medians. Hit, miss and insertion compare with 3.7.1. Iteration compares
with `Dictionary<int,int>`.

| Runtime | Keys      | Count | Hit ns (3.7.1 ratio) | Miss ns (3.7.1 ratio) | Insert ns (3.7.1 ratio) | Iterate ns/entry (Dictionary ratio) |
| ------- | --------- | ----: | -------------------: | --------------------: | ----------------------: | ----------------------------------: |
| .NET 10 | dense     |     8 |       0.628 (1.013×) |        0.936 (1.983×) |         11.559 (0.556×) |                      0.452 (0.976×) |
| .NET 10 | dense     |    64 |       0.723 (1.048×) |        0.665 (1.149×) |          8.088 (1.079×) |                      0.487 (1.089×) |
| .NET 10 | dense     | 1,024 |       0.675 (1.071×) |        0.486 (1.070×) |          7.101 (1.083×) |                      0.423 (0.972×) |
| .NET 10 | sparse    |     8 |       0.840 (1.029×) |        0.593 (0.997×) |          6.972 (1.014×) |                      0.497 (1.069×) |
| .NET 10 | sparse    |    64 |       0.819 (0.987×) |        0.846 (1.082×) |          6.620 (0.965×) |                      0.448 (1.014×) |
| .NET 10 | sparse    | 1,024 |       0.803 (0.979×) |        0.732 (0.821×) |          6.886 (1.017×) |                      0.360 (0.824×) |
| .NET 10 | shift1024 |     8 |       0.728 (0.887×) |        0.593 (0.997×) |          6.610 (1.048×) |                      0.440 (1.021×) |
| .NET 10 | shift1024 |    64 |       0.888 (0.975×) |        0.661 (1.000×) |          7.390 (1.010×) |                      0.447 (0.941×) |
| .NET 10 | shift1024 | 1,024 |       0.796 (1.000×) |        0.727 (0.993×) |          7.291 (1.036×) |                      0.360 (0.829×) |
| Mono    | dense     |     8 |       1.969 (0.987×) |        2.531 (1.038×) |         10.297 (0.925×) |                      4.033 (1.154×) |
| Mono    | dense     |    64 |       1.925 (1.040×) |        1.462 (1.014×) |         10.994 (1.017×) |                      2.710 (1.092×) |
| Mono    | dense     | 1,024 |       1.839 (1.024×) |        1.338 (1.021×) |         42.112 (1.188×) |                      2.442 (1.062×) |
| Mono    | sparse    |     8 |       1.996 (1.024×) |        2.484 (1.038×) |         10.421 (0.953×) |                      4.168 (1.198×) |
| Mono    | sparse    |    64 |       2.066 (1.012×) |        1.968 (1.114×) |         11.222 (1.046×) |                      2.718 (1.097×) |
| Mono    | sparse    | 1,024 |       2.033 (1.025×) |        2.094 (1.139×) |         43.973 (1.202×) |                      2.468 (1.060×) |
| Mono    | shift1024 |     8 |       1.949 (0.986×) |        2.536 (1.055×) |         10.495 (0.946×) |                      4.115 (1.164×) |
| Mono    | shift1024 |    64 |       3.025 (1.249×) |        1.488 (1.021×) |         12.439 (1.069×) |                      2.757 (1.104×) |
| Mono    | shift1024 | 1,024 |       1.899 (1.016×) |        1.461 (1.016×) |         42.383 (1.195×) |                      2.480 (1.069×) |

All 12 required iteration cells at 64 and 1,024 entries meet the 1.5× Dictionary
limit. Eight of 54 hit, miss and insertion cells exceed the 1.10× limit.
Both .NET failures were dense misses at 8 and 64 keys. The six Mono failures were
a 64-key shared-low-bit hit, sparse misses at 64 and 1,024 keys, and all three
1,024-key insertion cells.
The million-operation churn workload retains 64 slots in every process.

A candidate that deferred reverse-index construction until removal passed all 66
required cells in three independent batches. Hit, miss and insertion used IntMap 3.7.1
as the baseline. Iteration used Dictionary. The candidate also passed the 170-case host
suite, including allocation-failure recovery. Compared with main, the first removal
from a 16,384-entry map increased from 0.433 to 119.749 µs on .NET and from 0.388 to 40.196 µs on Mono,
so the candidate was rejected. A later candidate coallocated the occupied-slot
index with the keys and limited reverse search to slot groups. It passed two batches,
then exceeded the insertion limit at 64 entries on both runtimes in the third batch.
Its first removal at 16,384 entries took 1.832× main on .NET and 2.473× on Mono.

The hit, miss and insertion limits remain open for
[#1131](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1131).
These measurements use the standalone consumer harness and host tests. Player
or backend-specific claims require their own measurements.

### Independent source and binary replays

Two further batches used the frozen consumer harness on .NET SDK 10.0.403
(runtime 10.0.14 with PGO) and Mono 6.14.1. The IntMap source at main
`990dd276` is byte-identical to the source in the preceding table.
Each batch used three processes per runtime. The ranges below cover the
preceding main batch and the two replays. An exact-source rebuild and a
separate run of the sealed binaries reproduced the passing iteration result.
Lookup and insertion still exceed the 1.10× target in some cells.

| Runtime |  Hit / 3.7.1 | Miss / 3.7.1 | Insert / 3.7.1 | Iterate / Dictionary |
| ------- | -----------: | -----------: | -------------: | -------------------: |
| .NET 10 | 0.887–1.074× | 0.821–1.994× |   0.556–1.287× |         0.823–1.089× |
| Mono    | 0.979–1.290× | 1.004–1.177× |   0.925–1.224× |         1.060–1.108× |

The ranges cover dense, seeded sparse, and shared-low-bit keys. The lookup
and insertion ranges include 8, 64, and 1,024 keys. The iteration range covers
the required 64 and 1,024 keys. All replays kept 64 slots through one million
churn operations.

A candidate that encoded the keys and grouped the reverse index in four-slot
groups passed all 66 required cells in its first three batches. Its fresh
source rebuild exceeded the Mono sparse 1,024-key insertion limit at 1.108×.
The archived binaries exceeded the .NET sparse 1,024-key miss limit at 1.391×.
Iteration remained within budget in every batch.

| Runtime |  Hit / 3.7.1 | Miss / 3.7.1 | Insert / 3.7.1 | Iterate / Dictionary |
| ------- | -----------: | -----------: | -------------: | -------------------: |
| .NET 10 | 0.861–1.038× | 0.695–1.391× |   0.268–1.049× |         0.938–1.210× |
| Mono    | 0.917–0.974× | 0.561–0.916× |   0.681–1.108× |         1.077–1.117× |

A separate first-removal comparison used the same candidate and current-main
source. Across three batches, Mono first removal took 1.114–1.190× main at 64
keys, 1.116–1.289× at 1,024, and 1.188–1.253× at 16,384. The source passed enumeration
invalidation and bounded churn on both runtimes. The removal regression and
the two original-budget misses ruled out the candidate.

### Current main and collision insertion

Two later batches replayed `8b2d09c2` with the same frozen consumer workload and
runtimes. One rebuilt the exact source. The other ran the archived executables against
the same inputs. Each batch retained three processes per runtime with 111 cells per
process, the expected checksum, and 64 slots through one million churn operations.
The ranges are ratios of median process medians over the same key shapes and sizes
as the preceding table.

| Runtime |  Hit / 3.7.1 | Miss / 3.7.1 | Insert / 3.7.1 | Iterate / Dictionary |
| ------- | -----------: | -----------: | -------------: | -------------------: |
| .NET 10 | 0.911–1.074× | 0.853–1.982× |   0.956–1.210× |         0.818–1.096× |
| Mono    | 0.971–1.246× | 0.993–1.141× |   0.931–1.228× |         1.058–1.101× |

Each batch passes all 12 required iteration cells and exceeds the 1.10× budget
in ten of 54 lookup or insertion cells. Separate controls on both runtimes passed
240 enumerator invalidation and membership checks on this source.

A candidate moved collision handling out of the primary insertion path. Its original
batch missed the Mono shared-low-bit 1,024-key insertion budget at 1.101×. The
independent source rebuild missed that insertion budget at 1.111× and the .NET
sparse 1,024-key miss budget at 1.243×. Replaying the first batch's exact binaries
missed the .NET sparse 64- and 1,024-key miss budgets at 1.110× and 1.174×.
All three batches passed the required iteration cells, and the candidate passed
the mutation and bounded churn controls on both runtimes.

In a separate paired first-removal workload, the candidate took 1.250× main
at 64 keys and 1.263× at 1,024 on Mono. A follow-up specialized removal for
group heads and measured 1.022× and 1.099× in a fresh batch. It still missed the Mono 1,024-key shared-low-bit insertion budget
at 1.134×. Both candidates were rejected.

## Rejected first-probe split

A separate candidate keeps the first `TryGet` probe inline and moves collision continuation to
a non-inlined helper. Three fresh processes per runtime compare it with unchanged main and
`Dictionary`, retaining all 195 cells per process. Each workload warms 64 calls, calibrates to
at least 20 ms and retains 21 rotated rounds. Raw exits are zero, both streams reach EOF, and
source and compiled payload hashes remain stable.

The preset requirement was a hit/miss geometric mean of at most 0.95 on each runtime and no
cell above 1.10. Ratios use medians of the three process medians, candidate divided by main.

| Runtime | Hit/miss geometric mean | Cells above 1.10 | Decision |
| ------- | ----------------------: | ---------------: | -------- |
| .NET 10 |                  1.2123 |               23 | Reject   |
| Mono    |                  0.8370 |                1 | Reject   |

The .NET regression is clear: dense eight-key misses cost 1.585 times main, with paired ratios
1.589, 1.582 and 1.565. Mono improves lookup cost overall, but its sparse 1024-entry iteration
cell also exceeds the limit: aggregate 1.171, with paired ratios 1.171, 1.012 and 1.041. That
iteration observation varies substantially and does not establish a repeatable iteration defect.
The .NET lookup regressions alone reject this candidate. Production `IntMap` remains unchanged.

The [rejected-candidate archive](https://raw.githubusercontent.com/Ambiguous-Interactive/unity-helpers/587d3de6fde16c76a0ba256872cfe8c800bf1287/docs/performance/intmap-first-probe-rejected.zip) contains exact sources,
binaries, all calibrated raw rounds, complete results, process receipts, source/payload seals
and the analysis. This compares with current main, not the original 3.7.1 acceptance baseline;
[#1131](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1131) remains open under
the original consumer budgets above.

### Rejected empty-count guard removal

A later isolated candidate removed only the nonzero-key `TryGet` empty-count guard. Two fresh
.NET 10 processes retained the calibrated 195-cell protocol and added three empty-map miss
controls, for 198 cells per capture. Both processes passed the timed checksums and dictionary
oracle, returned zero, reached both stream ends, and preserved source and payload hashes.

The candidate was rejected by its preregistered early-stop rule. Empty-map misses cost 1.4523
and 1.4891 times main; dense 16,384-key hits cost 1.2511 and 1.2291 times main. Sparse and shifted
large-map hit controls also regress in both captures. The lookup geometric-mean ratios are
0.9697 and 0.9834, below the required 5% benefit. Production `IntMap` is unchanged, and no Mono
qualification is claimed for this candidate.

The [rejection record](./intmap-empty-guard-rejection.csv) contains all 396 reported cells,
process receipts, sealed source/binary hashes and the decision rule. Raw per-round logs and
the frozen harness remain in ignored `progress/`. This experiment does not replace the original
consumer lookup, insertion and iteration budgets tracked in
[the open acceptance issue](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1131).

## November 14 rebuild experiment

A further candidate walked the existing dense occupied-slot index during table growth instead
of scanning all old key slots. It was rejected: the median paired insertion geometric ratios
were 0.988 on .NET 10 and 0.992 on Mono, missing the preregistered 0.95 threshold. Mono's
16,384-entry shifted-key iteration cell regressed by 12.4%. Production `IntMap` is unchanged.

Three fresh processes per runtime used the existing 198-cell comparison, 64 warmups, at least
20 ms calibrated samples and 21 rotated rounds. Every process returned zero, drained both
output streams, retained all 198 distinct cells and kept source and payload hashes unchanged.
No other owned build, test or timing capture overlapped. The [complete cell summaries](./intmap-rebuild-20261114.csv)
include Dictionary controls; the [qualification record](./intmap-rebuild-20261114.json) retains
all paired ratios, process receipts, environment lines, source/payload hashes and the rejected diff.
Raw round observations remain in ignored session evidence.

This experiment compares commit `635bd87f` with the candidate. It does not replace the original
consumer baseline or resolve the remaining lookup/insertion budgets in
[#1131](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1131).

## November 14 insertion array-cache experiment

Caching the key-array reference in the nonzero insertion/update probe loop did not meet the
retention gate. Three fresh processes on each runtime produced median paired insertion geometric
ratios of 0.99505 on .NET 10 and 0.99401 on Mono, missing the preregistered 0.95 threshold.
No non-insertion median paired cell exceeds 1.10. The candidate is rejected and production
`IntMap` is unchanged.

Every process retained all 198 distinct cells, returned zero, drained both output streams and kept
source and payload hashes stable. Each cell retained 64 warmups, calibration for at least 20 ms
and 21 rotated rounds. No other owned test, build or timing capture overlapped. The
[1,188 cell summaries](./intmap-key-cache-20261114.csv) include all Dictionary controls; the
[qualification record](./intmap-key-cache-20261114.json) contains every paired ratio, process receipt,
environment line, source/payload seal and the rejected diff. Raw rounds remain in ignored session
evidence, with hashes in that record.

This compares with commit `10138d86`. It does not replace the original 3.7.1 consumer baseline or
close [#1131](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1131).

## Rejected nonzero guard layout

A candidate keeps nonzero `TryGet` probing together and moves the direct zero-key return after it.
The empty-count guard, hash, probe loop, values, mutation behavior and dense indices remain unchanged.
Two serial fresh .NET 10 processes each complete 198 cells with 64 warm calls, at least 20 ms
calibration, 21 rotating rounds, Dictionary controls and timed checksums.

Hit/miss geometric candidate/main ratios are **1.00398** and **1.00491**. Both meet the preset
early-rejection rule of at least 1.0, so the candidate is rejected without further runtime runs.
No non-insertion cell exceeds 1.10 in both processes. Small differences near 1.0 do not establish
a causal slowdown. All 136 existing host correctness/recovery cases pass for the candidate.
Both captures retain zero exits, both stream ends and unchanged source/payload hashes.

The [qualification record](./intmap-guard-layout-20261117.json) retains all paired cell ratios,
process receipts, seals, the rejected diff and environment limitations. Raw sources and all rounds
remain in ignored session storage. A fresh SDK image's failed net48 setup remains recorded;
this triage explicitly builds net10.0 and does not qualify Mono or native players.
The earlier recovered consumer replay measured 13 of 54 lookup/insert cells above the original
budget. Production `IntMap` is unchanged, and
[#1131](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1131) remains open.
