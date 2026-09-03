# Skill: GitHub Actions Shell Scripting

<!-- trigger: workflow shell, actions bash, gh api, runner temp, heredoc | Shell scripting best practices for GitHub Actions | Core -->

**Trigger**: When you need an entry point for GitHub Actions inline shell best practices, checklists, and cross-links.

This skill governs shell committed to a workflow and executed on a runner. It does not authorize
agent-side `gh` or `curl` calls. Agent-initiated GitHub operations follow
[github-operations](./github-operations.md) and use GitHub MCP first.

---

## When to Use

- Starting a new inline `run:` step and want the standard guardrails
- Auditing existing GitHub Actions shell steps for common risks
- Looking for the right sub-skill for a specific pattern (outputs, polling, API calls)

---

## When NOT to Use

- The logic can be moved into a standalone script (use [github-actions-script-pattern](./github-actions-script-pattern.md))
- A maintained action already provides the needed behavior
- The change is pure workflow YAML without shell scripting

---

## How to Use This Skill

- Use [github-actions-shell-foundations](./github-actions-shell-foundations.md) for strict mode, heredocs, text parsing, temp files, and safe variable handling
- Use [github-actions-shell-workflow-patterns](./github-actions-shell-workflow-patterns.md) for outputs, polling, API calls, idempotency, annotations, and step summaries

---

## Checklist

- [ ] Start every `run:` block with `set -euo pipefail` (see [github-actions-shell-foundations](./github-actions-shell-foundations.md))
- [ ] Use `$RUNNER_TEMP` for temp files and API payloads (see [github-actions-shell-foundations](./github-actions-shell-foundations.md))
- [ ] Avoid inline multiline bodies; write to file or use env + `printf` (see [github-actions-shell-foundations](./github-actions-shell-foundations.md))
- [ ] Prefer action outputs over polling; if polling, use backoff and timeouts (see [github-actions-shell-workflow-patterns](./github-actions-shell-workflow-patterns.md))
- [ ] Add retry logic for critical API writes (see [github-actions-shell-workflow-patterns](./github-actions-shell-workflow-patterns.md))
- [ ] Use `GITHUB_OUTPUT`, `GITHUB_ENV`, and `GITHUB_STEP_SUMMARY` correctly (see [github-actions-shell-workflow-patterns](./github-actions-shell-workflow-patterns.md))
- [ ] Mask secrets and add annotations for visibility (see [github-actions-shell-workflow-patterns](./github-actions-shell-workflow-patterns.md))
- [ ] Run `actionlint` before commit (see [validate-before-commit](./validate-before-commit.md))

---

## Related Skills

- [github-operations](./github-operations.md) - GitHub MCP-first agent operations.
- [github-actions-shell-foundations](./github-actions-shell-foundations.md) - Inline shell safety patterns.
- [github-actions-shell-workflow-patterns](./github-actions-shell-workflow-patterns.md) - Outputs, polling, API calls, summaries.
- [github-actions-script-pattern](./github-actions-script-pattern.md) - Prefer extracting scripts for complex logic.
- [validate-before-commit](./validate-before-commit.md) - actionlint and workflow validation.
