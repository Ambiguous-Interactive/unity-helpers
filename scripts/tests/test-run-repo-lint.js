#!/usr/bin/env node
// MIT License — Copyright (c) wallstop studios
//
// Contract tests for scripts/run-repo-lint.js.
//
// Consolidating 24 workflows into one runner trades a whole class of cheap failure (a workflow that
// obviously never ran) for a quiet one (a registry entry that was renamed, or a linter nobody
// wired up). These are the assertions that keep the quiet failure loud.

"use strict";

const assert = require("assert");
const { spawnSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const repoRoot = path.resolve(__dirname, "..", "..");
const runnerPath = path.join(repoRoot, "scripts", "run-repo-lint.js");
const { CHECKS, runChecks } = require(runnerPath);
const packageScripts = JSON.parse(
  fs.readFileSync(path.join(repoRoot, "package.json"), "utf8")
).scripts;

let passed = 0;
let failed = 0;
const failedTests = [];

const tests = [];

/** Queued rather than run inline, because the concurrency assertions below have to await. */
function runTest(name, fn) {
  tests.push({ name, fn });
}

async function runQueuedTests() {
  for (const test of tests) {
    try {
      await test.fn();
      console.log(`  [PASS] ${test.name}`);
      passed++;
    } catch (err) {
      console.log(`  [FAIL] ${test.name}`);
      console.log(`         ${err.message}`);
      failed++;
      failedTests.push(test.name);
    }
  }
}

/**
 * Expands an npm script into the leaf shell commands it ultimately runs.
 *
 * @param {string} name npm script name.
 * @param {Set<string>} seen Cycle guard.
 * @returns {string[]} Leaf commands.
 */
function expandNpmScript(name, seen = new Set()) {
  if (seen.has(name)) {
    return [];
  }
  seen.add(name);
  const body = packageScripts[name];
  if (!body) {
    return [];
  }
  const commands = [];
  for (const rawPart of body.split(/&&|\|\|/)) {
    const part = rawPart.trim();
    const nested = part.match(/^npm run ([\w:.-]+)/);
    if (nested) {
      commands.push(...expandNpmScript(nested[1], seen));
    } else if (part) {
      commands.push(part);
    }
  }
  return commands;
}

/**
 * @param {string[]} commands Shell commands to scan.
 * @returns {Set<string>} Every `scripts/...` path they mention.
 */
function scriptPathsIn(commands) {
  const found = new Set();
  for (const command of commands) {
    for (const match of command.matchAll(/scripts\/[\w./-]+\.(?:ps1|sh|js)/g)) {
      found.add(match[0]);
    }
  }
  return found;
}

/** Leaf commands the registry ultimately executes, npm indirection resolved. */
function registryLeafCommands() {
  return CHECKS.flatMap((check) => {
    const npmScript = check.run.match(/^npm run ([\w:.-]+)$/);
    return npmScript ? expandNpmScript(npmScript[1]) : [check.run];
  });
}

console.log("Testing scripts/run-repo-lint.js...\n");

runTest("every check has a unique, non-empty id and name", () => {
  const seen = new Set();
  for (const check of CHECKS) {
    assert.ok(check.id && check.id.trim(), `check has empty id: ${JSON.stringify(check)}`);
    assert.ok(check.name && check.name.trim(), `check ${check.id} has empty name`);
    assert.ok(check.run && check.run.trim(), `check ${check.id} has empty run`);
    assert.ok(!seen.has(check.id), `duplicate check id: ${check.id}`);
    seen.add(check.id);
  }
});

runTest("every check resolves to a real npm script or an existing file", () => {
  for (const check of CHECKS) {
    const npmScript = check.run.match(/^npm run ([\w:.-]+)$/);
    if (npmScript) {
      assert.ok(
        packageScripts[npmScript[1]],
        `check ${check.id} runs missing npm script ${npmScript[1]}`
      );
      continue;
    }
    const scriptPath = check.run.match(/scripts\/[\w./-]+\.(?:ps1|sh|js)/);
    assert.ok(scriptPath, `check ${check.id} runs neither an npm script nor a scripts/ file`);
    assert.ok(
      fs.existsSync(path.join(repoRoot, scriptPath[0])),
      `check ${check.id} runs missing file ${scriptPath[0]}`
    );
  }
});

/**
 * Runs the real `runChecks` with the log suppressed.
 *
 * Output is swallowed for the duration: a synthetic failure makes the runner emit a real `::error::`
 * line, and GitHub turns that into an annotation on this job -- a green run that reports an error it
 * deliberately caused. Only the return value is under test.
 *
 * `drainMs` keeps the capture open after the last check settles. A duplicate write is emitted
 * *after* the promise resolves, so restoring stdout immediately sends it to the real log where no
 * assertion can see it -- the capture would show one fold while the job log showed two.
 *
 * @param {{id: string, name: string, run: string}[]} checks Synthetic checks.
 * @param {number} jobs Worker count.
 * @param {number} [drainMs] Milliseconds to keep capturing after the run resolves.
 * @returns {Promise<{captured: string, results: object[]}>} Suppressed log and outcomes.
 */
async function runQuietly(checks, jobs, drainMs = 0) {
  const realWrite = process.stdout.write.bind(process.stdout);
  let captured = "";
  try {
    process.stdout.write = (chunk) => {
      captured += chunk;
      return true;
    };
    const results = await runChecks(checks, jobs);
    if (drainMs > 0) {
      await new Promise((resolve) => setTimeout(resolve, drainMs));
    }
    return { captured, results };
  } finally {
    process.stdout.write = realWrite;
  }
}

runTest("a failing check does not stop the ones after it", async () => {
  // The whole reason the runner exists rather than an `&&` chain. Asserted on the real function,
  // with synthetic checks, so it cannot pass by coincidence of the registry currently being green.
  const { results } = await runQuietly(
    [
      { id: "synthetic-pass-first", name: "synthetic pass", run: "true" },
      { id: "synthetic-fail", name: "synthetic fail", run: "exit 3" },
      { id: "synthetic-pass-after-failure", name: "synthetic pass after failure", run: "true" }
    ],
    1
  );

  assert.strictEqual(results.length, 3, "every supplied check must produce a result");
  assert.strictEqual(results[0].ok, true);
  assert.strictEqual(results[1].ok, false, "a non-zero exit must be recorded as a failure");
  assert.strictEqual(
    results[2].ok,
    true,
    "the check after a failure must still have run -- this is the aggregation contract"
  );
});

runTest("the aggregation contract survives concurrency", async () => {
  // The same contract at `--jobs 4`. A worker pool that re-raised, or that stopped feeding the queue
  // on a non-zero exit, would pass the serial assertion above and fail this one.
  const checks = Array.from({ length: 12 }, (unused, index) => ({
    id: `synthetic-${index}`,
    name: `synthetic ${index}`,
    run: index % 3 === 0 ? "exit 3" : "true"
  }));
  const { results } = await runQuietly(checks, 4);

  assert.strictEqual(results.length, checks.length, "every supplied check must produce a result");
  assert.deepStrictEqual(
    results.map((result) => result.id),
    checks.map((check) => check.id),
    "results must stay in registry order however the workers interleave"
  );
  assert.deepStrictEqual(
    results.map((result) => result.ok),
    checks.map((check) => check.run === "true"),
    "each check's outcome must be its own, not the outcome of whichever worker finished first"
  );
});

runTest("concurrent checks actually overlap", async () => {
  // Without this the pool could be a serial loop wearing a Promise, and every wall-clock claim made
  // for `--jobs` would be false while all the correctness assertions above still passed.
  const checks = Array.from({ length: 4 }, (unused, index) => ({
    id: `sleeper-${index}`,
    name: `sleeper ${index}`,
    run: "sleep 1"
  }));
  const startedAt = Date.now();
  await runQuietly(checks, 4);
  const elapsed = (Date.now() - startedAt) / 1000;
  assert.ok(
    elapsed < 2.5,
    `four one-second checks at --jobs 4 took ${elapsed.toFixed(2)}s; they did not overlap`
  );
});

runTest("a command bash itself cannot be launched for settles exactly once", async () => {
  // The first version of this test ran `exec /nonexistent-bin`, which spawns bash perfectly well
  // and exits 127 -- so it never reached the spawn-error path at all and passed with the `error`
  // handler deleted outright. Emptying PATH is what actually fails the spawn (measured: ENOENT).
  //
  // Node then emits BOTH `error` and `close`, so this pins the settle-once guard as much as the
  // handler: without it the fold and the `::error::` line are written twice and the group nesting
  // the concurrent log depends on comes apart.
  const realPath = process.env.PATH;
  let captured;
  let results;
  try {
    process.env.PATH = "";
    ({ captured, results } = await runQuietly(
      [{ id: "synthetic-no-bash", name: "synthetic no bash", run: "true" }],
      1,
      250
    ));
  } finally {
    process.env.PATH = realPath;
  }

  const count = (needle) => captured.split(needle).length - 1;
  assert.strictEqual(count("::group::"), 1, "the fold must be written exactly once");
  assert.strictEqual(count("::endgroup::"), 1, "the fold must be closed exactly once");
  assert.strictEqual(count("::error::"), 1, "the annotation must be written exactly once");
  assert.strictEqual(results.length, 1, "a command that cannot launch must still produce a result");
  // Pins the `error` handler itself, which the settle-once assertions above do not: `close` fires
  // for a failed spawn too, so deleting the handler still yields one fold and one failed result --
  // just a fold that never says why. The reason has to be in the log or the check is unactionable.
  assert.match(
    captured,
    /ENOENT/,
    "the fold must carry the spawn error's reason, not just report a failure"
  );
  assert.strictEqual(results[0].ok, false, "it must be recorded as a failed check");
});

runTest("each check's output stays inside its own fold", async () => {
  // `::group::` is positional: GitHub folds everything up to the next `::endgroup::`. Streaming
  // concurrent children straight to stdout would interleave them into each other's folds, so the
  // captured log must contain balanced, non-overlapping group markers.
  const checks = Array.from({ length: 8 }, (unused, index) => ({
    id: `chatty-${index}`,
    name: `chatty ${index}`,
    run: `for i in 1 2 3 4 5; do echo "line-$i-of-${index}"; done`
  }));
  const { captured } = await runQuietly(checks, 4);

  let depth = 0;
  for (const line of captured.split("\n")) {
    if (line.startsWith("::group::")) {
      depth++;
      assert.strictEqual(depth, 1, `a group opened inside another group: ${line}`);
    } else if (line.startsWith("::endgroup::")) {
      depth--;
      assert.strictEqual(depth, 0, "a group closed without being open");
    }
  }
  assert.strictEqual(depth, 0, "every group must be closed");

  for (const index of checks.keys()) {
    const fold = captured.match(new RegExp(`::group::chatty ${index} [^]*?::endgroup::`));
    assert.ok(fold, `no fold emitted for chatty ${index}`);
    for (let line = 1; line <= 5; line++) {
      assert.ok(
        fold[0].includes(`line-${line}-of-${index}`),
        `chatty ${index} lost line ${line} out of its own fold`
      );
    }
  }
});

runTest("--jobs rejects a value that is not a positive integer", () => {
  // A shell expansion that produced nothing would otherwise read as `--jobs` with no value and
  // silently fall back to the default, so a run that was meant to be serial would not be.
  for (const value of ["0", "-1", "2.5", "many", ""]) {
    const result = spawnSync(
      process.execPath,
      [runnerPath, "--jobs", value, "--only", "changelog"],
      {
        cwd: repoRoot,
        encoding: "utf8"
      }
    );
    assert.notStrictEqual(result.status, 0, `--jobs ${JSON.stringify(value)} must fail`);
    assert.ok(
      /--jobs must be a positive integer/.test(result.stdout + result.stderr),
      `--jobs ${JSON.stringify(value)} must say why it failed`
    );
  }
});

runTest("--only with an unknown id fails instead of passing vacuously", () => {
  const result = spawnSync(
    process.execPath,
    [runnerPath, "--only", "definitely-not-a-real-check-id"],
    { cwd: repoRoot, encoding: "utf8" }
  );
  assert.notStrictEqual(
    result.status,
    0,
    "an unknown id must fail; otherwise a renamed check turns into a green run of nothing"
  );
  assert.ok(
    /Unknown check id/.test(result.stdout + result.stderr),
    "the failure must name the unknown id"
  );
});

runTest("--list prints every registered id", () => {
  const result = spawnSync(process.execPath, [runnerPath, "--list"], {
    cwd: repoRoot,
    encoding: "utf8"
  });
  assert.strictEqual(result.status, 0, "--list must succeed");
  const listed = result.stdout.trim().split(/\r?\n/).filter(Boolean);
  assert.deepStrictEqual(
    listed.sort(),
    CHECKS.map((check) => check.id).sort(),
    "--list output must match the registry exactly"
  );
});

runTest("no linter in scripts/ has been left unreachable", () => {
  // Linters that legitimately do not belong in Repo Lint, each with the thing that does run it.
  // An entry here is a claim that must stay true; anything else new in scripts/ has to be wired up.
  const runElsewhere = new Map([
    ["scripts/lint-llm-instructions.ps1", ".github/workflows/llm-instructions-lint.yml (cross-OS)"],
    ["scripts/lint-skill-sizes.ps1", ".github/workflows/llm-instructions-lint.yml (cross-OS)"],
    ["scripts/lint-unity-test-modules.ps1", ".github/workflows/unity-tests.yml"],
    ["scripts/lint-csharp-format.ps1", ".github/workflows/csharpier-autofix.yml gates C# format"],
    ["scripts/lint-staged-links.ps1", "git hooks: staged files only, nothing to check in CI"],
    ["scripts/lint-staged-markdown.ps1", "git hooks: staged files only, nothing to check in CI"],
    ["scripts/validate-git-push-config.ps1", "pre-push: inspects local git config, not the tree"]
  ]);

  const reachable = new Set([
    ...scriptPathsIn(registryLeafCommands()),
    // Local Gates runs these two aggregates; anything they already cover need not be repeated.
    ...scriptPathsIn(expandNpmScript("validate:tests")),
    ...scriptPathsIn(expandNpmScript("typecheck:unity"))
  ]);

  const linters = fs
    .readdirSync(path.join(repoRoot, "scripts"))
    .filter((name) => /^(?:lint|validate)-.*\.(?:ps1|sh|js)$/.test(name))
    .map((name) => `scripts/${name}`)
    .sort();

  const orphans = linters.filter((file) => !reachable.has(file) && !runElsewhere.has(file));
  assert.deepStrictEqual(
    orphans,
    [],
    `these linters exist but nothing runs them -- add them to CHECKS in scripts/run-repo-lint.js, ` +
      `or to runElsewhere here with the workflow that does: ${orphans.join(", ")}`
  );

  // The allowlist must not outlive the files it names, or it silently excuses nothing.
  const stale = [...runElsewhere.keys()].filter(
    (file) => !fs.existsSync(path.join(repoRoot, file))
  );
  assert.deepStrictEqual(
    stale,
    [],
    `allowlist names files that no longer exist: ${stale.join(", ")}`
  );
});

runQueuedTests().then(() => {
  console.log(`\n${passed} passed, ${failed} failed`);
  if (failed > 0) {
    console.log(`Failed: ${failedTests.join(", ")}`);
    process.exit(1);
  }
  process.exit(0);
});
