# validate-before-commit - Part 1

## Split Content

**Trigger**: **MANDATORY** before completing any task that modifies code or documentation.

---

## When to Use

Use this skill for pre-commit validation:

- Before completing any coding task
- Before asking user to review changes
- Before any discussion of "done" or "complete"
- After making ANY modifications to files

For detailed linter commands and configurations, see [linter-reference](../skills/linter-reference.md).
For troubleshooting common errors, see [validation-troubleshooting](../skills/validation-troubleshooting.md).

---

## Quick Reference

```bash
# Fast changed-file preflight (MANDATORY before marking task complete)
npm run agent:preflight:fix

# Final fast Git/config safety check before pushing
npm run validate:prepush
```

Run `agent:preflight:fix` during edits, targeted checks before pushing, and `validate:prepush` last. Use `validate:local` for a warranted repository aggregate.
CI also runs exhaustive hook fixtures; run `npm run validate:tests:hook-regressions` locally when hook or agent-preflight behavior changes.

For Unity workflow or cleanup changes, run `npm run test:portable-cleanup-classifier` with `BUILD_LOCK_POLICY_ROOT` pointing to the exact central action checkout.
Resolve that revision with `scripts/resolve-build-lock-pin.js`. CI supplies this checkout; local aggregates otherwise skip the contract.
A skip does not validate cleanup. Preserve classifier, executor and final-gate coverage when replacing a legacy caller.

**C#/tests/JSON/YAML/skill/CHANGELOG edits: run `npm run lint:spelling`** — cspell covers every file matching its `files` glob, not just Markdown. See [Rule 4: Spell-Check EVERY Change cspell Covers](../skills/validate-before-commit.md#rule-4-spell-check-every-change-cspell-covers) for the failure-recovery decision tree. To add a new word: `npm run lint:spelling:add -- <bucket> <word>`.

---

## The Golden Rules

### Rule 0: Preflight Before Completion

Run `npm run agent:preflight:fix` before declaring a task complete.

Hooks are a last-resort safety net. Do not rely on hook-time auto-fixes as the normal workflow.

This catches hook-class failures early for changed files:

- Missing Unity `.meta` files on changed paths
- Unstaged Unity `.meta` companions for currently staged source files
- Spelling regressions in changed markdown files (`.md`, `.markdown`)
- Skill/context files approaching hard size limits
- LLM index/trigger drift when `.llm/` files changed
- Test-lint regressions with auto-fix for Unity null assertions

After creating any file/folder under Unity meta-required roots (`Runtime/`, `Editor/`, `Tests/`, `Samples~/`, `Shaders/`, `Styles/`, `URP/`, `docs/`, `scripts/`):

1. Generate `.meta` immediately with `./scripts/generate-meta.sh <path>` for new or empty folders, or `npm run agent:preflight:fix` for changed files discovered by Git.
2. Run `npm run agent:preflight:fix` before continuing work.

Run `agent:preflight:fix` after staging candidate files (right before commit prep) so staged `.meta` companion drift is corrected before hooks run.
By default (no `-Paths`), preflight validates all changed files from git; passing `-Paths` scopes checks to those targets.

Preferred commit prep order:

1. Stage candidate files.
2. Run `npm run agent:preflight:fix`.
3. Resolve any reported issues.
4. Commit (hooks should only catch unexpected regressions).

### Rule 1: Format IMMEDIATELY After Every Change

**Do NOT batch formatting at the end of a task.** Format immediately after each file modification.

| File Type       | Formatter | Command                                          |
| --------------- | --------- | ------------------------------------------------ |
| C# (`.cs`)      | CSharpier | `dotnet tool run csharpier format .`             |
| Everything else | Prettier  | `node scripts/run-prettier.js --write -- <file>` |

### Rule 2: Run Linters IMMEDIATELY After Every Change

**Do NOT wait until task completion.** Run the appropriate linter after each file modification and fix issues before proceeding.

### Rule 3: Fix Before Moving On

1. Make a change to a file
2. Run the appropriate linter(s) for that file type
3. Fix any issues found
4. Only then move to the next file or task

---

## Common Mistakes

**Wrong** (batching until end):

1. Edit markdown file
2. Edit C# file
3. Edit YAML file
4. ... more edits ...
5. Run all formatters/linters at the end

**Correct** (format immediately after each):

1. Edit markdown -> `node scripts/run-prettier.js --write -- <file>` -> `npm run lint:markdown`
2. Edit C# -> `dotnet tool run csharpier format .`
3. Edit YAML -> `node scripts/run-prettier.js --write -- <file>` -> `pwsh -NoProfile -File scripts/lint-yaml.ps1 -Paths <file>`
4. Edit test file -> `pwsh -NoProfile -File scripts/lint-tests.ps1` -> `dotnet tool run csharpier format .`

For detailed workflow patterns and more examples, see [formatting](../skills/formatting.md).

### A new analyzer diagnostic needs a CLEAN typecheck, because the incremental one lies

`npm run typecheck:tests` exited **0** on a tree the same command reported **four `WPROTO044`
errors** on once `TestCheck/obj` was deleted (session 238): the `.cs` files had not changed, MSBuild
skipped the compile, and a gate that looked at nothing prints what a pass prints. Deleting `obj/` is
not sufficient either -- session 239, a shared `VBCSCompiler` served a stale snapshot and reported
diagnostics at pre-edit line numbers.

```bash
npm run typecheck:unity:clean               # every tree
npm run typecheck:unity:clean typecheck:tests   # or just one
```

It deletes every `Generator~/*/obj` and `bin` and exports `UseSharedCompilation=false`, which
MSBuild reads as a global property. Several times slower, so reach for it only when an analyzer DLL
is in the diff.

### Editor test fixtures

`EditorTestCheck` compiles `Tests/Editor/**` and is the only thing that does
([#616](https://github.com/Ambiguous-Interactive/unity-helpers/issues/616)); before it landed, one
fixture reached the Unity matrix twice with `typecheck:tests` green both times.

A `[WProtoContract]` fixture there has one trap: `WPROTO001` wants `partial` on the type **and
every type enclosing it**, because the formatter is nested. A `[TestFixture]` cannot be partial, so
put such fixtures at namespace scope.

**A new `Runtime/` file that an existing `Runtime/` file depends on breaks a build no typecheck
project runs.** `Proto.Generator.Tests` names its Runtime sources one by one rather than globbing,
so an interface `SerializableValueTuple` implements compiled clean in all sixteen `typecheck:unity`
legs and failed there with `CS0246` (session 240). When the diff adds a `Runtime/` file that
something in that csproj references, run `dotnet test -c Release -p:ProtobufNetOracle=v3` there.

**`WPROTO044` is Unity-only, and no check project can change that.** It reports a subclass whose
base is in **another assembly**, and every check project flattens many asmdefs into ONE compilation,
where "same assembly" is the _correct_ answer -- so narrowing the guard does not help. The rule is
held cross-assembly in the generator's own suite instead, and a **generic** base is deliberately
exempt forever. Full reasoning, the blocker for a split project, and the tests recording both
decisions are in `EditorTestCheck`'s csproj header
([#650](https://github.com/Ambiguous-Interactive/unity-helpers/issues/650)).

---
