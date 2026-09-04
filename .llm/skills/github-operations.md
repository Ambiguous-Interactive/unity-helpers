<!-- trigger: github, pull request, issue, review, check run, workflow run, release | Use the configured GitHub MCP server before other remote GitHub tooling | Core -->

# GitHub Operations

Use this guide for every agent-initiated interaction with GitHub. It governs remote reads and
mutations, including pull requests, issues, reviews, comments, checks, workflow runs and logs,
releases, tags, branches, repository metadata, and settings.

## Non-Negotiable Tool Order

1. Use the configured **GitHub MCP server first** for every remote GitHub operation it exposes.
2. Use plain `git` for local repository state and Git transport: status, diff, log, worktrees,
   staging, commits, fetch, and push.
3. If the GitHub MCP server is genuinely unavailable or lacks the exact operation, prefer an
   existing repository script that implements the operation and its safety checks.
4. Only then use the direct REST API or GraphQL API with the prompt-free credential source below.
5. **Never use the local `gh` CLI for agent work.** Its presence in the image is not permission or
   a fallback. Tracked workflow code may invoke `gh` on a GitHub-hosted or self-hosted runner.

Plain `git` does not replace GitHub MCP. A normal shipping flow pushes commits with `git`, then uses
GitHub MCP to create or inspect the pull request, read reviews and checks, monitor workflow runs,
and merge. Do not prefer an editor extension, editor connector, app connector, shell command, or ad
hoc HTTP request over an available GitHub MCP capability.

## What "Available" Means

Treat GitHub MCP as available when the current frontend exposes tools backed by the configured
`github` MCP server. Tool prefixes and discovery UI vary by frontend, so identify the provider by
its server provenance and capability description rather than by one hard-coded tool name.

Before declaring it unavailable:

1. Inspect the tools already exposed by the frontend.
2. Use the frontend's tool-discovery mechanism when MCP tools are loaded lazily.
3. Confirm that the missing item is the operation itself, not merely a guessed tool name.
4. If the server returned a transient transport error, retry once after checking the error.
5. If authentication failed, report authentication failure; do not relabel it as missing tooling.

An MCP tool gap is operation-specific. A server that cannot cancel a workflow run may still be the
required first choice for reading that run, its jobs, and its logs.

## Match the Tool to the Boundary

| Work                                 | First choice                                       | Reason                                                |
| ------------------------------------ | -------------------------------------------------- | ----------------------------------------------------- |
| Read or change GitHub-hosted state   | GitHub MCP                                         | It is the shared, authenticated GitHub control plane. |
| Inspect or edit the local checkout   | Local filesystem tools                             | No remote GitHub state is involved.                   |
| Create commits or manage worktrees   | Plain `git`                                        | These are local repository operations.                |
| Fetch or push Git objects            | Plain `git`                                        | MCP does not replace Git transport.                   |
| Verify all four PR feedback surfaces | GitHub MCP, then `npm run pr:feedback -- <number>` | The repository script is the coverage backstop.       |
| Unsupported remote MCP operation     | Repository script, then direct API                 | Preserve automation without choosing `gh`.            |

## Remote Operation Procedure

For every GitHub read or mutation:

1. State the repository, object, and intended operation precisely.
2. Read current state through GitHub MCP before mutating when the target or transition matters.
3. Choose the narrowest MCP tool that performs the operation.
4. Supply only the fields required by that tool. Never place credentials in tool arguments.
5. Treat the tool result as evidence, not intent. Verify the returned number, URL, state, or SHA.
6. For asynchronous work, poll the same object until it reaches the requested terminal state.
7. If the MCP operation fails, preserve its error and classify the failure before using a fallback.

Do not repeat mutations speculatively. Before retrying a create, merge, dispatch, rerun, cancel,
comment, or release operation, read state and prove the first request did not already take effect.

## Pull Requests and Reviews

- Push the branch once with plain `git`, then use GitHub MCP to find or create the pull request.
- Use GitHub MCP for PR metadata, mergeability, requested reviewers, reviews, discussion comments,
  inline comments, check status, and merge operations whenever those capabilities are exposed.
- Follow the title and body limits in [ship-changes](./ship-changes.md). Write bodies to a temporary
  file when a fallback API needs them; do not risk shell interpolation of Markdown.
- After every push and immediately before completion, inspect feedback through GitHub MCP first.
  Then run `npm run pr:feedback -- <number>` as the repository-owned coverage check for inline
  review threads, review submissions, conversation comments, and check-run annotations.
- Treat line comments as potentially general policy: fix the line, sweep the class, and decide
  whether a durable rule is warranted.
- Before merging, verify the current head SHA and every repository-owned required check. After
  merging, verify the merge state and merge commit, then fetch `main` with plain `git`.

## Checks and Workflow Runs

- Use GitHub MCP first to list checks, inspect workflow runs and jobs, read annotations and failed
  logs, dispatch workflows, rerun jobs, or cancel obsolete runs.
- Distinguish queued, in-progress, completed, skipped, cancelled, neutral, and failed states. A
  queued job is not evidence of failure; a successful request to enable auto-merge is not evidence
  that a PR has merged.
- Poll long-running work instead of guessing. Keep the user updated while a requested terminal
  outcome is pending.
- Read the failed step and its log before changing code. Prove whether the cause is the change,
  runner infrastructure, authentication, an external app entitlement, or stale/closed-PR policy.
- Re-read state before rerunning or cancelling. Do not spend shared runner capacity on work made
  obsolete by a newer SHA or an already-merged pull request.
- Tracked `.github/workflows/**` scripts may use `gh` in CI. This exception does not permit an agent
  to invoke the local `gh` executable while operating GitHub.

## Issues, Releases, and Repository Settings

- Search through GitHub MCP before creating an issue, release, tag, or other named object to avoid
  duplicates.
- Use GitHub MCP for issue creation, edits, labels, comments, and closure when available. Verify the
  issue number, state, and URL after a mutation.
- Use GitHub MCP for release and tag reads or mutations when available. Re-read the tag target and
  published release assets after a write.
- Treat settings, permissions, secrets, environments, branch protections, and destructive release
  changes as high-impact. Read current state first and stay within the user's authorization.
- Never expose secret values while inspecting configuration. Report presence, source, or failure,
  not the credential itself.

## Safe Fallbacks

When no exposed GitHub MCP tool can perform the exact remote operation, **announce the missing
capability in the same message that runs the fallback, before running it**, then use this order.
Record which capability was missing in that announcement.

Naming the gap at the final handoff is too late. A `curl` or a script invocation that arrives with
no explanation is indistinguishable from bypassing MCP out of habit -- which is the thing this guide
exists to prevent -- and the reader cannot audit a decision they were not shown. Say which operation
you needed, which server you asked, and what it did not expose: "the GitHub MCP server exposes no
workflow-run listing, so this reads the Actions API directly."

1. A repository-owned script, such as `npm run pr:feedback -- <number>`, when one exists.
2. A direct REST API or GraphQL API request using a token obtained exactly once with:

   ```bash
   TOKEN="$(bash scripts/github-token.sh)"
   export TOKEN
   ```

   Pass the token from the environment inside the HTTP client process. Do not interpolate it into
   a command-line argument, URL, log message, temporary file, or working-tree file.

3. If `scripts/github-token.sh` exits 3, stop that fallback and ask the user to run
   `npm run github:token:bootstrap` or `npm run github:token:store`.

Never run `git credential fill` or invoke the Dev Containers credential helper directly. Those can
raise a dialog on the owner's desktop. Never use `gh`, even when MCP and direct API attempts fail;
report the exact missing capability or external blocker instead.

For fetch and push failures, run `npm run check:container-git-credentials`; use `-- --fix` when it
reports repairable configuration drift. The Git credential helper and the GitHub MCP server share
the same durable token sources, but success in one path does not prove the other path is configured.

## Evidence in the Handoff

Report the remote outcome, not merely the request:

- Include the object URL or number for a created issue or pull request.
- Include the terminal state and relevant SHA for a merge.
- Identify any check that did not run and why.
- Name an MCP capability gap when a fallback was necessary.
- Never include tokens, authorization headers, or credential file contents.

## Related Skills

- [ship-changes](./ship-changes.md) - Commit, publish, review, and merge workflow
- [github-workflow-permissions](./github-workflow-permissions.md) - Permissions inside workflow code
- [github-actions-shell-scripting](./github-actions-shell-scripting.md) - Shell authored for runners
- [git-safe-operations](./git-safe-operations.md) - Safe local Git and staging operations
