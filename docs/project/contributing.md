# Contributing

Thanks for helping make Unity Helpers better! This project uses a few automated checks and formatters to keep the codebase consistent and easy to review.

## Dev Container Setup (Recommended)

The easiest way to contribute is using the included dev container, which has all CI/CD tools pre-installed:

1. Open in VS Code with the Dev Containers extension
2. Click "Reopen in Container" when prompted
3. Run `npm run verify:tools` to confirm all tools are available

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

- Install tools once:
  - `npm ci` (or `npm i --no-audit --no-fund`)
  - `dotnet tool restore`
  - `npm run hooks:install`: installs git hooks. The install script also configures `push.autoSetupRemote=true` and `push.default=simple` locally, so `git push` on a new branch sets tracking automatically.
- Verify all tools: `npm run verify:tools`
- Format C#: `dotnet tool run csharpier format`
- Check docs/JSON/YAML: `npm run validate:content`
- Run the complete contract suite: `npm run validate:tests`. Hook regressions share the bounded
  worker pool with the fast checks; any check that mutates the repository runs exclusively first.
  `npm run validate:tests:fast` keeps its smaller scope, and
  `npm run validate:tests:hook-regressions` remains available for hook changes.
  To select a hook check in the full runner, use
  `node scripts/run-contract-tests.js --include-hook-regressions --only agent-preflight`.
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

Maintainers prepare releases from the default branch with the **Release Prepare** workflow. Choose a `major`, `minor`, or `patch` bump, review the generated release PR, and squash-merge it with the default `release: X.Y.Z` title. After merge, the release automation tags that commit, validates and packs the npm package, creates the `.unitypackage` without opening Unity, publishes npm, and publishes the GitHub Release assets.
