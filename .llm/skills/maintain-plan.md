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
- Target: 100 lines or fewer; a passing line count alone does not prove the plan is lean.
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

| Information                                            | Destination                        |
| ------------------------------------------------------ | ---------------------------------- |
| Temporary work notes and raw validation output         | Ignored local `progress/` files    |
| Durable evidence, measurements, and decisions          | Canonical issue or commit body     |
| Repository-wide invariant or mandatory convention      | [context](../context.md)           |
| Reusable procedure                                     | `.llm/skills/`                     |
| Reusable technical facts or lookup tables              | `.llm/references/`                 |
| Feature behavior and architecture                      | `docs/` or the owning issue/design |
| Issue discussion, rationale, and long-form task detail | Canonical issue or design file     |

Do not duplicate those sources in the working plan; link them.

---

## Cleanup Without Losing Work

1. Save the original plan and baseline counts in an ignored local `progress/` session file.
2. Inventory open actions, blockers, dependencies, acceptance conditions, and issue references.
3. Route completed-work narratives and measurements to the session file. Promote only reusable
   rules or procedures to context, skills, or references; keep issue-specific facts with the issue.
4. Rewrite each initiative around its remaining actions. Preserve release, compatibility, owner,
   and measurement conditions that still constrain those actions. Do not reopen shipped work.
5. Compare the rewrite with the inventory. Account for every removed issue reference; duplicate
   or historical references belong in the session record, not the open-work list.
6. Verify both the size contract and the semantic audit below after formatting. Record the result
   in the session file; keep audit reports and snapshots out of the working plan.

Use a failing characterization before cleanup and repeat it afterward: count lines and initiatives,
check for historical narratives, and compare open actions and references. For a prose-only cleanup,
these checks and documentation linters supply the evidence; a new test framework is unnecessary.

---

## Task Lifecycle

1. Before work, state a falsifiable baseline or failing characterization in an ignored local note.
2. Keep the working-plan entry to the next action and its acceptance signal.
3. Record experiments and raw results in ignored local notes while the work is active.
4. When the task finishes, remove it from the working plan in the same session.
5. Put durable evidence in the canonical issue or commit body; promote reusable lessons to context,
   a skill, or a reference.
6. Run the size contract and verify that every remaining item is still actionable.

Never stage or force-add `progress/` or `progress.meta`. These are disposable agent scratch files;
the repository ignore rules and lint gate deliberately keep them out of history.

A completed checkbox, shipped section, session narrative, evidence table, or retrospective in
the working plan is a routing failure. Move it; do not summarize it in place.

---

## Audit Questions

- Can someone act on every line now or in a named future phase?
- Does it describe remaining work, rather than what shipped, what a session found, or benchmark results?
- Are statuses and priorities preserved without implying unverified issue closures?
- Is status represented by the presence of open work rather than a history of closed work?
- Does each task have one observable completion signal?
- Is detailed context linked instead of copied?
- Would deleting any paragraph change a future action? If not, delete or route it.

---

## Related Skills

- [review-plan](./review-plan.md) - Challenge scope, risk, and test coverage before implementation.
- [run-retrospective](./run-retrospective.md) - Capture completed-session evidence and learnings.
- [manage-skills](./manage-skills.md) - Maintain procedural agent knowledge.
