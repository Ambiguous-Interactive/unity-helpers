# Skill: Git Hook Syntax & Portability

<!-- trigger: hook regex, case pattern, end-of-options, CRLF newline, grep portability, stderr suppression | Hook regex, CLI safety, CRLF handling, portable grep patterns | Core -->

## Reference Parts

- [Part 1](../references/git-hook-syntax-portability-part-1.md)
- [Part 2](../references/git-hook-syntax-portability-part-2.md)

## When to Use

[Read section](../references/git-hook-syntax-portability-part-1.md#when-to-use)

## When NOT to Use

[Read section](../references/git-hook-syntax-portability-part-1.md#when-not-to-use)

## Git Hook Regex Patterns (CRITICAL)

[Read section](../references/git-hook-syntax-portability-part-1.md#git-hook-regex-patterns-critical)

## Bash Case Patterns vs Filename Globbing (CRITICAL)

[Read section](../references/git-hook-syntax-portability-part-1.md#bash-case-patterns-vs-filename-globbing-critical)

## CLI Argument Safety: End-of-Options Separator

[Read section](../references/git-hook-syntax-portability-part-1.md#cli-argument-safety-end-of-options-separator)

### [Never Transport File Lists Through `echo ... | xargs`](../references/git-hook-syntax-portability-part-1.md#never-transport-file-lists-through-echo---xargs)

### [File-Reading Commands Also Need `--`](../references/git-hook-syntax-portability-part-1.md#file-reading-commands-also-need---)

## CRLF-Aware Newline Handling

[Read section](../references/git-hook-syntax-portability-part-1.md#crlf-aware-newline-handling)

### [Detecting Line Endings Before Appending](../references/git-hook-syntax-portability-part-1.md#detecting-line-endings-before-appending)

### [Why This Matters](../references/git-hook-syntax-portability-part-1.md#why-this-matters)

### [PowerShell Equivalent](../references/git-hook-syntax-portability-part-1.md#powershell-equivalent)

## Portable Grep & Stderr Hygiene (CRITICAL)

[Read section](../references/git-hook-syntax-portability-part-2.md#portable-grep--stderr-hygiene-critical)

### [Git Path Parsing Must Not Assume Single-Field Paths](../references/git-hook-syntax-portability-part-2.md#git-path-parsing-must-not-assume-single-field-paths)

### [Repo-Root Anchoring for `git ls-files` and Similar Commands](../references/git-hook-syntax-portability-part-2.md#repo-root-anchoring-for-git-ls-files-and-similar-commands)

## `git check-ignore` Requires Repo-Relative POSIX Paths (CRITICAL)

[Read section](../references/git-hook-syntax-portability-part-2.md#git-check-ignore-requires-repo-relative-posix-paths-critical)

## Related Skills

[Read section](../references/git-hook-syntax-portability-part-2.md#related-skills)
