# IList Sorting Performance Benchmarks

Unity Helpers ships several custom sorting algorithms for `IList<T>` that cover different trade-offs between adaptability, allocation patterns, and stability. This page gathers context and benchmark snapshots so you can choose the right algorithm for your workload and compare results across operating systems.

## Algorithm Cheatsheet

| Algorithm                  | Stable? | Best For                                                                   | Reference                                                                                                 |
| -------------------------- | ------- | -------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| Ghost Sort                 | No      | Mixed workloads that benefit from adaptive gap sorting and few allocations | Upstream project by Will Stafford Parsons (public repository currently offline)                           |
| Meteor Sort                | No      | Almost-sorted data where gap shrinking beats plain insertion sort          | Upstream project by Will Stafford Parsons (public repository currently offline)                           |
| Pdqsort-inspired introsort | No      | General-purpose quicksort with a heapsort fallback                         | [pdqsort by Orson Peters](https://github.com/orlp/pdqsort)                                                |
| Grail Sort                 | Yes     | Large datasets where stability + low allocations matter                    | [GrailSort](https://github.com/Mrrl/GrailSort)                                                            |
| Power Sort                 | Yes     | Partially ordered data that benefits from adaptive run detection           | [PowerSort (Munro & Wild)](https://arxiv.org/abs/1805.04154)                                              |
| Tim Sort                   | Yes     | General-purpose stable sorting with abundant natural runs                  | [Wikipedia - Timsort](https://en.wikipedia.org/wiki/Timsort)                                              |
| Jesse Sort                 | No      | Data with long runs or duplicates where dual patience piles shine          | [JesseSort](https://github.com/lewj85/jessesort)                                                          |
| Green Sort                 | Yes     | Sustainable stable merges that trim ordered prefixes                       | [greeNsort](https://www.greensort.org/index.html)                                                         |
| Ska Sort                   | No      | Branch-friendly partitioning on large unstable datasets                    | [Ska Sort](https://probablydance.com/2016/12/27/i-wrote-a-faster-sorting-algorithm/)                      |
| Ipn Sort                   | No      | In-place adaptive quicksort scenarios needing strong pivots                | [ipnsort write-up](https://github.com/Voultapher/sort-research-rs/tree/main/writeup/ipnsort_introduction) |
| Smooth Sort                | No      | Weak-heap hybrid that approaches O(n) for presorted data                   | [Smoothsort - Wikipedia](https://en.wikipedia.org/wiki/Smoothsort)                                        |
| Block Merge Sort           | Yes     | Stable merges with √n buffer (WikiSort style)                              | [WikiSort](https://github.com/BonzaiThePenguin/WikiSort)                                                  |
| IPS⁴o Sort                 | No      | Cache-aware samplesort with multiway partitioning                          | [IPS⁴o paper](https://arxiv.org/abs/1705.02257)                                                           |
| Power Sort Plus            | Yes     | Enhanced run-priority merges inspired by Wild & Nebel                      | [PowerSort paper](https://arxiv.org/abs/1805.04154)                                                       |
| Glide Sort                 | Yes     | Stable galloping merges from the Rust glidesort research                   | [sort-research-rs](https://github.com/Voultapher/sort-research-rs)                                        |
| Flux Sort                  | No      | Dual-pivot quicksort tuned for modern CPUs                                 | [sort-research-rs](https://github.com/Voultapher/sort-research-rs)                                        |
| Yam Sort                   | Yes     | Sequential or reverse-sequential data, where it approaches O(n)            | [YamSort by Gary Gende](https://github.com/gendeg/YamSort)                                                |
| Insertion Sort             | Yes     | Tiny or nearly sorted collections where O(n²) is acceptable                | [Wikipedia - Insertion sort](https://en.wikipedia.org/wiki/Insertion_sort)                                |

> **What does “stable” mean?** Stable sorting algorithms preserve the relative order of elements that compare as equal. This matters when items carry secondary keys (e.g., sorting people by last name but keeping first-name order deterministic). Unstable algorithms can reshuffle equal entries, which is usually fine for numeric keys but can break deterministic pipelines.
>
> The Stable? column is a promise the test suite holds. `IListSortCorrectnessTests` decorates every element with its original index and enumerates a bounded but genuinely exhaustive domain -- every sequence over a three symbol alphabet up to length six, every binary sequence up to length ten, and every permutation of seven distinct elements -- checking that each result is a permutation of its input rather than merely sorted, and that every algorithm the table calls stable kept equal elements in order. A new `SortAlgorithm` member fails the suite until the table is extended.
>
> **Heads up:** Ghost Sort and Meteor Sort have no reachable upstream. Both were published by Will Stafford Parsons and both repositories now return 404, so the implementation in this package is the reference for what these algorithms do here. Anything a third party reports about them cannot be checked against a source.

## JesseSort

`JesseSort` adapts [Jesse Lew's allocating live-phase pipeline](https://github.com/lewj85/jessesort/tree/1bf1f3d5b719c869880d98443050a836a65f47c1),
including its E750 run precompaction policy. Sorted, reverse-sorted, and equal input retain a linear
early exit. Other inputs are divided into monotone, direct, and dual-patience regions. Region probes
grow from 1,024 to 8,192 values; a route change must persist through two confirmation probes.
Direct regions use the package's `IpnSort` backend. Patience regions record compact pile assignments,
reuse equivalent-value assignments and previous pile hints, and switch a pressured game's remaining
suffix to direct sorting. Descending piles are reconstructed before ascending piles.

The pipeline also recognizes sparse disorder from 10,000 values and bounded natural-run layouts
from 50,000 values. Prepared runs merge in adjacent pairs, with ordered-boundary and
reverse-disjoint shortcuts, route-specific galloping, and selective outer-run precompaction.

This remains a C# adaptation: direct sorting uses `IpnSort`, pile searches use binary search, and
pooled cursors replace upstream's reconstruction storage choices. Large values remain directly
sorted. The upstream compact-index route took roughly twice as long as the same pipeline without
that route on the measured 40-byte records, with both interface and struct comparers. These
measurements cover Editor Mono; they do not establish a crossover for larger records or players.
These implementation differences
preclude a claim of identical C++ timings. The algorithm remains unstable; comparer-equivalent
items may change relative order. Tests hold ordering and preservation of every original payload
across array, list, and indexer-only backings, including large records and route thresholds.

### Native Unity comparison

The [final dataset](./jessesort-native-comparison.json) retains all 26 cells and 2,080 raw timing
slots: 20 integer array shapes, four integer list shapes, and two 40-byte record shapes, each with
100,000 elements. These measurements were taken on Unity 6000.4.6f1, Editor Mono, Windows 11,
and an Intel Core Ultra 9 285K. The debugger and profiler were disabled. The previous implementation
is pinned to `99f0520c`; the final source and helper hashes are recorded in the dataset. The list
cells compare source clones that share an indexer-loop writeback adapter. Production list writeback
uses `Clear` and `AddRange`, so these cells do not measure the exact production list backend. Array
timings are unaffected.

Each arm warms for at least 100 ms. Eight batches use the sequence `ABBABAABCD`, producing 32
samples for each JesseSort implementation and eight each for the framework sort and `IpnSort`
controls. One timer surrounds a whole batch of prepared replicas; cloning and validation occur
outside timing. All arms use a common replication count within a cell. The shortest observed slot
was 38.96 ms. The table reports median milliseconds per sort and the ratio of final to previous
medians; a ratio below one means the final implementation took less time. The 95% intervals use
2,000 bootstrap resamples of eight complete paired batches, preserving A/B pairing.

| Input                                  | Backing | Previous median (ms) | Final median (ms) | Final / previous | 95% ratio interval |
| -------------------------------------- | ------- | -------------------: | ----------------: | ---------------: | ------------------ |
| Random                                 | Array   |               11.162 |            10.590 |            0.949 | 0.943–0.951        |
| Sorted                                 | Array   |                0.377 |             0.330 |            0.874 | 0.869–0.895        |
| Equal                                  | Array   |                0.227 |             0.208 |            0.917 | 0.906–0.923        |
| OrganPipe                              | Array   |                2.785 |             0.781 |            0.280 | 0.278–0.283        |
| Sawtooth                               | Array   |                8.672 |             3.764 |            0.434 | 0.429–0.435        |
| Noise1                                 | Array   |                6.512 |             5.943 |            0.913 | 0.900–0.920        |
| Runs38                                 | Array   |                3.927 |             0.316 |            0.081 | 0.080–0.081        |
| OverlapRuns38                          | Array   |                3.652 |             1.481 |            0.405 | 0.400–0.409        |
| Random                                 | List    |               11.467 |            10.933 |            0.953 | 0.945–0.959        |
| Noise1                                 | List    |                6.641 |             6.139 |            0.924 | 0.921–0.930        |
| Random (40-byte record)                | Array   |               15.826 |            14.893 |            0.941 | 0.916–0.953        |
| AlternatingDuplicates (40-byte record) | Array   |                6.396 |             5.787 |            0.905 | 0.895–0.921        |

The [faithful upstream-policy reference](./jessesort-upstream-reference.json), pinned to
`4ec12c5d`, is also retained. In that earlier session, `Noise1` regressed: array ratio 1.047
(95% interval 1.033–1.063) and list ratio 1.038 (1.024–1.048). The final session's lower ratios
above do not erase those observations; session variation limits conclusions about this small
tradeoff. On the 40-byte inputs, the faithful compact-index route also regressed against the
previous implementation. The [isolated index-routing comparison](./jessesort-index-routing.json)
then compared the same pipeline with only that route disabled. Direct values took 0.445–0.526
of the compact-index time across the two shapes and both interface and concrete struct comparer
forms. The final C# adaptation therefore omits that route.

These are same-host editor measurements, not native C++ timings or target-player results. They
include managed comparer and runtime costs, and do not establish IL2CPP behavior, a larger-record
crossover, or a universal speedup. The allocating positive control returned zero from
`GC.GetAllocatedBytesForCurrentThread`, so no allocation count is claimed. Each final integer
replica equals the framework sort output; each final wide replica preserves its complete tuple
at its original 64-bit identity and has ordered keys. Historical wide timing validation was weaker,
as stated in those datasets; separate NUnit regressions verify full payload preservation.

### Reproduce the comparison

From a checkout containing the recorded baseline commit, use Python 3.10 or later and the
probe generator at `scripts/benchmarks~/generate-jesse-parity.py`. It writes renamed copies
of both implementations and identical helper bodies into an ignored hidden probe, leaving
production sources untouched. Its shared indexer-loop writeback adapter reproduces the measured
list clones, rather than the production bulk writeback:

```bash
python3 scripts/benchmarks~/generate-jesse-parity.py --baseline 99f0520c
```

In the Unity project containing this package, call the Unity MCP `run_script` tool with these
parameters. `args` is a JSON-encoded array of five arguments: shape, element count, list backing,
first batch, and batch count.

```json
{
  "file": "Packages/com.wallstop-studios.unity-helpers/progress/.jesse-benchmark/JessePairedNative.cs",
  "entry": "WallstopStudios.UnityHelpers.Core.Extension.JesseParityHarness.Main",
  "args": "[\"Random\",100000,false,0,8]",
  "timeout_ms": 50000
}
```

Repeat for the shapes and backings in the final dataset; pass `true` for list cells. To generate
the two wide cells, run:

```bash
python3 scripts/benchmarks~/generate-jesse-parity.py --baseline 99f0520c --wide
```

Use the same `file` and five-argument format, change `entry` to
`WallstopStudios.UnityHelpers.Core.Extension.JesseWideParityHarness.Main`, and select `Random`
or `AlternatingDuplicates` with `false` for list backing. `--struct-comparer` selects the concrete
struct comparer for additional wide comparisons. To replay the faithful reference instead of the
working-tree candidate, add `--candidate-ref 4ec12c5d` to either generator command. Preserve every
returned slot and emitted source hash. Compare source hashes with the dataset before interpreting
a replay; normalized LF hashes are also recorded to distinguish line-ending changes. Packaged replay templates add license headers,
formatting, and a separate comparer file, so their template hashes differ from the original
measured templates as explained in each dataset.

To compare the faithful index route against the final direct-value implementation, use the faithful
commit as the baseline:

```bash
python3 scripts/benchmarks~/generate-jesse-parity.py --baseline 4ec12c5d --wide
```

Run both wide shapes as above, then add `--struct-comparer` and repeat for the concrete comparer.
The historical isolated dataset disabled only the index predicate; this replay compares the
anchored faithful source with the final source that removes that route.

The [tracking issue](https://github.com/Ambiguous-Interactive/unity-helpers/issues/747) records the
upstream comparison. The historical Jesse columns below measure earlier C# implementations,
rather than this live-phase revision.

## Where the Time Actually Goes

Every algorithm here sorts a `T[]`, never an `IList<T>` directly. Reaching an element through the
`IList<T>` indexer is an interface call, and a sort makes O(n log n) of them; copying a list into a
pooled array and copying it back is 2n moves and then the whole sort runs on direct array indexing.
Pass a `T[]` and it is sorted in place with no copy at all.

Measured on .NET 9 with a struct comparer, sorting `int`, best of nine runs, the same source before
and after the change:

| Algorithm | Shape         |       n | `T[]` before | `T[]` after | `List<T>` before | `List<T>` after |
| --------- | ------------- | ------: | -----------: | ----------: | ---------------: | --------------: |
| Grail     | shuffled      | 100,000 |     13.32 ms | **5.29 ms** |          6.28 ms |         5.39 ms |
| Tim       | shuffled      | 100,000 |     11.85 ms | **4.55 ms** |          5.66 ms |         4.65 ms |
| Grail     | nearly sorted | 100,000 |      5.65 ms | **1.42 ms** |          2.14 ms |         1.51 ms |
| Grail     | reversed      | 100,000 |      5.84 ms | **1.07 ms** |          1.98 ms |         1.13 ms |
| Tim       | reversed      | 100,000 |      0.38 ms | **0.06 ms** |          0.08 ms |         0.11 ms |

The one shape that pays rather than gains is a `List<T>` an adaptive sort would finish in O(n)
anyway: there the copy is most of the work, and it costs tens of microseconds on 100,000 elements.
These are desktop CLR numbers, where the JIT can speculatively devirtualize `List<T>`; a Unity player
cannot, so the run the Unity benchmark below produces is the one that describes a build.

### Why a `List<T>` is copied rather than sorted where it lies

Copying looks like the wasteful option and is not. Sorting a `List<T>` in place was measured against
copying it, using a struct accessor so the in-place path paid no interface dispatch at all, the best
case an in-place sort can have:

| Shape         |       n | Sorted in place | Copied, sorted, copied back | Array sorted directly |
| ------------- | ------: | --------------: | --------------------------: | --------------------: |
| shuffled      | 100,000 |        398.3 ms |                **258.5 ms** |              258.7 ms |
| nearly sorted | 100,000 |         72.0 ms |                 **47.9 ms** |               47.7 ms |
| reversed      | 100,000 |        788.2 ms |                **517.4 ms** |              531.0 ms |

A sort makes O(n log n) element accesses and a copy is O(n) contiguous bytes, so paying a slightly
dearer access n log n times to save 2n copies loses at every size and shape measured. The right-hand
columns are the same to within noise, which is the point: **the copy costs nothing measurable, and
the array accesses inside the sort are what the whole exercise is buying.**

Both directions of that copy are bulk operations. `CopyTo` is on `ICollection<T>`, so reading is one
`Array.Copy` for any list that implements it sensibly. Writing back is one `Array.Copy` for a
`List<T>` (`AddRange` takes its `ICollection<T>` fast path for an `ArraySegment<T>`), which is 4.4x
to 13x faster than assigning through the indexer:

|         n | Indexer loop | `Clear` + `AddRange` |
| --------: | -----------: | -------------------: |
|   100,000 |     0.065 ms |         **0.005 ms** |
| 1,000,000 |     0.723 ms |         **0.164 ms** |

So: a `T[]` is sorted where it lies, a `List<T>` moves in and out in two bulk copies, and any other
`IList<T>` reads in bulk and writes back through its indexer, because that is all the interface
offers.

## Bulk Operations on a List

Sorting is not the only `IList<T>` operation that was reaching every element through an interface
call. The same measurement was repeated for the rest of them, and it splits cleanly in two.

**An operation that always touches the whole range can afford a copy, and often does not need one,
because the BCL already has a bulk primitive for it.** `Reverse` and `Fill` take `Array.Reverse` and
`Array.Fill`; `List<T>` carries its own `Reverse(index, count)`. `Shift` stopped reversing anything:
a rotation is two contiguous runs of the input, so the copy is written back in two `Array.Copy`
calls rather than three reversal passes.

**An operation that can stop early must never copy.** `IndexOf` and `LastIndexOf` with a predicate
return at the first match, and a copy would have read every remaining element before the predicate
ran once. They get the free half of the change (direct indexing when the list already is a `T[]`)
and nothing else.

Measured on .NET 9, `int` elements, best of nine runs, the same sources before and after. The
`IList<T>` column is a list that is neither a `T[]` nor a `List<T>`, measured against two
implementations so the JIT cannot prove the receiver's type and devirtualize the indexer; a first
pass that used one sealed class reported a 4x _regression_ that did not exist:

| Operation            | n       |  `T[]` | `List<T>` | `IList<T>` |
| -------------------- | ------- | -----: | --------: | ---------: |
| `Reverse`            | 1,000   | 39.79x |    29.58x |      1.00x |
| `Reverse`            | 100,000 | 30.45x |    28.76x |      1.04x |
| `Shift`              | 1,000   | 30.63x |    43.19x |      4.06x |
| `Shift`              | 100,000 | 32.81x |    35.76x |      3.52x |
| `Fill`               | 1,000   | 38.00x |    17.88x |      1.74x |
| `Fill`               | 100,000 | 24.20x |    10.15x |      1.76x |
| `Shuffle`            | 1,000   |  2.06x |     2.04x |      1.61x |
| `Shuffle`            | 100,000 |  2.01x |     1.98x |      1.54x |
| `IndexOf(predicate)` | 100,000 |  3.07x |     2.37x |      2.41x |

`Reverse` on an `IList<T>` is unchanged by design: a partial range has no bulk write-back, and
copying the whole list to reverse a few elements of it would be a pessimization.

Some of the `IList<T>` column is not the copy at all. `Count` was being read on every iteration of
every loop (one interface call per element, for a value that cannot change) and hoisting it alone
is worth 1.26x to 1.76x. That accounts for the whole of `Fill(value)`'s gain there, which is why it
copies only for a list that offers bulk replacement and runs a plain hoisted loop for anything else.

**A value that cannot change, except where it can.** The methods that take a `Func<>` (`Fill(factory)`,
`IndexOf`, `LastIndexOf`, `FindAll`, `Partition`) deliberately keep re-reading `Count` and give up
that 1.26x to 1.76x. A caller's factory or predicate can remove elements from the list it is being run
over, and a hoisted bound then indexes past the end of a shorter list: an `ArgumentOutOfRangeException`
out of a public API, where the loop used to stop. Their array fast paths still hoist, because an array
cannot change length underneath one.

## Dataset Scenarios

- **Sorted** – ascending integers, verifying best-case behavior.
- **Nearly Sorted (2% swaps)** – deterministic neighbor swaps introduce light disorder to expose adaptive optimizations.
- **Shuffled (deterministic)** – Fisher–Yates shuffle using a fixed seed for reproducibility across runs and machines.

Each benchmark sorts a fresh copy of the dataset once and reports wall-clock duration. A cell reading `pending` means nobody has run this suite on that operating system, not that the algorithm is slow there.

## Windows (Editor/Player)

<!-- ILIST_SORT_WINDOWS_START -->

Last updated 2026-09-14 05:19 UTC on Windows 11 (10.0.26200).

Times are single-pass measurements in milliseconds (lower is better). `n/a` indicates the algorithm was skipped for the dataset size.

### Sorted

<table data-sortable>
  <thead>
    <tr>
      <th align="left">List Size</th>
      <th align="right">Ghost</th>
      <th align="right">Meteor</th>
      <th align="right">Pattern-Defeating QuickSort</th>
      <th align="right">Grail</th>
      <th align="right">Power</th>
      <th align="right">Insertion</th>
      <th align="right">Tim</th>
      <th align="right">Jesse</th>
      <th align="right">Green</th>
      <th align="right">Ska</th>
      <th align="right">Ipn</th>
      <th align="right">Smooth</th>
      <th align="right">Block</th>
      <th align="right">IPS4o</th>
      <th align="right">Power+</th>
      <th align="right">Glide</th>
      <th align="right">Flux</th>
      <th align="right">Yam</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">100</td><td align="right">0.004 ms</td><td align="right">0.001 ms</td><td align="right">0.001 ms</td><td align="right">0.001 ms</td><td align="right">0.001 ms</td><td align="right">0.000 ms</td><td align="right">0.001 ms</td><td align="right">0.000 ms</td><td align="right">0.000 ms</td><td align="right">0.002 ms</td><td align="right">0.001 ms</td><td align="right">0.001 ms</td><td align="right">0.000 ms</td><td align="right">0.001 ms</td><td align="right">0.000 ms</td><td align="right">0.001 ms</td><td align="right">0.001 ms</td><td align="right">0.000 ms</td></tr>
    <tr><td align="left">1,000</td><td align="right">0.008 ms</td><td align="right">0.010 ms</td><td align="right">0.004 ms</td><td align="right">0.004 ms</td><td align="right">0.003 ms</td><td align="right">0.002 ms</td><td align="right">0.003 ms</td><td align="right">0.002 ms</td><td align="right">0.002 ms</td><td align="right">0.032 ms</td><td align="right">0.004 ms</td><td align="right">0.008 ms</td><td align="right">0.003 ms</td><td align="right">0.029 ms</td><td align="right">0.003 ms</td><td align="right">0.003 ms</td><td align="right">0.015 ms</td><td align="right">0.003 ms</td></tr>
    <tr><td align="left">10,000</td><td align="right">0.099 ms</td><td align="right">0.134 ms</td><td align="right">0.039 ms</td><td align="right">0.036 ms</td><td align="right">0.021 ms</td><td align="right">0.021 ms</td><td align="right">0.019 ms</td><td align="right">0.019 ms</td><td align="right">0.023 ms</td><td align="right">0.444 ms</td><td align="right">0.039 ms</td><td align="right">0.078 ms</td><td align="right">0.025 ms</td><td align="right">0.493 ms</td><td align="right">0.023 ms</td><td align="right">0.021 ms</td><td align="right">0.203 ms</td><td align="right">0.024 ms</td></tr>
    <tr><td align="left">100,000</td><td align="right">1.19 ms</td><td align="right">1.75 ms</td><td align="right">0.395 ms</td><td align="right">0.352 ms</td><td align="right">0.206 ms</td><td align="right">n/a</td><td align="right">0.204 ms</td><td align="right">0.187 ms</td><td align="right">0.248 ms</td><td align="right">5.53 ms</td><td align="right">0.393 ms</td><td align="right">0.764 ms</td><td align="right">0.243 ms</td><td align="right">6.18 ms</td><td align="right">0.205 ms</td><td align="right">0.187 ms</td><td align="right">2.62 ms</td><td align="right">0.257 ms</td></tr>
  </tbody>
</table>

### Nearly Sorted (2% swaps)

<table data-sortable>
  <thead>
    <tr>
      <th align="left">List Size</th>
      <th align="right">Ghost</th>
      <th align="right">Meteor</th>
      <th align="right">Pattern-Defeating QuickSort</th>
      <th align="right">Grail</th>
      <th align="right">Power</th>
      <th align="right">Insertion</th>
      <th align="right">Tim</th>
      <th align="right">Jesse</th>
      <th align="right">Green</th>
      <th align="right">Ska</th>
      <th align="right">Ipn</th>
      <th align="right">Smooth</th>
      <th align="right">Block</th>
      <th align="right">IPS4o</th>
      <th align="right">Power+</th>
      <th align="right">Glide</th>
      <th align="right">Flux</th>
      <th align="right">Yam</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">100</td><td align="right">0.001 ms</td><td align="right">0.001 ms</td><td align="right">0.001 ms</td><td align="right">0.001 ms</td><td align="right">0.002 ms</td><td align="right">0.000 ms</td><td align="right">0.001 ms</td><td align="right">0.004 ms</td><td align="right">0.000 ms</td><td align="right">0.002 ms</td><td align="right">0.001 ms</td><td align="right">0.001 ms</td><td align="right">0.000 ms</td><td align="right">0.001 ms</td><td align="right">0.002 ms</td><td align="right">0.001 ms</td><td align="right">0.001 ms</td><td align="right">0.000 ms</td></tr>
    <tr><td align="left">1,000</td><td align="right">0.008 ms</td><td align="right">0.010 ms</td><td align="right">0.018 ms</td><td align="right">0.004 ms</td><td align="right">0.009 ms</td><td align="right">0.002 ms</td><td align="right">0.007 ms</td><td align="right">0.031 ms</td><td align="right">0.003 ms</td><td align="right">0.034 ms</td><td align="right">0.016 ms</td><td align="right">0.008 ms</td><td align="right">0.003 ms</td><td align="right">0.033 ms</td><td align="right">0.016 ms</td><td align="right">0.007 ms</td><td align="right">0.015 ms</td><td align="right">0.003 ms</td></tr>
    <tr><td align="left">10,000</td><td align="right">0.101 ms</td><td align="right">0.137 ms</td><td align="right">0.243 ms</td><td align="right">0.045 ms</td><td align="right">0.146 ms</td><td align="right">0.023 ms</td><td align="right">0.112 ms</td><td align="right">0.336 ms</td><td align="right">0.026 ms</td><td align="right">0.451 ms</td><td align="right">0.233 ms</td><td align="right">0.089 ms</td><td align="right">0.036 ms</td><td align="right">0.521 ms</td><td align="right">0.276 ms</td><td align="right">0.077 ms</td><td align="right">0.205 ms</td><td align="right">0.026 ms</td></tr>
    <tr><td align="left">100,000</td><td align="right">1.22 ms</td><td align="right">1.78 ms</td><td align="right">3.02 ms</td><td align="right">0.419 ms</td><td align="right">2.51 ms</td><td align="right">n/a</td><td align="right">1.53 ms</td><td align="right">3.47 ms</td><td align="right">0.297 ms</td><td align="right">5.55 ms</td><td align="right">2.87 ms</td><td align="right">0.802 ms</td><td align="right">0.304 ms</td><td align="right">6.77 ms</td><td align="right">4.01 ms</td><td align="right">1.54 ms</td><td align="right">2.69 ms</td><td align="right">0.271 ms</td></tr>
  </tbody>
</table>

### Shuffled (deterministic)

<table data-sortable>
  <thead>
    <tr>
      <th align="left">List Size</th>
      <th align="right">Ghost</th>
      <th align="right">Meteor</th>
      <th align="right">Pattern-Defeating QuickSort</th>
      <th align="right">Grail</th>
      <th align="right">Power</th>
      <th align="right">Insertion</th>
      <th align="right">Tim</th>
      <th align="right">Jesse</th>
      <th align="right">Green</th>
      <th align="right">Ska</th>
      <th align="right">Ipn</th>
      <th align="right">Smooth</th>
      <th align="right">Block</th>
      <th align="right">IPS4o</th>
      <th align="right">Power+</th>
      <th align="right">Glide</th>
      <th align="right">Flux</th>
      <th align="right">Yam</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">100</td><td align="right">0.003 ms</td><td align="right">0.003 ms</td><td align="right">0.002 ms</td><td align="right">0.003 ms</td><td align="right">0.004 ms</td><td align="right">0.006 ms</td><td align="right">0.004 ms</td><td align="right">0.007 ms</td><td align="right">0.003 ms</td><td align="right">0.003 ms</td><td align="right">0.003 ms</td><td align="right">0.004 ms</td><td align="right">0.003 ms</td><td align="right">0.002 ms</td><td align="right">0.021 ms</td><td align="right">0.005 ms</td><td align="right">0.003 ms</td><td align="right">0.003 ms</td></tr>
    <tr><td align="left">1,000</td><td align="right">0.065 ms</td><td align="right">0.065 ms</td><td align="right">0.050 ms</td><td align="right">0.067 ms</td><td align="right">0.069 ms</td><td align="right">0.523 ms</td><td align="right">0.069 ms</td><td align="right">0.095 ms</td><td align="right">0.057 ms</td><td align="right">0.057 ms</td><td align="right">0.052 ms</td><td align="right">0.089 ms</td><td align="right">0.058 ms</td><td align="right">0.077 ms</td><td align="right">0.386 ms</td><td align="right">0.070 ms</td><td align="right">0.053 ms</td><td align="right">0.053 ms</td></tr>
    <tr><td align="left">10,000</td><td align="right">0.960 ms</td><td align="right">0.983 ms</td><td align="right">0.701 ms</td><td align="right">0.949 ms</td><td align="right">0.998 ms</td><td align="right">53.4 ms</td><td align="right">0.912 ms</td><td align="right">1.26 ms</td><td align="right">0.856 ms</td><td align="right">0.815 ms</td><td align="right">0.721 ms</td><td align="right">1.26 ms</td><td align="right">0.792 ms</td><td align="right">1.24 ms</td><td align="right">5.43 ms</td><td align="right">0.902 ms</td><td align="right">0.814 ms</td><td align="right">0.893 ms</td></tr>
    <tr><td align="left">100,000</td><td align="right">14.3 ms</td><td align="right">13.4 ms</td><td align="right">9.37 ms</td><td align="right">12.7 ms</td><td align="right">13.0 ms</td><td align="right">n/a</td><td align="right">12.1 ms</td><td align="right">15.4 ms</td><td align="right">11.8 ms</td><td align="right">11.1 ms</td><td align="right">9.34 ms</td><td align="right">17.0 ms</td><td align="right">10.6 ms</td><td align="right">18.0 ms</td><td align="right">73.5 ms</td><td align="right">12.4 ms</td><td align="right">10.3 ms</td><td align="right">12.1 ms</td></tr>
  </tbody>
</table>

<!-- ILIST_SORT_WINDOWS_END -->

## macOS

<!-- ILIST_SORT_MACOS_START -->

Pending: run the IList sorting benchmark suite on macOS to capture results.

<!-- ILIST_SORT_MACOS_END -->

## Linux

<!-- ILIST_SORT_LINUX_START -->

Pending: run the IList sorting benchmark suite on Linux to capture results.

<!-- ILIST_SORT_LINUX_END -->

## Other Platforms

<!-- ILIST_SORT_OTHER_START -->

Pending: run the IList sorting benchmark suite on the target platform to capture results.

<!-- ILIST_SORT_OTHER_END -->

## Refreshing these numbers

Run `IListSortingPerformanceTests.Benchmark` from Unity's Test Runner. It rewrites the section matching the operating system it ran on and leaves the others alone.
