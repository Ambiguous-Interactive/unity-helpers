# Skill: GitHub Workflow Permissions

<!-- trigger: workflow, permissions, pr, pull-request, github-actions, ci, token, automated-pr | Workflow permissions, automated PRs, debugging | Feature -->

**Trigger**: When workflows fail with permission errors, need to create automated PRs, or debug CI failures related to token permissions.

---

## When to Use

- Workflow fails with "GitHub Actions is not permitted to create or approve pull requests"
- Workflow fails with "Resource not accessible by integration"
- Setting up automated PR creation workflows
- Debugging token permission errors (403, forbidden)
- Understanding GITHUB_TOKEN vs PAT requirements
- Configuring repository permissions for CI/CD

---

## When NOT to Use

- General GitHub Actions syntax questions (see GitHub docs)
- Non-permission-related workflow failures (see [validation-troubleshooting](./validation-troubleshooting.md))
- Extracting workflow logic to scripts (see [github-actions-script-pattern](./github-actions-script-pattern.md))

---

## Repository Settings Configuration

Workflows that create pull requests require specific repository settings. Without these settings, workflows fail with:

```text
GitHub Actions is not permitted to create or approve pull requests
```

### Required Steps

1. Navigate to: `Repository > Settings > Actions > General`
2. Under **Workflow permissions**:
   - Select **Read and write permissions**
   - Check **Allow GitHub Actions to create and approve pull requests**
3. Click **Save**

### Organization-Level Restrictions

Organization admins can restrict workflow permissions at the org level:

- Navigate to: `Organization > Settings > Actions > General`
- These settings can override repository-level settings
- If repository settings appear correct but permissions fail, check org settings

---

## Workflow Permission Declaration

Always declare minimal permissions at the workflow or job level:

```yaml
permissions:
  contents: write # Push commits, create branches
  pull-requests: write # Create/update PRs
```

### Common Permission Scopes

| Permission             | Use Case                             |
| ---------------------- | ------------------------------------ |
| `contents: read`       | Clone repository, read files         |
| `contents: write`      | Push commits, create/delete branches |
| `pull-requests: read`  | Read PR metadata, comments           |
| `pull-requests: write` | Create PRs, add comments, labels     |
| `issues: write`        | Create/update issues, add labels     |
| `actions: read`        | Read workflow run details            |
| `packages: write`      | Publish to GitHub Packages           |

### Permission Hierarchy

Workflow permissions require BOTH:

1. **Workflow-level declaration** in YAML (`permissions:` block)
2. **Repository-level enablement** in Settings > Actions > General

The repository setting acts as a global gate. Even with correct YAML permissions, workflows fail if the repository setting is disabled.

---

## PR Creation Patterns

### Pattern 1: peter-evans/create-pull-request

Used in: `update-dotnet-tools.yml`

```yaml
- name: Create Pull Request
  id: create_pr
  uses: peter-evans/create-pull-request@v8.0.0
  with:
    base: main # Explicit base branch
    branch: chore/my-update # PR head branch
    delete-branch: true # Clean up after merge
    title: "chore: my update"
    commit-message: "chore: description"
    body: |
      Automated update description.
    labels: dependencies
    assignees: wallstop
    reviewers: wallstop

- name: PR created summary
  if: steps.create_pr.outputs.pull-request-number
  run: |
    {
      echo "## PR Created"
      echo "PR #${{ steps.create_pr.outputs.pull-request-number }}"
      echo "URL: ${{ steps.create_pr.outputs.pull-request-url }}"
    } >> "$GITHUB_STEP_SUMMARY"
```

### Pattern 2: actions/github-script

Used in: `csharpier-autofix.yml`, `prettier-autofix.yml`

```yaml
- name: Create PR
  uses: actions/github-script@v7
  with:
    script: |
      const { data: pr } = await github.rest.pulls.create({
        owner: context.repo.owner,
        repo: context.repo.repo,
        title: 'chore: automated update',
        head: 'bot/update-branch',
        base: 'main',
        body: 'Automated update.'
      });
      core.setOutput('pr_number', pr.number);
      core.setOutput('pr_url', pr.html_url);
```

### Pattern 3: gh CLI Inside a Workflow Runner

This is tracked workflow code executed by GitHub Actions. It is not permission for an agent to use
the local `gh` CLI; agent-initiated GitHub operations follow
[github-operations](./github-operations.md) and use GitHub MCP first.

```yaml
- name: Create PR
  env:
    GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
  run: |
    gh pr create \
      --title "chore: automated update" \
      --body "Automated update description." \
      --base main \
      --head my-branch \
      --label dependencies
```

---

## GITHUB_TOKEN Limitations

PRs created with `GITHUB_TOKEN` have important limitations:

| Limitation                 | Description                      | Workaround                      |
| -------------------------- | -------------------------------- | ------------------------------- |
| No workflow triggers       | PRs don't trigger CI workflows   | Use PAT or GitHub App           |
| No protected branch bypass | Can't push to protected branches | Use PAT with bypass permissions |
| Scoped to current repo     | Can't access other repos         | Use PAT with cross-repo access  |

### Fork PR Limitations

PRs from forked repositories receive a read-only `GITHUB_TOKEN` by default for security:

- Fork PRs cannot write to the base repository
- Fork PRs cannot access repository secrets (except `GITHUB_TOKEN`)
- Workflows triggered by `pull_request` from forks have restricted permissions

### When to Use a PAT

Use a Personal Access Token (stored as a secret) when:

- The created PR must trigger CI workflows
- The workflow needs cross-repository access
- Protected branch rules need bypassing

```yaml
- name: Create PR with PAT
  uses: peter-evans/create-pull-request@v8.0.0
  with:
    token: ${{ secrets.PAT_TOKEN }} # Not GITHUB_TOKEN
    # ... other options
```

Fine-grained PATs (recommended over classic PATs) allow scoped permissions per repository.

---

## Debugging Workflow Failures

### Reading CI Logs

1. Go to the **Actions** tab in the repository
2. Click the failed workflow run
3. Expand the failed job and step
4. Look for `##[error]` lines for the actual error message

### Common Failure Patterns

| Error Message                                                        | Cause                                | Fix                                            |
| -------------------------------------------------------------------- | ------------------------------------ | ---------------------------------------------- |
| "GitHub Actions is not permitted to create or approve pull requests" | Repository setting disabled          | Enable in Settings > Actions > General         |
| "Resource not accessible by integration"                             | Insufficient token permissions       | Add required permissions to workflow           |
| "refusing to allow a GitHub App to create or update workflow"        | Modifying `.github/workflows/` files | Use PAT with `workflow` scope                  |
| "The requested URL returned error: 403"                              | Token lacks required scope           | Check permissions block                        |
| "push declined due to branch protections"                            | Branch protection blocking push      | Use PAT with bypass or target different branch |

### Debugging Checklist

1. **Check workflow permissions block** - Is the required permission declared?
2. **Check repository settings** - Is "Allow GitHub Actions to create and approve pull requests" enabled?
3. **Check organization settings** - Are org-level restrictions overriding repo settings?
4. **Check branch protection** - Does the target branch have rules blocking the action?
5. **Check token type** - Is `GITHUB_TOKEN` sufficient or is a PAT needed?
6. **Check action version** - Is the action up-to-date and not deprecated?

### Enabling Debug Logging

Add repository secrets to enable verbose logging:

- `ACTIONS_RUNNER_DEBUG`: Set to `true` for runner diagnostic logs
- `ACTIONS_STEP_DEBUG`: Set to `true` for step debug logs

Or use the "Re-run jobs" dropdown and select "Enable debug logging".

---

## Best Practices Checklist

### Workflow Configuration

- [ ] Declare minimal `permissions:` block
- [ ] Use explicit `base:` branch in PR creation
- [ ] Add `delete-branch: true` for auto-cleanup
- [ ] Capture PR outputs with `id:` for summaries
- [ ] Add job summaries with `$GITHUB_STEP_SUMMARY`
- [ ] Use grouped commands for multiple redirects (shellcheck SC2129)

### Repository Configuration

- [ ] Enable "Read and write permissions" for workflows
- [ ] Enable "Allow GitHub Actions to create and approve pull requests"
- [ ] Configure branch protection rules to allow required actions
- [ ] Store PATs as repository secrets (not in workflow files)

### Security

- [ ] Use minimal permission scope
- [ ] Pin actions to specific versions (not `@main`)
- [ ] Review third-party actions before use
- [ ] Don't expose tokens in logs (`add-mask` if needed)
- [ ] Use environment protection for production deployments

---

## Agent-Side Inspection and Control

Use GitHub MCP first to inspect workflow status, failed logs, repository permissions, and available
workflows, or to dispatch a workflow. Tool names vary by frontend; choose the tools backed by the
configured `github` MCP server rather than assuming a particular prefix.

- For status, read the workflow run, its jobs, and failed-step logs through GitHub MCP.
- For permissions, read the repository Actions permissions through GitHub MCP when that endpoint is
  exposed.
- For a manual run, dispatch the named workflow through GitHub MCP, then verify and monitor the new
  run to a terminal state.

If the current MCP toolset lacks the exact endpoint, use the fallback order in
[github-operations](./github-operations.md). Never substitute the local `gh` CLI.

---

## Related Skills

- [github-operations](./github-operations.md) - GitHub MCP-first remote operations and fallbacks
- [github-actions-script-pattern](./github-actions-script-pattern.md) - Extract workflow logic to testable scripts
- [validate-before-commit](./validate-before-commit.md) - Pre-push validation workflow
- [validation-troubleshooting](./validation-troubleshooting.md) - Common CI failure fixes
