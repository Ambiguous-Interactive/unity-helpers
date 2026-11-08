# Contributing

Thanks for helping make Unity Helpers better! This project uses a few automated checks and formatters to keep the codebase consistent and easy to review.

This guide is for changing Unity Helpers itself. To add the package to your game, use
[Getting Started](../overview/getting-started.md). The tools and runner access below are for
repository development.

## Development and maintainer guides

Choose the setup you need:

- [Unity container licensing](../guides/unity-devcontainer-licensing.md) for local container tests.
- [MCP local setup](../guides/mcp-local-setup.md) for repository coding-tool connections.
- [AI model backends](../guides/ai-model-backends.md) for optional development launchers.
- [MCP ecosystem notes](../guides/mcp-ecosystem.md) for the shared tool catalog's design.

## Dev Container Setup (Recommended)

The easiest way to contribute is using the included dev container, which has all CI/CD tools pre-installed:

1. Open in VS Code with the Dev Containers extension
2. Click "Reopen in Container" when prompted
3. Run `npm run verify:tools` to confirm all tools are available

Run `bash scripts/validate-devcontainer-urls.sh` to check the Dockerfile's download URLs.
If HEAD probes fail, the validator confirms reachability with a GET limited to one byte.
This fallback requires [curl 8.4.0 or newer](https://curl.se/docs/manpage.html#--max-filesize);
older or unrecognized versions retain the failed HEAD result.
A successful 200 or 206 response also permits curl's size-limit stop when a server ignores the range.
Missing resources, HTTP errors, and other transfer failures still fail validation.
Run `npm run test:validate-devcontainer-urls` for the offline HTTP and parser controls.

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

The WProto compatibility inventory checks referenced enum values independently of source file
order. Its regression compiles both declaration orders and requires the same inventory, including
the enum underlying type and constants. Preserve the frozen inventory when fixing its collector.

### Test runtime reports

Use the typed NUnit timing reporter to rank slow fixtures and cases:

```bash
uv run --project scripts/python~ --locked --no-dev python scripts/unity/report_slow_tests.py --results-path results.xml --top 30 --fixture-budget-seconds 120 --step-summary
```

Add `--fail-over-budget` to enforce the fixture budget; otherwise overruns produce warnings.
The budget covers every fixture, including those outside `--top`. Names use ordinal tie ordering.
Malformed, negative or nonfinite durations and results without fixtures or cases fail the command;
missing durations on skipped tests count as zero. A step summary appends UTF-8 Markdown when
`GITHUB_STEP_SUMMARY` is set. The existing PowerShell command retains its parameter names.

### Typed Python scripts

Documentation counts use `scripts/doc_counts.py` through locked uv. `npm run lint:doc-counts`
checks without writing; `npm run sync:doc-counts` updates only the documented target files.
Counts ignore C# comments and literal contents. Linked target files and parent directories are rejected.
For metadata JSON, run `uv run --project scripts/python~ --locked --no-dev python scripts/doc_counts.py generate`.
The three PowerShell commands remain compatibility entry points. Run `npm run test:lint-doc-counts`
for count, encoding, newline, read-only, and wrapper exit-code controls.

`npm run eol:check` runs the typed Python line-ending checker. It checks worktree line endings
and UTF-8 BOMs, plus committed text blobs whose CR bytes would make fresh clones appear dirty.
Use `-- --paths <path> <path>` for literal file scopes. Missing selected text files, linked inputs,
and failed Git reads fail explicitly. A full scan requires nonempty tracked text input.
`npm run test:check-eol` runs the Git-blob, worktree, path-scope and compatibility-launcher controls.
The two PowerShell entry points remain available for existing hooks and callers.

`npm run validate:lint-error-codes` harvests error-code prefixes from lint scripts, their controls,
and Git hooks, including typed Python names. It sends one batch through the repository's actual
cspell configuration and prints source locations plus a root-word patch for rejected families.
`npm run test:validate-lint-error-codes` also runs the retained PowerShell runspace controls, which
continue to protect the fixture host while other PowerShell suites still use it.

`npm run lint:code-samples` runs the typed Python documentation sample extractor. Its report
keeps each C# fence's actual opening line across LF, CRLF, and CR input.
The CommonMark parser handles matching backtick/tilde markers, blockquotes and nested lists.
Source maps retain the opening line; extracted code omits the Markdown container prefixes.
The sample commands select the documentation dependency group; metadata-only tools keep an empty base environment.
The default scan uses Git's tracked and nonignored file inventory, so local evidence and dependency directories do not
extend the walk. `CODE_SAMPLES_ROOT` or `--scan-root` selects an explicit fixture tree instead.
The Node launcher and PowerShell test launcher remain compatible entry points.

`npm run test:extract-code-samples` covers classification, source locations, generated files,
name collisions, empty inputs, path ownership, and both launchers. The extractor retains the
existing JSON report fields. Corrected source lines can change generated filenames.
Source paths on another Windows volume remain absolute in reports and generated identities.
Complete struct and interface examples remain at type scope when compilation units are generated.
Generation is a heuristic convenience; the standalone documentation compiler checks remain the
separate compilation gate.

Release payload staging, archive creation, and Unity metadata generation run through uv. Install uv 0.13.7, then use:

```bash
python -m pip install --disable-pip-version-check uv==0.13.7
uv sync --project scripts/python~ --locked
npm run lint:python
npm run test:create-unitypackage
npm run test:asmdef
npm run test:protobuf-candidate
npm run test:native-debugger-download
npm run test:meta-generation
npm run test:fixture-assets
npm run test:frozen-files
npm run meta:generate -- Runtime/Example.cs
uv run --project scripts/python~ --locked --no-dev python scripts/unity/create_unitypackage.py --output .artifacts/release/local.unitypackage
uv run --project scripts/python~ --locked --no-dev python scripts/unity/stage_unitypackage.py --project-dir .artifacts/unity/local-export
```

`lint:repo`, `validate:local`, `validate:tests:fast`, `validate:tests:hook-regressions`, and
`test:agent-preflight` require uv, including the metadata parity check. Outside the devcontainer,
run the bootstrap and locked sync above before these developer entry points.

Image fixture generators use the locked `fixtures` dependency group. They keep existing `.meta`
files byte for byte, including asset GUIDs and sprite IDs. Missing metadata is created once;
competing creators preserve the first file. Pixel output remains the same on repeated runs.
New metadata is published with a hard link after its temporary file closes. Unsupported filesystems
report an error and leave the destination absent; failed writes do not leave a partial metadata file.
Use `--output-dir` to generate an isolated fixture set:

```bash
uv run --project scripts/python~ --locked --no-dev --group fixtures python scripts/generate-test-sprites.py --output-dir .artifacts/sprites
uv run --project scripts/python~ --locked --no-dev --group fixtures python scripts/generate-test-textures.py --extended --output-dir .artifacts/textures
```

Repository lint and contract execution share `scripts/check_runner.py`. Existing Node entry points
retain their importable interfaces and delegate process execution, concurrency, selection, and
reporting to the locked Python environment. Exclusive checks finish before concurrent checks start;
failed checks do not prevent the remaining checks from running. Windows uses the Bash executable
from its Git for Windows installation. `npm run test:check-runner`
exercises these contracts, including an interrupted child that exits zero.

A check can declare a `completionPattern` when exit status alone is insufficient. The native
acceptance self-test requires its final completion line after cleanup as well as exit zero.
Missing completion evidence fails the aggregate and preserves the captured output; it cannot count
as a passing suite. This requirement does not imply that other checks have completion receipts.

The WProto player and host source censuses use `scripts/unity/frozen_files.py` through locked uv. The PowerShell
library entry points retain their names and result properties. Census walks include untracked and
hidden inputs, prune only generated compiler output, and bind the Python project and lock alongside
the source files. The host census also binds its Python implementation and requires the actual Git
repository root. Spaces in Git root directory names are preserved.
Each variant retains its own selection rules: the host scans every physical file
in its four source trees, while the player filters compiler sources and also scans Unity scripts.
The scans reject redirects and missing roots. They compare file identity, size and timestamps
before opening, after reading and through the final path, refusing observed changes. Windows
hashing permits concurrent readers while excluding writers and deletion until its handle closes.
On other platforms, metadata checks cannot detect a rewrite that preserves every observed value.
Keep source inputs unchanged throughout a capture on every platform.
Every request reads the current bytes. A PowerShell process reuses one locked Python worker for
source scans. It checks the implementation, project and lock hashes before and after each request;
changes restart the worker or refuse the capture. Protocol, request and process identities must
match. The worker exits when its owner's input closes, or callers can explicitly call
`Stop-WProtoSourceCensusWorker`. Small player inventories retain
their existing implementation to avoid process startup overhead. Run `npm run test:frozen-files`
for the focused controls.

The devcontainer runs the locked sync during creation. Python 3.11.8 or newer
is required; uv can provision a compatible interpreter. The environment lives under
`scripts/python~/.venv`, separate from the documentation environment. Unity ignores that tooling
folder. The release CLI still accepts `--output`; staging still accepts `--project-dir` and
`UNITY_VERSION`, with the same safe scratch-directory restrictions. Invalid framework and module policies fail
before an existing export project is removed. Archive checksums support UTF-8 output filenames.

All repository Python sources now belong to the strict scope, including the standard-library-only
agent-state tool that runs before uv is available during container startup. The source registry
also covers Python stubs. The bounded-random proof uses the locked `proof` group and a narrow
stub for the public Z3 API it uses. Z3 comparisons return symbolic formulas; their stub return
types state that behavior explicitly. Production arithmetic is parsed with a closed Python AST
grammar rather than evaluated as Python code.

The production test-hook naming gate also runs in Python. It checks identifiers and explicit
markers in production source, plus literal and concatenated C# emitted by generators. It is a
naming check, not a semantic C# parser: references inside interpolation expressions remain outside
its scope. Existing Node commands and imports remain compatibility entry points; import the Python
helpers for content tests that make many calls.

The MIT-header and conditional-call checks also run in typed Python. Use
`npm run lint:license-headers` and `npm run lint:conditional-call-chains`; the PowerShell entry points
forward existing calls. Both checks require their source roots and a nonempty corpus, avoid linked
or generated directories, and fail on unreadable input. Body checks require UTF-8; header checks also retain BOM-marked UTF-16 and UTF-32 support. Header reads stop after 20 lines and use at most eight concurrent readers; findings keep a fixed order.
The conditional check masks comments and string literals, includes same-line bodies and generic
calls, and rejects methods whose bodies cannot be inspected. It remains a source check by method
name, with explicit external receivers; it does not replace C# semantic analysis.
The Odin Undo check shares that lexer and keeps its PowerShell path selectors. Missing inputs fail;
an unrelated nearby null check cannot excuse an unsafe array cast. Suppressions must be comments,
not string literals. Its remaining nearby-filter recognition is a source heuristic, not proof that
every Undo target is valid. `npm run lint:odin-undo-safety` checks drawers and
`npm run test:odin-undo-safety` runs its focused tests.
Run `npm run test:csharp-lints` for discovery, parser and direct/PowerShell CLI controls.

The concurrent-cache checker also runs in typed Python through `npm run lint:concurrent-cache-fill`.
It requires Runtime, Editor and Styles source roots, ignores comments and literal text, and preserves
explicit overwrite reasons. Only a plain `SINGLE_THREADED` guard exempts writes; compound conditions
remain checked because another symbol may enable that branch in a concurrent build. Use
`npm run test:lint-concurrent-cache-fill` for the original fixtures, parser boundaries and both CLI routes.
The PowerShell entry points remain compatibility launchers.

The lockfile pins Ruff, mypy, Pyright, pytest, and their dependencies. `lint:python` checks Ruff
formatting and correctness rules plus both type checkers in strict mode. Function signatures must
be fully annotated and explicit `Any` is forbidden. CI runs these checks on Linux and Windows,
along with the package tests.
The existing `scripts/generate-meta.sh` entry point delegates to Python and retains `--force`,
CRLF metadata, and importer defaults. Use `npm run meta:generate -- <path>` or invoke
`scripts/generate_meta.py` through uv for a shell-independent entry point. Existing metadata is
refused unless `--force` is explicit; a competing creation also preserves the first file. Runtime
commands use `--no-dev`, so they do not install analysis or test packages into a fresh environment.
They retain tools already installed in a shared warm environment; compatible Python may still need
to be provisioned on the first run.

The documentation identifier, runtime attribute-preservation, and spelling-bucket tools also use
the locked Python environment. Their npm commands invoke Python directly; the old Node and
PowerShell paths remain compatibility launchers. The preservation gate includes generic reflection
calls such as `GetCustomAttribute<PoolPurgePolicyAttribute>()`. Spelling fixes retain the first
spelling of each word and the file's line endings; word insertion refuses cross-bucket duplicates
before writing. Fixes leave path-only dictionaries and absent word lists unchanged.
Use `--config <path>` when checking or updating a separate spelling configuration.
Set `PRESERVE_ATTRIBUTES_ROOT` to check a separate runtime source tree. Diagnostics use absolute
paths when that tree is on a different Windows drive from the repository.
These command-line tools write UTF-8 diagnostics even when Windows pipes use a legacy encoding.

```bash
npm run lint:doc-identifiers
npm run lint:preserve-attributes
npm run lint:spelling:config
npm run lint:spelling:add -- tech-terms ExampleWord
uv run --project scripts/python~ --locked pytest scripts/tests/test_doc_identifiers.py scripts/tests/test_preserve_attributes.py scripts/tests/test_cspell_tools.py
```

`npm run pr:feedback -- <number>` uses the locked Python environment to read all four
PR feedback surfaces and audit author identities. The shell entry point remains available;
`uv run --project scripts/python~ --locked --no-dev python scripts/pr_feedback.py <number>`
also works directly. Run its offline controls with `--self-test`, or its full regression
suite with `uv run --project scripts/python~ --locked pytest scripts/tests/test_pr_feedback.py`.
CodeRabbit is trusted only when its exact login belongs to a Bot account. Unknown bots
and outside people still require issue-specific direction before action.
HTTP redirects fail closed to keep credentials on the GitHub API origin.

`npm run verify:shipped-analyzers` compares all three committed compiler-input DLLs
with isolated Release builds through the same locked environment. Add `:fix` to refresh
stale or missing payloads. A failed copy or replacement preserves the previous DLL and
its importer metadata. The Node entry points remain compatibility launchers.

`npm run lint:repo -- --only meta-files` checks tracked asset coverage and C# importer shapes through typed Python.
The PowerShell and shell test commands remain compatibility launchers. C# companions require
one valid `fileFormatVersion: 2`, one 32-digit hexadecimal GUID, and a nonempty `MonoImporter`
mapping with valid YAML syntax and distinct setting keys. Changed-path preflight checks metadata-only edits and validates
both the working copy and any indexed companion. Validation reads at most 1 MiB
per C# companion and never rewrites existing bytes or index entries. Keep customized references,
execution order, and other importer settings; repair a stub by retaining its headers and adding
the generated importer block. Samples retain their companions when imported into Assets;
Unity-ignored tooling folders remain excluded.
Metadata checks select the `metadata` dependency group; generation uses only Python's standard library.

The three standalone Markdown checkers under `.github/scripts` also join strict validation.
Invoke them through the locked environment, for example:

```bash
uv run --project scripts/python~ --locked --no-dev python .github/scripts/validate_markdown_links.py docs
uv run --project scripts/python~ --locked pytest scripts/tests/test_meta_lint.py scripts/tests/test_markdown_checks.py
```

They retain their link policies and diagnostics. Missing inputs, empty source scans, and
unreadable Markdown now fail rather than reporting an unchecked pass. These standalone checks
remain separate from the repository's normal documentation lint commands.

Wiki generation and its test suite use the same locked Python project. The deployment and
validation workflows provision uv before running these commands. Link text, code blocks, page
names, sidebars, and footer output retain their existing contracts.

```bash
uv run --project scripts/python~ --locked pytest scripts/wiki/test_wiki_scripts.py -v
uv run --project scripts/python~ --locked --no-dev python scripts/wiki/prepare_wiki.py --source . --dest /tmp/test-wiki --verbose
npm run test:wiki-generation
```

New Python files must join the strict `tool.pyright.include` list in
`scripts/python~/pyproject.toml`; a Git-backed inventory rejects unregistered files. The explicit
legacy list records existing Python scripts still awaiting migration. Remove an entry when its
script joins strict validation; do not add new scripts to that backlog.

Assembly-definition and bundled-BCL validation, the protobuf candidate builder and verifier, and
the native-debugger downloader also use this locked environment. The PowerShell assembly-check
entry points remain compatibility launchers; npm calls Python directly. The assembly check rejects
missing source roots, empty assembly scans, invalid references and version expressions, and
package defines used without a declaration in the owning assembly. File fixtures cover nested
assemblies, optional package guards, case-insensitive cache exclusions, and hidden files. Unreadable
C# files report their relative paths without hiding later ownership errors. JSON input is validated
before using its typed values. CI runs these assembly fixtures on Linux and Windows; the Windows
job exercises real hidden attributes on both files and directories.

`npm run test:protobuf-candidate` exercises real child-process exit, timeout, descendant cleanup,
source inventory and shipped-byte checks. `npm run test:native-debugger-download` serves a local
nested archive to check bounded range requests, hashes, sizes and destination paths. These tests
do not download or execute a debugger, rebuild shipped binaries, or qualify Unity player behavior.
Native fault capture requires `uv` on the Windows host for its locked downloader command; it keeps
the existing process watchdog and retained download receipt.

The package tests check archive headers, GUIDs, checksums, repeated output, failure preservation,
CLI errors, policy validation, and the real npm payload. They also inject type, lint, and formatting
defects to prove each gate rejects them. Port remaining scripts incrementally while keeping their
CLI contracts and executable controls.

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

The container also installs the coding agent CLIs at startup. With npm 12, the installer
allows the OpenCode and Claude install scripts by package name, then checks that each
launcher responds to `--version`. It repairs a broken launcher even when its package is
already current. Registry failures remain nonfatal and are reported in the startup log.

### Required Tools (All Environments)

These tools are required and installed via npm/dotnet:

- **markdownlint**: Markdown linter (via npm)
- **prettier**: Markdown/JSON/YAML formatter (via npm)
- **cspell**: Spell checker (via npm)
- **CSharpier**: C# formatter (via .NET tools)
- **uv**: Locked Python script environment and analysis tools

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

Stack-allocation and memory-access checks use the existing Roslyn policy tool and require .NET SDK 9.0.320.
CI installs that SDK into an isolated runner directory and verifies the selected version.
A newer SDK preinstalled on the runner cannot override the declared compiler.

The compiler resolves constants and the actual length symbol;
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

Use Python 3.11 for the repository's Python checks. The dev container pins `uv==0.13.7` and
exposes it as `uv`. Documentation CI installs that same version with pip, then installs the
fully pinned `requirements-docs.lock` with uv. `requirements-docs.txt` lists the direct
constraints; the lock records their transitive versions. Run locally with:

```bash
uv venv .venv --python 3.11
uv pip install --python .venv/bin/python -r requirements-docs.lock
.venv/bin/mkdocs build --strict
```

| Python path                                   | Dependency source                                     | Installer and cache                                                 | Invocation                                                                                                                  |
| --------------------------------------------- | ----------------------------------------------------- | ------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| Documentation validation and Pages deployment | `requirements-docs.lock` from `requirements-docs.txt` | Pinned uv; `setup-python` caches the pip bootstrap wheel            | `mkdocs build --strict`                                                                                                     |
| Wiki validation and generation                | `scripts/python~/uv.lock`                             | Pinned uv; the shared project installs test tools only for tests    | `uv run --project scripts/python~ --locked pytest scripts/wiki/test_wiki_scripts.py -v`; generation uses `--no-dev`         |
| Local Gates bounded-random proof              | `scripts/python~/uv.lock`                             | locked uv environment                                               | `uv run --project scripts/python~ --locked --no-dev --group proof python scripts/random-quality/verify-bounded-sampling.py` |
| Dev container image                           | `.devcontainer/requirements-tools.txt`                | Pinned uv installs yamllint and pinned MCP tools; pip bootstraps uv | Tools run from the image, not during hooks                                                                                  |
| Optional local hooks and Unity/result helpers | No Python packages installed by hooks                 | Existing interpreter or optional yamllint tool                      | Hooks call `yamllint` when available; helper scripts call `python3`                                                         |

Release, metadata, binary-check, feedback, wiki, changelog, and XML-summary commands use the shared locked uv project.
The asset fixture generators use its `fixtures` group, and the bounded-random proof uses its `proof` group.
Every Python source is in strict scope; PowerShell, Bash, JavaScript, and inline workflow implementations
still need migration. The old
`requirements-wiki.in` and `requirements-wiki.txt` remain compatibility inputs for existing
standalone environments; current wiki CI uses the shared uv lock.

Earlier installer comparisons measured the bootstrap costs. On Python 3.11, fresh environments with separate cold and
warm caches gave documentation installs of 8.66/5.90 seconds with pip and 5.42/0.15 seconds
with uv. Pytest took 1.26/0.85 seconds with pip and 0.35/0.04 seconds with uv; Z3 took
2.94/0.76 seconds with pip and 2.20/0.04 seconds with uv. Bootstrapping pinned uv itself
with pip took another 3.57/0.99 seconds. Wiki CI and the bounded-random proof now use the shared uv project; these
historical install measurements do not describe its current end-to-end runtime.

To update Python dependencies, edit the direct constraints, then regenerate the locks with
the pinned uv version:

```bash
uv lock --project scripts/python~
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
stale-source controls. Install its matching Chromium and view the editor, browser, and artifact
path options with:

```bash
uv run --project scripts/python~ --locked --group browser playwright install chromium
uv run --project scripts/python~ --locked --group browser python scripts/unity/browser_consumer.py --help
```

Use `--browser-executable` to select an existing browser instead. This manual check does not replace
the supported Unity CI matrix or provide performance measurements. The browser runner and its tests
use the shared locked Python toolchain; the former hyphenated entry point remains a compatibility launcher.

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
  outputs. Read-only C# metadata validation also follows selected staged rename destinations
  to their former companion paths; formatter targets and write scope remain unchanged. Fixes must resolve inside the repository; linked targets keep partial-staging
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
baseline.

Maintainers prepare releases from the default branch with the **Release Prepare** workflow. Choose a `major`, `minor`, or `patch` bump, review the generated release PR, and squash-merge it with the default `release: X.Y.Z` title. **Auto publish** is enabled by default. After the merged commit passes Unity Tests, Local Gates, Repo Lint, and every other triggered push validation workflow, automation starts **Release Publish** with that exact commit. It validates and packs npm, creates the `.unitypackage` without opening Unity, creates the release tag when needed, and publishes npm and GitHub Release assets.

Clear **Auto publish** during preparation to publish manually. In **Release Publish**, choose a branch, tag, or commit with **source_ref**; leave **version** blank to read it from that source's `package.json`. **Export only** validates and produces downloadable archives without changing tags or publishing. Automatic publication never moves an existing tag. A failed validation or prior publication attempt requires inspection and manual recovery; duplicate completion events do not dispatch the same release twice. Remove `<!-- unity-helpers:auto-publish -->` from an unmerged release PR to cancel its automatic publication.

Before dispatch, automation atomically creates the internal `release-publish-claims/X.Y.Z` tag at the validated commit. This retained claim prevents duplicate dispatch even before GitHub lists the publication run. A lost response or interrupted publisher leaves the claim in place: inspect the publication history, then use **Release Publish** manually with the claimed source commit. Every main validation workflow uses a separate concurrency group for each pushed commit; newer pull-request revisions still cancel older pull-request runs. Documentation deployment, issue-template synchronization and devcontainer publication have separate recovery paths and do not validate release archives, so their pending or superseded runs do not block publication.

## Rebuild bundled serialization dependencies

These are maintainer steps for changing the shipped assemblies, not package-installation steps.
The [assembly conflict guide](../guides/bundled-assembly-conflicts.md) explains consumer diagnosis.

```bash
uv run --project scripts/python~ --locked --no-dev python scripts/build-protobuf-candidate.py --output /tmp/protobuf-candidate
uv run --project scripts/python~ --locked --no-dev python scripts/verify-protobuf-shipped.py --candidate-root /tmp/protobuf-candidate/assemblies
```

The retained candidate builder requires SDK 9.0.319 and produces isolated binaries with a source-bound build receipt. Repository validation uses SDK 9.0.320; install the older SDK separately when rebuilding that retained candidate.
The independent current lane uses `ProtobufNetOracle=candidate` and
`ProtobufNetCandidateRoot=/tmp/protobuf-candidate/assemblies`. Frozen v2/v3 lanes retain their
original DLLs under `Generator~`; dependency upgrades must not refresh those historical oracles.
The retained Dox consumer suite exercises the frozen v3 and current builds.

Keep the Unity-version constraints, importer GUIDs and assembly ownership policy when rebuilding.
`npm run lint:bundled-assemblies` checks the `Runtime/Binaries/` constraints and absence rules.
It does not inventory the separate protobuf dependency directory. Preserve the documented source
and binary hashes when updating provenance.

A .NET Standard target and passing host tests do not establish Unity assembly ownership or backend
health. The complete supported-version, stripped IL2CPP, WebGL and native process qualification
remains tracked in [the dependency qualification issue](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1052).
