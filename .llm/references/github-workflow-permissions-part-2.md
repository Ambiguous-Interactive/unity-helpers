# github-workflow-permissions - Part 2

## Split Content

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
[github-operations](../skills/github-operations.md). Never substitute the local `gh` CLI.

---

## Related Skills

- [github-operations](../skills/github-operations.md) - GitHub MCP-first remote operations and fallbacks
- [github-actions-script-pattern](../skills/github-actions-script-pattern.md) - Extract workflow logic to testable scripts
- [validate-before-commit](../skills/validate-before-commit.md) - Pre-push validation workflow
- [validation-troubleshooting](../skills/validation-troubleshooting.md) - Common CI failure fixes
