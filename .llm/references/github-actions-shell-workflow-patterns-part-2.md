# github-actions-shell-workflow-patterns - Part 2

## Split Content

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

### Pattern 8: Local Actions Depend on This Job's Checkout

An explicit status function can run a local action after checkout was skipped, failed or cancelled.
Give the checkout an `id` and include `steps.checkout.outcome == 'success'` in diagnostic/redaction
conditions alongside `always()` or the existing failure/cancellation predicate. Files left on a
self-hosted runner are not evidence that the current checkout succeeded. Preserve license return
and lock release conditions based on actual acquisition. Exercise the real predicates for every
checkout outcome, including failure/cancellation after a successful checkout (#714).

---

## Related Skills

- [github-actions-shell-scripting](../skills/github-actions-shell-scripting.md) - Overview, checklist, and scope.
- [github-actions-shell-foundations](../skills/github-actions-shell-foundations.md) - Inline shell safety and text handling patterns.
