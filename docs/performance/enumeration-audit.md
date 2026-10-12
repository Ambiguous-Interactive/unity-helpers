# Enumeration Audit

This audit records the enumeration bindings in the package, editor, tests, generators and samples
for [#882](https://github.com/Ambiguous-Interactive/unity-helpers/issues/882).
The [complete inventory](./enumeration-audit.csv) contains one row for every source site, including
its source hash, compiler binding, concrete enumerator type and retained-allocation disposition.

## Coverage

The inventory covers 2,559 C# files and 3,475 distinct `foreach` or explicit `GetEnumerator` sites.
Syntax discovery parses every combination of conditional symbols in each file, deduplicating sites
by path, source offset and syntax kind. It includes tracked sources and the new enumeration files;
generated build outputs are excluded.

Every discovered site has semantic binding evidence. The coverage join has no missing sites,
unresolved enumerators or compilation errors in its input compilations:

- Native Unity 6000.4.6 references and C# 9 bind the package assemblies, two samples and the two
  nullable-map acceptance assemblies, in default and `SINGLE_THREADED` configurations.
- MSBuild compiler inputs and resolved references bind all 16 owned host projects, with an
  additional protobuf v2 configuration for the two conditional oracle branches.
- The Odin editor-test configuration binds the remaining conditional array traversal.
- The shipped protobuf generator runs before semantic analysis where source generation is needed.

The authored CSV includes the current variance and random-snapshot ownership changes. Source hashes
detect when a row needs another audit; it is not a permanent allowlist or a check that silently
accepts later code. The two samples and acceptance templates are
included explicitly because Unity does not import those package folders automatically.

## Results

| Binding                  | Sites | Interpretation                                                           |
| ------------------------ | ----: | ------------------------------------------------------------------------ |
| Compiler-indexed arrays  | 1,246 | No enumerator object; element conversions can still box                  |
| Compiler-indexed strings |    22 | UTF-16 character indexing; no enumerator object                          |
| Value return             | 1,830 | Concrete value protocol, with source and traversal-storage caveats below |
| Reference return         |   377 | 242 reviewed test sites and 135 retained production sites                |

The changes avoid boxing for interface-held arrays, exact lists, exact hash sets and exact
dictionaries. Custom sequences and derived collections retain their original acquisition,
mutation, exception and cleanup protocols. Existing null failures remain failures in migrated
internal calls. Public view constructors treat null as empty; the public `RequireSource`
factories retain acquisition failures.

Explicit generic enumerators use the same value view. Internally owned generator lists keep their
concrete type through five loops. Bit-set JSON writers avoid their iterator factories, regex
scanning follows `NextMatch`, and private reflection results retain their array type. Sixty ordinary
fixture traversals use the value view; eight enum-array loops keep their concrete element type.
A second fixture review removes 29 more reference traversals through concrete owned dictionaries,
lists and queues, required value views, a typed enum array, regex matching and indexing fixed-size
array-backed snapshots. Independent traversal oracles and allocating controls retain their source
protocol.

Untyped array traversal uses value enumeration for ranks one through three, including nonzero
lower bounds and live element reads. Higher ranks retain the original BCL enumerator: the public
`GetValue` API requires an index array for those shapes. Value-array elements still box when exposed
as `object`; removing the enumerator object does not remove that conversion.

## Retained Allocation

The CSV gives each retained reference site a disposition. Production sites retain these policies:

- Lazy reflection, Roslyn, filesystem, XML, UI, ordering and owned iterator factories retain their
  traversal, early termination, exception timing and cleanup. A wrapper cannot remove an iterator
  already created by its factory.
- Eleven reflected non-generic sources retain their custom interface protocol and conversions.
  Their element type and concrete traversal are not part of the caller's contract.
- Four constrained generic set loops retain their receiver. An interface-based wrapper can box
  or copy a mutable struct set, changing behavior.
- Five fallback acquisitions deliberately invoke the source protocol. The value views optimize
  only their documented concrete shapes.
- One custom collection branch follows the earlier array/list/hash-set branches. One returned
  interface enumerator keeps its compatibility signature. One concurrent values traversal keeps
  its snapshot semantics instead of switching to a live map iterator.

Value returns also need care. The 52 sorted-tree sites retain BCL traversal storage; a struct
enumerator can allocate its internal stack. The 68 trie sites use pooled traversal storage, whose
cold rents and growth can allocate, and string results can allocate. Other factories, conversions
and loop bodies remain independent of the enumerator representation.

The 242 retained test reference sites have specific reviewed dispositions:

| Disposition                | Sites | Reason                                                         |
| -------------------------- | ----: | -------------------------------------------------------------- |
| Protocol and compatibility |    72 | Directly exercises the original iterator or interface contract |
| Lazy iterator or query     |   109 | The source factory already owns its reference iterator         |
| Independent oracle         |    24 | Keeps behavior comparisons and allocation controls independent |
| Framework collection       |    15 | Backing storage or traversal belongs to the framework          |
| Reflection stream          |     8 | Reflection discovery or its deferred filter owns the iterator  |
| Filesystem source          |     7 | Keeps deferred file discovery and cleanup                      |
| Concurrent registry        |     5 | Retains the BCL concurrent traversal protocol                  |
| Unity hierarchy            |     2 | Retains engine hierarchy observation order                     |

Each site retains its source hash and semantic witness in the CSV. The review removes ordinary
fixture inputs whose concrete storage can be retained or dispatched safely. It preserves sources
that test the original protocol, independent expected results and allocating positive controls.
A value wrapper alone cannot remove a reference iterator already created by a lazy factory.

## Generated Code

The [generated inventory](./generated-enumeration-audit.csv) separately records emitted protobuf
loops. Unity 6000.4.6 references and C# 9 bind 45 assemblies in both default and
`SINGLE_THREADED` configurations, with no compilation errors or missing sources.
Each configuration contains 150 emitted sites: 127 value returns, 16 indexed arrays and seven
reference returns. The previous generator had 106 value returns and 28 reference returns.

Interface-declared repeated and map collections now use required value views, avoiding boxing at
21 emitted sites. Concrete classes and structs retain their declared enumeration protocol.
The seven retained reference returns belong to `ReadOnlyCollection`, `ReadOnlyDictionary` and
`Collection`; their framework traversal is preserved. The inventory records emitted source hashes
and the shipped generator hash. Its coverage is the compiled fixture and sample contracts, rather
than every contract a future consumer could declare.

Eight semantic compiler controls cover the supported repeated and dictionary interfaces. All eight
fail against the previous generator and pass against the changed generator. The complete Release
protobuf compatibility suites pass 1,250 v3 cases and 1,218 v2 cases without skips.

## Validation Limits

Fresh native controls on the loaded required-source API pass 117 cases across the value views,
collection marshals and generated repeated/map fixtures. One dictionary allocation case skips
because its positive control observes no allocation. These are editor behavior results.

The Release host enumeration suite passes 67 tests without skips. Native Unity 6000.4.6 passes the
array behavior controls, dictionary protocols and affected extension, geometry, JSON, editor and
reflection fixtures. The affected protobuf host fixtures pass 209 tests for each oracle version. Allocation assertions
require an allocating positive control; allocation-only inconclusive results remain unqualified.

Host controls observe interface enumerator allocations and zero enumerator allocations for the
documented optimized shapes. The connected editor's thread byte counter reports zero even when
allocation is expected, so that channel remains unqualified. Its `GC.Alloc` profiler marker does
observe allocation counts: nine PlayMode cases cover arrays, lists and hash sets at 1, 16 and 4,096
elements. Each case repeats fresh-box, preboxed and traversal controls three times. The ordinary
interface path records 128 allocations over 128 calls; the value path records zero. Recorder
overflow fails the test, and an unavailable or inert marker skips it. The three older byte-counter
cases still skip. Counts are not byte measurements, and skips are not proof of zero allocation.

Unity documents its allocation counters as unavailable in
[Release players](https://docs.unity3d.com/6000.0/Documentation/Manual/profiler-counters-reference.html).
These editor results do not qualify Release-player or IL2CPP performance, and the CSV does not
claim that every value-returning operation is allocation-free.
