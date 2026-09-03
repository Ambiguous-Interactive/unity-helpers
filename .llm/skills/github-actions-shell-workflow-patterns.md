# Skill: GitHub Actions Shell Workflow Patterns

<!-- trigger: actions shell, outputs, polling, gh api, step summary | Workflow integration patterns for GitHub Actions shell steps | Core -->

**Trigger**: When inline shell steps must integrate with GitHub Actions outputs, API calls, polling, or job summaries.

The `gh` and `curl` examples below are tracked commands executed inside a GitHub Actions runner.
They do not define agent-side tooling. For agent-initiated GitHub operations, follow
[github-operations](./github-operations.md) and use GitHub MCP first.

---

## When to Use

- Reading outputs from other actions
- Polling for asynchronous operations
- Calling GitHub API with `gh api` or `curl`
- Ensuring steps are idempotent on re-runs
- Publishing job outputs or step summaries

---

## When NOT to Use

- The action already exposes outputs you can consume directly
- The logic can be moved into a standalone script
- A maintained action already provides the needed behavior

---

## Patterns

### Pattern 1: Use Action Outputs Instead of Polling

Many actions provide outputs directly. Always check action documentation before implementing polling.

```yaml
# ❌ BAD: Polling for release ID after release-drafter runs
- uses: release-drafter/release-drafter@v6
  # No id: specified, so outputs not accessible

- name: Find release
  run: |
    # Wasteful polling loop
    for i in {1..10}; do
      RELEASE_ID=$(gh api repos/... --jq '...')
      if [ -n "$RELEASE_ID" ]; then break; fi
      sleep 5
    done

# ✅ GOOD: Use action outputs directly (most actions provide them)
- name: Draft release
  id: release_drafter
  uses: release-drafter/release-drafter@v6

- name: Update release
  env:
    RELEASE_ID: ${{ steps.release_drafter.outputs.id }}
    RELEASE_URL: ${{ steps.release_drafter.outputs.html_url }}
  run: |
    set -euo pipefail
    if [ -z "$RELEASE_ID" ]; then
      echo "::error::No release ID from action"
      exit 1
    fi
    gh api "repos/.../releases/$RELEASE_ID" -X PATCH ...
```

**Tip**: Check the action README or action.yml for available outputs before writing polling code.

### Pattern 2: Polling With Exponential Backoff (When Necessary)

When polling is truly required (external APIs without webhooks), use proper timeouts.

```bash
# ❌ BAD: No timeout, fixed delay, silent failures
run: |
  while true; do
    STATUS=$(gh api ...)
    if [ "$STATUS" = "completed" ]; then break; fi
    sleep 10
  done

# ✅ GOOD: Timeout, exponential backoff, stderr captured for debugging
run: |
  set -euo pipefail
  MAX_ATTEMPTS=10
  DELAY=5
  API_STDERR="${RUNNER_TEMP}/api_stderr.log"

  for attempt in $(seq 1 $MAX_ATTEMPTS); do
    echo "Attempt $attempt/$MAX_ATTEMPTS: Checking status..."

    STATUS=$(gh api repos/${{ github.repository }}/actions/runs/$RUN_ID \
      --jq '.status' 2>"$API_STDERR") || STATUS="error"

    if [ -s "$API_STDERR" ]; then
      echo "::debug::API stderr: $(cat "$API_STDERR")"
    fi

    if [ "$STATUS" = "completed" ]; then
      echo "Operation completed successfully"
      exit 0
    fi

    if [ "$attempt" -eq "$MAX_ATTEMPTS" ]; then
      echo "::error::Timeout after $MAX_ATTEMPTS attempts"
      exit 1
    fi

    echo "Status: $STATUS. Waiting ${DELAY}s..."
    sleep "$DELAY"
    DELAY=$((DELAY * 2))  # Exponential backoff
  done
```

### Pattern 3: GitHub API Error Handling

Always check API responses and handle rate limits.

```bash
# ❌ BAD: No error checking, silent failures
run: |
  RESULT=$(gh api repos/$REPO/pulls)
  echo "$RESULT" | jq '.[0].number'

# ✅ GOOD: Check response, handle errors, capture stderr for debugging
run: |
  set -euo pipefail
  API_STDERR="${RUNNER_TEMP}/api_stderr.log"

  if ! RESULT=$(gh api repos/${{ github.repository }}/pulls \
    --header "Accept: application/vnd.github+json" \
    --jq '.[0].number' 2>"$API_STDERR"); then

    # Log stderr for debugging
    if [ -s "$API_STDERR" ]; then
      echo "::debug::API stderr: $(cat "$API_STDERR")"
    fi

    if grep -q "rate limit" "$API_STDERR" 2>/dev/null; then
      echo "::warning::Rate limited, waiting 60s..."
      sleep 60
      RESULT=$(gh api repos/${{ github.repository }}/pulls --jq '.[0].number')
    else
      echo "::error::API call failed: $RESULT"
      exit 1
    fi
  fi

  echo "PR Number: $RESULT"
```

### Pattern 3b: API Retry with Exponential Backoff

While Pattern 3 handles single-request error detection, critical API operations (updates, patches, creates) need retry logic for transient failures.

```bash
# ❌ BAD: No retry - transient failures cause workflow failure
run: |
  gh api "repos/$REPO/releases/$ID" -X PATCH -F body=@body.md

# ✅ GOOD: Retry with exponential backoff for transient API failures
run: |
  set -euo pipefail

  # Retry parameters: 3 attempts, starting at 2 seconds
  MAX_ATTEMPTS=3
  DELAY=2

  for attempt in $(seq 1 $MAX_ATTEMPTS); do
    if gh api "repos/${{ github.repository }}/releases/$RELEASE_ID" \
      -X PATCH \
      -F body=@"${RUNNER_TEMP}/new_body.md"; then
      echo "API update succeeded on attempt $attempt"
      break
    fi

    # Check if we've exhausted retries
    if [ "$attempt" -eq "$MAX_ATTEMPTS" ]; then
      echo "::error::API call failed after $MAX_ATTEMPTS attempts"
      exit 1
    fi

    echo "::warning::Attempt $attempt failed, retrying in ${DELAY}s..."
    sleep "$DELAY"
    DELAY=$((DELAY * 2))  # 2s -> 4s -> 8s
  done
```

**Key differences from polling backoff (Pattern 2):**

| Aspect            | Polling (Pattern 2)                  | Retry (Pattern 3b)                             |
| ----------------- | ------------------------------------ | ---------------------------------------------- |
| **Purpose**       | Wait for async operation to complete | Retry failed synchronous operation             |
| **Trigger**       | Status not yet "completed"           | API call returned error                        |
| **Max attempts**  | Higher (10+) - waiting is expected   | Lower (3-5) - failures are exceptional         |
| **Initial delay** | Longer (5-10s) - reduce API load     | Shorter (1-2s) - fail fast on permanent errors |

### Pattern 4: Idempotency Checks

Workflows may be re-run manually or due to failures. Check if an operation was already done.

```bash
# ❌ BAD: Blindly appends, creating duplicates on re-run
run: |
  set -euo pipefail
  {
    echo "## Changelog"
    cat changelog.md
  } >> release_body.md
  gh api ... -F body=@release_body.md

# ✅ GOOD: Check before modifying to prevent duplicates
run: |
  set -euo pipefail

  # Check if changelog already exists (case-insensitive, allow leading whitespace)
  if grep -qiE '^\s*## Changelog' "${RUNNER_TEMP}/current_body.md"; then
    echo "::notice::Changelog already present, skipping"
    exit 0
  fi

  # Safe to add changelog
  {
    echo "## Changelog"
    cat changelog.md
    cat "${RUNNER_TEMP}/current_body.md"
  } > "${RUNNER_TEMP}/new_body.md"
  gh api ... -F body=@"${RUNNER_TEMP}/new_body.md"
```

### Pattern 5: Job Outputs and Step Summaries

Export job-level outputs for downstream jobs and create visible summaries.

```yaml
jobs:
  build:
    runs-on: ubuntu-latest
    outputs:
      version: ${{ steps.extract.outputs.version }}
      release-id: ${{ steps.release.outputs.id }}
    steps:
      - id: extract
        run: echo "version=1.2.3" >> "$GITHUB_OUTPUT"
```

```bash
run: |
  set -euo pipefail

  # Create markdown summary visible in workflow run UI
  {
    echo "### Release Draft Updated"
    echo ""
    echo "| Property | Value |"
    echo "|----------|-------|"
    echo "| **Version** | \`$VERSION\` |"
    echo "| **Release ID** | \`$RELEASE_ID\` |"
    echo "| **URL** | $RELEASE_URL |"
  } >> "$GITHUB_STEP_SUMMARY"
```

### Pattern 6: Environment Variables and Outputs

Use the GitHub-provided files for outputs, environment variables, and PATH updates. Use random delimiters for multiline outputs to prevent injection or truncation.

```bash
run: |
  set -euo pipefail

  # Set output for other steps
  echo "version=1.2.3" >> "$GITHUB_OUTPUT"

  # Set environment for subsequent steps
  echo "MY_VAR=value" >> "$GITHUB_ENV"

  # Add to PATH for subsequent steps
  echo "$HOME/.local/bin" >> "$GITHUB_PATH"

  # Multiline output (use RANDOM delimiter to prevent injection)
  DELIMITER="__EOF_$(date +%s%N)_${RANDOM}__"
  {
    printf 'changelog<<%s\n' "$DELIMITER"
    cat CHANGELOG.md
    printf '%s\n' "$DELIMITER"
  } >> "$GITHUB_OUTPUT"
```

**Warning**: Never use a fixed delimiter like `EOF` for multiline outputs. If the content contains the delimiter on its own line, it terminates early and corrupts output.

### Pattern 7: GitHub Actions Annotations

Use workflow commands for structured output, grouping, and masking secrets.

```bash
run: |
  set -euo pipefail

  # Errors (fail the step visually)
  echo "::error file=src/main.cs,line=10::Null reference found"

  # Warnings (yellow badge)
  echo "::warning::Deprecated API usage detected"

  # Debug (only shown with debug logging enabled)
  echo "::debug::Processing file: $FILE"

  # Group output for collapsible sections
  echo "::group::Installation logs"
  npm install
  echo "::endgroup::"

  # Mask sensitive values
  echo "::add-mask::$SECRET_VALUE"
```

---

## Related Skills

- [github-actions-shell-scripting](./github-actions-shell-scripting.md) - Overview, checklist, and scope.
- [github-actions-shell-foundations](./github-actions-shell-foundations.md) - Inline shell safety and text handling patterns.
