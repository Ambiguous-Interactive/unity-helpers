# github-workflow-permissions - Part 1

## Split Content

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
- Non-permission-related workflow failures (see [validation-troubleshooting](../skills/validation-troubleshooting.md))
- Extracting workflow logic to scripts (see [github-actions-script-pattern](../skills/github-actions-script-pattern.md))

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
[github-operations](../skills/github-operations.md) and use GitHub MCP first.

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
