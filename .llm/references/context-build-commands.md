# Context Build Commands

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
npm run typecheck:controls                              # Prove the five type-check gates can fail
```

For a warm edit loop after the first successful restore, run
`dotnet build Generator~/WallstopStudios.UnityHelpers.TypeCheck/WallstopStudios.UnityHelpers.TypeCheck.csproj --no-restore --nologo -v quiet`.
This checks the default runtime branch; run `npm run typecheck:unity` before push for all variants.
Re-run restore when project files, imported props, lock files, SDK versions, or package references
change. On 2026-09-23, the warm default check took 1.97 seconds without restore versus 2.90 seconds
through the npm command on this host.

See [unity-devcontainer-testing](../skills/unity-devcontainer-testing.md) for full details.

---
