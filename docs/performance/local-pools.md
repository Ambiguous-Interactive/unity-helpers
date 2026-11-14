# Local pool costs

`LocalPool<T>` provides explicit rent/return for a caller that owns all access. Its optional
disposable lease uses the existing shared disposal tracker. The [pooling guide](../features/utilities/pooling-guide.md#single-owner-pools)
describes return ownership and thread-local array behavior.

Three fresh Unity 6000.6.7f1 Windows x64 Editor Mono processes compare a warmed `Stack<object>`,
plain local rent/return, local disposable leases, and `WallstopGenericPool<object>` leases. Each
arm warms 64 complete loops, calibrates to at least twenty milliseconds, and runs 21 rotated
rounds. Checksums and a positive elapsed-clock control must pass. No other owned compilation,
documentation build or benchmark overlaps these captures.

| Process |     Stack | Local rent/return | Local lease | Shared lease |
| ------: | --------: | ----------------: | ----------: | -----------: |
|       1 | 25.081 ns |         25.583 ns |   80.706 ns |   399.400 ns |
|       2 | 24.631 ns |         25.471 ns |   79.445 ns |   401.997 ns |
|       3 | 24.921 ns |         24.770 ns |   78.811 ns |   387.827 ns |

Cells are nanoseconds per rent/return pair and medians of the 21 observations in that process. The plain local path is close to the
stack control; leases add measurable disposal-tracking work. These observations cover this
runtime and warmed object reuse only, without array-clearing costs, contention, player builds
or general application throughput. They do not establish equivalent results on other runtimes.

Each process also passes all 32 functional and allocation-control cases, including nested
rentals, copied/stale leases, reset failures, thread isolation, thread transfer and disposal.
Positive allocation controls accompany the four warmed zero-allocation observations. All three
processes return raw exit zero, reach both stream ends without a drain stop, retain identical
source hashes and print the runtime module identity matching the captured DLL.

The [replay archive](https://raw.githubusercontent.com/Ambiguous-Interactive/unity-helpers/587d3de6fde16c76a0ba256872cfe8c800bf1287/docs/performance/local-pool-runtime-qualification.zip) contains frozen inputs, controllers,
all 252 timing rows, complete NUnit trees and process receipts. It also contains the byte-format
sprite correctness controls and additional Editor lifecycle experiment. Replay each arm with its
named source snapshot and preserve new process identities separately.

## Shared buffer synchronization

A separate six-process A–B–B–A–A–B campaign compares main's shared pool implementation with a
single synchronization root for storage, usage tracking and rolling samples. Producers and
user callbacks still run outside that lock. The four shared paths improve by 21.3% in geometric
mean across the paired observations. These are warmed Unity 6000.6.7f1 Windows Editor Mono
measurements, with the same calibration and 21-round protocol described above.

| Path             | Baseline median | Candidate median | Paired ratios          |
| ---------------- | --------------: | ---------------: | ---------------------- |
| Stack            |       25.064 ns |        25.215 ns | 1.0236, 1.0061, 0.9965 |
| Local            |       25.831 ns |        26.355 ns | 1.0120, 1.0197, 1.1000 |
| LocalLease       |       80.801 ns |        80.649 ns | 1.0105, 0.9934, 0.9930 |
| SharedLease      |      406.001 ns |       315.712 ns | 0.7842, 0.7787, 0.7646 |
| SharedList       |      406.245 ns |       313.817 ns | 0.7831, 0.7661, 0.7845 |
| SharedHashSet    |      429.781 ns |       339.899 ns | 0.8111, 0.7859, 0.7930 |
| SharedDictionary |      435.934 ns |       345.981 ns | 0.8160, 0.7937, 0.7803 |

All seven paths are shown, including unchanged stack and local controls. One local control pair
is 10.001% slower; it is outside the four-path shared acceptance scope and is retained here.
Every shared pair improves, with a worst ratio of 0.8161. The preregistered rule required at
least 5% shared geometric-mean benefit and no shared pair over 1.10. Each process passed all
33 functional and allocation controls, emitted 147 timing rows, returned raw zero, reached
both stream ends and matched its loaded runtime module to the captured DLL.

The [shared buffer qualification record](./shared-buffer-qualification.csv) contains all 882
timing observations, process receipts, case results and source/module hashes. The measured
snapshot precedes the unrelated coalesced-file-writer addition. The pool implementation is
unchanged in the final runtime validation, which passes 866 cases with four existing skips.
Raw NUnit trees, controllers and frozen source inputs remain in ignored `progress/`.
These results do not qualify contention, player builds or another runtime.

## November 14 follow-up

Two further shared-pool experiments were rejected. Caching the current rolling-history bucket
produced a candidate/baseline geometric ratio of 0.9994 across shared paths; moving statistics
increments under the existing pool lock produced 1.0058. Neither met the preregistered 0.95
threshold, so neither implementation is retained. These results do not establish a speedup.

Each experiment used six fresh Unity 6000.6.7f1 Editor Mono processes in A B B A A B order, 64 warmups,
21 rotated rounds and calibrated samples of at least 20 ms. All processes completed their 33
functional/allocation cases and all 147 timing observations, with source identity checks,
loaded-module and DLL identities, zero process exits and both output streams drained. The
[full observations](./local-pools-20261114.csv) and [identities and all paired ratios](./local-pools-20261114.json)
include controls: the bucket-cache experiment's first Stack control ratio was about 1.136,
so its apparently faster individual paths should not be isolated as evidence of improvement.

The statistics review also found a correctness defect: a reentrant time-provider callback could
rent an item after the pool size was captured. Statistics now invoke that callback before
capturing the size, preserving a coherent snapshot for that callback's completed rental.
Further performance work remains tracked in [#1173](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1173).

## Direct clock conversion

The shared pools now sample the Stopwatch counter directly and convert elapsed counter ticks to
seconds. Custom time providers and separate origins for each pool type keep their existing behavior.
The ordinary native timestamp and pool cohort passes 334 cases with four existing skips.

Six fresh Windows Unity 6000.6.7f1 Editor Mono processes in A B B A A B order retain the change.
Across all three pairs and the four shared paths, the geometric candidate/baseline ratio is 0.946566,
meeting the preregistered 0.95 limit. Every shared pair improves; individual ratios range from
0.90836 to 0.97691. No control repeats a regression above 1.10. All 33 functional cases and 147
observations pass per process, with matching loaded-module/DLL identities, stable selected sources,
raw zero exits and both output streams drained.

Median process medians, nanoseconds per rent/return pair:

| Path                |  Before |   After |
| ------------------- | ------: | ------: |
| Stack control       |  26.125 |  25.850 |
| Local control       |  26.433 |  26.039 |
| Local lease control |  87.024 |  82.765 |
| Shared lease        | 324.110 | 309.534 |
| Shared list         | 331.181 | 308.434 |
| Shared hash set     | 360.014 | 338.159 |
| Shared dictionary   | 356.078 | 341.737 |

Each cell uses 64 warm calls and 21 rotated rounds. Batches were calibrated for 20 ms; the fastest
observed batch was 19.7063 ms. The local-lease control also varied, including one 10.7% faster pair;
these controls remain in the record. This is a warmed helper observation, not player, contention,
ARM or whole-application timing qualification.

[All 882 observations](./local-pools-clock-20261114.csv) and the
[selection criteria, pair ratios and source/module/process identities](./local-pools-clock-20261114.json)
are retained. Raw NUnit trees, process receipts and runtime DLLs remain in ignored session evidence.
