# validation-troubleshooting - Part 3

## Split Content

## Debugging Failed CI Runs

1. **Check which check failed** in GitHub Actions output:
   - `validate:content` — Documentation/formatting
   - `lint:csharp-naming` — C# naming convention
   - `eol:check` — Line endings
   - `validate:tests` — Test lifecycle

2. **Reproduce locally**: `npm run validate:local`

3. **Fix and verify**: Fix the issue, run the specific command, then run `npm run validate:local`

---

## Special Cases

### Pre-Existing Warnings

Check if a warning exists on main before fixing (see [`check_preexisting`](../code-samples/patterns/ValidationFixPatterns.sh)). If it exists on main, it's pre-existing and doesn't block your PR.

### Conflicts Between Linters

Resolution priority: **Prettier** (formatting) > **Markdownlint** (structure) > **CSpell** (spelling — always fix or add to dictionary).

### Files That Should Be Ignored

Add files to `.prettierignore`, `.markdownlintignore`, or `cspell.json` `ignorePaths` as appropriate.

---

## PowerShell Exit Code Linter (Potential Safeguard)

Several `scripts/*.ps1` files end without an explicit `exit 0` on their success paths (e.g., `format-staged-csharp.ps1`, `format-staged-prettier.ps1`). A static linter could check that every script-level `.ps1` file (excluding libraries like `git-staging-helpers.ps1`) has an explicit `exit` as the last statement.

**Simple heuristic**: Parse each `.ps1`, skip trailing whitespace/comments/closing braces, and verify the last meaningful statement is `exit $something` or `exit <number>`. Scripts ending with a closing `}` from an `if` block that contains `exit` on both branches pass, but scripts that fall through after a conditional exit fail.

**Current state**: The extensionless pre-commit launcher delegates to `.githooks/pre-commit.ps1`, which treats non-zero child script exits as hook failures. The primary remaining risk is direct CI or developer invocation of `.ps1` scripts that fall through without an explicit success exit. Most lint scripts already have proper `exit 0/1` on all paths. Monitor for future regressions.

---

## Quick Recovery Commands

See [`quick_recovery`](../code-samples/patterns/ValidationFixPatterns.sh) for the full script, or run individually:
`node scripts/run-prettier.js --write -- .` | `npm run eol:fix` | `dotnet tool run csharpier format .` | `npm run validate:local`

---

## Related Skills

- [validate-before-commit](../skills/validate-before-commit.md) — Quick validation workflow
- [linter-reference](../skills/linter-reference.md) — Detailed linter commands
- [formatting](../skills/formatting.md) — CSharpier, Prettier, markdownlint workflow
- [markdown-reference](../skills/markdown-reference.md) — Link formatting, structural rules
