# github-actions-shell-foundations - Part 3

## Split Content

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

- [github-actions-shell-scripting](../skills/github-actions-shell-scripting.md) - Overview, checklist, and scope.
- [github-actions-shell-workflow-patterns](../skills/github-actions-shell-workflow-patterns.md) - Workflow integration patterns.
