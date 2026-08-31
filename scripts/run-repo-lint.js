"use strict";

// One runner for the repository's fast lint and contract checks.
//
// Before this existed every check owned a workflow: ~26 GitHub Actions runs per push, each paying a
// runner boot and a checkout to execute a script that finishes in seconds. Measured on `main` at
// e94d04d5 that was ~810 s of billable runner time for well under two minutes of actual work, and
// `scripts/tests/test-comment-stripping.ps1` alone ran five times because five workflows each
// needed it.
//
// Two properties this keeps that a chain of `&&` does not:
//   * every check runs even after one fails, so a push reports its whole lint state in one go
//     instead of one failure per round trip;
//   * the registry is data, so scripts/tests/test-run-repo-lint.js can assert that every linter in
//     `scripts/` is actually reached by something -- the failure mode consolidation invites is a
//     linter that still exists and no longer runs.
//
// The checks run concurrently (`--jobs`, one worker per core by default). Consolidation traded the
// lint signal's wall clock for its billable cost -- measured on `main`, the twenty retired workflows
// finished in 128 s of wall clock for ~730 s of runner time, and the one job that replaced them took
// 317 s for 317 s. Concurrency buys the wall clock back without buying the runner boots back: the
// checks are independent processes that only read the tree, so the constraint is cores, not order.
//
// Commands are the ones the retired workflows ran, verbatim, so consolidation moved the work
// without changing it. Where an npm script was already the exact entry point, that script is used
// so the definition still lives in exactly one place.
//
// The concurrent driver itself lives in scripts/check-runner.js, because the contract-test suite
// (scripts/run-contract-tests.js) is the same shape and spent a year as a serial `&&` chain.

const { runChecks, runRegistry } = require("./check-runner");

/** A check whose `run` is executed with `bash -c` from the repository root. */
const CHECKS = [
  // Shared helper: five workflows used to run this, once each.
  {
    id: "comment-stripping",
    name: "Comment-stripping helper",
    run: "pwsh -NoProfile -File scripts/tests/test-comment-stripping.ps1 -VerboseOutput"
  },

  // Workflow and action hygiene.
  {
    id: "workflow-action-pins",
    name: "Immutable action pins",
    run: "pwsh -NoProfile -File scripts/tests/test-workflow-action-pins.ps1"
  },
  {
    id: "pwsh-invocations",
    name: "PowerShell invocations",
    run: "npm run lint:pwsh-invocations"
  },
  {
    id: "workflow-run-expression-length",
    name: "Workflow run-expression length",
    run: "npm run lint:workflow-run-expression-length"
  },

  // Assemblies and C# source rules.
  {
    id: "asmdef",
    name: "Assembly definitions",
    run: "pwsh -NoProfile -File scripts/lint-asmdef.ps1 -VerboseOutput"
  },
  {
    id: "conditional-call-chains",
    name: "[Conditional] call chains",
    run: "npm run lint:conditional-call-chains"
  },
  {
    id: "concurrent-cache-fill-test",
    name: "Concurrent cache fill linter self-test",
    run: "npm run test:lint-concurrent-cache-fill"
  },
  {
    id: "concurrent-cache-fill",
    name: "Concurrent cache fills",
    run: "npm run lint:concurrent-cache-fill"
  },
  {
    id: "bundled-assemblies",
    name: "Bundled assembly importers",
    run: "npm run lint:bundled-assemblies"
  },
  {
    id: "typecheck-asmdef-references",
    name: "Typecheck projects reference only what their asmdefs declare",
    run: "npm run lint:typecheck-asmdef-references"
  },
  {
    id: "csharp-naming-test",
    name: "C# naming linter self-test",
    run: "pwsh -NoProfile -File scripts/tests/test-lint-csharp-naming.ps1 -VerboseOutput"
  },
  {
    id: "csharp-naming",
    name: "C# method names",
    run: "npm run lint:csharp-naming"
  },
  {
    id: "unsafe-code-test",
    name: "Unsafe-code gate self-test",
    run: "npm run test:lint-unsafe-code"
  },
  {
    id: "unsafe-code",
    name: "No compiler-unsafe code",
    run: "npm run lint:unsafe-code"
  },
  {
    id: "no-regions",
    name: "No #region directives",
    run: "pwsh -NoProfile -File scripts/lint-no-regions.ps1 -VerboseOutput"
  },
  {
    id: "license-headers",
    name: "MIT license headers",
    run: "pwsh -NoProfile -File scripts/lint-license-headers.ps1 -VerboseOutput"
  },
  {
    id: "drawer-multiobject-test",
    name: "Drawer multiobject linter self-test",
    run: "pwsh -NoProfile -File scripts/tests/test-lint-drawer-multiobject.ps1 -VerboseOutput"
  },
  {
    id: "drawer-multiobject",
    name: "Drawer multiobject",
    run: "pwsh -NoProfile -File scripts/lint-drawer-multiobject.ps1 -VerboseOutput"
  },
  {
    id: "odin-undo-safety-test",
    name: "Odin undo-safety linter self-test",
    run: "pwsh -NoProfile -File scripts/tests/test-lint-odin-undo-safety.ps1 -VerboseOutput"
  },
  {
    id: "odin-undo-safety",
    name: "Odin undo safety",
    run: "pwsh -NoProfile -File scripts/lint-odin-undo-safety.ps1 -VerboseOutput"
  },
  {
    id: "unity-file-naming-test",
    name: "Unity file-naming linter self-test",
    run: "pwsh -NoProfile -File scripts/tests/test-lint-unity-file-naming.ps1 -VerboseOutput"
  },
  {
    id: "unity-file-naming",
    name: "Unity Object file naming",
    run: "npm run lint:unity-file-naming"
  },
  {
    id: "preserve-attributes",
    name: "IL2CPP-preserved runtime attributes",
    run: "node scripts/lint-preserve-attributes.js"
  },
  {
    id: "comparison-direction",
    name: "Left-to-right comparisons",
    run: "npm run lint:comparison-direction"
  },
  {
    id: "nested-type-placement",
    name: "Nested types at the end of their containing type",
    run: "npm run lint:nested-type-placement"
  },
  {
    id: "xml-doc-summaries",
    name: "One <summary> per doc comment block",
    run: "npm run lint:xml-doc-summaries"
  },
  {
    id: "shipped-analyzers",
    name: "Shipped analyzer assemblies match their sources",
    run: "npm run verify:shipped-analyzers"
  },
  {
    id: "meta-exclusions",
    name: "Meta lint exclusions",
    run: "bash scripts/tests/test-lint-meta-exclusions.sh"
  },
  {
    id: "meta-files",
    name: "Unity meta files",
    run: "pwsh -NoProfile -File scripts/lint-meta-files.ps1 -VerboseOutput"
  },

  // Documentation.
  { id: "changelog", name: "CHANGELOG format", run: "npm run lint:changelog" },
  {
    id: "doc-links-test",
    name: "Doc-link linter self-test",
    run: "pwsh -NoProfile -File scripts/tests/test-lint-doc-links.ps1 -VerboseOutput"
  },
  {
    id: "deprecated-external-links",
    name: "Deprecated external link rules",
    run: "pwsh -NoProfile -File scripts/tests/test-deprecated-external-links.ps1 -VerboseOutput"
  },
  // One invocation, not three. `-Mode All` is the default and sets both `checkTargets` and
  // `checkFormat`, so the separate `-Mode Targets` and `-Mode Format` runs were re-walking every
  // Markdown file to redo work this one already did. Two workflows each ran their own half before,
  // which is why the redundancy was invisible until they landed in one serial job.
  {
    id: "doc-links",
    name: "Markdown links (targets and format)",
    run: "pwsh -NoProfile -File scripts/lint-doc-links.ps1 -Mode All -VerboseOutput"
  },
  {
    id: "gitignore-docs",
    name: "Gitignore safety docs",
    run: "pwsh -NoProfile -File scripts/lint-gitignore-docs.ps1 -VerboseOutput"
  },
  {
    id: "github-pages-css",
    name: "GitHub Pages CSS",
    run: "bash scripts/validate-github-pages-css.sh"
  },
  {
    id: "code-fence-syntax",
    name: "Code fence syntax",
    run: "bash scripts/check-code-fence-syntax.sh"
  },
  { id: "markdown", name: "Markdownlint", run: "npm run lint:markdown" },
  {
    id: "doc-code-samples",
    name: "Documentation code samples",
    run: "npm run lint:code-samples"
  },
  {
    id: "doc-identifiers",
    name: "Documentation namespace and assembly names",
    run: "npm run lint:doc-identifiers"
  },
  // Gaps found while consolidating: these four are in `validate:content` / `validate:local` and so
  // ran on a developer's machine, but no workflow invoked any of them. They pass today; the point
  // is that nothing would have said so if they stopped.
  { id: "doc-counts", name: "Documentation counts", run: "npm run lint:doc-counts" },
  // `docs/readme.md` is generated from `README.md`; before #593 both were hand-maintained and
  // had drifted 138 lines apart in both directions.
  {
    id: "readme-mirror-test",
    name: "README mirror generator self-test",
    run: "npm run test:sync-readme-mirror"
  },
  {
    id: "readme-mirror",
    name: "README / docs mirror",
    run: "npm run lint:readme-mirror"
  },

  // Formatting.
  { id: "format-md", name: "Prettier (Markdown)", run: "npm run format:md:check" },
  { id: "format-json", name: "Prettier (JSON / asmdef)", run: "npm run format:json:check" },
  { id: "format-js", name: "Prettier (JS)", run: "npm run format:js:check" },
  { id: "format-yaml", name: "Prettier (YAML)", run: "npm run format:yaml:check" },
  { id: "yaml", name: "YAML style", run: "npm run lint:yaml" },
  {
    id: "duplicate-usings",
    name: "Duplicate using directives",
    run: "npm run lint:duplicate-usings"
  },
  { id: "eol", name: "Line endings and BOM", run: "npm run eol:check" },
  {
    id: "final-newline",
    name: "Final newline contract",
    run: "bash scripts/tests/test-final-newline.sh"
  },
  {
    id: "formatting",
    name: "Formatter coverage",
    run: "bash scripts/validate-formatting.sh"
  },

  // Configuration and tooling.
  { id: "dependabot", name: "Dependabot config", run: "npm run lint:dependabot" },
  {
    id: "lint-error-codes",
    name: "Lint-error-code cspell coverage",
    run: "npm run validate:lint-error-codes"
  },
  {
    id: "lint-error-codes-test",
    name: "Lint-error-code validator self-test",
    run: "npm run test:validate-lint-error-codes"
  },
  { id: "mcp-config", name: "MCP config", run: "npm run validate:mcp-config" },
  { id: "npm-package", name: "NPM package", run: "npm run validate:npm-package" },
  {
    id: "devcontainer-config",
    name: "Devcontainer config",
    run: "pwsh -NoProfile -File scripts/validate-devcontainer-config.ps1 -VerboseOutput"
  },
  {
    id: "devcontainer-urls",
    name: "Devcontainer download URLs",
    run: "bash scripts/validate-devcontainer-urls.sh"
  },
  {
    id: "devcontainer-urls-test",
    name: "Devcontainer URL contracts",
    run: "bash scripts/tests/test-validate-devcontainer-urls.sh"
  },
  {
    id: "post-create",
    name: "Post-create script",
    run: "bash scripts/tests/test-post-create.sh --verbose"
  },

  // Git hooks.
  { id: "hook-patterns", name: "Hook patterns", run: "bash scripts/tests/test-hook-patterns.sh" },
  {
    id: "hook-permissions",
    name: "Hook permissions",
    run: "bash scripts/validate-hook-permissions.sh"
  },
  {
    id: "hook-sync-calls",
    name: "Hook sync calls",
    run: "pwsh -NoProfile -File scripts/validate-hook-sync-calls.ps1 -VerboseOutput"
  },
  {
    id: "hook-spell-parity",
    name: "Hook spell-check parity",
    run: "bash scripts/tests/test-hook-spell-parity.sh"
  },

  // Test-suite linters' own suites.
  {
    id: "lint-tests-test",
    name: "Test linter self-test",
    run: "pwsh -NoProfile -File scripts/tests/test-lint-tests.ps1 -VerboseOutput"
  },
  {
    id: "report-slow-tests",
    name: "Slow-test reporter",
    run: "pwsh -NoProfile -File scripts/tests/test-report-slow-tests.ps1 -VerboseOutput"
  },
  {
    id: "unity-nunit-results",
    name: "Unity NUnit results gate",
    run: "bash scripts/tests/test-unity-nunit-results.sh"
  },
  {
    id: "process-watchdog",
    name: "Process watchdog",
    run: "pwsh -NoProfile -File scripts/tests/test-process-watchdog.ps1 -VerboseOutput"
  },
  {
    id: "unity-license-activation-retry",
    name: "Unity license activation retry",
    run: "pwsh -NoProfile -File scripts/tests/test-unity-license-activation-retry.ps1 -VerboseOutput"
  },
  {
    id: "unity-configure-upm-retry",
    name: "Unity configure UPM retry",
    run: "pwsh -NoProfile -File scripts/tests/test-unity-configure-upm-retry.ps1 -VerboseOutput"
  },
  {
    id: "catastrophic-pattern-sync",
    name: "Catastrophic pattern sync",
    run: "pwsh -NoProfile -File scripts/tests/test-catastrophic-pattern-sync.ps1 -VerboseOutput"
  },

  // Spelling and licensing.
  { id: "spelling", name: "Spell check", run: "npm run lint:spelling" },
  {
    id: "spelling-config",
    name: "cspell dictionary hygiene",
    run: "npm run lint:spelling:config"
  },
  {
    id: "license-years",
    name: "License year audit",
    run: "bash scripts/audit-license-years.sh --summary"
  }
];

module.exports = { CHECKS, runChecks };

if (require.main === module) {
  runRegistry({
    checks: CHECKS,
    title: "Repo Lint",
    command: "node scripts/run-repo-lint.js",
    argv: process.argv.slice(2)
  }).then((code) => {
    process.exitCode = code;
  });
}
