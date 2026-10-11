# Skill: Maintain Plan

<!-- trigger: plan, roadmap, backlog, PLAN.md, GOAL.md, plan-hygiene | Keeping goals and plans general and ordered by priority | Core -->

**Trigger**: When creating, reviewing, or updating the root goal, the root plan, or repository planning guidance.

---

## Purpose

The root planning files are local scaffolding:

```text
GOAL.md: general objective and broad completion criteria
PLAN.md: general strategy and priority order
```

The issue tracker owns the current backlog and each issue's scope and acceptance criteria.

## Goal and Plan Boundaries

- Keep the root goal limited to the general objective and broad completion criteria.
- Keep the root plan limited to priority ordering, work selection, validation, and delivery rules.
- Never put specific issue or PR numbers, selected-issue lists, fixed issue quotas, delivery-round
  inventories, or task-specific implementation and acceptance details in either file. Keep those
  details with the canonical issue or PR; use ignored local notes for temporary selection details.
- Never store session logs, progress tracking, status updates, completed-work summaries,
  measurements, validation results, CI handles, blockers, or retrospectives in either file.
- Update these files only when the user changes the objective, strategy, or broad completion criteria.
  Starting or completing an issue or session does not require a goal or plan update.

## Priority Order

1. Gameplay correctness and retained player data.
2. Runtime safety, lifecycle reliability, and compatibility.
3. Measured performance and resource use.
4. Editor workflows, build reliability, and developer iteration.
5. Documentation, consistency, and style.

Respect explicit user priorities and scope. Resolve dependencies that prevent higher-priority work.

## Work Selection and Delivery

1. Review the complete current issue backlog with terminal pagination before choosing work.
2. Select the highest-priority actionable work, accounting for dependencies and user direction.
3. Read the canonical issue's full scope and acceptance criteria; reproduce the problem before fixing it.
4. Implement a complete fix and check for the same failure elsewhere.
5. Validate with appropriate tests and evidence; verify the current PR head and every feedback surface.
6. Close an issue only when its full acceptance criteria are proven. Retain partial work and remaining
   requirements in the canonical issue; an inventory or documentation cleanup does not prove closure.
7. Reassess the backlog and repeat until no actionable open issues remain. Report blocked or
   unauthorized issues accurately rather than treating them as closed.

## Route Work Records

| Information                                     | Destination                           |
| ----------------------------------------------- | ------------------------------------- |
| Temporary selections, next actions, raw output  | Ignored local `progress/` files       |
| Issue scope, dependencies, remaining acceptance | Canonical issue or design             |
| Durable evidence, measurements, decisions       | Canonical issue or commit body        |
| PR state, checks, and reviewer discussion       | Canonical PR                          |
| Repository-wide invariant or convention         | [context](../context.md)              |
| Reusable procedure                              | `.llm/skills/`                        |
| Reusable technical facts                        | `.llm/references/`                    |
| Feature behavior and architecture               | `docs/` or the canonical issue/design |

Never stage or force-add `progress/` or `progress.meta`. These are disposable agent scratch files.

## Cleanup and Validation

1. Save the original goal or plan in an ignored local `progress/` file before removing work records.
2. Preserve any unique unfinished requirements in their canonical issues or local notes. Removing
   them from the goal or plan does not complete, cancel, reopen, or authorize the underlying work.
3. Rewrite the goal and plan around their general objective and strategy; remove issue-specific
   selections and all session or progress records without summarizing them in place.
4. Format and lint both files explicitly: they are gitignored, so normal changed-file checks may skip them.
5. Verify that neither file contains specific issue selections, PR state, progress records, or results.

Keep the root plan under 150 lines, targeting 100 or fewer. A passing line count alone does not prove
that the plan is general or free of tracking. Check the content as well as its size.

```bash
line_count=$(wc -l < PLAN.md)
test "$line_count" -le 150
! rg -n '#[0-9]+|^## (Session|Progress|Shipped|Done|Closed|Retired)' GOAL.md PLAN.md
```

## Related Skills

- [review-plan](./review-plan.md) - Review issue-specific designs before implementation.
- [run-retrospective](./run-retrospective.md) - Keep session evidence and learnings in their proper homes.
- [manage-skills](./manage-skills.md) - Maintain procedural agent knowledge.
