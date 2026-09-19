# search-codebase - Part 2

## Split Content

## Common Workflows

### Find All Usages of a Type

```bash
rg "SerializableDictionary" --type cs -l
```

### Find Test Files for a Class

```bash
fd "BuffersTests\.cs$"
# or
rg "class BuffersTests" --type cs -l
```

### Check for TODO/FIXME Comments

```bash
rg "TODO|FIXME" --type cs -c
```

### Find Files Modified Today

```bash
fd -e cs --changed-within 1d
```

### Search and Replace Preview

```bash
rg "OldName" --type cs -l | xargs -I {} bat --paging=never {} -r 1:5
```

### Validate All Files Have Namespace

```bash
fd -e cs | while read -r f; do
    if ! rg -q "^namespace" "$f"; then
        echo "Missing namespace: $f"
    fi
done
```

---

## Code Statistics

```bash
# Full project statistics
tokei

# Specific directory
tokei Runtime/

# Exclude Tests
tokei -e Tests

# Only C# files
tokei -t "C#"

# Sort by lines of code
tokei --sort code
```

---

## ❌ NEVER Use These

```bash
# Traditional tools are slower and have worse output
grep -r "pattern" .           # Use rg instead
find . -name "*.cs"           # Use fd instead
cat file.cs                   # Use bat --paging=never instead
```

---

## Portable Shell Scripting (CI/CD & Bash Scripts)

When writing bash scripts for CI/CD pipelines or git hooks, use POSIX-compliant tools to ensure portability across Linux, macOS, and other Unix-like systems.

### ❌ Avoid GNU-Specific Options

| GNU-Specific               | Portable Alternative                       | Notes                                           |
| -------------------------- | ------------------------------------------ | ----------------------------------------------- |
| `grep -oP` (Perl regex)    | `grep -oE` + `sed`                         | `-P` is GNU-only, unavailable on macOS BSD grep |
| `grep -oP '\K'` lookbehind | `grep -oE` + `sed 's/prefix//'`            | `\K` is Perl-specific                           |
| `grep -oP '(?=...)'`       | `grep -oE` then post-process               | Lookahead is Perl-specific                      |
| `sed -i ''` vs `sed -i`    | Use temp file: `sed ... > tmp && mv tmp f` | In-place edit syntax differs between GNU/BSD    |
| `readarray` / `mapfile`    | `while read` loop                          | Bash 4+ only, not available everywhere          |

### Extract Markdown Links (Portable)

```bash
# ❌ GNU-only (fails on macOS)
echo "$line" | grep -oP '\]\(\K[^)]+(?=\))'

# ✅ POSIX-compliant (works everywhere)
echo "$line" | grep -oE '\]\([^)]+\)' | sed 's/^](//;s/)$//'
```

### Why This Matters

- **GitHub Actions runners**: Ubuntu uses GNU tools, but self-hosted runners may vary
- **Developer machines**: macOS uses BSD tools by default
- **Docker containers**: Alpine Linux has BusyBox tools with limited features
- **CI reproducibility**: Scripts should work identically across environments

### When to Use Modern Tools vs POSIX Tools

| Context                             | Use                 | Reason                                       |
| ----------------------------------- | ------------------- | -------------------------------------------- |
| Interactive development (local)     | `rg`, `fd`, `bat`   | Best UX, fastest performance                 |
| CI/CD workflow scripts (`.yml`)     | POSIX tools or `rg` | `rg` is installed; avoid GNU-specific `grep` |
| Git hooks (`.githooks/`)            | POSIX tools         | Must work on all developer machines          |
| Bash scripts (`scripts/*.sh`)       | POSIX tools         | Maximum portability                          |
| Dev container commands (`Makefile`) | `rg`, `fd`, `bat`   | Controlled environment                       |

---

## xargs with Modern Tools

Shell aliases don't work in subshells. Always use explicit tool names:

```bash
# ✅ CORRECT
fd -e cs | xargs -I {} sh -c 'rg "pattern" "{}"'
fd -e cs -x rg "using" {}

# ❌ WRONG (aliases don't work)
fd -e cs | xargs grep "pattern"
```

---

## PowerShell Exit Code Handling

When calling external commands in PowerShell scripts, `$LASTEXITCODE` can be unreliable in certain contexts, especially with array splatting.

### The Problem

```powershell
# ❌ Potentially unreliable
$args = @('-c', $config)
& $tool @args
$exitCode = $LASTEXITCODE  # May not capture correctly in all PS versions
```

### Recommended Pattern

```powershell
# ✅ Capture immediately with error handling
$result = $null
try {
    & $tool @args
    $result = $LASTEXITCODE
} catch {
    $result = 1
}

# Ensure we have a valid exit code
if ($null -eq $result) {
    $result = 0
}
```

### Why This Matters

- `$LASTEXITCODE` is only set after native commands, not PowerShell cmdlets
- Array splatting with `&` operator can have edge cases in older PS versions
- Try-catch ensures we handle both terminating errors and exit codes
- Null check provides a safe fallback
