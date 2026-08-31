# Session 242 — Eight silent state corruptions made explicit

**Addressed:** #627, #637, #643, #644, #645, #647, #648, and #663.

This session selected eight independently reproducible correctness failures from the fully
paginated open-issue inventory. The common failure mode was a successful-looking operation that
left hidden state stale, partially mutated, or reusable under a contract it no longer satisfied.

## The failures and their roots

| Issue | Root cause | Result |
| --- | --- | --- |
| #627 | A copied editor scope carried only the old global value, so each copy believed it owned restoration and out-of-order disposal overwrote newer owners or external edits. | `RestorableEditorGlobal<T>` gives every owner a generation-stamped slot, splices out-of-order owners, preserves intervening external values, and rejects stale copies. Indent and label-width scopes use it. |
| #637 | `RuntimeSingleton<T>.Awake` trusted the generic base and coerced `this` to `T`; a sibling component inheriting a different closed singleton base could reach that path. | Awake now accepts only an actual `T`, reports the malformed hierarchy, and leaves the canonical cache untouched. |
| #643 | Pool return checked disposal before `onRelease`, but the callback could dispose the pool before the item was parked. The concurrent implementation had the same check/use race. | Both builds recheck after callbacks; the threaded build decides under the pool lock. Retired and callback-failed returns invoke disposal once and decrement active-rental accounting. |
| #644 | Singleton creation had no process-level shutdown signal. A teardown callback could therefore create a replacement, and no-domain-reload play sessions could retain a quit flag. | A shared registry tracks application/editor shutdown, resets at subsystem and scene-entry boundaries, refuses new runtime and ScriptableObject singleton work during shutdown, and preserves an instance already loaded. |
| #645 | Swap-back assigned the last item before `RemoveAt`; fixed-size `IList<T>` implementations accepted the assignment and then threw from the structural removal. | Removal happens before replacement assignment, so fixed-size and invalid removals are failure-atomic while mutable-list swap semantics stay unchanged. |
| #647 | `TryReadMessage` trusted a formatter's Boolean return. A buggy formatter could return true with unread bytes, clear malformed state, reset the reader, or replace it with an equal-length buffer. | Every nested and root facade snapshots the exact reader and accepts success only at the field boundary with unchanged depth and buffer provenance. Consume and explicit skip remain valid. |
| #648 | Completed, cancelled, or failed validation runs all went through the same mutating commit methods; a malformed finding could also throw after the old snapshot was cleared. | Full and scoped commits reject incomplete, cancelled, failed, and malformed runs before mutation. Full replacement builds a complete candidate snapshot and swaps it atomically; the window retains and explains previous results, and auto-run requeues targets. |
| #663 | `SerializedStringComparer` can still be mutated after a hash collection captures it, making keys unreachable, and the compiler could not distinguish the dangerous order from ordinary configuration. | `WUH011` reports writes after construction of known hash collections, including field, compound, increment, ref/out, and deconstruction writes. Freeze, definite rebind, lexical branch boundaries, and non-retaining consumers are characterized to constrain false positives. |

## Why the first fixes were not enough

Three adversarial passes changed the implementation rather than merely adding assertions:

- The first comparer analysis tracked only source order. Rebinding, conditional control flow,
  arbitrary constructors, `Freeze()` receivers, and compound writes showed why collection identity
  and lexical execution regions both matter.
- Reader position and depth were insufficient proof of formatter completion: assigning a different
  reader over an equal-length buffer reproduced the same counters. Buffer-reference identity is now
  part of the completion contract.
- Rechecking pool disposal after `onRelease` stopped resurrection but initially left a phantom
  rental when the callback threw or won disposal. Retirement now updates accounting without
  inventing a timestamp.
- Validation rejected failed runs, but the first full-commit implementation cleared the current
  snapshot before indexing an invalid finding. Candidate construction now precedes the state swap.

## Evidence

- `SerializedStringComparerMutationAnalyzerTests`: 48 passed, including constructor initializers,
  short-circuit and filtered regions, ref/out aliasing, deconstruction, and non-retaining consumers.
- Clean runtime-test and editor-test typecheck projects passed after the final semantic changes.
- Changed tests pass the repository lifecycle/null-check lint, and all changed C# is CSharpier
  formatted.
- Independent runtime, editor, and serializer/analyzer reviewers reached zero findings after the
  final adversarial passes.
- Unity editor `6000.4.6f1` is the live MCP target for final fixture and reflection probes; the
  repository has no standalone Unity license, so the PR matrix remains the full cross-version
  execution authority.

## Remaining work

These are correctness slices, not claims that the broad audit epics are complete. `PLAN.md` retains
the evidence-gated optimization halves and the remaining relational-component, serializer-fuzzing,
project-scale validation, unsafe conversion, and algorithm-measurement work.
