# github-actions-shell-foundations - Part 2

## Split Content

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
