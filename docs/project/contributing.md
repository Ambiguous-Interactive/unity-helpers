# Contributing

Thanks for helping make Unity Helpers better! This project uses a few automated checks and formatters to keep the codebase consistent and easy to review.

## Dev Container Setup (Recommended)

The easiest way to contribute is using the included dev container, which has all CI/CD tools pre-installed:

1. Open in VS Code with the Dev Containers extension
2. Click "Reopen in Container" when prompted
3. Run `npm run verify:tools` to confirm all tools are available

### Pre-installed CI/CD Tools (Container Only)

The dev container includes these additional tools that are **not required** on your host machine. Git hooks gracefully skip them if not present—CI will catch any issues:

- **actionlint** — GitHub Actions workflow linter
- **shellcheck** — Shell script linter
- **yamllint** — YAML linter
- **lychee** — Fast link checker

### Required Tools (All Environments)

These tools are required and installed via npm/dotnet:

- **markdownlint** — Markdown linter (via npm)
- **prettier** — Markdown/JSON/YAML formatter (via npm)
- **cspell** — Spell checker (via npm)
- **CSharpier** — C# formatter (via .NET tools)

## Formatting and Linting

- C# formatting: CSharpier (via dotnet tools)
- Markdown/JSON/YAML formatting: Prettier
- Markdown linting: markdownlint
- Link checks: lychee and custom script
- YAML linting: yamllint
- Workflow linting: actionlint

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
  - `npm run hooks:install` — installs git hooks. The install script also configures `push.autoSetupRemote=true` and `push.default=simple` locally, so `git push` on a new branch sets tracking automatically.
- Verify all tools: `npm run verify:tools`
- Format C#: `dotnet tool run csharpier format`
- Check docs/JSON/YAML: `npm run validate:content`
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

Maintainers prepare releases from the default branch with the **Release Prepare** workflow. Choose a `major`, `minor`, or `patch` bump, review the generated release PR, and squash-merge it with the default `release: X.Y.Z` title. After merge, the release automation tags that commit, validates and packs the npm package, exports the `.unitypackage`, publishes npm, and publishes the GitHub Release assets.
