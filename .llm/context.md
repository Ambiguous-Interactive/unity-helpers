# LLM Agent Instructions

Procedural skills are in the [skills/](./skills/) directory.

---

## Repository Overview

**Package**: `com.wallstop-studios.unity-helpers`
**Version**: 3.3.0
**Repository**: <https://github.com/wallstop/unity-helpers>
**Root Namespace**: `WallstopStudios.UnityHelpers`

**Design Principles**: Zero boilerplate, performance-proven (11,000+ tests, IL2CPP/WebGL compatible), DRY architecture, self-documenting code (minimal comments, descriptive names).

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
    Random/                # 15+ PRNG implementations with IRandom interface
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
16. `AssetPostprocessor` callbacks MUST defer non-trivial work through `AssetPostprocessorDeferral.Schedule` to avoid `SendMessage cannot be called...` warnings during Unity's import phase (see [asset-postprocessor-safety](./skills/asset-postprocessor-safety.md))

### Documentation Rules

- **Documentation is NOT optional.** Every user-facing change MUST update: CHANGELOG, XML docs, feature docs in `docs/`
- CHANGELOG is for USER-FACING changes ONLY. Internal changes (CI/CD, build scripts, dev tooling) do NOT belong
- All public members require `<summary>` XML tags
- See [update-documentation](./skills/update-documentation.md) for detailed standards

### Markdown & Links

- Internal links MUST use `./` or `../` prefix; never use absolute GitHub Pages paths (`/unity-helpers/...`)
- Never use backtick-wrapped markdown file references; use proper links
- Escape example links with code blocks/backticks; escape pipe characters in tables with `\|`
- Markdown code blocks require language specifiers; never use emphasis as headings

### Formatting & Validation (Run After Each Change)

Run formatters/linters **immediately after each file change**, not batched at task end:

- **C#**: `dotnet tool run csharpier format .`
- **Non-C#** (`.md`, `.json`, `.yaml`, `.yml`): `node scripts/run-prettier.js --write -- <file>` (repo-local launcher; run `npm install` first on the host that runs hooks)
- **Markdown**: `npm run lint:docs` + `npm run lint:markdown`
- **YAML**: `npm run lint:yaml` (then `actionlint` for workflows)
- **Spelling**: `npm run lint:spelling` (add valid terms to `cspell.json`). A Claude Code PostToolUse hook (`scripts/hooks/cspell-post-edit.js`, registered in the tracked [`.claude/settings.json`](../.claude/settings.json) which ships with the repo) auto-runs cspell after every Edit/Write/MultiEdit/NotebookEdit, so typos surface immediately; manual invocation before completion remains the expectation (the hook is a safety net, not a substitute -- it does not fire in CI or when editing outside Claude Code)
- **Tests**: `pwsh -NoProfile -File scripts/lint-tests.ps1 -FixNullChecks -Paths <changed test files>`, then `pwsh -NoProfile -File scripts/lint-tests.ps1 -Paths <changed test files>`
- **Skill files and [context](./context.md)**: `pwsh -NoProfile -File scripts/lint-skill-sizes.ps1` (500-line limit)
- **Commit prep**: stage files, then run `npm run agent:preflight:fix` (includes changed spell-checkable file checks) before any commit attempt
- **Pre-push parity**: run `npm run validate:prepush` (includes full `lint:spelling`) before push; treat git hooks as last-resort only. For the push step itself (setup, redirection, rejection handling) follow [ship-changes Step 9](./skills/ship-changes.md#step-9-push-to-remote)

See [formatting](./skills/formatting.md) and [validate-before-commit](./skills/validate-before-commit.md) for details.

### Additional Technical Rules

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
- If a script derives `REPO_ROOT` / `$repoRoot` from its own location, every `git ls-files` / `git diff --relative` / similar repo-relative git call must also be anchored there (`git -C "$REPO_ROOT" ...` or `cd "$REPO_ROOT"` first). Never combine repo-root-derived filesystem paths with caller-cwd-derived git output.
- When adding formatter support for a new language, add explicit `[language]` entry in `devcontainer.json`
- When adding new script calls to git hooks, update the hook's step comments AND the "What the Hook Does" list in [formatting-and-linting](./skills/formatting-and-linting.md)
- Never run `pwsh -File .githooks/<hook>` for extensionless hook launchers. Run the hook directly through Git/shell, or invoke `.githooks/<hook>.ps1` when debugging the PowerShell implementation.
- Never redirect git command output to files in the working tree (e.g. `git push 2> pre-push.txt`) — creates gitignored pollution. Let errors stream to stderr; pre-push and `npm run agent:preflight:fix` auto-remove gitignored hook artifacts before validation

---

## Build & Development Commands

```bash
# Setup
npm run hooks:install                                   # Install git hooks
dotnet tool restore                                     # Restore .NET tools (CSharpier, etc.)

# Formatting & Linting
npm run agent:preflight:fix                            # Fast changed-file preflight with safe auto-fixes
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

**Critical**: Test assemblies use `overrideReferences: true`, so each must independently list ALL required precompiled DLLs. Include `Sirenix.Serialization.dll` if the assembly uses any type derived from `ScriptableObjectSingleton<T>`. See [manage-assembly-definitions](./skills/manage-assembly-definitions.md).

---

## Agent-Specific Rules

- Keep changes minimal and focused; respect folder boundaries (Runtime vs Editor)
- Follow `.editorconfig` formatting rules strictly
- NEVER pipe output to `/dev/null`; NEVER hard-code machine-specific absolute paths
- NEVER use `git add` or `git commit` -- user handles all staging/committing
- For git-interacting scripts, use retry helpers from `scripts/git-staging-helpers.sh` (see [git-safe-operations](./skills/git-safe-operations.md))
- Write exhaustive tests for every change (see [create-test](./skills/create-test.md))
- Use high-performance search tools: `rg` not `grep`, `fd` not `find`, `bat --paging=never` not `cat` (see [search-codebase](./skills/search-codebase.md))
- For CI/CD bash scripts, use POSIX-compliant tools (see [validate-before-commit](./skills/validate-before-commit.md#portable-shell-scripting-in-workflows-critical))
- **Do not commit**: `Library/`, `obj/`, secrets, tokens. **Do commit**: `.meta` files for all assets
- **Verify `.asmdef` references** when adding new namespaces
- Commits: short, imperative summaries (e.g., "Fix JSON serialization for FastVector"); group related changes
- PRs: clear description, link related issues (`#123`), include before/after screenshots for UI changes

### Test Execution

Run Unity tests directly via Docker-in-Docker:

1. Check license: `pwsh -NoProfile -File scripts/unity/setup-license.ps1 -Check`
   - If exit code 1: warn user to run `npm run unity:setup-license`, skip Unity steps, continue with `npm run validate:prepush`
2. Compile: `bash scripts/unity/compile.sh`
   - If output contains `Machine bindings don't match` or `No valid Unity Editor license found`: license issue, not code issue. Warn user, skip Unity tests, continue with `npm run validate:prepush`
   - If compilation fails for other reasons: fix the code
3. Run `bash scripts/unity/run-tests.sh` (EditMode) and `bash scripts/unity/run-tests.sh --mode playmode` (PlayMode)
4. Parse test results and fix any failures before marking work complete
5. Always run `npm run validate:prepush` regardless of Unity license availability

See [unity-devcontainer-testing](./skills/unity-devcontainer-testing.md) for targeted test filters and troubleshooting.
