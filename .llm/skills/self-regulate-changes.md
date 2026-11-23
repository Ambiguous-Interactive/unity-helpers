# Skill: Self-Regulate Changes

<!-- trigger: self-regulate, risk-score, wtf-score, stop-condition, change-risk | Know when to stop: risk scoring and hard caps for cascading changes | Core -->

**Trigger**: When making multiple fixes in sequence, or when a change starts cascading into more changes.

---

## When to Use

- After fixing 3+ issues in a single session
- When a "simple fix" leads to discovering more problems
- When you're tempted to fix "one more thing"
- During review workflows that include auto-fix phases

---

## When NOT to Use

- For planned, scoped refactoring with clear boundaries
- For initial implementation of new features
- For mechanical bulk operations (rename, formatting)

---

## Risk Scoring

Every fix made during a review or QA pass accumulates risk. Track risk using this heuristic:

| Action                          | Risk Points        |
| ------------------------------- | ------------------ |
| Fix typo or spelling            | +0                 |
| Add missing null check          | +1                 |
| Fix formatting or whitespace    | +0                 |
| Add missing `.meta` file        | +0                 |
| Change logic in existing method | +3                 |
| Modify public API signature     | +5                 |
| Touch 3+ files in one fix       | +5                 |
| Revert a previous fix           | +15                |
| Fix leads to another fix        | +1 per chain depth |
| Change behavior without a test  | +10                |

---

## Hard Caps

Goal sessions have a hard three-hour wall-clock limit under the
[production session plan](../PLAN.d/production-session.md). CI, review, and handoff count toward
the limit. Use the remaining time to close out work and stop at the deadline even if the risk
budget has room.

| Threshold                                                  | Action                                                                           |
| ---------------------------------------------------------- | -------------------------------------------------------------------------------- |
| Risk score reaches **15**                                  | Pause. Review all changes made so far. Document remaining issues without fixing. |
| Risk score reaches **25**                                  | Stop fixing. Switch to report-only mode. List remaining issues for human review. |
| **Any single revert**                                      | Immediately stop auto-fixing. Document what happened and why.                    |
| **3+ chained fixes** (fix A required fix B required fix C) | Stop. The root cause needs human analysis.                                       |

---

## Cascading Fix Detection

A cascading fix is when:

1. You fix issue A
2. Fix A causes or reveals issue B
3. You fix issue B
4. Fix B causes or reveals issue C

**When you detect a cascade:**

```text
⚠ Cascading fix detected (depth: N)
  Chain: A → B → C
  Root cause assessment: [description]
  Risk score: [current]
  Recommendation: STOP | CONTINUE with caution
```

---

## Session Change Tracking

Maintain a mental ledger of changes during any multi-fix session:

```text
Change Log:
  1. [file:line] Description (+risk points)
  2. [file:line] Description (+risk points)
  ...
  Running risk score: N / 25
  Auto-fix budget remaining: Y
```

---

## When to Stop vs. Continue

| Scenario                                      | Decision                       |
| --------------------------------------------- | ------------------------------ |
| Next fix is mechanical and isolated           | Continue                       |
| Next fix touches code you haven't read fully  | **Stop** — read first          |
| Next fix changes behavior                     | **Stop** — needs test          |
| Next fix is in a file you haven't touched yet | Pause — assess necessity       |
| You're unsure if a fix is correct             | **Stop** — ask                 |
| You've been fixing for 20+ changes            | **Stop** — diminishing returns |

---

## Integration with Review Workflow

When used during [review-code-changes](./review-code-changes.md):

- Track risk score alongside the Fix-First AUTO-FIX actions
- Each AUTO-FIX action adds to the risk score
- When risk score hits threshold, remaining AUTO-FIX items become ASK items
- Report final risk score in review summary

---

## Related Skills

- [review-code-changes](./review-code-changes.md) - Code review workflow with Fix-First
- [apply-completeness](./apply-completeness.md) - Tension: completeness vs. self-regulation
- [validate-before-commit](./validate-before-commit.md) - Final validation gate
