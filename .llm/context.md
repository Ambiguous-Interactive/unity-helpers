# LLM Agent Instructions

Procedural skills are in the [skills/](./skills/) directory.

---

## Repository Overview

**Package**: `com.wallstop-studios.unity-helpers`
**Version**: 3.6.1
**Repository**: <https://github.com/wallstop/unity-helpers>
**Root Namespace**: `WallstopStudios.UnityHelpers`

**Design Principles**: Zero boilerplate, performance-proven (14,000+ tests, IL2CPP/WebGL compatible), DRY architecture, self-documenting code (minimal comments, descriptive names).

---

## Project Structure

```text
Runtime/                   # Runtime C# libraries
  Core/                    # Attributes, DataStructure, Extension, Helper, Math,
                           # Model, OneOf, Random, Serialization, Threading
  Tags/                    # Effects/attribute system (AttributeEffect, TagHandler, Cosmetics)
  Visuals/                 # Visual components (EnhancedImage, LayeredImage)

Editor/                    # Editor-only tooling
  CustomDrawers/           # Property drawers (including Odin/ subdirectory)
  CustomEditors/           # Custom inspectors (including Odin inspectors)
  Tools/                   # Editor windows (Animation Creator, Texture tools, etc.)

Tests/                     # Runtime/ (PlayMode), Editor/ (EditMode), Core/ (shared utilities)

Samples~/                  # Sample projects (imported via Package Manager)
```

---

## Skills Reference

See the generated [Skills Index](./skills/index.md). Regenerate it after adding or editing any
skill's trigger comment with `pwsh -NoProfile -File scripts/generate-skills-index.ps1` (validated by
`scripts/lint-llm-instructions.ps1`).

## Critical Rules Summary

See [create-csharp-file](./skills/create-csharp-file.md) for detailed C# rules.

### C# Code Rules

1. `using` directives INSIDE namespace; `#if` blocks INSIDE namespace; `#define` at file top
2. NO underscores in method names (including tests)
3. Explicit types over `var`
4. **NEVER use `#region` or `#endregion`** (see [no-regions](./skills/no-regions.md))
5. NEVER use nullable reference types (`string?`)
6. One file per MonoBehaviour/ScriptableObject (production AND tests). One member ordering across the codebase (#672): const, events, delegates, static properties, static fields, properties, fields, constructors, static methods, methods -- each tier ordered public → protected → internal → private, including const. A nested type goes at the END of its containing type or in its own file, never between members. `npm run lint:nested-type-placement` enforces both and `:fix` reorders what it can; a member whose move would cross a `#if` boundary or a directive is reported, never moved ([#575](https://github.com/Ambiguous-Interactive/unity-helpers/issues/575), [#672](https://github.com/Ambiguous-Interactive/unity-helpers/issues/672); see [create-csharp-file](./skills/create-csharp-file.md))
7. NEVER use `?.`, `??`, `??=` on UnityEngine.Object types
8. **Aim for zero comments.** Reach for a better name before a better sentence, and spell names out rather than abbreviating. A comment that survives that explains **why**, never **what**; a non-doc comment INSIDE a type or member spanning more than one line uses the `/* ... */` block form, the two-line license header excepted (see [create-csharp-file](./skills/create-csharp-file.md)). `npm run lint:comment-block-form` enforces the block form and `:fix` converts what it can; `Runtime/`, `Editor/`, `Tests/` and `Generator~/` are enforced without baseline exemptions ([#635](https://github.com/Ambiguous-Interactive/unity-helpers/issues/635)). Delete before converting -- a run that only restates the code says **what**
9. Generate `.meta` files with `./scripts/generate-meta.sh <path>` after creating ANY file/folder -- never commit Unity's auto-written stub, which omits the importer block. And **an `AddComponent`-able MonoBehaviour belongs in a runtime-capable test assembly**: Unity refuses one it can identify as an editor script, and a type with no `MonoScript` merely escapes that policy until someone gives it a correctly-named file (12 red tests, session 244). `npm run lint:editor-assembly-monobehaviours` now holds it statically from `includePlatforms` plus asmdef ownership, over the 14 that legitimately live in an Editor-only assembly -- each carrying a reason and a FROZEN `AddComponent` site count, so a new call site reds the excuse instead of hiding behind it ([#678](https://github.com/Ambiguous-Interactive/unity-helpers/issues/678)). Exception: no `.meta` for dot folders (`.llm/`, `.github/`, `.git/`, `.vscode/`). See [create-unity-meta](./skills/create-unity-meta.md)
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
    that already works, so it is **capped at `DiagnosticSeverity.Warning` and suppressible**:
    taking a package upgrade must never fail a consumer's build. On by default, with three exceptions --
    `WUH010` (a dictionary read by indexer), `WUH013` (a counting loop that can use `foreach`),
    and `WUH018` (string equality whose comparison policy is implicit) remain opt-in for consumers
    because their correct shapes are ubiquitous. **The package opts into WUH010, WUH013, and WUH018
    in its shared check-project ruleset**. Every owned `Generator~` project self-hosts both shipped analyzer assemblies and promotes every `WUH###`; the five Unity source gates cover Runtime, Editor, integrations, and both test trees without changing consumer defaults.
    Retain indexed loops when the index is needed or enumeration changes behavior.
    Both DLLs are committed under `Runtime/Analyzers`, byte-compared in CI, and **an edit to
    either is not finished until you rebuild it**. See [analyzers](../docs/performance/analyzers.md)
18. NEVER size an allocation from a number a payload states -- only from what it delivers. A length prefix is safe because the reader refuses one longer than the bytes it holds; a capacity is a bare claim, and six bytes can ask for 8 GB. Clamp it with `SerializationCapacityLimits.Clamp` where it is a growth hint, refuse it with `TryAccept` where it is semantic. **A `stackalloc` sized from a caller's argument is the same rule with a worse failure** -- `StackOverflowException` is caught by nothing, so a length must be a compile-time constant or compared against one in the same statement, with a `SystemArrayPool` rent above `StackAllocation.MaxByteBudget`; `npm run lint:unsafe-code` holds it over 56 sites ([#637](https://github.com/Ambiguous-Interactive/unity-helpers/issues/637)). See [untrusted-payload-limits](./skills/untrusted-payload-limits.md). **The same gate refuses `[Il2CppSetOption(Option.NullChecks, false)]`** and the `ArrayBoundsChecks`/`DivideByZeroChecks` forms, however spelled -- deleting IL2CPP's runtime checks reaches the same undefined behaviour as `unsafe` with neither the keyword nor `allowUnsafeCode`, so nothing else can see it. Writing one with `true`, or with no value, leaves the check on and stays green. Zero sites exist and there is no baseline: the first one reds the build, and the gate carries a positive control because a zero-subject scan cannot prove itself

19. Named methods use brace bodies; lambdas may use expression bodies. Existing expression-bodied methods need the migration and lint gate in [#866](https://github.com/Ambiguous-Interactive/unity-helpers/issues/866).
20. Prefer `string.IsNullOrWhiteSpace` for paths and user-facing text. Use `string.IsNullOrEmpty` when whitespace is valid and only null or empty is the error case.
21. Put each new named class in its own `.cs` file. The current file-naming gate only enforces this for `MonoBehaviour` and `ScriptableObject`; [#865](https://github.com/Ambiguous-Interactive/unity-helpers/issues/865) tracks the wider migration and enforcement policy.

### Documentation Rules

- **Documentation is NOT optional.** Every user-facing change MUST update: CHANGELOG, XML docs, feature docs in `docs/`
- CHANGELOG is for USER-FACING changes ONLY. Internal changes (CI/CD, build scripts, dev tooling) do NOT belong
- **CHANGELOG entries are SHORT** -- one or two sentences, plain language, lead with the user-visible effect, and start with the verb its section names (`Add`, `Fix`, `Bound`, ...). No root-cause narration, no mechanism, no run IDs or "verified on...". Longer explanations go in a `docs/` guide with a link. `npm run lint:changelog` fails an entry over **300 rendered characters** (issue references and link targets are not counted), so the limit is enforced rather than remembered
- A fix for a defect that was never in a release is NOT a `Fixed` entry (nor a `Changed`/`Security` one). The feature ships correct: fold what the fix guarantees a user into that feature's `Added` entry and drop the rest. Decide with git -- `git ls-tree -r --name-only <last-tag> -- <path>` -- not memory
- Public members carry a **minimal** `<summary>` -- this is a public library, and a consumer reads the API surface without the source. Minimal means one short sentence; `<remarks>` is for when it is genuinely needed
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
- **YAML**: `pwsh -NoProfile -File scripts/lint-yaml.ps1 -Paths <changed files>` (then `actionlint <changed workflows>`)
- **Spelling**: `npm run lint:spelling` (add valid terms to `cspell.json`). Run it manually before completion; `npm run agent:preflight` and CI provide the final safety net
- **Tests**: `pwsh -NoProfile -File scripts/lint-tests.ps1 -FixNullChecks -Paths <changed test files>`, then `pwsh -NoProfile -File scripts/lint-tests.ps1 -Paths <changed test files>`. Passing more than one path only works because every `-Paths` script declares BOTH a `ValueFromRemainingArguments` sibling and `[CmdletBinding(PositionalBinding = $false)]` -- `pwsh -File` binds the first token and offers the rest to the other named parameters positionally, so the sibling alone only works when every neighbor happens to be a `[switch]`. Measured: `ensure-editor.ps1 -RequiredEditorPayloadRelativePath a b` put `b` in `-InstallRoot`. `PWS005` enforces both halves
- **Skill files and [context](./context.md)**: `pwsh -NoProfile -File scripts/lint-skill-sizes.ps1` (500-line limit)
- **Commit prep**: stage files, then run `npm run agent:preflight:fix` (includes changed spell-checkable file checks) before any commit attempt
- **Pre-push validation**: run `npm run validate:prepush` before push; it is a roughly one-second
  last-resort Git/config safety check. Run relevant changed-file checks through
  `npm run agent:preflight`; use `npm run validate:local` only when a complete repository-wide
  aggregate is warranted. When hook or agent-preflight behavior changes, also run
  `npm run validate:tests:hook-regressions`. CI always runs the combined `validate:tests`
  aggregate. Treat git hooks as last-resort only. Follow
  [ship-changes Step 9](./skills/ship-changes.md#step-9-push-to-remote) for the push step.

See [formatting](./skills/formatting.md) and [validate-before-commit](./skills/validate-before-commit.md) for details.

## Additional Technical Rules

Read [technical rules](./references/context-technical-rules.md) when changing runtime, editor, or test code.

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

## Agent Workflow and CI

Read [build commands](./references/context-build-commands.md) before running local checks.
Read [agent operations](./references/context-agent-operations.md) before GitHub, git, or review work.
Read [CI and test guidance](./references/context-ci-and-testing.md) before validation or pushing.
