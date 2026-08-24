# LLM Agent Instructions

Procedural skills are in the [skills/](./skills/) directory.

---

## Repository Overview

**Package**: `com.wallstop-studios.unity-helpers`
**Version**: 3.5.1
**Repository**: <https://github.com/wallstop/unity-helpers>
**Root Namespace**: `WallstopStudios.UnityHelpers`

**Design Principles**: Zero boilerplate, performance-proven (12,000+ tests, IL2CPP/WebGL compatible), DRY architecture, self-documenting code (minimal comments, descriptive names).

---

## Project Structure

```text
Runtime/                   # Runtime C# libraries
  Core/
    Attributes/            # Inspector & component attributes
    DataStructure/         # Spatial trees, heaps, queues, tries, cyclic buffers
    Extension/             # Extension methods for Unity types, collections, strings, math
    Helper/                # Buffers, pooling, singletons, compression, logging
    Math/                  # Math utilities, ballistics, geometry
    Model/                 # Serializable types (Dictionary, HashSet, Nullable, Type, Guid)
    OneOf/                 # Discriminated unions
    Random/                # 20+ PRNG implementations with IRandom interface
    Serialization/         # JSON/Protobuf serialization with Unity type converters
    Threading/             # Thread pools, main thread dispatcher, guards
  Tags/                    # Effects/attribute system (AttributeEffect, TagHandler, Cosmetics)
  Visuals/                 # Visual components (EnhancedImage, LayeredImage)

Editor/                    # Editor-only tooling
  CustomDrawers/           # Property drawers (including Odin/ subdirectory)
  CustomEditors/           # Custom inspectors (including Odin inspectors)
  Tools/                   # Editor windows (Animation Creator, Texture tools, etc.)

Tests/
  Runtime/                 # PlayMode tests mirroring Runtime/ structure
  Editor/                  # EditMode tests mirroring Editor/ structure
  Core/                    # Shared test utilities and helper types

Samples~/                  # Sample projects (imported via Package Manager)
```

---

## Skills Reference

The full skill catalog, grouped by category, is in the generated
**[Skills Index](./skills/index.md)**. Regenerate it after adding or editing any
skill's trigger comment with `pwsh -NoProfile -File scripts/generate-skills-index.ps1`
(validated by `scripts/lint-llm-instructions.ps1`).

## Critical Rules Summary

See [create-csharp-file](./skills/create-csharp-file.md) for detailed C# rules.

### C# Code Rules

1. `using` directives INSIDE namespace; `#if` blocks INSIDE namespace; `#define` at file top
2. NO underscores in method names (including tests)
3. Explicit types over `var`
4. **NEVER use `#region` or `#endregion`** (see [no-regions](./skills/no-regions.md))
5. NEVER use nullable reference types (`string?`)
6. One file per MonoBehaviour/ScriptableObject (production AND tests)
7. NEVER use `?.`, `??`, `??=` on UnityEngine.Object types
8. Minimal comments -- only explain **why**, never **what**
9. Generate `.meta` files after creating ANY file/folder (see [create-unity-meta](./skills/create-unity-meta.md)); exception: no `.meta` for dot folders (`.llm/`, `.github/`, `.git/`, `.vscode/`). Use `./scripts/generate-meta.sh <path>` for new or empty folders, then run `npm run agent:preflight:fix` for changed-file `.meta` recovery.
10. Enums: explicit values, `None`/`Unknown` = 0 with `[Obsolete]` (see [create-enum](./skills/create-enum.md))
11. Never reflect on our own code; use `internal` + `[InternalsVisibleTo]` (see [avoid-reflection](./skills/avoid-reflection.md))
12. Never use magic strings; use `nameof()` (see [avoid-magic-strings](./skills/avoid-magic-strings.md))
13. All code must follow [high-performance-csharp](./skills/high-performance-csharp.md) and [defensive-programming](./skills/defensive-programming.md) (never throw from public APIs; use `TryXxx` patterns; handle all inputs gracefully)
14. For forbidden patterns and alternatives, see [forbidden-patterns reference](./references/forbidden-patterns.md)
15. All editor mutation paths must follow the complete undo policy (see [editor-undo-complete](./skills/editor-undo-complete.md)); classify paths as Tier A/B/C and never claim full reversal for Tier C file/reimport side effects
16. `AssetPostprocessor` callbacks MUST defer non-trivial work through `AssetPostprocessorDeferral.Schedule` to avoid `SendMessage cannot be called...` warnings during Unity's import phase — and deferral is **necessary, not sufficient**: a deferred `LoadAllAssetsAtPath` still deserializes the asset and still runs the consumer's `OnValidate`, so never answer a metadata question with a load (see [asset-postprocessor-safety](./skills/asset-postprocessor-safety.md))
17. **The package ships TWO analyzer assemblies, and a new diagnostic has to pick the right one.**
    `WPROTO###` (`Generator~/WallstopStudios.UnityHelpers.Proto.Generator`) reports a serialization
    contract that cannot be honoured, so it is an **error** -- the alternative is an exception from
    inside a shipped player. `WUH###`
    (`Generator~/WallstopStudios.UnityHelpers.Analyzers`) reports an allocation or footgun in code
    that already works, so it is **on by default, capped at `DiagnosticSeverity.Warning`, and
    suppressible**: taking a package upgrade must never fail a consumer's build. Putting a
    general-purpose rule in the `WPROTO` family makes the error code lie about its scope. Both DLLs
    are committed under `Runtime/Analyzers`, both are byte-compared against a fresh
    `dotnet build -c Release` of their sources in CI (SDK pinned to 9.0.306), and **an edit to
    either one is not finished until you rebuild it** -- CSharpier reformatting the source is enough
    to change the deterministic bytes. See [analyzers](../docs/performance/analyzers.md)
18. NEVER size an allocation from a number a payload states -- only from what it delivers. A length prefix is safe because the reader refuses one longer than the bytes it holds; a capacity is a bare claim, and six bytes can ask for 8 GB. Clamp it with `SerializationCapacityLimits.Clamp` where it is a growth hint, refuse it with `TryAccept` where it is semantic (see [untrusted-payload-limits](./skills/untrusted-payload-limits.md))

### Documentation Rules

- **Documentation is NOT optional.** Every user-facing change MUST update: CHANGELOG, XML docs, feature docs in `docs/`
- CHANGELOG is for USER-FACING changes ONLY. Internal changes (CI/CD, build scripts, dev tooling) do NOT belong
- **CHANGELOG entries are SHORT** -- one or two sentences, plain language, lead with the user-visible effect, and start with the verb its section names (`Add`, `Fix`, `Bound`, ...). No root-cause narration, no mechanism, no run IDs or "verified on...". Longer explanations go in a `docs/` guide with a link. `npm run lint:changelog` fails an entry over **300 rendered characters** (issue references and link targets are not counted), so the limit is enforced rather than remembered
- A fix for a defect that was never in a release is NOT a `Fixed` entry (nor a `Changed`/`Security` one). The feature ships correct: fold what the fix guarantees a user into that feature's `Added` entry and drop the rest. Decide with git -- `git ls-tree -r --name-only <last-tag> -- <path>` -- not memory
- All public members require `<summary>` XML tags
- See [update-documentation](./skills/update-documentation.md) for detailed standards

### Markdown & Links

- Internal links MUST use `./` or `../` prefix; never use absolute GitHub Pages paths (`/unity-helpers/...`)
- Never use backtick-wrapped markdown file references; use proper links
- Escape example links with code blocks/backticks; escape pipe characters in tables with `\|`
- Markdown code blocks require language specifiers; never use emphasis as headings

### Formatting & Validation (Run After Each Change)

Run formatters/linters **immediately after each file change**, not batched at task end:

- **C#**: `dotnet tool run csharpier format .` (or `npm run format:csharp`). `npm run agent:preflight:fix` formats changed C# files and `npm run agent:preflight` / `validate:local` fail on unformatted C#, so a later edit that undoes the formatting is caught locally rather than by CI
- **Non-C#** (`.md`, `.json`, `.yaml`, `.yml`): `node scripts/run-prettier.js --write -- <file>` (repo-local launcher; run `npm install` first on the host that runs hooks)
- **Markdown**: `npm run lint:docs` + `npm run lint:markdown`
- **YAML**: `npm run lint:yaml` (then `actionlint` for workflows)
- **Spelling**: `npm run lint:spelling` (add valid terms to `cspell.json`). A Claude Code PostToolUse hook (`scripts/hooks/cspell-post-edit.js`, registered in the tracked [`.claude/settings.json`](../.claude/settings.json) which ships with the repo) auto-runs cspell after every Edit/Write/MultiEdit/NotebookEdit, so typos surface immediately; manual invocation before completion remains the expectation (the hook is a safety net, not a substitute -- it does not fire in CI or when editing outside Claude Code)
- **Tests**: `pwsh -NoProfile -File scripts/lint-tests.ps1 -FixNullChecks -Paths <changed test files>`, then `pwsh -NoProfile -File scripts/lint-tests.ps1 -Paths <changed test files>`. Passing more than one path only works because every `-Paths` script now declares a `ValueFromRemainingArguments` sibling -- `pwsh -File` binds the first token and drops the rest, so before that these commands linted ONE file and printed "No issues found". `PWS005` enforces the declaration
- **Skill files and [context](./context.md)**: `pwsh -NoProfile -File scripts/lint-skill-sizes.ps1` (500-line limit)
- **Commit prep**: stage files, then run `npm run agent:preflight:fix` (includes changed spell-checkable file checks) before any commit attempt
- **Pre-push validation**: run `npm run validate:prepush` before push; it is a roughly one-second
  last-resort Git/config safety check. Run relevant changed-file checks through
  `npm run agent:preflight`; use `npm run validate:local` only when a complete repository-wide
  aggregate is warranted. When hook or agent-preflight behavior changes, also run
  `npm run validate:tests:hook-regressions`. CI always runs the combined `validate:tests`
  aggregate. Treat git hooks as last-resort only. For
  the push step itself (setup, redirection, rejection handling) follow
  [ship-changes Step 9](./skills/ship-changes.md#step-9-push-to-remote)

See [formatting](./skills/formatting.md) and [validate-before-commit](./skills/validate-before-commit.md) for details.

### Additional Technical Rules

- **Run the control FIRST, and let it decide whether the platform can be measured.** An allocation
  assertion needs an instrument that can see an allocation; on an IL2CPP standalone player it cannot,
  so `Is.Not.AllocatingGCMemory()` there is the absence of a measurement rather than a pass. Assert
  the control moves, and `Assert.Ignore` when it does not, instead of asserting the subject and
  hoping. A control asserted _after_ the subject turns an unmeasurable platform into a red build
  (session 220, two gated IL2CPP legs).
- **Order every comparison left-to-right: use only `<` and `<=`.** `index >= 0` becomes
  `0 <= index`, `a > b` becomes `b < a`, and a range reads as one line of number line:
  `0 <= sum && sum < max`. Swap the operands, not the meaning -- and check for side effects before
  swapping, because swapping changes evaluation order. `npm run lint:comparison-direction` enforces
  this over `Runtime/`, `Editor/`, `Tests/` and `Generator~` and runs in `lint:repo`;
  `npm run lint:comparison-direction:fix` rewrites what it can and reports the rest with the reason
  it declined. Relational patterns (`c is >= 'A' and <= 'Z'`) have no left-hand operand to move and
  are exempt. `Runtime/Utils/SevenZip` is vendored upstream verbatim and is excluded.
- **`Scene.handle` is an `int` up to Unity 6000.4 and a `SceneHandle` from 6000.5**, where the
  implicit conversion to `int` is obsolete-as-an-**error**. Compare `Scene` values (`==`,
  `IsValid()`) instead of caching a handle. No local gate catches this: `typecheck:unity` uses
  2021.3 reference assemblies and the MCP editor is on 6000.4, so it cost a full Unity matrix run to
  find. Same class as [#553](https://github.com/Ambiguous-Interactive/unity-helpers/issues/553).
- **`UnityEngine.Object` declares `implicit operator bool`, so no `Component`-shaped expression is
  ever a type error in a boolean position** -- not in a `return`, an `if`, an `&&` or a `!`. A
  `bool`-returning method that ends `return FindTheThing(...);` compiles, converts the found object
  to `true`, and **discards it**. Read a `bool` method with an `out` parameter as one unit: if a path
  returns without writing the `out`, the caller gets a stale value and the compiler will not say so,
  because definite-assignment is satisfied by any earlier write -- including one a failed filter has
  since invalidated. That shipped in 3.5.1: every single relational field with
  `IncludeInactive = false` bound the disabled candidate ahead of the enabled one
  ([#529](https://github.com/Ambiguous-Interactive/unity-helpers/issues/529)).
- **Reach for the math helpers rather than open-coding the arithmetic.** `WallMath.WrappedAdd`,
  `WrappedIncrement` and `PositiveMod` already exist; `(i + 1) % capacity` is a re-implementation
  that also gets the negative case wrong.
- **Never compare against a magic sentinel; test for the valid range.** `index != -1` becomes
  `index >= 0` and `index == -1` becomes `index < 0`. The comparison then says what it means, and it
  refuses a value that is invalid for a reason the sentinel does not cover. Swept to zero across
  `Runtime/` and `Editor/` in session 220; owner review, PR #551.
- **An auto-property's data is serialized under `<Name>k__BackingField`, not `Name`.** Any lookup
  that resolves a member the author NAMED -- a `[WShowIf]` condition, a value source -- must try the
  source name first and `SerializedMemberNames.BackingFieldFor(name)` second, or it silently falls
  through to reading the live C# member and stops seeing un-applied Inspector edits. `[field: Attr]`
  puts an attribute on that backing field, so `AttributeTargets.Field` does not exclude a serialized
  property (#550).
- When editing `.gitignore`, validate with `git check-ignore -v <path>` and run `pwsh -NoProfile -File scripts/lint-gitignore-docs.ps1`
- When adding abbreviations, add them to `cspell.json` (see [cspell dictionary categories](#cspell-dictionary-quick-reference))
- When introducing ANY new all-caps token or acronym in a skill/doc/script (lint error code, new abbreviation, new API name), add it to the correct cspell dictionary category before committing. `npm run agent:preflight` catches this before pre-commit; the `validate-lint-error-codes` contract enforces lint-error-code families permanently
- When introducing a new lint-error-code family (e.g., `UNH001`, `PWS002`), register the 2+ letter uppercase prefix in the root `words` array of `cspell.json`; `npm run validate:lint-error-codes` enforces this contract and fails with a copy-pasteable patch on drift
- Verify GitHub Actions config files exist AND are on default branch
- Never use `((var++))` in bash with `set -e`; use `var=$((var + 1))`
- Line endings must be synchronized across `.gitattributes`, `.prettierrc.json`, `.yamllint.yaml`, `.editorconfig`
- Git hook regex patterns use single backslashes, not double-escaped
- Devcontainer Codex lifecycle changes must keep `.devcontainer/install-codex.sh`, `.devcontainer/post-create.sh`, `.devcontainer/post-start.sh`, and `scripts/tests/test-post-create.sh` in sync (package, command, retry behavior, and lifecycle wiring)
- Codex login in this repository is browser-first (no automatic device-auth fallback). Keep this behavior aligned with `scripts/codex-login.sh`, `.devcontainer/devcontainer.json` port `1455`, and `scripts/tests/test-post-create.sh`
- Use `npm run codex:yolo` (wrapper: `scripts/codex-yolo.sh`) for yolo flows in scripts or non-TTY contexts. Raw `codex --yolo` is interactive-only and should be avoided in automation.
- Release/package changes must keep the `.unitypackage` export smoke gate intact. `Samples~` is renamed to `Samples` by `scripts/unity/export-unitypackage.sh`, so sample assemblies must compile as a release payload, not only as ignored UPM samples.
- Unity licensing logs can contain serial/email fragments even when GitHub secrets are masked. Docker Unity activation/return output must be redacted before it reaches CI logs, and release paths must keep serial return behavior covered by contract tests.
- If a script derives `REPO_ROOT` / `$repoRoot` from its own location, every `git ls-files` / `git diff --relative` / similar repo-relative git call must also be anchored there (`git -C "$REPO_ROOT" ...` or `cd "$REPO_ROOT"` first). Never combine repo-root-derived filesystem paths with caller-cwd-derived git output.
- When adding formatter support for a new language, add explicit `[language]` entry in `devcontainer.json`
- When adding new script calls to git hooks, update the hook's step comments AND the "What the Hook Does" list in [formatting-and-linting](./skills/formatting-and-linting.md)
- Never run `pwsh -File .githooks/<hook>` for extensionless hook launchers. Run the hook directly through Git/shell, or invoke `.githooks/<hook>.ps1` when debugging the PowerShell implementation.
- Never redirect git command output to files in the working tree (e.g. `git push 2> pre-push.txt`) — creates gitignored pollution. Let errors stream to stderr; pre-push and `npm run agent:preflight:fix` auto-remove gitignored hook artifacts before validation
- **A new `.sh` needs `git update-index --chmod=+x <path>` after staging.** `.git` is bind-mounted
  from the host (`devcontainer.json` `mounts`), so `.git/config` carries Git-for-Windows'
  `filemode = false` and `chmod +x` in the container never reaches the index — the file stays
  `100644` there while the filesystem shows `755`. Do NOT "fix" this by setting `core.fileMode true`:
  the host shares that config and would then see every file as modified. `test:shell-portability`
  catches the mismatch, but only for **tracked** files, so stage first and validate second (the
  order [validate-before-commit](./skills/validate-before-commit.md) already prescribes) or the
  check passes locally and fails in CI

---

## Build & Development Commands

```bash
# Setup
npm run hooks:install                                   # Install git hooks
dotnet tool restore                                     # Restore .NET tools (CSharpier, etc.)

# Formatting & Linting
npm run agent:preflight:fix                            # Fast changed-file preflight with safe auto-fixes
npm run lint:repo                                       # Every check the Repo Lint workflow runs
npm run lint:repo -- --list                             # List the check ids
npm run lint:repo -- --only doc-links,spelling          # Re-run just the checks that failed
npm run lint:repo -- --jobs 1                           # Serialize (default: one worker per core)
dotnet tool run csharpier format .                      # Format C#
npm run lint:spelling                                   # Spell check
npm run lint:docs                                       # Lint documentation links
npm run lint:markdown                                   # Markdownlint rules
npm run lint:yaml                                       # YAML style
npm run lint:dependabot                                 # Dependabot config schema
pwsh -NoProfile -File scripts/lint-tests.ps1            # Lint test lifecycle
pwsh -NoProfile -File scripts/lint-skill-sizes.ps1      # Skill file sizes
pwsh -NoProfile -File scripts/lint-gitignore-docs.ps1   # Validate gitignore safety
pwsh -NoProfile -File scripts/lint-doc-counts.ps1       # Validate doc counts match codebase
pwsh -NoProfile -File scripts/sync-doc-counts.ps1       # Sync doc counts to all files

# Unity Compilation & Testing (via Docker) -- run directly, don't ask user
bash scripts/unity/setup.sh                             # One-time setup (idempotent)
bash scripts/unity/compile.sh                           # Compile package
bash scripts/unity/run-tests.sh                         # Run EditMode tests
bash scripts/unity/run-tests.sh --mode playmode         # Run PlayMode tests
bash scripts/unity/run-tests.sh --mode all              # Run all tests
```

See [unity-devcontainer-testing](./skills/unity-devcontainer-testing.md) for full details.

---

## Naming Conventions

| Element               | Convention  | Example                     |
| --------------------- | ----------- | --------------------------- |
| Types, public members | PascalCase  | `SerializableDictionary`    |
| Fields, locals        | camelCase   | `keyValue`, `itemCount`     |
| Interfaces            | `I` prefix  | `IResolver`, `ISpatialTree` |
| Type parameters       | `T` prefix  | `TKey`, `TValue`            |
| Events                | `On` prefix | `OnValueChanged`            |
| Constants (public)    | PascalCase  | `DefaultCapacity`           |

- C# files: 4 spaces indentation; config files (`.json`, `.yaml`, `.asmdef`): 2 spaces
- Line endings: CRLF for most files; YAML/`.github/**`/Markdown/Jekyll includes use LF
- Encoding: UTF-8 (no BOM)

---

## cspell Dictionary Quick Reference

Add unknown words to the appropriate dictionary in `cspell.json`:

| Dictionary      | Purpose                                                 | Examples                                |
| --------------- | ------------------------------------------------------- | --------------------------------------- |
| `unity-terms`   | Unity Engine APIs, components, lifecycle                | MonoBehaviour, GetComponent, OnValidate |
| `csharp-terms`  | C# language features, .NET types                        | readonly, nullable, LINQ, StringBuilder |
| `package-terms` | This package's public API and type names                | WallstopStudios, IRandom, SpatialHash   |
| `tech-terms`    | General programming/tooling terms                       | async, config, JSON, middleware         |
| root `words`    | Project-specific tokens, incl. lint-error-code prefixes | UNH, PWS (covers UNH001, PWS002…)       |

Lint-error-code prefixes (`^[A-Z]{2,}\d{3}$` tokens like `UNH001`, `PWS002`) must be registered in the root `words` array. `npm run validate:lint-error-codes` is the contract test and will fail with a copy-pasteable patch on drift.

---

## Assembly Definitions

| Assembly                                      | Purpose                       |
| --------------------------------------------- | ----------------------------- |
| `WallstopStudios.UnityHelpers`                | Runtime code                  |
| `WallstopStudios.UnityHelpers.Editor`         | Editor code                   |
| `WallstopStudios.UnityHelpers.Tests.Runtime`  | Runtime tests                 |
| `WallstopStudios.UnityHelpers.Tests.Editor`   | Editor tests (parent)         |
| `WallstopStudios.UnityHelpers.Tests.Editor.*` | Feature-specific editor tests |
| `WallstopStudios.UnityHelpers.Tests.Core`     | Shared test utilities         |

**Critical**: Test assemblies use `overrideReferences: true`, so each must independently list ALL required precompiled DLLs it directly compiles against. Odin-specific source must list the Sirenix DLLs it directly compiles against and define `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR` from the `odininspector` package. Runtime Odin bases use that package-owned define with Unity fallbacks, and the runtime asmdef stays in editor-style auto-reference mode (`overrideReferences: false`) so no-Odin registry installs never name missing Sirenix DLLs. See [manage-assembly-definitions](./skills/manage-assembly-definitions.md).

---

## Agent-Specific Rules

- Keep changes minimal and focused; respect folder boundaries (Runtime vs Editor)
- Follow `.editorconfig` formatting rules strictly
- NEVER pipe output to `/dev/null`; NEVER hard-code machine-specific absolute paths
- Agents may stage, commit, and push completed work when the task calls for publication. Use the
  repository's git staging retry helpers, keep commits focused, and never rewrite or discard user
  history without explicit authorization.
- NEVER invoke the local GitHub CLI (`gh`) for agent work. Use the VS Code GitHub extension/connector
  first for GitHub reads and mutations, then plain `git` for repository operations the connector
  cannot perform. If neither path can complete the task, report the blocker instead of falling back
  to `gh`. This restriction applies to agent tooling, not tracked GitHub Actions steps that invoke
  `gh` inside CI.
- **The GitHub API IS reachable from inside the devcontainer, including headless.** Two sessions in a
  row reported "no token exists in the container" and handed the pull request back to the owner, and
  both were wrong. Opening a pull request or filing an issue is an agent step, not a hand-back.

  **There is exactly one way to get the credential, and it never asks the Dev Containers helper:**

  ```bash
  TOKEN="$(bash scripts/github-token.sh)"    # exit 3 and an actionable message when there is none
  ```

  It reads `$GITHUB_TOKEN` / `$GH_TOKEN` when they are non-empty (in this container they are exported
  and **empty**, which is why emptiness rather than definedness is the test) and otherwise a 0600
  cache file. `git push` and `git fetch` read the same cache, because
  `scripts/normalize-container-git-config.sh` installs the script as the **only** credential helper
  for github.com in the container's `~/.gitconfig`.

  **NEVER run `git credential fill`, and never invoke the Dev Containers helper directly.** That
  helper answers by raising a dialog **on the owner's desktop**, one per invocation — a session that
  probes it, then pushes, then retries has interrupted a human three times for work nobody was
  watching. The single deliberate prompt lives behind `npm run github:token:bootstrap`, which **a
  human** runs once per container; `npm run github:token:store` takes a pasted token on stdin with no
  dialog at all. If the script exits 3, report that and ask — do not go looking for another way to
  ask the desktop.

  With the cache populated nothing prompts at all. With it **empty**, git falls back to
  `GIT_ASKPASS`, which no credential helper can override — so the container **is** the askpass:
  `remoteEnv` points it at `scripts/git-askpass-refuse.sh`, which prints the fix to stderr and exits
  non-zero. An exit 3 is therefore a request for a human, not an invitation to retry the operation
  until something answers.

  When it does prompt: **hang versus immediate answer is the only discriminator**, never empty
  output. A blocked helper is a dialog nobody has answered yet, and reading its truncated empty read
  as "no credential exists" is how three sessions concluded the container had none and handed a
  finished branch back without pushing it.

  Never echo the token, never write it to a file in the working tree, and pass it to a subprocess
  through the environment rather than on a command line.

- For git-interacting scripts, use retry helpers from `scripts/git-staging-helpers.sh` (see [git-safe-operations](./skills/git-safe-operations.md))
- Write exhaustive tests for every change (see [create-test](./skills/create-test.md))
- Use high-performance search tools: `rg` not `grep`, `fd` not `find`, `bat --paging=never` not `cat` (see [search-codebase](./skills/search-codebase.md))
- For CI/CD bash scripts, use POSIX-compliant tools (see [validate-before-commit](./skills/validate-before-commit.md#portable-shell-scripting-in-workflows-critical))
- **Do not commit**: `Library/`, `obj/`, secrets, tokens. **Do commit**: `.meta` files for all assets
- **Verify `.asmdef` references** when adding new namespaces
- Commits: short, imperative summaries (e.g., "Fix JSON serialization for FastVector"); group related changes
- PRs: **short and plain.** A title of 50 characters or fewer naming the effect the user sees,
  then one `**Why:**` sentence, two to five one-line `**What:**` bullets, and `Fixes #123`.
  Nothing else -- no root causes, no measurements, no validation reports. Those go in the commit
  body, the progress log, or the linked issue. Include before/after screenshots for UI changes.
  See [ship-changes](./skills/ship-changes.md#step-9b-open-the-pull-request-yourself)

### Re-running local aggregates costs your session -- CI runs them anyway

The rule above is about not wasting CI. This one is its opposite number, and session 218 got it
wrong: it re-ran `lint:repo`, `validate:tests` and `typecheck:unity` after nearly every change, and
forced Unity **clean rebuilds at ~5 minutes each** to sweep an assembly that a `rg` over the same
directory had already answered. CI runs those exact gates on the push. Spending an hour proving
locally what the matrix will prove anyway buys nothing and costs the next issue.

- **The edit loop is `npm run agent:preflight` (2.9 s) plus the targeted check for what you touched.**
  Run the aggregate ONCE, before the push, not after each commit.
- **Prefer the cheap instrument that answers the question.** A `rg` for the shape, a single
  `--only <id>`, one `dotnet test --filter` -- before a whole-tree rebuild. Reach for the expensive
  one when the cheap one is genuinely inconclusive, and say which you used.
- **A Unity clean rebuild is a last resort**, not a routine sweep. `AssetDatabase.Refresh` alone is
  usually enough; `RequestScriptCompilationOptions.CleanBuildCache` recompiles everything.
- **When you skip a gate, name what is unverified** rather than implying parity. "Runtime is
  analyzer-swept with a control; Editor is grep-checked only" is a useful sentence. "All clean" when
  one of the three was a grep is not.
- Costs, measured 2026-08-23: `agent:preflight` 2.9 s, `validate:prepush` 1.3 s,
  `validate:tests:fast` ~150 s, `lint:repo` ~300 s, `typecheck:unity` minutes, a Unity clean rebuild
  ~5 min. The three checks that dominate the contract suite are tracked on
  [#540](https://github.com/Ambiguous-Interactive/unity-helpers/issues/540).

### Pushing costs a full CI matrix -- batch before you push

**Every push to the remote triggers the whole CI matrix**, including four Unity editor versions in
editmode, playmode and gated IL2CPP standalone. That is expensive and slow. Treat a push as a
deliberate act, not the tail of every commit.

- **Commit locally as often as is useful; push once**, when a coherent unit of work is verified.
  Small, focused commits are still right -- it is the _pushing_ that is costly, not the committing.
- **Exhaust the local gates first.** In rough order of cost, all of them cheaper than one CI run:
  - `npm run typecheck:unity` -- compiles the real `Runtime/**` against UnityEngine reference
    assemblies with the shipped analyzer loaded, in seconds. Catches `CS####` and `WPROTO###`.
    **Those reference assemblies are `UnityEngine.Modules` 2021.3.33, older than every editor CI
    runs, and that is the only version the package has ever published** -- so the pin cannot be
    moved and the gate prints what it is on every compile. Member signatures are safe to check
    here; anything resolved out of Unity's own metadata (attribute targets, defaults, serialization
    behaviour) has to be confirmed in a real editor, because the failure mode is a confident answer
    for a Unity nobody ships ([#553](https://github.com/Ambiguous-Interactive/unity-helpers/issues/553)).
    It builds each source tree four ways, because four different branches ship: the
    `WALLSTOP_PROTO` default, the legacy define-off fallback,
    `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR` (`typecheck:unity:odin` / `typecheck:tests:odin`) and
    `SINGLE_THREADED` (`typecheck:unity:singlethreaded` / `typecheck:tests:singlethreaded`).
    `SINGLE_THREADED` is guarded for the same reason as Odin and was found the same way (#533): it
    swaps declarations, not just call sites -- `ReflectionHelpers` alone moves five caches between
    `ConcurrentDictionary` and `Dictionary` under it -- and CI runs two `SINGLE_THREADED` legs, so a
    cache added without the matching branch passed every local gate and cost a full matrix run.
    The Odin configuration exists because Odin changes the **base class** of
    `RuntimeSingleton<T>`, `ScriptableObjectSingleton<T>` and `AttributeEffect`, and that branch
    compiled nowhere in automation until #347 -- which is how #275 shipped a compile break to
    consumers. Odin is paid and has no NuGet package, so `Shims/OdinInspectorShim.cs` declares the
    two base classes the runtime aliases and nothing else. The **editor-side** Odin surface (nine
    drawers, three inspectors) still compiles nowhere: reaching it means compiling the whole Editor
    assembly, which needs a `UnityEditor` reference the community reference assemblies do not
    carry. That half is tracked on #347.
  - `dotnet test -c Release -p:ProtobufNetOracle=v3` and then
    `dotnet test -c Release -p:ProtobufNetOracle=v2` in
    `Generator~/WallstopStudios.UnityHelpers.Proto.Generator.Tests` -- the real serializer sources
    against protobuf-net 3.2.56 and 2.4.9 in isolated processes. **`-c Release` is what CI runs and
    what the throughput gate needs**: the oracle is a precompiled release assembly whatever the
    configuration says, so an unoptimized run would be comparing one implementation's debug build
    against another's release build. Those assertions are skipped, loudly, outside Release; the
    allocation gates are configuration-independent and run either way.
  - `npm run agent:preflight:fix` then `npm run agent:preflight`.
  - `.venv/bin/mkdocs build --strict` when the change touches `docs/**` or `mkdocs.yml`. It is
    the exact command the Validate Documentation job runs, takes ~30 s, and is the only local
    check that catches a link leaving the docs tree -- a relative link from `docs/` out to
    `.github/workflows/` aborts the strict build, and `lint:docs` and `lint:markdown` both pass
    it. Reference workflow files as inline code, the way the runbooks do.
  - Relevant targeted checks for the files changed; `npm run validate:local` is the explicit
    repository-wide lint and contract aggregate when that broader evidence is warranted.
  - `npm run validate:prepush` as the final fast Git/config safety check.
  - The Unity MCP bridge, which compiles your working tree in a real editor **and runs real
    fixtures against it**. `Unity_RunCommand` cannot _name_ a package type -- its sandbox
    assembly does not reference them, and `using System.Reflection;` is refused -- but fully
    qualified reflection reaches everything, including generic package types and their private
    members. See
    [unity-devcontainer-testing](./skills/unity-devcontainer-testing.md#no-license-the-mcp-editor-still-runs-the-real-fixtures)
    for the loop and its traps ([#435](https://github.com/Ambiguous-Interactive/unity-helpers/issues/435)).
- **When a change spans both suites, update both before pushing.** A packed-encoding change in
  session 175 updated the `Generator~` differentials, missed the Unity golden vectors in
  `Tests/Runtime/Serialization/`, and cost a full matrix run to discover. Grep for the affected byte
  literals in `Tests/` as well as `Generator~/`.
- **Superseding your own run is pure waste**, and it also reds the previous commit: a stale run
  fails with a `Stale pull request run for <sha>` annotation, which looks like breakage until you
  read the annotations.

### Test Execution

Run Unity tests directly via Docker-in-Docker:

1. Check license: `pwsh -NoProfile -File scripts/unity/setup-license.ps1 -Check`
   - If exit code 1: warn user to run `npm run unity:setup-license`, then reach for the MCP bridge rather than skipping Unity entirely -- it needs no license and runs EditMode fixtures against your working tree. Docker legs and PlayMode stay skipped; continue with relevant non-Unity checks
2. Compile: `bash scripts/unity/compile.sh`
   - If output contains `Machine bindings don't match` or `No valid Unity Editor license found`: license issue, not code issue. Warn user, skip Unity tests, continue with relevant non-Unity checks
   - If compilation fails for other reasons: fix the code
3. Run `bash scripts/unity/run-tests.sh` (EditMode) and `bash scripts/unity/run-tests.sh --mode playmode` (PlayMode). A `--filter` that matches nothing now fails rather than reporting a clean run
4. Parse test results and fix any failures before marking work complete
5. Always run the relevant targeted non-Unity checks and the fast `npm run validate:prepush` safety check regardless of Unity license availability

See [unity-devcontainer-testing](./skills/unity-devcontainer-testing.md) for targeted test filters and troubleshooting.
