# search-codebase - Part 1

## Split Content

**Trigger**: When searching for code, files, or patterns in this repository.

---

## Tool Selection

| Task                    | Tool                 | Example                      |
| ----------------------- | -------------------- | ---------------------------- |
| Search file contents    | `rg` (ripgrep)       | `rg "pattern" --type cs`     |
| Find files by name/path | `fd`                 | `fd "Tests\.cs$"`            |
| View file contents      | `bat --paging=never` | `bat --paging=never file.cs` |
| Code statistics         | `tokei`              | `tokei Runtime/`             |

---

## Searching File Contents with `rg`

### Basic Searches

```bash
# Search C# files for pattern
rg "SerializableDictionary" --type cs

# Case-insensitive search
rg "error" -i --type cs

# Search with regex
rg "class \w+Tests" --type cs

# Search in specific directory
rg "IRandom" Runtime/Core/Random/

# Search with glob pattern
rg "TODO|FIXME" -g "*.cs"
```

### Context and Output

```bash
# Show 3 lines of context
rg "pattern" -C 3 --type cs

# Show 5 lines after, 2 before
rg "pattern" -A 5 -B 2 --type cs

# List matching files only
rg "pattern" -l --type cs

# Count occurrences per file
rg "pattern" -c --type cs
```

### Advanced Options

```bash
# Include gitignored files
rg "pattern" --no-ignore --type cs

# Use PCRE2 regex (more features)
rg "class \w+" --pcre2 --type cs

# Search multiple patterns
rg "GetOrAdd|GetOrElse|TryRemove" --type cs

# Invert match (lines NOT matching)
rg -v "using" --type cs
```

---

## Finding Files with `fd`

### Basic Searches

```bash
# Find all C# files
fd "\.cs$"

# Find test files
fd "Tests\.cs$"

# Find directories named Tests
fd "Tests" --type d

# Find files with multiple extensions
fd -e cs -e json
```

### Path Filtering

```bash
# Find .meta files in Runtime/
fd "\.meta$" Runtime/

# Find files modified recently
fd -e cs --changed-within 1d

# Include hidden files
fd "pattern" --hidden

# Include gitignored files
fd "pattern" --no-ignore
```

### Execution

```bash
# Execute command on each result
fd -e cs -x rg "TODO" {}

# Batch execution (all files as args)
fd -e cs -X rg "TODO"

# Show first 10 lines of each file
fd -e cs -x bat --paging=never -r 1:10 {}
```

---

## Viewing Files with `bat`

⚠️ **ALWAYS use `--paging=never`** or bat will block waiting for input.

```bash
# View with syntax highlighting
bat --paging=never file.cs

# Show specific line range
bat --paging=never -r 10:50 file.cs

# Show line numbers only
bat --paging=never -n file.cs

# Plain output (no decorations)
bat --paging=never -p file.cs

# Force language highlighting
bat --paging=never -l cs file.txt
```

### Combining with Other Tools

```bash
# First 50 lines with highlighting
head -n 50 file.cs | bat --paging=never -l cs

# Search results with highlighting
rg "pattern" -C 3 | bat --paging=never -l cs
```

---
