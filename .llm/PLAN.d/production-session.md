# Production Goal Session

Use this plan for the bare objective shown below and for continuations of that objective.
Follow the user's explicit scope and priorities. Honor any shorter time limit they set.

```text
/goal Achieve GOAL.md
```

Read the repository's [agent guidance](../context.md) and [Maintain Plan skill](../skills/maintain-plan.md).
Read the root goal and work plan if they are present. In a fresh checkout, apply this objective
directly. Close as many production defects as the session can fully verify and deliver.
Aim for zero open production defects while protecting correctness, compatibility and reliability.
Scope this work to ordinary production defects and tooling with a direct path to closure.
Exclude cybersecurity work, investigations, tools and reporting.

## Open the Session

- Record the session start in UTC as soon as the user authorizes a new session. The hard limit is
  three hours of elapsed wall time, including discovery, implementation, CI, reviews, and handoff.
  Record the start, 120-minute planning checkpoint, 150-minute intake cutoff, and 180-minute
  deadline in an ignored local handoff. If the user sets a shorter limit, record that earlier
  deadline and bring any later checkpoint or cutoff forward. Carry the original deadline through
  turns, interruptions, and context compaction. Check the remaining time before each job, retry,
  or wait.
- Inspect the working tree, current branch, available remotes and applicable instructions.
  Identify the canonical repository and base branch from live evidence. Use an isolated checkout
  for source work and preserve existing changes, worktrees and configured Unity MCP state.
- Within the first ten minutes, use the configured GitHub MCP to enumerate open PRs including
  drafts, page through the issue backlog, and record canonical base SHA and starting counts.
  Record both the whole-backlog and in-scope production-defect counts.
- Read the latest ignored handoff when one is available. Recheck its heads, bases, live jobs,
  reviewed trees and outstanding decisions. Recover with a verified bundle or patch if needed.
  If the tracker or remote is unavailable, work from an existing authorized scope with recorded
  evidence. If no such scope is available, checkpoint the blocker. Report any live state that
  could not be verified.

## Select and Verify Work

- Check canonical main's current CI at session startup and fix actionable failures before opening
  new PRs. Review the authorized PR queue and leave outside-owner decisions with their owners.
- **Serialize deliveries.** Keep only one ordinary PR open at a time. A native GitHub stack of
  dependent PRs counts as one delivery. Use an isolated checkout and finish CI, review, merge,
  and merge readback for the whole delivery before starting another task.
- Prioritize retained player data and gameplay correctness, then runtime reliability and
  compatibility. Use the root work plan's priority order when available. Otherwise use
  the Maintain Plan skill's priority order.
  Choose the highest-impact item with a decisive witness and a credible path to delivery.
- Read the canonical issue, comments and retained evidence. Resume at the first unmet acceptance
  criterion. Record the expected failure and pass condition before editing. Record the blocker
  and stay with the active delivery while awaiting a needed platform, fixture or decision.
- Make small reversible changes. Reproduce the expected failure, pass the same check with the fix,
  and recheck after simplification. Exercise the real entry point and affected platforms.
  Check success, failure, cleanup and scale where they matter. Keep performance comparisons
  matched and preserve failed or inconclusive evidence.

## Deliver and Close

- Run the applicable local checks before publishing. Verify the exact PR head, base and integration
  tree. Run the repository feedback backstop after every push and before merge. Read inline
  threads, conversation, review summaries and check annotations. Reproduce findings before
  changing code. Require applicable CI and valid approvals on the current head.
- For licensed Unity work, verify the actual test and package evidence, plus license return and
  lock release. Check cancellation behavior before replacing a live run. Preserve original
  failures and artifacts. Respect advertised retry times and leave billing and branch protections
  intact.
- Follow the [GitHub operations skill](../skills/github-operations.md). For dependent PRs,
  use [native stacked PRs](../references/github-stacked-pull-requests.md).
  Use protected asynchronous merge with the expected head and bypass
  disabled. Confirm terminal merge and GitHub readback, fetch the canonical base branch, and
  compare the full landed tree with the qualified integration tree.
- Close an issue only when landed work proves its full acceptance. Record partial work and
  precise remaining criteria on the canonical issue. Reassess the live backlog while intake is open.
- At 120 minutes, estimate the full delivery time for the next item from observed runner, review
  and API waits. Start it only if it fits. At 150 minutes, stop new intake and use the rest for
  delivery and closeout. Before long waits, save the head, base, tree, job IDs, pending gates,
  next retry and any submitted merge UUID in the ignored handoff. Save a verified bundle or patch
  for unpublished work.
- Before the deadline, finish the handoff and report verified closures, landed PRs, the
  canonical base SHA, and exact verified counts, including a fresh draft-inclusive open-PR count
  when available. Record any unfinished objective, its blocker, and the first safe resume action.
- At the deadline, stop implementation, polling, retries, pushes, and merge actions. Let running
  workflows finish their configured cleanup. Keep unfinished acceptance open. Start a fresh clock
  only when the user explicitly authorizes another session.
