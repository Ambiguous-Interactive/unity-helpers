# github-actions-shell-foundations - Part 1

## Split Content

**Trigger**: When writing inline shell in GitHub Actions workflows, especially for strict mode, temp files, heredocs, quoting, and text processing.

The `gh` examples in this skill are tracked commands that execute inside a GitHub Actions runner.
They are not agent-side commands. For agent-initiated GitHub reads or mutations, follow
[github-operations](../skills/github-operations.md) and use GitHub MCP first.

---

## When to Use

- Writing `run:` steps in GitHub Actions workflows
- Handling multiline strings or heredocs in YAML
- Parsing command output with `grep`, `awk`, or `sed`
- Creating temporary files for API payloads or artifacts
- Validating prerequisites before running commands

---

## When NOT to Use

- The workflow logic belongs in a standalone, testable script
- A maintained action already provides the needed behavior
- You can avoid inline shell by using action outputs

---

## Patterns

### Pattern 1: Always Use Strict Mode

Every shell script block must start with strict mode to catch errors early.

```bash
# BAD: No error handling - failures silently ignored
run: |
  some_command
  another_command  # Runs even if some_command failed

# GOOD: Strict mode catches all errors
run: |
  set -euo pipefail
  some_command
  another_command  # Only runs if some_command succeeded
```

**Flags explained:**

- `-e`: Exit immediately on any command failure
- `-u`: Error on undefined variables
- `-o pipefail`: Pipeline fails if any command in pipe fails

### Pattern 2: Use $RUNNER_TEMP for Temporary Files

GitHub Actions provides `$RUNNER_TEMP` which is cleaned up automatically and isolated per job.

```bash
# BAD: /tmp is shared, may have stale files, not cleaned
run: |
  echo "$CONTENT" > /tmp/body.md
  gh api ... -F body=@/tmp/body.md

# GOOD: $RUNNER_TEMP is job-isolated and auto-cleaned
run: |
  set -euo pipefail
  BODY_FILE="${RUNNER_TEMP}/pr-body.md"
  echo "$CONTENT" > "$BODY_FILE"
  gh api ... -F body=@"$BODY_FILE"
```

### Pattern 3: File-Based API Bodies Instead of Inline

Multiline strings with `-f` parameters corrupt whitespace. Use file-based `-F field=@file` instead.

```bash
# BAD: Inline heredoc mangles whitespace and newlines
run: |
  gh api repos/$REPO/pulls -X POST \
    -f title="$TITLE" \
    -f body="$(cat <<'EOF'
  ## Summary
  This PR does things.

  ## Changes
  - Item 1
  - Item 2
  EOF
  )"

# GOOD: Write to file, then use -F with @file syntax
run: |
  set -euo pipefail
  BODY_FILE="${RUNNER_TEMP}/pr-body.md"
  cat > "$BODY_FILE" << 'EOF'
  ## Summary
  This PR does things.

  ## Changes
  - Item 1
  - Item 2
  EOF
  gh api repos/${{ github.repository }}/pulls -X POST \
    -f title="$TITLE" \
    -F body=@"$BODY_FILE"
```

### Pattern 4: Heredoc Indentation Control

Use `<<-` with tabs for indented heredocs, or unindent the content entirely.

```bash
# BAD: Spaces in heredoc become part of content
run: |
  if true; then
    cat << EOF
      This line has 6 leading spaces in the output!
    EOF
  fi

# GOOD: Unindent heredoc content to column 0
run: |
  set -euo pipefail
  if true; then
    # Heredoc content starts at column 0 to avoid whitespace issues
  cat << 'EOF'
  No indentation issues here.
  Content starts at column 0.
  EOF
  fi
```

**Notes:**

- Quote the delimiter (`'EOF'`) to prevent variable expansion when you want literal content
- The `<<-` operator strips leading tabs (not spaces) but requires literal tab characters

### Pattern 5: AWK and sed Exact Field Matching

Partial matches cause false positives. Use field delimiters and exact patterns.

```bash
# BAD: Partial match - "feature-test" also matches "test"
run: |
  echo "$BRANCHES" | grep "test"

# BAD: AWK partial match on field
run: |
  echo "$OUTPUT" | awk '/test/ {print $2}'

# GOOD: Exact word boundary matching with grep
run: |
  set -euo pipefail
  echo "$BRANCHES" | grep -w "test"        # Word boundary
  echo "$BRANCHES" | grep "^test$"         # Exact line match
  echo "$BRANCHES" | grep -F "test"        # Fixed string (no regex)

# GOOD: AWK exact field comparison
run: |
  set -euo pipefail
  echo "$OUTPUT" | awk '$1 == "test" {print $2}'           # Exact field match
  echo "$OUTPUT" | awk -F'\t' '$1 == "test" {print $2}'    # With delimiter
```
