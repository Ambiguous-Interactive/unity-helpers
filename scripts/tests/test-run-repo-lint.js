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

function runTest(name, fn) {
  try {
    fn();
    console.log(`  [PASS] ${name}`);
    passed++;
  } catch (err) {
    console.log(`  [FAIL] ${name}`);
    console.log(`         ${err.message}`);
    failed++;
    failedTests.push(name);
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

runTest("a failing check does not stop the ones after it", () => {
  // The whole reason the runner exists rather than an `&&` chain. Asserted on the real function,
  // with synthetic checks, so it cannot pass by coincidence of the registry currently being green.
  //
  // Output is swallowed for the duration: the synthetic failure makes the runner emit a real
  // `::error::` line, and GitHub turns that into an annotation on this job -- a green run that
  // reports an error it deliberately caused. Only the return value is under test.
  const realWrite = process.stdout.write.bind(process.stdout);
  let results;
  try {
    process.stdout.write = () => true;
    results = runChecks([
      { id: "synthetic-pass-first", name: "synthetic pass", run: "true" },
      { id: "synthetic-fail", name: "synthetic fail", run: "exit 3" },
      { id: "synthetic-pass-after-failure", name: "synthetic pass after failure", run: "true" }
    ]);
  } finally {
    process.stdout.write = realWrite;
  }

  assert.strictEqual(results.length, 3, "every supplied check must produce a result");
  assert.strictEqual(results[0].ok, true);
  assert.strictEqual(results[1].ok, false, "a non-zero exit must be recorded as a failure");
  assert.strictEqual(
    results[2].ok,
    true,
    "the check after a failure must still have run -- this is the aggregation contract"
  );
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

console.log(`\n${passed} passed, ${failed} failed`);
if (failed > 0) {
  console.log(`Failed: ${failedTests.join(", ")}`);
  process.exit(1);
}
process.exit(0);
