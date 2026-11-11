# Skill: Review Code Changes

<!-- trigger: review, code-review, pre-landing, diff-review, pr-review | Pre-landing code review with two-pass analysis | Core -->

**Trigger**: When reviewing code changes before committing, creating a PR, or after implementing a feature.

---

## When to Use

- Before finalizing any implementation
- When asked to review a diff, PR, or set of changes
- As a quality gate before marking a task complete
- When another agent's output needs review

---

## When NOT to Use

- For plan-level architecture review (use [review-plan](./review-plan.md))
- For simple formatting fixes (use [formatting](./formatting.md))
- For test-only changes with no logic changes

---

## Two-Pass Review

### Pass 1: Critical (Must Fix)

Focus exclusively on issues that cause bugs, data loss, security holes, or crashes:

| Category            | What to Check                                                                         |
| ------------------- | ------------------------------------------------------------------------------------- |
| **Null Safety**     | UnityEngine.Object null checks use `== null` not `?.`; bounds checks on all indexing  |
| **Thread Safety**   | Shared state properly guarded; main-thread-only APIs not called from background       |
| **Resource Leaks**  | IDisposable properly disposed; pooled resources returned; event handlers unsubscribed |
| **Data Integrity**  | Serialization round-trips correctly; undo properly recorded; no silent data loss      |
| **API Contracts**   | Public APIs never throw; TryXxx patterns used; return values match docs               |
| **Platform Safety** | IL2CPP/AOT compatible; no reflection on own types; no LINQ in hot paths               |

### Pass 2: Informational (Should Fix)

After all critical issues are resolved, check for code quality concerns:

| Category           | What to Check                                                                |
| ------------------ | ---------------------------------------------------------------------------- |
| **DRY Violations** | Same logic in multiple places; extract to shared utility                     |
| **Naming**         | Types/methods named for what they DO, not how; follows naming conventions    |
| **Dead Code**      | Unreachable branches; unused parameters; commented-out code                  |
| **Missing Tests**  | New code paths without test coverage; changed behavior without updated tests |
| **Allocation**     | Unnecessary heap allocations; boxing; LINQ in frequently-called code         |
| **Documentation**  | Public API missing XML docs; CHANGELOG missing entry for user-facing changes |

---

## Fix-First Heuristic

Classify each finding:

| Classification | When                               | Action                                               |
| -------------- | ---------------------------------- | ---------------------------------------------------- |
| **AUTO-FIX**   | Mechanical, no judgment needed     | Fix immediately without asking                       |
| **ASK**        | Requires judgment or has tradeoffs | Present the issue, recommend a fix, ask for approval |

**AUTO-FIX examples**: Missing `using` disposal, formatting violations, missing null checks on Unity objects, missing `.meta` files, spelling errors in code strings.

**ASK examples**: Architecture changes, API signature changes, behavior changes, performance tradeoffs, removing functionality.

---

## Review Output Format

For each finding, report:

```text
[CRITICAL|INFORMATIONAL] [file:line] Problem description
  → Recommended fix: specific action
  → Classification: AUTO-FIX | ASK
```

After all findings:

```text
Review Summary:
  Critical issues: N (M auto-fixed, K need decision)
  Informational: N (M auto-fixed, K noted)
  Test coverage: assessed | gaps identified
```

---

## Current Pull Request Feedback

When review feedback is part of the task, fetch current issue comments, reviews, and inline threads for every referenced pull request, including merged pull requests. A handoff or prior completion receipt describes its recorded time. Compare new comment timestamps and thread state with that receipt before treating the work as complete.

Give each current actionable comment a disposition backed by the implementation, a regression test, or a specific explanation. Read full comment bodies and nested replies. After a push, inspect new feedback against the pushed commit and repeat before merge. Record any remaining requests in the issue or handoff with their current identifiers.

---

## Failure Mode Check

For each new code path, ask:

1. What happens when the input is null?
2. What happens when the collection is empty?
3. What happens when the operation times out or fails?
4. Is the failure visible (logged, returned) or silent?
5. Is there a test covering this failure?

Any path with **no test AND no error handling AND silent failure** is a **critical gap**.

---

## CHANGELOG Cross-Reference

If the changes include user-facing modifications:

1. Verify CHANGELOG has an entry
2. Verify entry matches the actual change (not outdated from an earlier iteration)
3. Verify format follows [update-documentation](./update-documentation.md) rules

---

## Related Skills

- [review-plan](./review-plan.md) - Architecture-level plan review
- [validate-before-commit](./validate-before-commit.md) - Pre-commit validation checks
- [investigate-test-failures](./investigate-test-failures.md) - When tests fail during review
- [update-documentation](./update-documentation.md) - Documentation requirements
- [defensive-programming](./defensive-programming.md) - Defensive coding patterns
