"use strict";

// One runner for the repository's contract and self-test suite -- what `npm run validate:tests:fast`
// executes, locally and in CI.
//
// This was a serial `&&` chain of forty-five npm scripts in package.json. Measured in the
// devcontainer on eight cores, that chain took 9 m 52 s of wall clock for roughly four CPU-minutes
// of work (#505, #425): every link paid an `npm run` start-up, most of them a `pwsh -NoProfile`
// start-up on top of it, and all of them waited for the one before. Nothing about the suite required
// that order -- these are independent processes that read the tree and write only into per-run
// temporary directories, which is why the same runner Repo Lint uses applies unchanged.
//
// Two properties this keeps that the chain did not have:
//   * every check runs even after one fails, so a run reports the whole contract state in one go
//     rather than one failure per round trip;
//   * the registry is data, so scripts/tests/test-run-contract-tests.js can assert the suite still
//     covers every contract test in `scripts/tests/` -- the failure mode consolidation invites is a
//     test that still exists and no longer runs.
//
// Commands are the npm scripts the chain ran, verbatim, so the definition of each check still lives
// in exactly one place and the move changed no work.

const { runChecks, runRegistry } = require("./check-runner");

/** A check whose `run` is executed with `bash -c` from the repository root. */
const CHECKS = [
  // Test-suite and source hygiene.
  { id: "lint-tests", name: "Test lifecycle linter", run: "npm run lint:tests" },
  { id: "check-eol", name: "Line-ending checker", run: "npm run test:check-eol" },
  {
    id: "out-parameters",
    name: "Out-parameter discipline",
    run: "npm run test:out-parameters"
  },
  {
    id: "analyzer-placement",
    name: "Shipped analyzer placement",
    run: "npm run test:analyzer-placement"
  },
  {
    id: "asset-postprocessor-reachability",
    name: "AssetPostprocessor reachability",
    run: "npm run test:asset-postprocessor-reachability"
  },
  {
    id: "wproto-annotations",
    name: "WallstopProto annotations",
    run: "npm run test:wproto-annotations"
  },

  // Linter self-tests.
  {
    id: "lint-dependabot",
    name: "Dependabot linter self-test",
    run: "npm run test:lint-dependabot"
  },
  {
    id: "lint-duplicate-usings",
    name: "Duplicate-usings linter self-test",
    run: "npm run test:lint-duplicate-usings"
  },
  {
    id: "lint-concurrent-cache-fill",
    name: "Concurrent cache fill linter self-test",
    run: "npm run test:lint-concurrent-cache-fill"
  },
  {
    id: "lint-pwsh-invocations",
    name: "PowerShell invocation linter self-test",
    run: "npm run test:lint-pwsh-invocations"
  },
  {
    id: "lint-workflow-run-expression-length",
    name: "Workflow run-expression linter self-test",
    run: "npm run test:lint-workflow-run-expression-length"
  },
  {
    id: "validate-lint-error-codes",
    name: "Lint-error-code validator self-test",
    run: "npm run test:validate-lint-error-codes"
  },
  {
    id: "gitignore-docs",
    name: "Gitignore-docs linter self-test",
    run: "npm run test:gitignore-docs"
  },
  { id: "run-repo-lint", name: "Repo Lint runner contracts", run: "npm run test:run-repo-lint" },
  {
    id: "run-contract-tests",
    name: "Contract runner contracts",
    run: "npm run test:run-contract-tests"
  },

  // Git and credential contracts.
  {
    id: "git-path-helpers",
    name: "Git path helpers",
    run: "npm run test:git-path-helpers"
  },
  {
    id: "git-staging-helpers",
    name: "Git staging helpers",
    run: "npm run test:git-staging-helpers"
  },
  {
    id: "configure-git-defaults",
    name: "Git default configuration",
    run: "npm run test:configure-git-defaults"
  },
  {
    id: "normalize-container-git-config",
    name: "Container git config normalization",
    run: "npm run test:normalize-container-git-config"
  },
  {
    id: "validate-git-push-config",
    name: "Git push configuration validator",
    run: "npm run test:validate-git-push-config"
  },
  { id: "github-token", name: "GitHub token helper", run: "npm run test:github-token" },

  // Workflow and CI contracts.
  {
    id: "sync-script-contracts",
    name: "Script and workflow sync contracts",
    run: "npm run test:sync-script-contracts"
  },
  {
    id: "pr-workflow-concurrency",
    name: "Pull-request workflow concurrency",
    run: "npm run test:pr-workflow-concurrency"
  },
  {
    id: "workflow-repository-guard",
    name: "Workflow repository guard",
    run: "npm run test:workflow-repository-guard"
  },
  {
    id: "unity-workflow-matrix-contract",
    name: "Unity workflow matrix contract",
    run: "npm run test:unity-workflow-matrix-contract"
  },
  {
    id: "build-lock-action-inputs",
    name: "Build-lock action inputs",
    run: "npm run test:build-lock-action-inputs"
  },
  {
    id: "build-lock-action-input-parser",
    name: "Build-lock action input parser",
    run: "npm run test:build-lock-action-input-parser"
  },
  {
    id: "portable-cleanup-classifier",
    name: "Portable cleanup classifier",
    run: "npm run test:portable-cleanup-classifier"
  },
  { id: "license-cache", name: "Unity license cache", run: "npm run test:license-cache" },
  {
    id: "unity-nunit-results",
    name: "Unity NUnit results gate",
    run: "npm run test:unity-nunit-results"
  },
  {
    id: "shell-portability",
    name: "Shell portability",
    run: "npm run test:shell-portability"
  },
  { id: "read-stdin-sync", name: "Synchronous stdin reader", run: "npm run test:read-stdin-sync" },

  // Environment and tooling.
  {
    id: "validate-mcp-config",
    name: "MCP configuration validator",
    run: "npm run test:validate-mcp-config"
  },
  { id: "unity-mcp", name: "Unity MCP helpers", run: "npm run test:unity-mcp" },
  { id: "accelerator", name: "Unity Accelerator configuration", run: "npm run test:accelerator" },
  {
    id: "project-workspace",
    name: "Project workspace resolution",
    run: "npm run test:project-workspace"
  },
  {
    id: "postinstall-hooks",
    name: "Postinstall hook wiring",
    run: "npm run test:postinstall-hooks"
  },
  { id: "add-cspell-word", name: "cspell word insertion", run: "npm run test:add-cspell-word" },

  // Documentation, release and reporting tools.
  {
    id: "github-pages-sortable",
    name: "GitHub Pages sortable tables",
    run: "npm run test:github-pages-sortable"
  },
  { id: "wiki-generation", name: "Wiki generation", run: "npm run test:wiki-generation" },
  {
    id: "npm-package-signature",
    name: "npm package signature",
    run: "npm run test:npm-package-signature"
  },
  {
    id: "npm-package-changelog",
    name: "npm package changelog",
    run: "npm run test:npm-package-changelog"
  },
  { id: "release-tools", name: "Release tooling", run: "npm run test:release-tools" },
  { id: "perf-tools", name: "Performance report tooling", run: "npm run test:perf-tools" },
  {
    id: "random-quality-stream",
    name: "Random-quality stream",
    run: "npm run test:random-quality-stream"
  },
  {
    id: "random-quality-outcomes",
    name: "Random-quality outcomes",
    run: "npm run test:random-quality-outcomes"
  }
];

module.exports = { CHECKS, runChecks };

if (require.main === module) {
  runRegistry({
    checks: CHECKS,
    title: "Contract Tests",
    command: "node scripts/run-contract-tests.js",
    argv: process.argv.slice(2)
  }).then((code) => {
    process.exitCode = code;
  });
}
