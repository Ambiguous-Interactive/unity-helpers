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

`JesseSort` adapts [Jesse Lew's dual-patience design](https://github.com/lewj85/jessesort).
It handles sorted, reverse-sorted, and equal input in linear time. Pile assignments are recorded,
reconstructed into contiguous ascending piles, then merged bottom-up, one adjacent pair at a time:
an ordered-boundary shortcut emits a single copy when two neighboring piles already read as one
sorted run, and a reverse-disjoint shortcut block-swaps the pair when every element of the right
pile precedes every element of the left one. Upstream alignment is still
in progress; the pairwise-merge adaptation's
[current measurements](https://github.com/Ambiguous-Interactive/unity-helpers/issues/747#issuecomment-5607805585)
are available in the tracking issue. The historical Jesse columns below measure the previous C#
port, and the tracking issue also holds the superseded numbers for the intermediate k-way merge
adaptation.

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
