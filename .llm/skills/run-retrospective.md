# Skill: Run Retrospective

<!-- trigger: retro, retrospective, session-review, post-mortem, learnings | Structured retrospective analyzing what happened, what worked, and what to improve | Core -->

**Trigger**: When a significant task, feature, or debugging session is complete and you want to capture learnings.

---

## When to Use

- After completing a multi-session or complex implementation
- After a difficult debugging session
- After a failed approach that required backtracking
- When explicitly asked for a retrospective or post-mortem
- At the end of a major feature branch

---

## When NOT to Use

- For trivial one-file changes
- Mid-implementation (finish first, then retro)
- When there's nothing notable to learn

---

## Retrospective Phases

### Phase 1: Change Inventory

List all files changed and categorize:

| Category         | Files | Lines Changed |
| ---------------- | ----- | ------------- |
| New feature code | list  | count         |
| Bug fixes        | list  | count         |
| Tests            | list  | count         |
| Documentation    | list  | count         |
| Configuration    | list  | count         |
| Refactoring      | list  | count         |

### Phase 2: Time Distribution

Estimate how effort was distributed:

| Activity                  | Estimated % | Notes                              |
| ------------------------- | ----------- | ---------------------------------- |
| Understanding the problem | %           | Reading code, searching, asking    |
| Implementation            | %           | Writing new code                   |
| Debugging                 | %           | Fixing issues that arose           |
| Testing                   | %           | Writing and running tests          |
| Review/Polish             | %           | Code review, docs, formatting      |
| Rework                    | %           | Redoing work due to wrong approach |

Flag if **Rework > 20%** — this signals a planning gap.

### Phase 3: Hotspot Analysis

Identify files that were modified most frequently during the session:

```text
Hotspots (files changed 3+ times):
  1. [file] — N changes. Reason: [why so many changes?]
  2. [file] — N changes. Reason: [why?]
```

Frequent changes to the same file may indicate:

- Incomplete understanding before starting
- Missing tests that would have caught issues earlier
- Overly complex code that's hard to get right

### Phase 4: What Went Well

List specific things that worked:

- Techniques that saved time
- Tools or skills that were effective
- Patterns that led to clean code
- Decisions that turned out to be correct

### Phase 5: What Could Improve

List specific things that could be better:

- Wrong approaches tried before the right one
- Missing context that caused rework
- Patterns or conventions that were unclear
- Tools or skills that were missing

### Phase 6: Concrete Takeaways

For each improvement, propose a concrete action:

| Problem Observed | Proposed Action | Type                                                            |
| ---------------- | --------------- | --------------------------------------------------------------- |
| Description      | Specific fix    | New skill / Update skill / Convention change / Tool improvement |

### Phase 7: Streak Tracking (Optional)

If running retros regularly, track quality trends:

```text
Session Quality Indicators:
  Rework percentage: N% (trending: up/down/stable)
  Hotspot count: N (trending: up/down/stable)
  Tests written vs code written ratio: N:M
  Self-regulation triggers hit: N times
```

---

## Output Format

```text
Retrospective: [Task/Feature Name]
Date: [date]

Change Summary:
  Files changed: N
  Lines added: N | Lines removed: N
  Tests added: N

Time Distribution:
  [table from Phase 2]

Hotspots:
  [list from Phase 3]

✓ What Went Well:
  - [items from Phase 4]

✗ What Could Improve:
  - [items from Phase 5]

Action Items:
  - [items from Phase 6]
```

---

## Storing Learnings

Keep the retrospective and session metrics in ignored local `progress/` files. Never store them
or progress tracking in the root goal or the root plan; follow [maintain-plan](./maintain-plan.md).

After completing a retrospective:

1. If a takeaway applies broadly across sessions, consider noting it in memory
2. If a takeaway suggests a new skill or skill update, note it for follow-up
3. If a takeaway reveals a missing convention, propose an update to [context](../context.md)

---

## Related Skills

- [review-code-changes](./review-code-changes.md) - Review findings feed into retro
- [self-regulate-changes](./self-regulate-changes.md) - Risk score trends inform retro
- [apply-completeness](./apply-completeness.md) - Did we finish everything?
- [manage-skills](./manage-skills.md) - When retro suggests new skills
