"use strict";

// The concurrent driver behind scripts/run-repo-lint.js and scripts/run-contract-tests.js.
//
// Both are the same shape: a registry of independent processes that only read the tree, run to
// completion regardless of how many fail, and report one table. That shape was written once for
// Repo Lint and then, for a year, the repository's other forty-five-check suite stayed a serial
// `&&` chain in package.json -- measured at 9 m 52 s of wall clock for roughly four CPU-minutes of
// work (#505), because every link paid an npm start-up and most of them a pwsh start-up too, one
// after another, on eight idle cores.
//
// Two properties a chain of `&&` does not have, and the reason this is a runner rather than a
// `xargs -P`:
//   * every check runs even after one fails, so a run reports its whole state in one go rather than
//     one failure per round trip;
//   * the registry is data, so a contract test can assert that every linter and every contract
//     suite in `scripts/` is actually reached by something -- the failure mode consolidation invites
//     is a check that still exists and no longer runs.
//
// Output is captured rather than streamed for the reason documented on runCheck: `::group::` is a
// positional marker, so concurrent writers would interleave into each other's folds.

const { spawn } = require("node:child_process");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

const repoRoot = path.resolve(__dirname, "..");

/**
 * Concurrency a runner uses when `--jobs` is not supplied. The checks are independent processes
 * that only read the tree, so this is bounded by cores rather than by anything about a registry.
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
 * one check's output in memory (the largest in either registry is well under a megabyte) and lets
 * the completed fold be written as a unit.
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

    // Node emits BOTH `error` and `close` when a spawn fails -- measured, `error` then
    // `close(-2, null)` for ENOENT -- so an unguarded pair of handlers writes the fold and the
    // `::error::` line twice and unbalances the group nesting the concurrent log depends on.
    // Listening only for `close` is not the fix either: the documentation is explicit that it "may
    // or may not fire after an error has occurred", and a check that never settles hangs the pool
    // until the job timeout. Both handlers, one settle.
    let settled = false;
    const finish = (status, signal) => {
      if (settled) {
        return;
      }
      settled = true;
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
 * distinguishes the runner from the `&&` chains it replaced, so it is exported for the contract
 * tests to assert directly rather than infer.
 *
 * Results are returned in registry order however the workers interleave, so the summary table does
 * not reorder itself run to run; the folds are written in completion order, which is what makes the
 * log show progress rather than arriving all at once.
 *
 * @param {{id: string, name: string, run: string, exclusive?: boolean}[]} checks Entries.
 * @param {number} [jobs] Maximum checks to run at once. Defaults to one worker per core.
 * @returns {Promise<{id: string, name: string, ok: boolean, seconds: number}[]>} One per check.
 */
async function runChecks(checks, jobs = defaultJobs()) {
  const results = new Array(checks.length);

  // A check marked `exclusive` mutates shared repository state and runs with nothing beside it.
  // This is not a theoretical hazard: `test-npm-package-changelog.ps1` rewrites the real
  // package.json and drops canary files into the working tree, restoring both in a `finally`.
  // Inside that window every other check sees the mutated tree -- and since every check in this
  // registry is launched through `npm run`, npm itself reads that package.json to resolve the
  // script. Measured: one run in five failed `sync-script-contracts` with "package.json files
  // entries the validator forbids: pr-description.md", which is that canary rather than anything
  // wrong with package.json.
  //
  // They run FIRST and serially, so the pool that follows sees a restored tree instead of racing
  // the restore.
  const exclusiveIndexes = [];
  const sharedIndexes = [];
  checks.forEach((check, index) => {
    (check.exclusive ? exclusiveIndexes : sharedIndexes).push(index);
  });

  for (const index of exclusiveIndexes) {
    results[index] = await runCheck(checks[index]);
  }

  let next = 0;
  const worker = async () => {
    for (let cursor = next++; cursor < sharedIndexes.length; cursor = next++) {
      const index = sharedIndexes[cursor];
      results[index] = await runCheck(checks[index]);
    }
  };
  await Promise.all(Array.from({ length: Math.min(jobs, sharedIndexes.length) }, worker));
  return results;
}

/**
 * @param {{id: string, name: string, ok: boolean, seconds: number}[]} results Completed outcomes.
 * @param {number} wallSeconds Elapsed time for the whole run.
 * @param {number} jobs Worker count the run used.
 * @param {{title: string, command: string}} registry Heading and the command that reproduces a check.
 */
function writeStepSummary(results, wallSeconds, jobs, registry) {
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
    `## ${registry.title}`,
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
      `${registry.command} --only ${failures.map((failure) => failure.id).join(",")}`,
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

/**
 * The whole command-line behaviour of a registry runner: selection, execution, reporting.
 *
 * @param {{checks: {id: string, name: string, run: string}[], title: string, command: string,
 *   argv: string[]}} registry The entries to run, how to name them, and the arguments to honour.
 * @returns {Promise<number>} Process exit code.
 */
async function runRegistry(registry) {
  const { only, list, jobs, jobsInvalid } = parseArguments(registry.argv);
  const checks = registry.checks;

  if (jobsInvalid !== null) {
    process.stdout.write(`::error::--jobs must be a positive integer, got: ${jobsInvalid}\n`);
    return 1;
  }

  if (list) {
    for (const check of checks) {
      process.stdout.write(`${check.id}\n`);
    }
    return 0;
  }

  const selected = only.size > 0 ? checks.filter((check) => only.has(check.id)) : checks;

  // An `--only` that matches nothing must fail rather than report a vacuous pass: a renamed id in a
  // caller would otherwise turn into a green check that ran no checks at all.
  if (only.size > 0) {
    const known = new Set(checks.map((check) => check.id));
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

  writeStepSummary(results, wallSeconds, jobs, registry);

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
        `Reproduce locally: ${registry.command} --only ${failures
          .map((failure) => failure.id)
          .join(",")}\n`
    );
    return 1;
  }
  return 0;
}

module.exports = { defaultJobs, parseArguments, runCheck, runChecks, runRegistry };
