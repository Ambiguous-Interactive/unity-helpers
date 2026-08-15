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

const { spawn } = require("node:child_process");
const fs = require("node:fs");
const os = require("node:os");
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
 * Concurrency the runner uses when `--jobs` is not supplied. The checks are independent processes
 * that only read the tree, so this is bounded by cores rather than by anything about the registry.
 *
 * @returns {number} Default worker count.
 */
function defaultJobs() {
  const cores =
    typeof os.availableParallelism === "function" ? os.availableParallelism() : os.cpus().length;
  return Math.max(1, cores);
}

/**
 * @param {string[]} argv Raw process arguments, without node and the script path.
 * @returns {{only: Set<string>, list: boolean, jobs: number, jobsInvalid: string|null}} Selection.
 */
function parseArguments(argv) {
  const only = new Set();
  let list = false;
  let jobs = 0;
  // Null rather than "" for absent: `--jobs ""` is itself an invalid value to report, and an empty
  // string is falsy, so a truthiness test would wave through the one spelling most likely to arrive
  // from a shell expansion that produced nothing.
  let jobsInvalid = null;
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
    } else if (argument === "--jobs") {
      // A malformed value must fail rather than silently fall back to the default: `--jobs 0` from a
      // shell expansion that produced nothing would otherwise look like it was honored.
      const value = argv[++index];
      const parsed = Number(value);
      if (!Number.isInteger(parsed) || parsed < 1) {
        jobsInvalid = String(value);
      } else {
        jobs = parsed;
      }
    }
  }
  return { only, list, jobs: jobs || defaultJobs(), jobsInvalid };
}

/**
 * Runs one check to completion, capturing its output rather than letting it stream.
 *
 * Capturing is what makes concurrency readable. `::group::` is a positional marker -- GitHub folds
 * everything between it and the next `::endgroup::` -- so two checks writing to the log at once
 * would interleave into each other's folds and the whole log becomes unreadable. Buffering costs
 * one check's output in memory (the largest in this registry is well under a megabyte) and lets the
 * completed fold be written as a unit.
 *
 * @param {{id: string, name: string, run: string}} check The registry entry to execute.
 * @returns {Promise<{id: string, name: string, ok: boolean, seconds: number}>} Its outcome.
 */
function runCheck(check) {
  return new Promise((resolve) => {
    const startedAt = process.hrtime.bigint();
    const child = spawn("bash", ["-c", check.run], {
      cwd: repoRoot,
      stdio: ["ignore", "pipe", "pipe"],
      env: process.env
    });

    // Interleaved into one buffer rather than two, so a script's stderr stays next to the stdout it
    // was describing instead of being appended after everything it explains.
    const chunks = [];
    child.stdout.on("data", (chunk) => chunks.push(chunk));
    child.stderr.on("data", (chunk) => chunks.push(chunk));

    const finish = (status, signal) => {
      const seconds = Number(process.hrtime.bigint() - startedAt) / 1e9;
      const ok = status === 0;
      // The name is echoed before the command so a failure is identifiable from the collapsed fold.
      let block = `::group::${check.name} (${check.id})\n$ ${check.run}\n`;
      block += Buffer.concat(chunks).toString("utf8");
      block += "::endgroup::\n";
      if (!ok) {
        // Outside the fold, so a red job shows what failed without expanding anything.
        const reason = status === null ? `signal ${signal}` : `exit ${status}`;
        block += `::error::${check.name} (${check.id}) failed with ${reason}.\n`;
      }
      process.stdout.write(block);
      resolve({ id: check.id, name: check.name, ok, seconds });
    };

    // A command bash cannot even launch must be a failed check, not an unhandled rejection.
    child.on("error", (error) => {
      chunks.push(Buffer.from(`${error.message}\n`, "utf8"));
      finish(null, "spawn-error");
    });
    child.on("close", finish);
  });
}

/**
 * Runs every supplied check regardless of how many of them fail. This is the property that
 * distinguishes the runner from the `&&` chain it replaced, so it is exported for
 * scripts/tests/test-run-repo-lint.js to assert directly rather than infer.
 *
 * Results are returned in registry order however the workers interleave, so the summary table does
 * not reorder itself run to run; the folds are written in completion order, which is what makes the
 * log show progress rather than arriving all at once.
 *
 * @param {{id: string, name: string, run: string}[]} checks Registry entries to execute.
 * @param {number} [jobs] Maximum checks to run at once. Defaults to one worker per core.
 * @returns {Promise<{id: string, name: string, ok: boolean, seconds: number}[]>} One per check.
 */
async function runChecks(checks, jobs = defaultJobs()) {
  const results = new Array(checks.length);
  let next = 0;
  const worker = async () => {
    for (let index = next++; index < checks.length; index = next++) {
      results[index] = await runCheck(checks[index]);
    }
  };
  await Promise.all(Array.from({ length: Math.min(jobs, checks.length) }, worker));
  return results;
}

/**
 * @param {{id: string, name: string, ok: boolean, seconds: number}[]} results Completed outcomes.
 * @param {number} wallSeconds Elapsed time for the whole run.
 * @param {number} jobs Worker count the run used.
 */
function writeStepSummary(results, wallSeconds, jobs) {
  const summaryPath = process.env.GITHUB_STEP_SUMMARY;
  if (!summaryPath) {
    return;
  }
  const failures = results.filter((result) => !result.ok);
  const total = results.reduce((sum, result) => sum + result.seconds, 0);
  // The per-check table goes to the summary because the job log has no other place it can be read
  // without expanding sixty folds, and it is the measurement any future re-balancing starts from.
  const slowest = [...results].sort((a, b) => b.seconds - a.seconds).slice(0, 10);
  const lines = [
    "## Repo Lint",
    "",
    `${results.length - failures.length}/${results.length} checks passed in ` +
      `${wallSeconds.toFixed(1)}s wall clock across ${jobs} job(s) (${total.toFixed(1)}s serial).`,
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
  lines.push(
    "<details><summary>Ten slowest checks</summary>",
    "",
    "| Check | id | Seconds |",
    "| --- | --- | ---: |",
    ...slowest.map(
      (result) => `| ${result.name} | \`${result.id}\` | ${result.seconds.toFixed(1)} |`
    ),
    "",
    "</details>",
    ""
  );
  fs.appendFileSync(summaryPath, lines.join("\n") + "\n", "utf8");
}

async function main() {
  const { only, list, jobs, jobsInvalid } = parseArguments(process.argv.slice(2));

  if (jobsInvalid !== null) {
    process.stdout.write(`::error::--jobs must be a positive integer, got: ${jobsInvalid}\n`);
    return 1;
  }

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

  const wallStartedAt = process.hrtime.bigint();
  const results = await runChecks(selected, jobs);
  const wallSeconds = Number(process.hrtime.bigint() - wallStartedAt) / 1e9;
  const failures = results.filter((result) => !result.ok);

  writeStepSummary(results, wallSeconds, jobs);

  process.stdout.write("\n");
  for (const result of results) {
    process.stdout.write(
      `${result.ok ? "PASS" : "FAIL"}  ${result.seconds.toFixed(1).padStart(6)}s  ${result.id}\n`
    );
  }
  const total = results.reduce((sum, result) => sum + result.seconds, 0);
  // Both numbers, because only the wall clock says what the job cost and only the sum says what the
  // registry costs -- and their ratio is the evidence for whether `--jobs` is still earning its keep.
  process.stdout.write(
    `\n${results.length - failures.length}/${results.length} checks passed in ` +
      `${wallSeconds.toFixed(1)}s wall clock across ${jobs} job(s) ` +
      `(${total.toFixed(1)}s serial).\n`
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
  main().then((code) => {
    process.exitCode = code;
  });
}
