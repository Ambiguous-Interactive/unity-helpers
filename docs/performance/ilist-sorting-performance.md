# IList Sorting Performance Benchmarks

Unity Helpers ships several custom sorting algorithms for `IList<T>` that cover different trade-offs between adaptability, allocation patterns, and stability. This page gathers context and benchmark snapshots so you can choose the right algorithm for your workload and compare results across operating systems.

## Algorithm Cheatsheet

| Algorithm                   | Stable? | Best For                                                                   | Reference                                                                                                 |
| --------------------------- | ------- | -------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| Ghost Sort                  | No      | Mixed workloads that benefit from adaptive gap sorting and few allocations | Upstream project by Will Stafford Parsons (public repository currently offline)                           |
| Meteor Sort                 | No      | Almost-sorted data where gap shrinking beats plain insertion sort          | Upstream project by Will Stafford Parsons (public repository currently offline)                           |
| Pattern-Defeating QuickSort | No      | General-purpose quicksort with protections against worst-case inputs       | [pdqsort by Orson Peters](https://github.com/orlp/pdqsort)                                                |
| Grail Sort                  | Yes     | Large datasets where stability + low allocations matter                    | [GrailSort](https://github.com/Mrrl/GrailSort)                                                            |
| Power Sort                  | Yes     | Partially ordered data that benefits from adaptive run detection           | [PowerSort (Munro & Wild)](https://arxiv.org/abs/1805.04154)                                              |
| Tim Sort                    | Yes     | General-purpose stable sorting with abundant natural runs                  | [Wikipedia - Timsort](https://en.wikipedia.org/wiki/Timsort)                                              |
| Jesse Sort                  | No      | Data with long runs or duplicates where dual patience piles shine          | [JesseSort](https://github.com/lewj85/jessesort)                                                          |
| Green Sort                  | Yes     | Sustainable stable merges that trim ordered prefixes                       | [greeNsort](https://www.greensort.org/index.html)                                                         |
| Ska Sort                    | No      | Branch-friendly partitioning on large unstable datasets                    | [Ska Sort](https://probablydance.com/2016/12/27/i-wrote-a-faster-sorting-algorithm/)                      |
| Ipn Sort                    | No      | In-place adaptive quicksort scenarios needing robust pivots                | [ipnsort write-up](https://github.com/Voultapher/sort-research-rs/tree/main/writeup/ipnsort_introduction) |
| Smooth Sort                 | No      | Weak-heap hybrid that approaches O(n) for presorted data                   | [Smoothsort - Wikipedia](https://en.wikipedia.org/wiki/Smoothsort)                                        |
| Block Merge Sort            | Yes     | Stable merges with √n buffer (WikiSort style)                              | [WikiSort](https://github.com/BonzaiThePenguin/WikiSort)                                                  |
| IPS⁴o Sort                  | No      | Cache-aware samplesort with multiway partitioning                          | [IPS⁴o paper](https://arxiv.org/abs/1705.02257)                                                           |
| Power Sort Plus             | Yes     | Enhanced run-priority merges inspired by Wild & Nebel                      | [PowerSort paper](https://arxiv.org/abs/1805.04154)                                                       |
| Glide Sort                  | Yes     | Stable galloping merges from the Rust glidesort research                   | [sort-research-rs](https://github.com/Voultapher/sort-research-rs)                                        |
| Flux Sort                   | No      | Dual-pivot quicksort tuned for modern CPUs                                 | [sort-research-rs](https://github.com/Voultapher/sort-research-rs)                                        |
| Insertion Sort              | Yes     | Tiny or nearly sorted collections where O(n²) is acceptable                | [Wikipedia - Insertion sort](https://en.wikipedia.org/wiki/Insertion_sort)                                |

> **What does “stable” mean?** Stable sorting algorithms preserve the relative order of elements that compare as equal. This matters when items carry secondary keys (e.g., sorting people by last name but keeping first-name order deterministic). Unstable algorithms can reshuffle equal entries, which is usually fine for numeric keys but can break deterministic pipelines.
>
> **Heads up:** The original Ghost Sort repository was formerly hosted on GitHub under `wstaffordp/ghostsort`, but it currently returns 404. The Unity Helpers implementation remains based on that source; we will relink if/when an official mirror returns.

## Dataset Scenarios

- **Sorted** – ascending integers, verifying best-case behavior.
- **Nearly Sorted (2% swaps)** – deterministic neighbor swaps introduce light disorder to expose adaptive optimizations.
- **Shuffled (deterministic)** – Fisher–Yates shuffle using a fixed seed for reproducibility across runs and machines.

Each benchmark sorts a fresh copy of the dataset once and reports wall-clock duration. If a cell still shows `pending`, re-run the benchmark suite to collect fresh data for that algorithm/dataset size.

Run the `IListSortingPerformanceTests.Benchmark` test inside Unity’s Test Runner to refresh the tables below. Results automatically land in the section that matches the current operating system.

## Windows (Editor/Player)

<!-- ILIST_SORT_WINDOWS_START -->

Last updated 2026-05-08 04:05 UTC on Windows 11 (10.0.26200) 64bit.

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
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">100</td><td align="right">0.005 ms</td><td align="right">0.002 ms</td><td align="right">0.001 ms</td><td align="right">0.001 ms</td><td align="right">0.002 ms</td><td align="right">0.001 ms</td><td align="right">0.001 ms</td><td align="right">0.093 ms</td><td align="right">0.001 ms</td><td align="right">0.006 ms</td><td align="right">0.001 ms</td><td align="right">0.002 ms</td><td align="right">0.001 ms</td><td align="right">0.001 ms</td><td align="right">0.001 ms</td><td align="right">0.002 ms</td><td align="right">0.002 ms</td></tr>
    <tr><td align="left">1,000</td><td align="right">0.026 ms</td><td align="right">0.031 ms</td><td align="right">0.009 ms</td><td align="right">0.007 ms</td><td align="right">0.006 ms</td><td align="right">0.007 ms</td><td align="right">0.006 ms</td><td align="right">1.16 ms</td><td align="right">0.007 ms</td><td align="right">0.110 ms</td><td align="right">0.009 ms</td><td align="right">0.019 ms</td><td align="right">0.006 ms</td><td align="right">0.038 ms</td><td align="right">0.006 ms</td><td align="right">0.006 ms</td><td align="right">0.032 ms</td></tr>
    <tr><td align="left">10,000</td><td align="right">0.324 ms</td><td align="right">0.443 ms</td><td align="right">0.090 ms</td><td align="right">0.071 ms</td><td align="right">0.055 ms</td><td align="right">0.069 ms</td><td align="right">0.053 ms</td><td align="right">14.4 ms</td><td align="right">0.071 ms</td><td align="right">1.56 ms</td><td align="right">0.091 ms</td><td align="right">0.190 ms</td><td align="right">0.059 ms</td><td align="right">0.616 ms</td><td align="right">0.055 ms</td><td align="right">0.054 ms</td><td align="right">0.434 ms</td></tr>
    <tr><td align="left">100,000</td><td align="right">3.93 ms</td><td align="right">5.78 ms</td><td align="right">0.894 ms</td><td align="right">0.703 ms</td><td align="right">0.542 ms</td><td align="right">n/a</td><td align="right">0.527 ms</td><td align="right">181 ms</td><td align="right">0.702 ms</td><td align="right">20.2 ms</td><td align="right">0.897 ms</td><td align="right">1.91 ms</td><td align="right">0.597 ms</td><td align="right">7.46 ms</td><td align="right">0.545 ms</td><td align="right">0.530 ms</td><td align="right">5.50 ms</td></tr>
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
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">100</td><td align="right">0.002 ms</td><td align="right">0.002 ms</td><td align="right">0.002 ms</td><td align="right">0.001 ms</td><td align="right">0.002 ms</td><td align="right">0.001 ms</td><td align="right">0.002 ms</td><td align="right">0.099 ms</td><td align="right">0.001 ms</td><td align="right">0.006 ms</td><td align="right">0.002 ms</td><td align="right">0.002 ms</td><td align="right">0.001 ms</td><td align="right">0.002 ms</td><td align="right">0.002 ms</td><td align="right">0.002 ms</td><td align="right">0.002 ms</td></tr>
    <tr><td align="left">1,000</td><td align="right">0.026 ms</td><td align="right">0.031 ms</td><td align="right">0.037 ms</td><td align="right">0.009 ms</td><td align="right">0.019 ms</td><td align="right">0.008 ms</td><td align="right">0.015 ms</td><td align="right">1.20 ms</td><td align="right">0.007 ms</td><td align="right">0.110 ms</td><td align="right">0.034 ms</td><td align="right">0.019 ms</td><td align="right">0.008 ms</td><td align="right">0.047 ms</td><td align="right">0.025 ms</td><td align="right">0.016 ms</td><td align="right">0.034 ms</td></tr>
    <tr><td align="left">10,000</td><td align="right">0.326 ms</td><td align="right">0.444 ms</td><td align="right">0.508 ms</td><td align="right">0.081 ms</td><td align="right">0.258 ms</td><td align="right">0.071 ms</td><td align="right">0.192 ms</td><td align="right">14.1 ms</td><td align="right">0.078 ms</td><td align="right">1.57 ms</td><td align="right">0.466 ms</td><td align="right">0.198 ms</td><td align="right">0.079 ms</td><td align="right">0.721 ms</td><td align="right">0.403 ms</td><td align="right">0.181 ms</td><td align="right">0.440 ms</td></tr>
    <tr><td align="left">100,000</td><td align="right">3.99 ms</td><td align="right">5.88 ms</td><td align="right">6.07 ms</td><td align="right">0.820 ms</td><td align="right">4.06 ms</td><td align="right">n/a</td><td align="right">2.75 ms</td><td align="right">189 ms</td><td align="right">0.802 ms</td><td align="right">20.3 ms</td><td align="right">5.74 ms</td><td align="right">1.96 ms</td><td align="right">0.720 ms</td><td align="right">8.66 ms</td><td align="right">5.75 ms</td><td align="right">2.43 ms</td><td align="right">5.58 ms</td></tr>
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
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">100</td><td align="right">0.010 ms</td><td align="right">0.008 ms</td><td align="right">0.006 ms</td><td align="right">0.007 ms</td><td align="right">0.008 ms</td><td align="right">0.018 ms</td><td align="right">0.011 ms</td><td align="right">0.041 ms</td><td align="right">0.008 ms</td><td align="right">0.009 ms</td><td align="right">0.008 ms</td><td align="right">0.011 ms</td><td align="right">0.005 ms</td><td align="right">0.006 ms</td><td align="right">0.023 ms</td><td align="right">0.012 ms</td><td align="right">0.008 ms</td></tr>
    <tr><td align="left">1,000</td><td align="right">0.167 ms</td><td align="right">0.137 ms</td><td align="right">0.096 ms</td><td align="right">0.107 ms</td><td align="right">0.111 ms</td><td align="right">1.67 ms</td><td align="right">0.159 ms</td><td align="right">0.442 ms</td><td align="right">0.117 ms</td><td align="right">0.139 ms</td><td align="right">0.104 ms</td><td align="right">0.200 ms</td><td align="right">0.092 ms</td><td align="right">0.109 ms</td><td align="right">0.432 ms</td><td align="right">0.172 ms</td><td align="right">0.105 ms</td></tr>
    <tr><td align="left">10,000</td><td align="right">2.32 ms</td><td align="right">2.00 ms</td><td align="right">1.30 ms</td><td align="right">1.48 ms</td><td align="right">1.55 ms</td><td align="right">166 ms</td><td align="right">1.67 ms</td><td align="right">5.46 ms</td><td align="right">1.46 ms</td><td align="right">1.90 ms</td><td align="right">1.39 ms</td><td align="right">2.89 ms</td><td align="right">1.27 ms</td><td align="right">1.63 ms</td><td align="right">5.95 ms</td><td align="right">1.94 ms</td><td align="right">1.49 ms</td></tr>
    <tr><td align="left">100,000</td><td align="right">33.6 ms</td><td align="right">26.4 ms</td><td align="right">16.2 ms</td><td align="right">18.8 ms</td><td align="right">18.9 ms</td><td align="right">n/a</td><td align="right">21.8 ms</td><td align="right">68.2 ms</td><td align="right">18.9 ms</td><td align="right">24.0 ms</td><td align="right">17.0 ms</td><td align="right">36.9 ms</td><td align="right">15.8 ms</td><td align="right">20.1 ms</td><td align="right">79.3 ms</td><td align="right">26.2 ms</td><td align="right">18.8 ms</td></tr>
  </tbody>
</table>

<!-- ILIST_SORT_WINDOWS_END -->

## macOS

<!-- ILIST_SORT_MACOS_START -->

Pending — run the IList sorting benchmark suite on macOS to capture results.

<!-- ILIST_SORT_MACOS_END -->

## Linux

<!-- ILIST_SORT_LINUX_START -->

Pending — run the IList sorting benchmark suite on Linux to capture results.

<!-- ILIST_SORT_LINUX_END -->

## Other Platforms

<!-- ILIST_SORT_OTHER_START -->

Pending — run the IList sorting benchmark suite on the target platform to capture results.

<!-- ILIST_SORT_OTHER_END -->
