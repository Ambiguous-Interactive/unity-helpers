# Enumeration Audit

This audit records the enumeration bindings in the package, editor, tests, generators and samples
for [#882](https://github.com/Ambiguous-Interactive/unity-helpers/issues/882).
The [complete inventory](./enumeration-audit.csv) contains one row for every source site, including
its source hash, compiler binding, concrete enumerator type and retained-allocation disposition.

## Coverage

The inventory covers 2,556 C# files and 3,473 distinct `foreach` or explicit `GetEnumerator` sites.
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

The CSV is a snapshot. Source hashes detect when a row needs another audit; it is not a permanent
allowlist or a check that silently accepts later code. The two samples and acceptance templates are
included explicitly because Unity does not import those package folders automatically.

## Results

| Binding                  | Sites | Interpretation                                                           |
| ------------------------ | ----: | ------------------------------------------------------------------------ |
| Compiler-indexed arrays  | 1,236 | No enumerator object; element conversions can still box                  |
| Compiler-indexed strings |    22 | UTF-16 character indexing; no enumerator object                          |
| Value return             | 1,744 | Concrete value protocol, with source and traversal-storage caveats below |
| Reference return         |   471 | 336 test-reference sites and 135 retained production sites               |

The changes avoid boxing for interface-held arrays, exact lists, exact hash sets and exact
dictionaries. Custom sequences and derived collections retain their original acquisition,
mutation, exception and cleanup protocols. Existing null failures remain failures in migrated
internal calls; the public views deliberately treat null as empty.

Explicit generic enumerators use the same value view. Internally owned generator lists keep their
concrete type through five loops. Bit-set JSON writers avoid their iterator factories, regex
scanning follows `NextMatch`, and private reflection results retain their array type.

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

Test reference sites include interface and custom-iterator contracts, compatibility returns,
differential oracles, allocating positive controls and ordinary interface-held fixture inputs.
The CSV identifies those references; it does not prove that every fixture input must retain a
reference enumerator. Review the remaining test inputs separately before treating #882 as closed.
Preserve independent oracles and allocating controls during that review.

## Validation Limits

The Release host enumeration suite passes 59 tests without skips. Native Unity 6000.4.6 passes the
array behavior controls, dictionary protocols and affected extension, geometry, JSON, editor and
reflection fixtures. Allocation assertions require an allocating positive control.

Host controls observe interface enumerator allocations and zero enumerator allocations for the
documented optimized shapes. The connected editor's allocation controls report zero even when
allocation is expected, so its allocation channel is unqualified. Editor skips are not proof of
zero allocation. Host and editor timing observations do not establish Release-player or IL2CPP
performance, and the CSV does not claim that every value-returning operation is allocation-free.
