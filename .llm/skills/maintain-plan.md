# Skill: Maintain Plan

<!-- trigger: plan, roadmap, backlog, PLAN.md, plan-hygiene | Keeping the working plan limited to active and future work | Core -->

**Trigger**: When creating, reviewing, or updating the working plan, a roadmap, or a task backlog.

---

## Purpose

The working plan is a disposable working set, not a project history, design specification, issue
mirror, or agent handbook. It answers only: what is being worked on, what comes next, and how
completion will be recognized.

---

## Size Contract

- Hard limit: 150 lines.
- Target: 100 lines or fewer.
- Every addition must remove or replace stale material when needed to stay within the limit.
- The working plan is gitignored, so run the local check below before handoff; CI cannot enforce it.

```bash
line_count=$(wc -l < PLAN.md)
test "$line_count" -le 150
! rg -n '^## (Session|Shipped|Done|Closed|Retired)' PLAN.md
```

---

## What Belongs

Each active or future initiative contains only:

1. Outcome: one sentence describing the user-visible or operational result.
2. Status: `In progress` or `Future`, with priority when order matters.
3. Next tasks: short, ordered, independently completable actions.
4. Acceptance: the smallest observable signal that proves the initiative is complete.
5. References: links to canonical issues, designs, documentation, or agent guidance.

Keep only unchecked work. A decision belongs only when it directly changes the next action and has
no canonical home to link instead.

---

## Route Everything Else

| Information                                                         | Destination                        |
| ------------------------------------------------------------------- | ---------------------------------- |
| Completed work, measurements, failed hypotheses, validation results | `progress/session-*.md`            |
| Repository-wide invariant or mandatory convention                   | [context](../context.md)           |
| Reusable procedure                                                  | `.llm/skills/`                     |
| Reusable technical facts or lookup tables                           | `.llm/references/`                 |
| Feature behavior and architecture                                   | `docs/` or the owning issue/design |
| Issue discussion, rationale, and long-form task detail              | Canonical issue or design file     |

Do not duplicate those sources in the working plan; link them.

---

## Task Lifecycle

1. Before work, state a falsifiable baseline or failing characterization in the session log.
2. Keep the working-plan entry to the next action and its acceptance signal.
3. Record experiments, results, and changed conclusions in the session log as they occur.
4. When the task finishes, remove it from the working plan in the same session.
5. Promote only reusable lessons to context, a skill, or a reference.
6. Run the size contract and verify that every remaining item is still actionable.

A completed checkbox, shipped section, session narrative, evidence table, or retrospective in
the working plan is a routing failure. Move it; do not summarize it in place.

---

## Audit Questions

- Can someone act on every line now or in a named future phase?
- Is status represented by the presence of open work rather than a history of closed work?
- Does each task have one observable completion signal?
- Is detailed context linked instead of copied?
- Would deleting any paragraph change a future action? If not, delete or route it.

---

## Related Skills

- [review-plan](./review-plan.md) - Challenge scope, risk, and test coverage before implementation.
- [run-retrospective](./run-retrospective.md) - Capture completed-session evidence and learnings.
- [manage-skills](./manage-skills.md) - Maintain procedural agent knowledge.
