# Skill: Review Plan

<!-- trigger: plan-review, scope-review, architecture-review, review-plan | Engineering review of implementation plans | Core -->

**Trigger**: When reviewing an implementation plan, technical design, or task breakdown before coding begins.

---

## When to Use

- Before starting implementation of a multi-file change
- When a plan feels overengineered or underspecified
- When asked to validate an approach before executing it
- When estimating scope or complexity of a proposed change

---

## When NOT to Use

- For reviewing already-written code (use [review-code-changes](./review-code-changes.md))
- For trivial one-file changes that need no planning
- For documentation-only changes

---

## Step 0: Scope Challenge

Before reviewing plan content, challenge the scope:

### Complexity Smell Test

| Signal                | Threshold                 | Response                                     |
| --------------------- | ------------------------- | -------------------------------------------- |
| Files touched         | 8+ files                  | Ask: can this be split into smaller PRs?     |
| New abstractions      | 2+ new classes/interfaces | Ask: do these earn their complexity?         |
| Cross-cutting changes | 3+ unrelated systems      | Ask: is this actually one change or several? |
| Duration estimate     | "a few days"              | Require decomposition into < half-day chunks |

### Existing Code Leverage

Before approving new code, verify:

1. Does this duplicate something already in the codebase?
2. Can an existing utility be extended instead of creating a new one?
3. Are there patterns in adjacent code that should be followed?

Use search to verify — do not rely on plan author's claims.

---

## Review Sections

### 1. Architecture Review

| Check                     | Question                                                              |
| ------------------------- | --------------------------------------------------------------------- |
| **Single Responsibility** | Does each new type have exactly one reason to change?                 |
| **Dependency Direction**  | Do dependencies flow toward stable abstractions?                      |
| **Seam Points**           | Can each new component be tested in isolation?                        |
| **Unity Patterns**        | Does this follow existing MonoBehaviour/ScriptableObject conventions? |
| **Breaking Changes**      | Does this break any public API? If so, is it justified?               |

### 2. Code Quality Forecast

| Check              | Question                                                     |
| ------------------ | ------------------------------------------------------------ |
| **Naming Clarity** | Will the proposed names make sense in 6 months?              |
| **Error Paths**    | Are failure modes explicitly addressed in the plan?          |
| **Edge Cases**     | Are boundary conditions identified?                          |
| **Performance**    | Are hot paths identified and optimization strategy noted?    |
| **Allocation**     | For frequently-called code, is allocation budget considered? |

### 3. Test Plan

| Check                 | Question                                                        |
| --------------------- | --------------------------------------------------------------- |
| **Coverage Strategy** | What test categories are needed (unit, integration, play mode)? |
| **Failure Cases**     | Are negative test cases planned, not just happy paths?          |
| **Existing Tests**    | Will existing tests need updating? Which ones?                  |
| **Test Data**         | Are test fixtures or mock data requirements identified?         |

### 4. Risk Assessment

| Check             | Question                                                         |
| ----------------- | ---------------------------------------------------------------- |
| **Reversibility** | Can this be reverted cleanly if it fails?                        |
| **Blast Radius**  | What breaks if this has a bug? One feature or the whole package? |
| **Migration**     | Does this require data migration or user action?                 |
| **Platform**      | IL2CPP/AOT implications considered?                              |

---

## One-Issue-at-a-Time

When presenting findings:

1. State the most critical concern first
2. Wait for resolution before raising the next concern
3. Group related concerns if they share a root cause
4. Prefix each concern with severity: `[BLOCKING]`, `[SIGNIFICANT]`, `[MINOR]`

---

## NOT-in-Scope Section

Every plan review should explicitly list what is NOT being changed. This prevents scope creep and clarifies boundaries:

```text
NOT in scope for this change:
- [ ] Item A (reason)
- [ ] Item B (reason)
- [ ] Item C (tracked in issue #X)
```

---

## Review Output Format

```text
Plan Review Summary:
  Scope: [APPROPRIATE | OVERSIZED | UNDERSIZED]
  Architecture: [SOUND | CONCERNS: list]
  Test Plan: [ADEQUATE | GAPS: list]
  Risk Level: [LOW | MEDIUM | HIGH]
  Blocking Issues: N
  Recommendation: [APPROVE | REVISE | RETHINK]
```

---

## Related Skills

- [maintain-plan](./maintain-plan.md) - Keep the working plan limited to actionable work.
- [review-code-changes](./review-code-changes.md) - Post-implementation code review
- [validate-before-commit](./validate-before-commit.md) - Pre-commit validation
- [performance-audit](./performance-audit.md) - Performance-specific review
- [manage-skills](./manage-skills.md) - When plan involves new skills
