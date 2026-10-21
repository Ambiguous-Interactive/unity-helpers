# Contributing

Thanks for helping make Unity Helpers better! This project uses a few automated checks and formatters to keep the codebase consistent and easy to review.

## Dev Container Setup (Recommended)

The easiest way to contribute is using the included dev container, which has all CI/CD tools pre-installed:

1. Open in VS Code with the Dev Containers extension
2. Click "Reopen in Container" when prompted
3. Run `npm run verify:tools` to confirm all tools are available

### Fast C# Feedback

Use fixture-scoped builds while editing runtime or Editor tests:

```bash
npm run typecheck:iterate -- tests --server --files Tests/Runtime/Serialization/WProtoEnumFormatterTests.cs
npm run typecheck:iterate -- editor-tests --server --files Tests/Editor/Tools/OdinMigration/OdinMigrationLiveDictionaryTests.cs
```

Add dependent fixtures to the same `--files` list. Production code, shared test support, test types,
and test doubles remain included. Each scope accepts files only from its corresponding test tree.
These commands retain source generators and compiler errors but omit performance analyzers.
Run the normal applicable typecheck commands and changed-file preflight before publishing.

The analyzer placement check evaluates MSBuild compiler inputs for each owned .NET project with
both protobuf oracle settings. Each shipped analyzer must appear exactly once at its package
path. Normal assembly references, including the compiler compatibility test reference, do not
count as compiler inputs. Run it with `node scripts/tests/test-analyzer-placement.js`.

### GitHub Credentials in the Container

The container resolves github.com credentials from a cached token. The Dev Containers credential
helper is out of the path entirely: that helper raises a dialog on the host desktop on **every**
invocation, and `git push`, `git fetch` and every API call invoke it.

With a token cached, nothing prompts. With an **empty** cache, git used to fall back to the editor's
own askpass dialog, another window on the host desktop from a different mechanism. The container
points `GIT_ASKPASS` at `scripts/git-askpass-refuse.sh` instead, so that path now ends in an error
naming the commands below rather than in a dialog. (The editor's own Git UI is unaffected: the Git
extension sets `GIT_ASKPASS` explicitly for the processes it launches.)

Supply the token once per container, either way:

```bash
npm run github:token:bootstrap   # asks the Dev Containers helper once; answer the dialog
npm run github:token:store       # paste a personal access token on stdin; no dialog at all
```

`git push`, `git fetch` and `scripts/github-token.sh` then read the same 0600 cache. A missing
credential is reported with the command that fixes it, never prompted for.

### Pre-installed CI/CD Tools (Container Only)

The dev container includes these additional tools that are **not required** on your host machine. Git hooks gracefully skip them if not present; CI will catch any issues:

- **actionlint**: GitHub Actions workflow linter
- **shellcheck**: Shell script linter
- **yamllint**: YAML linter
- **lychee**: Fast link checker

### Required Tools (All Environments)

These tools are required and installed via npm/dotnet:

- **markdownlint**: Markdown linter (via npm)
- **prettier**: Markdown/JSON/YAML formatter (via npm)
- **cspell**: Spell checker (via npm)
- **CSharpier**: C# formatter (via .NET tools)

## Formatting and Linting

- C# formatting: CSharpier (via dotnet tools)
- Markdown/JSON/YAML formatting: Prettier
- Markdown linting: markdownlint
- Link checks: lychee and custom script
- YAML linting: yamllint
- Workflow linting: actionlint

`npm run lint:markdown` checks tracked Markdown and new files that Git does not ignore,
then applies `.markdownlintignore`. Tracked files remain eligible even when a Git ignore rule
matches them. Hidden paths stay excluded, and generated, ignored local artifacts do not slow
file discovery. The command fails if discovery fails or finds no eligible documents.

`npm run lint:unsafe-code` refuses compiler-unsafe code, unchecked memory access, disabled IL2CPP
safety checks, and unbounded stack allocations. A variable stack allocation must be the direct
branch of an assignment whose condition compares that length with a constant. For example,
`count <= Max ? stackalloc int[count] : default` and
`count > Max ? default : stackalloc int[count]` are bounded. Reversing those branches is refused.
Compound conditions and intervening expressions are refused because a nearby comparison alone
does not establish the bound on the path that allocates.

IL2CPP safety options must omit the value or use the exact literal `true`. A constant expression
such as `!true` disables checks even though it contains that word. Named constants, compound
expressions, attribute aliases and numeric option casts follow the same rule. Each scan must report both a
literal-false control and a negated-true control before it can report a clean package.

Memory-access and IL2CPP option checks parse C# syntax, including Unicode escapes, invisible identifier format characters and
interpolation expressions. Comments, literal text and format strings remain excluded. Every conditional branch
is checked, and each source site is counted once. Aliases and static imports of `Unsafe` are
refused because they hide member names. Size and reference-identity queries remain permitted.
The positive controls require plain, escaped, formatted and interpolated calls to be found.
Separate sources exercise format characters without an ordinary spelling that could mask a prefilter regression.

Stack-allocation and memory-access checks use the existing Roslyn policy tool and require .NET SDK 9.0.306.
Repo Lint installs that SDK before running the checks. The compiler resolves constants and the actual length symbol;
parameters, locals, readonly fields and properties cannot borrow an unrelated constant's name.
Constant expressions and shared constants remain supported. The scan covers conditional branches
and follows the declarations and base types used by a bound to include their conditional symbols.
Unresolved bounds fail validation. Compiler contexts are cached only within one source snapshot,
with at most eight cached contexts; concurrent commands build into separate temporary output folders.

To scan another package checkout with the current tool, pass `--root /path/to/checkout`.
Report paths are relative to that checkout regardless of argument order. The checkout must contain
the normal required source trees, runtime assembly definition and check projects.

During linter development, `npm run test:lint-unsafe-code -- --filter "semantic binding"` selects
matching controls without running the full corpus. The summary reports the unselected count, and
an empty selection fails. Run the unfiltered suite before publication.

Use `npm run lint:unsafe-code -- --sarif artifacts/unsafe-code.sarif` to retain a
[SARIF 2.1.0](https://docs.oasis-open.org/sarif/sarif/v2.1.0/os/schemas/sarif-schema-2.1.0.json)
report. Repo Lint retains the same report as the `unsafe-code-sarif` artifact. Findings use the
`unsafe-code` rule, repository-relative URI paths and deterministic fingerprints. Every finding
fails the gate; there is no exception baseline. Reports include per-tree source and assembly
counts plus positive-control results. Missing required subjects, discovery/read errors and report
write failures fail validation; an execution failure cannot be mistaken for a clean result list.

## Python Tooling

Use Python 3.11 for the repository's Python checks. The dev container pins `uv==0.12.10` and
exposes it as `uv`. Documentation CI installs that same version with pip, then installs the
fully pinned `requirements-docs.lock` with uv. `requirements-docs.txt` lists the direct
constraints; the lock records their transitive versions. Run locally with:

```bash
uv venv .venv --python 3.11
uv pip install --python .venv/bin/python -r requirements-docs.lock
.venv/bin/mkdocs build --strict
```

| Python path                                   | Dependency source                                     | Installer and cache                                                 | Invocation                                                                                                |
| --------------------------------------------- | ----------------------------------------------------- | ------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| Documentation validation and Pages deployment | `requirements-docs.lock` from `requirements-docs.txt` | Pinned uv; `setup-python` caches the pip bootstrap wheel            | `mkdocs build --strict`                                                                                   |
| Wiki link validation                          | `requirements-wiki.txt` from `requirements-wiki.in`   | pip, without a persisted cache                                      | `python -m pytest scripts/wiki/test_wiki_scripts.py -v`; wiki generation scripts use the standard library |
| Local Gates bounded-random proof              | `requirements-random-quality.txt`                     | pip cache from `setup-python`                                       | `python3 scripts/random-quality/verify-bounded-sampling.py`                                               |
| Dev container image                           | `.devcontainer/requirements-tools.txt`                | Pinned uv installs yamllint and pinned MCP tools; pip bootstraps uv | Tools run from the image, not during hooks                                                                |
| Optional local hooks and Unity/result helpers | No Python packages installed by hooks                 | Existing interpreter or optional yamllint tool                      | Hooks call `yamllint` when available; helper scripts call `python3`                                       |

The other Python entry points use the interpreter and standard library without installing
packages: `scripts/wiki/*.py` outside the pytest suite, `scripts/generate-test-*.py`,
`scripts/pr-feedback.sh`, and `scripts/unity/lib/nunit-results.sh`. The wiki deployment
workflow runs the wiki scripts directly. The local wiki guide installs from the same locked
requirements file as CI.

The pip exceptions are measured. On Python 3.11, fresh environments with separate cold and
warm caches gave documentation installs of 8.66/5.90 seconds with pip and 5.42/0.15 seconds
with uv. Pytest took 1.26/0.85 seconds with pip and 0.35/0.04 seconds with uv; Z3 took
2.94/0.76 seconds with pip and 2.20/0.04 seconds with uv. Bootstrapping pinned uv itself
with pip took another 3.57/0.99 seconds. Thus the two small CI installs stay on pip; adding
uv there would increase their cold and warm runtime. These are local install measurements,
not full GitHub runner timings.

To update Python dependencies, edit the direct constraints, then regenerate the locks with
the pinned uv version:

```bash
uv pip compile --universal --python-version 3.11 requirements-docs.txt --output-file requirements-docs.lock
uv pip compile --universal --python-version 3.11 requirements-wiki.in --output-file requirements-wiki.txt
```

Review the lock diff, install into a fresh Python 3.11 environment, run the strict MkDocs
build and wiki tests, and keep the CI `uv` pin aligned with `.devcontainer/Dockerfile`. The
single Z3 requirement and container tooling requirements are exact pins; update those and
their checks together. The `pip install` commands that bootstrap uv are deliberate because
uv cannot install itself into an otherwise empty runner or container.

## LLM Scratch Artifacts

- Files or folders starting with `_llm_` are git-ignored and automatically removed from the Unity package during imports.
- Keep temporary AI outputs outside the package root (or rename them) to avoid unexpected deletions by the asset cleaner.

### Dependabot PRs

Dependabot PRs are auto-formatted by CI. The bot pushes commits (same‑repo PRs) or opens a formatting PR (forked PRs) so they pass formatting gates.

### Opt‑In Formatting for Contributor PRs

If you want the bot to apply formatting to your PR:

- Comment on your PR with `/format` (aliases: `/autofix`, `/lint-fix`).
  - If your branch is in this repo, the bot pushes a commit with fixes.
  - If your PR is from a fork, the bot opens a formatting PR targeting the base branch.
  - The commenter must be the PR author or a maintainer/collaborator.
- Or run manually from the Actions tab: select "Opt‑in Formatting", click "Run workflow", and enter the PR number.

What gets auto‑fixed:

- C# via CSharpier
- Markdown/JSON/YAML via Prettier
- Markdown lint via markdownlint with `--fix`

What does not auto‑fix:

- Broken links (lychee)
- YAML issues that require manual edits

## Run Checks Locally

For browser correctness checks, `scripts/unity/run-browser-consumer.py` builds an isolated
WebGL consumer and runs retained-map, enum, RNG, and spatial-query cases in Chromium. It requires
an installed Unity Editor with WebGL support and Playwright. The runner uses Release IL2CPP,
High stripping, and `SINGLE_THREADED`; it checks exact golden bytes and rejects corrupt-byte and
stale-source controls. Run `uv run scripts/unity/run-browser-consumer.py --help` for the editor,
browser, and artifact path options. This manual check does not replace the supported Unity CI
matrix or provide performance measurements.

- Install tools once:
  - `npm install --no-audit --no-fund` (the generated root lockfile stays ignored)
  - `dotnet tool restore`
  - `npm run hooks:install`: installs git hooks. The install script also configures `push.autoSetupRemote=true` and `push.default=simple` locally, so `git push` on a new branch sets tracking automatically.
- Verify all tools: `npm run verify:tools`
- Format C#: `dotnet tool run csharpier format`
- Check member ordering: `npm run lint:nested-type-placement`. Tuple return types, including
  tuples inside generic return types, keep methods in their method tier. Tuple fields and
  properties keep their field or property tier; initializer calls do not turn them into methods.
- Check docs/JSON/YAML: `npm run validate:content`
- Run the complete contract suite: `npm run validate:tests`. Hook regressions share the bounded
  worker pool with the fast checks; any check that mutates the repository runs exclusively first.
  `npm run validate:tests:fast` keeps its smaller scope, and
  `npm run validate:tests:hook-regressions` remains available for hook changes.
  To select a hook check in the full runner, use
  `node scripts/run-contract-tests.js --include-hook-regressions --only agent-preflight`.
- Run changed-file checks with `npm run agent:preflight` (or `agent:preflight:fix`).
  Select paths with `-Paths first/path second/path`, or use `-PathList list.txt` for a file
  containing one path per line or separated by NUL characters. Relative list filenames resolve
  from the repository root. Missing, directory and unreadable list inputs fail before any mutation,
  including with `-Fix`. An empty readable list selects no files.
  Unsupported options such as `--files` fail before any mutation.
  Trailing paths require `-Paths`; prefix filenames starting with a hyphen with `./`, or use
  an absolute path. With no path arguments, preflight checks the changed-file set.
  A failed required Git query stops preflight with Git's status and diagnostic. Initial index
  queries run before any fixes.
  Explicit selections use literal Git path scopes for their safety checks and known generated
  outputs. Fixes must resolve inside the repository; linked targets keep partial-staging
  protection. A linked destination that resolves to the repository root is refused before tools start. Changed destinations outside the checked scope and hard links with unknown
  destinations fail before writing. Metadata recovery checks every staged source and its parent/file companions,
  including sources outside the selected validation list. With no explicit selection, discovery
  still covers the whole repository.
- Enforce EOL/encoding: `npm run eol:check`
- Lint GitHub Actions: `actionlint`
- Verify Markdown/code links: `npm run lint:doc-links` (cross-platform wrapper that locates PowerShell automatically)
  - The wrapper lives at `scripts/run-doc-link-lint.js` so you can also run `node ./scripts/run-doc-link-lint.js --verbose` if you are not using npm scripts.
  - The underlying PowerShell script validates intra-repo Markdown links _and_ any `docs/...` references inside source files or scripts. The `lint-doc-links` GitHub Actions workflow runs it on every PR, so run it locally before pushing large doc updates.

## Style and Naming

Please follow the conventions outlined in `.editorconfig` and the repository guidelines (PascalCase types, camelCase fields, explicit types, braces required, no regions).

## Releases and Versioning

This project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Key points:

- **Git tags** use the format `3.1.5` (no `v` prefix)
- **package.json** contains the authoritative version number
- **SVG banner** displays the version with a `v` prefix (e.g., `v3.1.5`) for visual consistency, synced automatically via pre-commit hook

When installing via Git URL, reference versions without the `v` prefix:

```text
https://github.com/wallstop/unity-helpers.git#3.1.5
```

Maintainers can dispatch **Unity Benchmarks** to validate the licensed benchmark lifecycle. The
weekly hosted scheduler dispatches it on protected main. Both workflows must be enabled in the
repository. Branch dispatches retain diagnostics without updating the canonical performance
baseline. See the [runner runbook](../runbooks/unity-runners-after-transfer.md#licensed-benchmarks)
for cleanup, cancellation, and reporting requirements.

Maintainers prepare releases from the default branch with the **Release Prepare** workflow. Choose a `major`, `minor`, or `patch` bump, review the generated release PR, and squash-merge it with the default `release: X.Y.Z` title. **Auto publish** is enabled by default. After the merged commit passes Unity Tests, Local Gates, Repo Lint, and every other triggered push validation workflow, automation starts **Release Publish** with that exact commit. It validates and packs npm, creates the `.unitypackage` without opening Unity, creates the release tag when needed, and publishes npm and GitHub Release assets.

Clear **Auto publish** during preparation to publish manually. In **Release Publish**, choose a branch, tag, or commit with **source_ref**; leave **version** blank to read it from that source's `package.json`. **Export only** validates and produces downloadable archives without changing tags or publishing. Automatic publication never moves an existing tag. A failed validation or prior publication attempt requires inspection and manual recovery; duplicate completion events do not dispatch the same release twice. Remove `<!-- unity-helpers:auto-publish -->` from an unmerged release PR to cancel its automatic publication.

Before dispatch, automation atomically creates the internal `release-publish-claims/X.Y.Z` tag at the validated commit. This retained claim prevents duplicate dispatch even before GitHub lists the publication run. A lost response or interrupted publisher leaves the claim in place: inspect the publication history, then use **Release Publish** manually with the claimed source commit. Every main validation workflow uses a separate concurrency group for each pushed commit; newer pull-request revisions still cancel older pull-request runs. Documentation deployment, issue-template synchronization and devcontainer publication have separate recovery paths and do not validate release archives, so their pending or superseded runs do not block publication.
