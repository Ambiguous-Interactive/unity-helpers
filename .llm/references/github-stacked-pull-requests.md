# Native GitHub Stacked Pull Requests

Use GitHub's native stack feature for dependent pull requests in current and future sessions.
Register actual stack membership and deliver the stack through its native merge operation.

## Prepare the stack

1. Inspect current branches, PRs, authors, feedback and stack membership through GitHub MCP.
2. Put each fix in its owning layer. Prepare a linear branch chain from the trunk upward.
3. Use isolated worktrees. Preserve published tips before a cascading rebase and verify the
   complete resulting source against the reviewed tree, accounting for intended new changes.
4. Publish a coherent batch with expected-head leases. Preserve contributors' work and existing
   review evidence; stop if a lease detects unexpected changes.
5. Register or verify the native stack with PR numbers ordered from bottom to top.

The user's instruction to stack and ship dependent agent-owned branches includes the normal
cascading preparation needed by native stacks. It does not authorize discarding outside changes,
bypassing rules, changing billing or broadening credentials.

## Tool order and API fallback

Follow [GitHub operations](../skills/github-operations.md). Use MCP whenever it exposes the exact
operation. If stack operations are absent, check repository helpers, announce the gap and use the
existing prompt-free token source for the REST API. Do not use the locally forbidden GitHub CLI.

The public stack API supports these operations:

- Read membership: `GET /repos/{owner}/{repo}/stacks?pull_request={number}`.
- Create membership: `POST /repos/{owner}/{repo}/stacks` with ordered `pull_requests`.
- Read the resulting stack: `GET /repos/{owner}/{repo}/stacks/{stack_number}`.
- Extend an existing stack: `POST /repos/{owner}/{repo}/stacks/{stack_number}/add`.

Read state before mutations and verify returned membership afterward. Never repeat a mutation
until readback proves that the previous request did not take effect.

## Validate and merge

GitHub applies the trunk's rules and PR workflow filters to every layer. Plan validation before
publishing, retain independent result gates and reuse completed evidence when source identity
supports it. All applicable current checks and review feedback must be resolved before landing.
Do not weaken tests or branch protections to accelerate the stack.

The hosted matrix planner needs full Git history. A native stack synthetic merge can omit the
PR’s advertised base from a shallow checkout, so `matrix-config` uses `fetch-depth: 0`.
Keep missing-base errors fatal; never substitute an empty affected-file inventory.

Merge the highest ready PR to include every open PR below it. Use the asynchronous endpoint:

- Submit `PUT /repos/{owner}/{repo}/pulls/{pull_number}/merge-async`.
- Guard the current head with `sha`; keep `bypass_rules` false.
- Use the repository's allowed merge method. This repository requires `squash`.
- Use `merge_action: default` so an existing merge queue remains effective.
- Poll `GET /repos/{owner}/{repo}/pulls/{pull_number}/merge-async/{uuid}`.

An accepted request or an `enqueued` result does not establish a merge. Read every affected PR
until its merged state and commit are verified. Fetch the trunk and verify the final tracked tree.
Preserve failures and exact blockers without retrying unchanged failures. Do not substitute serial
feature-branch squash merges for the requested native stack workflow.

## Primary references

- [Stack requirements and CI semantics](https://docs.github.com/en/pull-requests/reference/stacked-pull-requests)
- [Stack REST operations](https://docs.github.com/en/rest/pulls/stacks)
- [Asynchronous merge and result endpoints](https://docs.github.com/en/rest/pulls/pulls#merge-a-pull-request-asynchronously)
