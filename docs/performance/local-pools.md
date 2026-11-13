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

The [replay archive](./local-pool-runtime-qualification.zip) contains frozen inputs, controllers,
all 252 timing rows, complete NUnit trees and process receipts. It also contains the byte-format
sprite correctness controls and additional Editor lifecycle experiment. Replay each arm with its
named source snapshot and preserve new process identities separately.
