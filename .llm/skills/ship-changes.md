# Skill: Ship Changes

<!-- trigger: ship, release, finalize, pre-landing, merge-ready, pr-ready | End-to-end workflow for shipping changes: validate, review, version, changelog, commit | Core -->

**Trigger**: When changes are ready to be finalized, committed, and prepared for merge.

---

## When to Use

- After implementation is complete and you want to ship
- When preparing a PR or final commit
- When asked to "ship it", "finalize", or "wrap up"

---

## When NOT to Use

- When implementation is still in progress
- When there are known failing tests or unresolved issues
- For documentation-only changes (use [update-documentation](./update-documentation.md) directly)

---

## Ship Workflow

Execute these steps in order. Each step must pass before proceeding.

### Step 1: Pre-Flight Checks

Run changed-file preflight, all checks relevant to the change, and the fast push safety check:

```bash
npm run agent:preflight
npm run validate:prepush
```

`validate:prepush` is deliberately a roughly one-second last-resort Git/config check. Do not expand it with
repository-wide lint or test suites. Run targeted lint/test commands for the files changed; use
`npm run validate:local` only when a complete repository-wide aggregate is warranted. Exhaustive
synthetic Git-hook fixtures remain mandatory in CI; if hook or agent-preflight behavior changed,
also run `npm run validate:tests:hook-regressions`. **All applicable checks must pass.**

**Blocker rule — do NOT push if any of these fail:**

- Any applicable targeted lint, formatting, typecheck, or test command — a failure must be fixed at
  Step 1, never deferred to push-time or CI.
- `validate:local` — mandatory only when the change's risk or breadth warrants the complete
  repository-wide aggregate.
- `validate:tests:hook-regressions` — mandatory when `.githooks/**`, `scripts/agent-preflight.ps1`,
  or their helpers/tests change.

If any check fails:

1. Fix the issue (see [validate-before-commit](./validate-before-commit.md#rule-4-spell-check-every-change-cspell-covers) for the spelling decision tree)
2. Re-run the failing check in isolation
3. When all pass, re-run the relevant targeted command, then the fast `npm run validate:prepush`
4. Only then proceed

### Step 2: Test Verification

Verify tests pass. If this is a Unity package change:

1. Confirm all existing tests still pass conceptually (note: Unity tests require Unity Editor)
2. Verify new tests exist for new functionality
3. Check test naming follows conventions: `MethodName_Condition_ExpectedResult`

### Step 3: Pre-Landing Review

Execute a [review-code-changes](./review-code-changes.md) pass on all staged/modified files:

1. Run two-pass review (Critical, then Informational)
2. Auto-fix mechanical issues (formatting, spelling, missing null checks)
3. Track risk score per [self-regulate-changes](./self-regulate-changes.md)
4. If critical issues found, fix and restart from Step 1

### Step 4: CHANGELOG Update

If changes include user-facing modifications:

1. Add entry under `## [Unreleased]` section
2. Use correct subsection: `### Added`, `### Fixed`, `### Changed`, `### Removed`
3. Reference issue numbers where applicable: `[#NNN](https://github.com/wallstop/unity-helpers/issues/NNN)` <!-- cspell:ignore NNN -->
4. **Keep entries SHORT — one or two sentences, plain language, user-visible effect first.** No
   root-cause narration, no mechanism, no run IDs. Put the long version in the commit body and, if
   users need it, a `docs/` guide you link to. See
   [update-documentation](./update-documentation.md#writing-good-changelog-entries).

### Step 5: Documentation Check

Per [update-documentation](./update-documentation.md):

1. Public API changes have XML doc comments
2. README updated if public-facing behavior changed
3. Skill files updated if workflow changed
4. `.meta` files exist for all new assets

### Step 6: Version Assessment

Assess whether version bump is needed (do NOT bump automatically — note for human):

| Change Type                      | Version Impact        |
| -------------------------------- | --------------------- |
| Bug fix, no API change           | Patch (3.2.1 → 3.2.2) |
| New feature, backward compatible | Minor (3.2.1 → 3.3.0) |
| Breaking API change              | Major (3.2.1 → 4.0.0) |
| Internal refactor only           | No bump needed        |

Report assessment but do not modify `package.json` version without explicit approval.

### Step 7: Commit Hygiene

Ensure commits are bisectable:

| Rule                      | Description                                              |
| ------------------------- | -------------------------------------------------------- |
| **Each commit compiles**  | No commit should leave the project in a broken state     |
| **Each commit is atomic** | One logical change per commit                            |
| **Message format**        | Imperative mood, <72 chars first line, body explains why |
| **No fixup commits**      | Squash "fix typo" commits into the original              |

### Step 8: Ship Summary

Output a final summary:

```text
Ship Summary:
  Pre-flight: PASS
  Tests: PASS | N/A (requires Unity Editor)
  Review: PASS (risk score: N/25)
  CHANGELOG: Updated | Not needed
  Documentation: Updated | Not needed
  Version: No bump needed | Recommend PATCH/MINOR/MAJOR
  Commits: N commits, all bisectable
  Ready to merge: YES | NO (blockers: list)
```

### Step 9: Push to Remote

The repo pre-configures `push.autoSetupRemote=true` and `push.default=simple`
locally during `npm run hooks:install` (and the devcontainer post-create), so
`git push` on a new branch sets upstream automatically — **do not** pass
`--set-upstream` / `-u` flags and never run wrapper scripts around `git push`.

Rules when pushing:

| Rule                           | Why                                                                          |
| ------------------------------ | ---------------------------------------------------------------------------- |
| **Never redirect output**      | `git push 2> pre-push.txt` creates gitignored pollution that confuses agents |
| **Never use `--no-verify`**    | Bypassing the pre-push hook skips the last-resort local safety gate          |
| **Let stderr stream normally** | Errors must be visible in the live output, not hidden in files               |

If `fatal: The current branch <x> has no upstream branch` appears, the local
config is missing. Remediation: `npm run agent:preflight:fix` (restores
`push.autoSetupRemote=true` and removes any stray
`<hook-name>.{txt,log,out,err,tmp}` artifact files). Do **not** work around it
with `git push -u origin <branch>`
— fix the config once so every future push is clean.

If a push is rejected for non-fast-forward reasons, prefer
`git pull --rebase`. Stash any unrelated local changes manually first; never
silently clobber history with `--force` without explicit user consent.

### Step 10: Read the checks, and know which ones are ours

"All checks green" means **every repository-owned check**: the workflows in
`.github/workflows/`, which this repository can fix. A pull request also carries
checks from GitHub Apps whose success depends on an account entitlement rather
than on the code, and those cannot be driven green from a branch.

The known case is the automatic Copilot reviewer (#428). Its signature:

| Signal                                      | Reading                           |
| ------------------------------------------- | --------------------------------- |
| `copilot-pull-request-reviewer` fails       | Not a repository workflow         |
| HTTP 402 / `exceeded your monthly quota`    | Account entitlement, not the diff |
| No review comments and no analysis produced | It never read the code            |
| Sub-minute duration                         | It failed before reviewing        |

**Policy: that failure does not block landing, and re-pushing cannot clear it.**
Every push re-requests the review and reproduces it. Record it in the pull
request summary as an external check, keep the repository's own checks green,
and rely on the Cursor review plus CI. Restoring the quota, or dropping the
reviewer from the required set, is an organization-settings action for the
owner — never work around it by requesting bot reviews by hand.

Anything else red is ours until proven otherwise. Read the annotations before
concluding a leg is infrastructure: a `Stale pull request run for <sha>` marks a
run the head moved past, not breakage.

---

## Related Skills

- [review-code-changes](./review-code-changes.md) - Pre-landing review (Step 3)
- [self-regulate-changes](./self-regulate-changes.md) - Risk scoring during review
- [validate-before-commit](./validate-before-commit.md) - Pre-flight checks (Step 1)
- [update-documentation](./update-documentation.md) - Documentation check (Step 5)
- [apply-completeness](./apply-completeness.md) - Don't ship incomplete work
