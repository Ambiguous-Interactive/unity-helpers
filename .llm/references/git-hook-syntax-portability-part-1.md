# git-hook-syntax-portability - Part 1

## Split Content

**Trigger**: When writing regex patterns, case statements, or CLI invocations in hook scripts, or fixing portability issues.

---

## When to Use

- Writing regex patterns in bash hooks (single vs double backslash)
- Using `case` statements to match file paths
- Passing file lists to CLI tools safely
- Fixing or appending newlines while preserving CRLF/LF style
- Ensuring grep patterns work on both GNU and BSD systems

---

## When NOT to Use

- For index safety or hook templates → see [git-hook-safety](../skills/git-hook-safety.md)
- For validation philosophy or debugging → see [git-hook-lifecycle-debugging](../skills/git-hook-lifecycle-debugging.md)

---

## Git Hook Regex Patterns (CRITICAL)

> **CRITICAL**: Regex patterns in bash git hooks require SINGLE backslashes.
> Double escaping causes patterns to match NOTHING silently!

```bash
# CORRECT - Single backslash
git diff --cached --name-only | grep -E '\.(md|markdown)$'

# WRONG - Double-escaped (matches nothing!)
git diff --cached --name-only | grep -E '\\.(md|markdown)$'
```

**ALWAYS test patterns manually after modification:**

```bash
# Test that pattern matches expected files
echo "test.md" | grep -E '\.(md|markdown)$'  # Should output: test.md
```

See [validate-before-commit](../skills/validate-before-commit.md#git-hook-regex-pattern-testing-critical)
for full testing checklist.

---

## Bash Case Patterns vs Filename Globbing (CRITICAL)

In bash `case` statements, `*` matches **any character including `/`**. This is DIFFERENT from filename globbing (e.g., `ls *.md`) where `*` does NOT match `/`.

```bash
# case: .llm/skills/*.md matches ALL depths
case "$file" in .llm/skills/*.md) echo "match" ;; esac
# Matches: .llm/skills/foo.md, .llm/skills/sub/dir/foo.md

# filename glob: .llm/skills/*.md matches ONE level only
ls .llm/skills/*.md       # Only matches .llm/skills/foo.md
ls .llm/skills/**/*.md    # ** needed for recursive glob
```

**Rules**:

- DO NOT use `**/*.md` notation in `case` comments -- `**` is glob-specific and irrelevant
- DO NOT add redundant depth alternatives like `.llm/skills/*.md|.llm/skills/*/*.md`
- The single pattern `.llm/skills/*.md` already matches all subdirectory depths in `case`
- Regression test: [test-hook-patterns.sh](../../scripts/tests/test-hook-patterns.sh)

---

## CLI Argument Safety: End-of-Options Separator

When passing staged file lists to CLI tools in hooks, ALWAYS place `--` between options and file arguments:

```bash
# WRONG - filenames can be interpreted as options
node scripts/run-prettier.js --write "${FILES[@]}"

# CORRECT - `--` prevents option injection from filenames
node scripts/run-prettier.js --write -- "${FILES[@]}"
node scripts/run-node-bin.js markdownlint --fix --config .markdownlint.json -- "${FILES[@]}"
yamllint -c .yamllint.yaml -- "${FILES[@]}"
```

**Why**: A staged filename like `--plugin=./evil.js` would be parsed as a CLI flag without `--`. This is an option injection vulnerability.

> **CAVEAT** — PowerShell's `-File` CLI does NOT honor POSIX `--` and will fail with "parameter name '' is ambiguous." When calling `pwsh -File <script>` or `powershell -File <script>` from bash, pass explicit named parameters instead (e.g. `-Paths "${ARR[@]}"`). See [bash-pwsh-invocation](../skills/bash-pwsh-invocation.md) for the full rule and enforcement lint.

### Never Transport File Lists Through `echo ... | xargs`

`echo "$FILES" | xargs tool ...` is unsafe for path lists. `xargs` re-splits on whitespace, so filenames with spaces are mangled, and newline-delimited shell variables are not a reliable file transport format.

```bash
# WRONG - whitespace in filenames is split into multiple arguments
echo "$CHANGED_MD" | xargs node scripts/run-node-bin.js markdownlint --config .markdownlint.json --

# CORRECT (bash) - keep paths in arrays and expand them directly
node scripts/run-node-bin.js markdownlint --config .markdownlint.json -- "${CHANGED_MD[@]}"

# CORRECT (generic) - if xargs is required, feed it NUL-delimited input
printf '%s\0' "${CHANGED_MD[@]}" | xargs -0 node scripts/run-node-bin.js markdownlint --config .markdownlint.json --
```

**Rule**: In bash hooks, prefer arrays plus `"${ARRAY[@]}"` over newline-delimited variables. Only use `xargs` with `-0` and NUL-delimited input.

### File-Reading Commands Also Need `--`

Commands that read file contents (`tail`, `head`, `cat`, `od`, etc.) are also vulnerable to option injection and must use `--`:

```bash
# WRONG - filename starting with '-' is interpreted as option
tail -n 1 "$file"
head -c 10 "$file"
cat "$file"
od -c "$file"

# CORRECT - `--` ensures filename is never parsed as an option
tail -n 1 -- "$file"
head -c 10 -- "$file"
cat -- "$file"
od -c -- "$file"
```

**Why**: A file named `-n` or `--help` would cause unexpected behavior or errors without `--`.

---

## CRLF-Aware Newline Handling

When fixing missing final newlines in hook scripts, you MUST detect the file's existing line ending style before appending. Blindly appending `\n` (LF) to a CRLF file creates mixed line endings that fail `eol:check`.

### Detecting Line Endings Before Appending

```bash
# WRONG - Always appends LF, breaks CRLF files
if [ "$(tail -c 1 -- "$file" | od -An -tx1 | tr -d ' ')" != "0a" ]; then
    printf '\n' >> "$file"
fi

# CORRECT - Detect existing line endings and match them
if [ "$(tail -c 1 -- "$file" | od -An -tx1 | tr -d ' ')" != "0a" ]; then
    # Check if file uses CRLF by looking for carriage return characters
    if grep -q $'\r' "$file" 2>/dev/null; then
        printf '\r\n' >> "$file"  # CRLF file - append CRLF
    else
        printf '\n' >> "$file"    # LF file - append LF
    fi
fi
```

### Why This Matters

1. Windows-style files use CRLF (`\r\n`) line endings
2. Unix-style files use LF (`\n`) line endings
3. Appending LF to a CRLF file creates a "mixed" file with both styles
4. The `eol:check` validation fails on mixed line endings
5. This is a common cause of CI failures after "fixing" final newlines

### PowerShell Equivalent

```powershell
# WRONG - -NoNewline then manual newline can create mixed endings
$content = Get-Content -Raw $file
# ... process content ...
Set-Content -NoNewline -Path $file -Value $content
Add-Content -Path $file -Value ""  # Adds OS-default line ending

# CORRECT - Detect and preserve existing line endings
$content = Get-Content -Raw $file
$useCRLF = $content -match "`r`n"
# ... process content ...
$newline = if ($useCRLF) { "`r`n" } else { "`n" }
[System.IO.File]::WriteAllText($file, $content + $newline)
```

---
