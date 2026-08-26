# Session 228: WallstopProto standard-library shapes

Started from `main` at `d7015d34`, after session 227's PR #576 merged. A fully paginated audit found
25 open issues and one open pull request, Dependabot #559. Main delivery CI was green. #559 remains
unstable on the existing #498/#411/#325 Unity-license return chain and was not mixed into this
serializer change.

Branch: `session-228-wproto-stdlib-shapes`; PR
[#579](https://github.com/Ambiguous-Interactive/unity-helpers/pull/579). This is a measured tranche of
#399 and #343, not their closure: `char`, `Uri`, `Type`, `IntPtr`, and `UIntPtr` remain in
protobuf-net v3's accepted inventory, the consumer-repository survey is still pending, and
`DateTimeOffset` is refused by both oracle majors. `PLAN.md` keeps that remaining work first.

## What shipped in this tranche

WallstopProto now has reflection-free built-in encodings for `DateTime`, `TimeSpan`, `Guid`, and
`decimal`. They reproduce protobuf-net's `bcl.proto` bytes as roots, ordinary and nullable members,
collection elements, map keys and values, and generic contract closures. Both nested formatters and
root marshals register during the existing built-in startup phase.

The generator recognizes the four types semantically instead of by source spelling. That matters
for `decimal`: Roslyn reports the special type as `decimal`, not `global::System.Decimal`. The
unsupported-shape diagnostic now names the complete supported BCL set and continues to reject
`DateTimeOffset`.

## Root causes found by differential testing

The first implementation was byte-close but not wire-compatible. The dual-oracle corpus and
adversarial review exposed each mismatch:

1. The Unix epoch constant was missing a zero. Every ordinary `DateTime` delta was wrong.
2. Negative scaled durations used the wrong multiplication bound and could reject valid values.
3. BCL values were treated as protobuf messages for duplicate occurrences. They are nested on the
   wire but have scalar last-wins semantics, so repeated occurrences must replace rather than merge.
4. A nested formatter used directly at the root omitted protobuf-net's field-1 wrapper. Dedicated
   root marshals now add that wrapper and take the last complete occurrence.
5. Default `TimeSpan`, empty `Guid`, and zero `decimal` members were emitted as empty messages.
   Optional members now follow protobuf-net's omission rules, including decimal negative zero;
   `DateTime.MinValue` remains present because it has a MinMax sentinel.
6. The assumed decimal union offsets were wrong. The verified layout is flags, high, low, middle;
   a portable `decimal.GetBits` fallback preserves correctness on a runtime with another layout.
   The normal verified layout stays allocation-free, while the documented fallback allocates a
   four-item array per decomposition.
7. Several early golden vectors described impossible source distinctions or used a varint key for
   a length-delimited decimal. The oracle bytes, not the hand-written expectations, won.
8. Generated members hardcoded the built-in singleton while roots and generic closures consulted
   the provider, breaking the documented last-registration-wins contract. Generated shapes and
   root wrappers now resolve the provider, while omission and duplicate semantics follow the BCL
   logical type rather than the concrete formatter. A counting override pins all three paths.

Malformed-input coverage now pins overlong varints, invalid scales and MinMax sentinels, tick
overflow, decimal scale overflow, invalid and mismatched wire types, truncated Guid halves, and
truncated root lengths. A valid but mismatched wire type is skipped as an unknown field, matching
protobuf behavior.

## Measurements

- protobuf-net 3.2.56 final full generator suite: **498/498 passed**.
- protobuf-net 2.4.9 final full generator suite: **497/497 passed**.
- The differential corpus exercises at least 100 contracts across extremes, scaling boundaries,
  deterministic random values, nullable values, lists, maps, roots, duplicate occurrences, generic
  closures, and all four map-key shapes.
- Unity MCP attached to Unity `6000.4.6f1`. An editor-compiled command registered the built-ins and
  serialized all four values successfully; this directly exercises the explicit decimal layout and
  Span-based Guid writer in Unity's runtime.

Final local gates are green: all Unity and test-assembly typecheck variants, shipped-analyzer
freshness, CSharpier, Prettier, Markdown, spelling, metadata and nested-type lint, changed-test lint,
the 67 repository checks, the 63 fast contract checks, agent preflight, and the pre-push guard. The
two aggregate runs each found one tranche-local issue first -- untracked package payload before
staging and out-parameter structure -- and their corrected targeted gates passed.

## Issue accounting

The requested ten-issue objective is already evidenced on current main by session 227: #509, #460,
#435, #417, #286, #285, #309, #575, #573, and #431 are closed. This session does not relabel that
history as new work. It advances #399 and #343 while leaving both open until their stated acceptance
criteria are actually complete.

## Adversarial review

The review loop found the epoch typo, signed overflow bound, root wrapper and duplicate semantics,
default-member omission, decimal layout, misleading XML remarks, malformed-input gap, and previously
uncovered BCL map keys. Every correctness or coverage finding was implemented and re-run against
both oracle majors. The last pass reported zero actionable defects. The two remaining observations
are explicit scope rather than hidden defects: #399 is not closed, and the portable decimal-layout
fallback trades allocations for correctness on an unverified runtime layout.
