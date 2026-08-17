# Skill: Git Hook Patterns

<!-- trigger: git hook, pre-commit, hook safety, hook patterns, hook permissions | Git hook safety, syntax, and debugging patterns (hub) | Core -->

## Purpose

Central hub for all git hook patterns. This skill links to focused detail files —
use the selection guide below to find the right one.

## When to Use This Skill

- Writing or modifying any git hook
- Debugging hook failures, lock errors, or exit code issues
- Ensuring hook scripts use portable, safe patterns
- Not sure which hook skill to read — start here

---

## Selection Guide

| Problem / Task                                   | Detail Skill                                                      |
| ------------------------------------------------ | ----------------------------------------------------------------- |
| `index.lock` errors, external tool conflicts     | [git-hook-safety](./git-hook-safety.md)                           |
| Hook not running (permissions)                   | [git-hook-safety](./git-hook-safety.md)                           |
| Starting a new hook from scratch (template)      | [git-hook-safety](./git-hook-safety.md)                           |
| Capturing staged files, partial staging          | [git-hook-safety](./git-hook-safety.md)                           |
| Getting file lists (`git diff`, `git ls-files`)  | [git-hook-safety](./git-hook-safety.md)                           |
| Regex single vs double backslash                 | [git-hook-syntax-portability](./git-hook-syntax-portability.md)   |
| `case` pattern matching vs filename globbing     | [git-hook-syntax-portability](./git-hook-syntax-portability.md)   |
| CLI `--` end-of-options for filenames            | [git-hook-syntax-portability](./git-hook-syntax-portability.md)   |
| CRLF vs LF newline handling                      | [git-hook-syntax-portability](./git-hook-syntax-portability.md)   |
| Portable grep (`\|`, `\s` issues)                | [git-hook-syntax-portability](./git-hook-syntax-portability.md)   |
| Stderr suppression hygiene (`2>/dev/null`)       | [git-hook-syntax-portability](./git-hook-syntax-portability.md)   |
| Validate-only vs auto-fix philosophy             | [git-hook-lifecycle-debugging](./git-hook-lifecycle-debugging.md) |
| `require_serial` / pre-commit framework config   | [git-hook-lifecycle-debugging](./git-hook-lifecycle-debugging.md) |
| `$LASTEXITCODE` leaking from PowerShell hooks    | [git-hook-lifecycle-debugging](./git-hook-lifecycle-debugging.md) |
| Debugging hook lock issues                       | [git-hook-lifecycle-debugging](./git-hook-lifecycle-debugging.md) |
| CI/CD environment differences                    | [git-hook-lifecycle-debugging](./git-hook-lifecycle-debugging.md) |
| Keeping hook descriptions accurate after changes | [git-hook-lifecycle-debugging](./git-hook-lifecycle-debugging.md) |

---

## Detail Files

### [git-hook-safety](./git-hook-safety.md) — Index Safety & Execution

Core patterns for safe hook execution: waiting for external tools, capturing state early,
handling partial staging, file list queries, hook permissions, and a starter template.

### [git-hook-syntax-portability](./git-hook-syntax-portability.md) — Syntax & Portability

Pattern correctness in hook scripts: regex escaping, bash `case` vs glob semantics,
CLI end-of-options safety, CRLF-aware newline handling, portable grep patterns, and
stderr suppression hygiene.

### [git-hook-lifecycle-debugging](./git-hook-lifecycle-debugging.md) — Lifecycle & Debugging

Validation philosophy (validate vs auto-fix), pre-commit framework configuration,
hook description accuracy, CI/CD differences, PowerShell `$LASTEXITCODE` leaking,
error handling, and debugging lock issues.

---

## Related Skills

- [git-safe-operations](./git-safe-operations.md) - Core git safety patterns and critical rules
- [git-staging-helpers](./git-staging-helpers.md) - PowerShell/Bash helper functions reference
- [validate-before-commit](./validate-before-commit.md) - Pre-commit validation commands
- [formatting](./formatting.md) - CSharpier, Prettier, markdownlint workflow
- [optimize-git-hooks](./optimize-git-hooks.md) - Performance optimization patterns for hooks

## Related Files

- [.githooks/pre-commit](../../.githooks/pre-commit) - Local pre-commit hook (uses helpers)
- [.githooks/pre-push](../../.githooks/pre-push) - Local pre-push hook
