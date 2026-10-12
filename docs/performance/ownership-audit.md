# Disposal Ownership Audit

This audit supports [#924](https://github.com/Ambiguous-Interactive/unity-helpers/issues/924).
The [site inventory](./ownership-audit.csv) records 994 distinct ownership-producing sites across
206 runtime and editor files, with source hashes, compiler-bound producers and reviewed dispositions.
It records the current contracts and private-operation candidates; a candidate is not an adopted
optimization or a measured result.

## Coverage

The semantic inventory uses the shipped generator, Unity 6000.4.6 references and C# 9 over 45
assemblies in default and `SINGLE_THREADED` configurations. Both compilations have no errors or
missing inputs. Sites are deduplicated by source path and offset. It includes construction and
factories of package-owned disposable types, shared disposal handles, asynchronous lease results,
and internal private-array rentals. Compiler symbols determine the produced type.

A separate syntax scan of all conditional-symbol combinations in 762 runtime and editor C# files
finds 116 explicit `SystemArrayPool<T>.Get` calls: 97 runtime and 19 editor sites. Every one has a
semantic witness in the inventory. The inventory also includes the forwarding call inside the
pool, other array factories, collection pools and restoration scopes. It does not claim every
possible consumer, generated contract, player-only branch or optional dependency configuration.
Source hashes bind the snapshot to the reviewed file bytes; later edits require another audit.

## Current Contracts

| Pattern                                                              | Runtime property that source checks cannot establish                                                                   |
| -------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| `PooledArray`, `PooledResource`, `DisposableScope`, `SemaphoreLease` | Copies share an exactly-once claim, including repeated disposal and stale copies after reuse                           |
| `PurgeBufferLease` and retirement snapshots                          | Nested callbacks and copied handles must not return a live retirement buffer twice                                     |
| `RestorableGlobal` and editor wrappers                               | Dynamic nesting, out-of-order release and restoration remain valid across copied scopes                                |
| `PartitionPooled`                                                    | Iterator cleanup and consumers share the same partition lease; early consumer disposal must not release a later rental |
| Serialization accumulators and scratch frames                        | Stored and transferred ownership survives growth, nested serialization and failure cleanup                             |
| Generic pools and caches                                             | Live rentals, retention limits, callback failures, concurrent operations and disposal depend on runtime state          |
| Scene and dispatcher owners                                          | Cancellation, callbacks and repeated or reentrant disposal share runtime completion state                              |

A copy diagnostic is advisory. It cannot exclude repeated disposal of one value, generated
`using` cleanup, warning suppression or separately compiled callers. A C# 9 ref struct also permits
local copies and repeated disposal. Existing shared claim and restoration checks remain necessary
under the shipped guarantees. [The analyzer guide](./analyzers.md#wuh022-boxing-a-disposable-value-type)
records the executable compiler counterexamples and the additive borrowed-span API.

The operation-owned `TryWithBuffer` contract removes the returned disposal handle. Its callback
receives a synchronous logical span and explicit state. Reference-bearing input requires
initialization before exposure, and the physical reference-bearing array is cleared on return.
Callback effects persist on failure, and cleanup attempts a return once. This contract gives up
retained backing buffers, asynchronous use and caller-controlled release; element objects and
independent copies can still outlive the call.

## Site Dispositions

| Disposition             | Sites | Decision                                                                             |
| ----------------------- | ----: | ------------------------------------------------------------------------------------ |
| Shared handle           |   665 | Preserve the producer's copy-safe ownership contract                                 |
| Restoration or callback |    78 | Preserve dynamic restoration and exactly-once callback behavior                      |
| Runtime owner           |   116 | Preserve resource validity, reentrancy and disposal state                            |
| Private candidate       |   110 | Review the complete operation and measure before adopting a different lifetime shape |
| Source protocol         |     8 | Preserve disposal of custom traversal sources                                        |
| Existing private owner  |    11 | Keep the current operation-owned rental, cleanup and clearing                        |
| Extended lifetime       |     6 | Preserve ownership that crosses the producing method's synchronous lifetime          |

The six explicit array sites with extended lifetimes are the sparse-set enumerator, two JSON
accumulator sites, the deferred subset iterator, the asynchronous JSON stream reader and the
returned texture-premultiplication lease. Stored or returned ownership must not become an
untracked local rental.

The 110 synchronous array candidates include sorting, geometry, spatial construction, random
selection, variance, UTF-8 decoding and editor image operations. Local `using` alone does not prove
an operation-owned replacement safe or worthwhile. Nested helpers, callbacks, source mutation,
engine calls, cancellation and clearing belong to each complete operation. The inventory retains
a reason for each family and site. Collection-pool producers retain their shared contracts; they
are not automatically treated as private array candidates.

## Adoption Requirements

Before changing a candidate, establish the buffer's full lifetime, all aliases and exactly one
cleanup attempt after acquisition. Preserve `Get(0)` returning an empty array without a lease;
a direct BCL `Rent(0)` can return a nonempty bucket. Preserve initialization of the logical prefix,
reference clearing of the physical return buffer, element order and exception timing. Confirm
that returned values, fields, spans, iterators, tasks and callback state do not retain the rental.
Exercise failure and reentrancy paths as well as ordinary completion.

Measure the complete operation against the current implementation, including acquisition,
initialization, callback work and cleanup. Retain allocating positive controls and before/after
identity. A smaller lease counter proves avoided lease work; it does not prove better timing,
zero allocation or lower retained memory.

The four numeric mean paths and the Base64 decoder already own their private rentals without a
returned handle. The current Base64 host measurements show reduced cost for short inputs and
near parity for large or rejected inputs, with unchanged allocation. They do not qualify Unity
Release-player performance. The connected editor's allocation counter fails retained-box controls;
its zero readings are unqualified. Floor/latest Release-player timing, allocation, retention and
callback acceptance remains open before stronger adoption claims.
