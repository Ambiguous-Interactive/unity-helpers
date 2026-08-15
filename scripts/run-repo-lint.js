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
// Commands are the ones the retired workflows ran, verbatim, so consolidation moved the work
// without changing it. Where an npm script was already the exact entry point, that script is used
// so the definition still lives in exactly one place.

const { spawnSync } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");

const repoRoot = path.resolve(__dirname, "..");

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
    id: "bundled-assemblies",
    name: "Bundled assembly importers",
    run: "npm run lint:bundled-assemblies"
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
  {
    id: "doc-links",
    name: "Markdown links",
    run: "pwsh -NoProfile -File scripts/lint-doc-links.ps1 -VerboseOutput"
  },
  {
    id: "doc-links-targets",
    name: "Markdown link targets exist",
    run: "pwsh -NoProfile -File scripts/lint-doc-links.ps1 -Mode Targets -VerboseOutput"
  },
  {
    id: "doc-links-format",
    name: "Markdown link format",
    run: "pwsh -NoProfile -File scripts/lint-doc-links.ps1 -Mode Format -VerboseOutput"
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
  // Gaps found while consolidating: these four are in `validate:content` / `validate:local` and so
  // ran on a developer's machine, but no workflow invoked any of them. They pass today; the point
  // is that nothing would have said so if they stopped.
  { id: "doc-counts", name: "Documentation counts", run: "npm run lint:doc-counts" },

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

/**
 * @param {string[]} argv Raw process arguments, without node and the script path.
 * @returns {{only: Set<string>, list: boolean}} Parsed selection.
 */
function parseArguments(argv) {
  const only = new Set();
  let list = false;
  for (let index = 0; index < argv.length; index++) {
    const argument = argv[index];
    if (argument === "--list") {
      list = true;
    } else if (argument === "--only") {
      const value = argv[++index];
      if (value) {
        for (const id of value.split(",")) {
          if (id.trim()) {
            only.add(id.trim());
          }
        }
      }
    }
  }
  return { only, list };
}

/**
 * @param {{id: string, name: string, run: string}} check The registry entry to execute.
 * @returns {{id: string, name: string, ok: boolean, seconds: number}} Its outcome.
 */
function runCheck(check) {
  const startedAt = process.hrtime.bigint();
  // GitHub renders `::group::` as a collapsible section, which is what keeps ~57 checks in one job
  // readable. The name is echoed before the command so a failure is identifiable from the fold.
  process.stdout.write(`::group::${check.name} (${check.id})\n`);
  process.stdout.write(`$ ${check.run}\n`);

  const result = spawnSync("bash", ["-c", check.run], {
    cwd: repoRoot,
    stdio: "inherit",
    env: process.env
  });

  const seconds = Number(process.hrtime.bigint() - startedAt) / 1e9;
  const ok = result.status === 0;
  process.stdout.write("::endgroup::\n");
  if (!ok) {
    // Outside the fold, so a red job shows what failed without expanding anything.
    const reason = result.status === null ? `signal ${result.signal}` : `exit ${result.status}`;
    process.stdout.write(`::error::${check.name} (${check.id}) failed with ${reason}.\n`);
  }
  return { id: check.id, name: check.name, ok, seconds };
}

/**
 * Runs every supplied check, in order, regardless of how many of them fail. This is the property
 * that distinguishes the runner from the `&&` chain it replaced, so it is exported for
 * scripts/tests/test-run-repo-lint.js to assert directly rather than infer.
 *
 * @param {{id: string, name: string, run: string}[]} checks Registry entries to execute.
 * @returns {{id: string, name: string, ok: boolean, seconds: number}[]} One outcome per check.
 */
function runChecks(checks) {
  return checks.map(runCheck);
}

/**
 * @param {{id: string, name: string, ok: boolean, seconds: number}[]} results Completed outcomes.
 */
function writeStepSummary(results) {
  const summaryPath = process.env.GITHUB_STEP_SUMMARY;
  if (!summaryPath) {
    return;
  }
  const failures = results.filter((result) => !result.ok);
  const total = results.reduce((sum, result) => sum + result.seconds, 0);
  const lines = [
    "## Repo Lint",
    "",
    `${results.length - failures.length}/${results.length} checks passed in ${total.toFixed(1)}s.`,
    ""
  ];
  if (failures.length > 0) {
    lines.push("| Failed check | id |", "| --- | --- |");
    for (const failure of failures) {
      lines.push(`| ${failure.name} | \`${failure.id}\` |`);
    }
    lines.push(
      "",
      "Reproduce a single check locally with:",
      "",
      "```bash",
      `node scripts/run-repo-lint.js --only ${failures.map((failure) => failure.id).join(",")}`,
      "```",
      ""
    );
  }
  fs.appendFileSync(summaryPath, lines.join("\n") + "\n", "utf8");
}

function main() {
  const { only, list } = parseArguments(process.argv.slice(2));

  if (list) {
    for (const check of CHECKS) {
      process.stdout.write(`${check.id}\n`);
    }
    return 0;
  }

  const selected = only.size > 0 ? CHECKS.filter((check) => only.has(check.id)) : CHECKS;

  // An `--only` that matches nothing must fail rather than report a vacuous pass: a renamed id in a
  // caller would otherwise turn into a green check that ran no checks at all.
  if (only.size > 0) {
    const known = new Set(CHECKS.map((check) => check.id));
    const unknown = [...only].filter((id) => !known.has(id));
    if (unknown.length > 0) {
      process.stdout.write(`::error::Unknown check id(s): ${unknown.join(", ")}\n`);
      return 1;
    }
  }

  const results = runChecks(selected);
  const failures = results.filter((result) => !result.ok);

  writeStepSummary(results);

  process.stdout.write("\n");
  for (const result of results) {
    process.stdout.write(
      `${result.ok ? "PASS" : "FAIL"}  ${result.seconds.toFixed(1).padStart(6)}s  ${result.id}\n`
    );
  }
  const total = results.reduce((sum, result) => sum + result.seconds, 0);
  process.stdout.write(
    `\n${results.length - failures.length}/${results.length} checks passed in ${total.toFixed(1)}s.\n`
  );

  if (failures.length > 0) {
    process.stdout.write(
      `\nFailed: ${failures.map((failure) => failure.id).join(", ")}\n` +
        `Reproduce locally: node scripts/run-repo-lint.js --only ${failures
          .map((failure) => failure.id)
          .join(",")}\n`
    );
    return 1;
  }
  return 0;
}

module.exports = { CHECKS, runChecks };

if (require.main === module) {
  process.exitCode = main();
}
