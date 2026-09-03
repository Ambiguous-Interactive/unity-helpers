# Skill: GitHub Actions Shell Foundations

<!-- trigger: actions shell, bash, strict mode, runner temp, heredoc, quoting | Core shell scripting safety for GitHub Actions | Core -->

**Trigger**: When writing inline shell in GitHub Actions workflows, especially for strict mode, temp files, heredocs, quoting, and text processing.

The `gh` examples in this skill are tracked commands that execute inside a GitHub Actions runner.
They are not agent-side commands. For agent-initiated GitHub reads or mutations, follow
[github-operations](./github-operations.md) and use GitHub MCP first.

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

### Pattern 6: Validate Commands and Files Exist

Check prerequisites before operations to provide clear error messages.

```bash
# BAD: Cryptic error if file missing
run: |
  cat config.json | jq '.version'

# GOOD: Validate before use with clear errors
run: |
  set -euo pipefail

  if ! command -v jq &> /dev/null; then
    echo "::error::jq is required but not installed"
    exit 1
  fi

  if [ ! -f "config.json" ]; then
    echo "::error::config.json not found in $(pwd)"
    exit 1
  fi

  jq '.version' config.json
```

### Pattern 7: Safe Variable Expansion

Quote variables and handle empty or unset cases explicitly.

```bash
# BAD: Unquoted variables break on whitespace, unset vars ignored
run: |
  FILES=$(git diff --name-only)
  for file in $FILES; do
    process $file
  done

# GOOD: Quoted variables, array handling, empty checks
run: |
  set -euo pipefail

  mapfile -t FILES < <(git diff --name-only)

  if [ ${#FILES[@]} -eq 0 ]; then
    echo "No files changed"
    exit 0
  fi

  for file in "${FILES[@]}"; do
    if [ -f "$file" ]; then
      process "$file"
    fi
  done
```

### Pattern 8: Arithmetic Without Subshell Errors

Bash arithmetic with `set -e` can cause unexpected exits.

```bash
# BAD: ((count++)) returns 1 when count=0, causing exit with set -e
run: |
  set -euo pipefail
  count=0
  ((count++))  # Exits here because (( )) returns 0's exit code as 1

# GOOD: Use arithmetic expansion or explicit assignment
run: |
  set -euo pipefail
  count=0
  count=$((count + 1))  # Safe - assignment always succeeds

  # Or use : prefix to discard exit code
  : $((count++))
```

### Pattern 9: Safe Multiline Content Handling

When passing multiline content between steps, use environment variables with printf to avoid heredoc injection vulnerabilities.

```bash
# BAD: Heredoc in YAML - content could contain delimiter, causing injection
- name: Use content
  run: |
    cat > body.md << 'EOF'
    ${{ steps.previous.outputs.content }}
    EOF
    # If content contains "EOF" on its own line, it terminates early!

# GOOD: Pass via environment variable and use printf
- name: Use content
  env:
    CONTENT: ${{ steps.previous.outputs.content }}
  run: |
    set -euo pipefail
    # printf safely handles any content without shell interpolation
    printf '%s\n' "$CONTENT" > "${RUNNER_TEMP}/body.md"
    gh api ... -F body=@"${RUNNER_TEMP}/body.md"
```

**Key insight**: GitHub Actions expressions (`${{ }}`) are expanded before the shell runs, so even single-quoted heredocs do not protect against content containing the delimiter. Always use environment variables with `printf` for user-controlled or file-derived content.

### Pattern 10: Avoid Redundant Error Suppression

Commands with built-in error handling should NOT have `|| true` appended.

```bash
# BAD: rm -f already suppresses "file not found" errors
# || true masks REAL errors like permission denied
run: |
  set -euo pipefail
  rm -f "$TEMP_FILE" || true  # WRONG - hides permission errors!
  rm -rf "$TEMP_DIR" || true  # WRONG - hides permission errors!

# GOOD: -f flag handles missing files, real errors should fail
run: |
  set -euo pipefail
  rm -f "$TEMP_FILE"   # Fails on permission errors (correct behavior)
  rm -rf "$TEMP_DIR"   # Fails on permission errors (correct behavior)
```

**Commands where `|| true` is redundant:**

| Command         | Why `\|\| true` is Wrong                                        |
| --------------- | --------------------------------------------------------------- |
| `rm -f file`    | `-f` = "ignore nonexistent"; masks permission/filesystem errors |
| `rm -rf dir`    | `-f` = "ignore nonexistent"; masks permission/filesystem errors |
| `mkdir -p path` | `-p` = "no error if exists"; masks permission errors            |

**When `|| true` IS appropriate:**

| Pattern                       | Why It's Correct                                           |
| ----------------------------- | ---------------------------------------------------------- |
| `grep pattern file \|\| true` | `grep` exits 1 when no match; that's not an error          |
| `diff file1 file2 \|\| true`  | `diff` exits 1 when files differ; that's not an error      |
| `((count++)) \|\| true`       | Bash arithmetic returns 1 when result is 0 (see Pattern 8) |

**Key insight**: Error suppression flags (`-f`, `-p`) exist to handle _expected_ conditions (file doesn't exist). Adding `|| true` after them suppresses _unexpected_ errors that indicate real problems.

---

### Pattern 11: End-of-Options Separator for File Arguments

When passing file lists to CLI tools, ALWAYS insert `--` before the file arguments to prevent option injection.

```bash
# BAD: A filename like '--plugin=evil.js' is treated as a flag
run: |
  set -euo pipefail
  node scripts/run-prettier.js --write "${FILES[@]}"

# GOOD: `--` stops option parsing; everything after is a filename
run: |
  set -euo pipefail
  node scripts/run-prettier.js --write -- "${FILES[@]}"
  node scripts/run-node-bin.js markdownlint --fix --config .markdownlint.json -- "${FILES[@]}"
  yamllint -c .yamllint.yaml -- "${FILES[@]}"
```

**Applies to**: `prettier`, `markdownlint`, `yamllint`, `eslint`, `csharpier`, and any tool that accepts a file list. Without `--`, an attacker-controlled filename like `--config=malicious.yml` becomes a CLI flag, enabling option injection.

#### File-Reading Commands Also Need `--`

Commands that read or inspect file contents are equally vulnerable:

```bash
# BAD: Filename starting with '-' is interpreted as option
run: |
  tail -n 1 "$file"
  head -c 10 "$file"
  cat "$file"
  od -c "$file"

# GOOD: `--` ensures filename is never parsed as an option
run: |
  set -euo pipefail
  tail -n 1 -- "$file"
  head -c 10 -- "$file"
  cat -- "$file"
  od -c -- "$file"
```

**Common vulnerable commands**: `tail`, `head`, `cat`, `od`, `wc`, `sort`, `uniq`, `cut`, `tr`, `file`, `stat`, `touch`, `chmod`, `chown`, `mv`, `cp`, `rm`, `ln`.

**Rule of thumb**: If a command takes a filename argument, use `--` before it.

---

## GitHub Actions Annotations

Use workflow commands for structured output:

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

## Environment Variables Best Practices

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
  # Generate unique delimiter to prevent content collision
  DELIMITER="__EOF_$(date +%s%N)_${RANDOM}__"
  {
    printf 'changelog<<%s\n' "$DELIMITER"
    cat CHANGELOG.md
    printf '%s\n' "$DELIMITER"
  } >> "$GITHUB_OUTPUT"
```

**Warning**: Never use a fixed delimiter like `EOF` for multiline outputs. If the content contains the delimiter on its own line, it will prematurely terminate the output and can cause data corruption or security issues. Always generate a random, unpredictable delimiter.

---

## Related Skills

- [github-actions-shell-scripting](./github-actions-shell-scripting.md) - Overview, checklist, and scope.
- [github-actions-shell-workflow-patterns](./github-actions-shell-workflow-patterns.md) - Workflow integration patterns.
