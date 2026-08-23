#!/usr/bin/env node
// MIT License — Copyright (c) wallstop studios
//
// Contract tests for scripts/run-contract-tests.js.
//
// Turning `validate:tests:fast` from a serial `&&` chain into a registry buys 9 m 13 s of wall
// clock (#505, #425) and costs the one property the chain had for free: a check listed in
// package.json obviously ran. These are the assertions that replace it -- that the npm script still
// points at the runner, that every contract suite in `scripts/tests/` is reached by something, and
// that the heavy hook regressions stay out of the fast aggregate.

"use strict";

const assert = require("assert");
const { spawnSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const repoRoot = path.resolve(__dirname, "..", "..");
const runnerPath = path.join(repoRoot, "scripts", "run-contract-tests.js");
const { CHECKS, runChecks } = require(runnerPath);
const repoLintChecks = require(path.join(repoRoot, "scripts", "run-repo-lint.js")).CHECKS;
const { expandNpmScript, scriptPathsIn, filesInvoking } = require(
  path.join(repoRoot, "scripts", "tests", "test-run-repo-lint.js")
);
const packageScripts = JSON.parse(
  fs.readFileSync(path.join(repoRoot, "package.json"), "utf8")
).scripts;

let passed = 0;
let failed = 0;
const failedTests = [];
const tests = [];

/** Queued rather than run inline, because the concurrency assertion below has to await. */
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

/** Leaf commands a registry ultimately executes, npm indirection resolved. */
function registryLeafCommands(checks) {
  return checks.flatMap((check) => {
    const npmScript = check.run.match(/^npm run ([\w:.-]+)$/);
    return npmScript ? expandNpmScript(npmScript[1]) : [check.run];
  });
}

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
    assert.ok(
      npmScript,
      `${check.id} must run an npm script so its definition lives in one place: ${check.run}`
    );
    assert.ok(
      packageScripts[npmScript[1]],
      `${check.id} runs npm script "${npmScript[1]}", which package.json does not define`
    );
    for (const file of scriptPathsIn(expandNpmScript(npmScript[1]))) {
      assert.ok(
        fs.existsSync(path.join(repoRoot, file)),
        `${check.id} runs a missing file: ${file}`
      );
    }
  }
});

runTest("validate:tests:fast is the runner, not a serial chain", () => {
  // The regression this prevents is the cheap one: someone adds a check by appending
  // `&& npm run test:x` to the npm script, and the registry -- which every assertion here reads --
  // silently stops describing what runs.
  assert.strictEqual(
    packageScripts["validate:tests:fast"],
    "node scripts/run-contract-tests.js",
    "validate:tests:fast must delegate to the registry runner"
  );
});

runTest("the heavy hook regressions stay out of the fast aggregate", () => {
  // They synthesize whole repositories and run the hooks against them; they are minutes, not
  // seconds, and `validate:tests` runs them separately for that reason. The chain made this
  // readable at a glance, so it has to be asserted now that the chain is gone.
  const heavy = [
    "test:agent-preflight",
    "test:precommit-integration",
    "test:pre-push-changed-files"
  ];
  const registered = new Set(CHECKS.map((check) => check.run));
  for (const script of heavy) {
    assert.ok(
      !registered.has(`npm run ${script}`),
      `${script} belongs in validate:tests:hook-regressions, not the fast registry`
    );
  }
  assert.strictEqual(
    packageScripts["validate:tests:hook-regressions"],
    heavy.map((script) => `npm run ${script}`).join(" && "),
    "the hook regressions must still be their own aggregate"
  );
});

runTest("runChecks runs every check even after one fails", async () => {
  // The property that distinguishes a registry from `&&`, asserted rather than inferred.
  const results = await runChecks(
    [
      { id: "ok-first", name: "ok first", run: "true" },
      { id: "boom", name: "boom", run: "exit 3" },
      { id: "ok-last", name: "ok last", run: "true" }
    ],
    2
  );
  assert.deepStrictEqual(
    results.map((result) => `${result.id}:${result.ok}`),
    ["ok-first:true", "boom:false", "ok-last:true"],
    "a failure must not stop the checks after it, and results stay in registry order"
  );
});

runTest("--only with an unknown id fails instead of passing vacuously", () => {
  const result = spawnSync(process.execPath, [runnerPath, "--only", "not-a-real-check"], {
    cwd: repoRoot,
    encoding: "utf8"
  });
  assert.notStrictEqual(result.status, 0, "an unknown id must fail");
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
  assert.deepStrictEqual(
    result.stdout.trim().split(/\r?\n/).filter(Boolean).sort(),
    CHECKS.map((check) => check.id).sort(),
    "--list output must match the registry exactly"
  );
});

runTest("no contract test in scripts/tests/ has been left unreachable", () => {
  // The same shape as the linter-orphan assertion in test-run-repo-lint.js, for the same reason:
  // consolidation trades a loud failure (a chain that visibly lost an entry) for a quiet one (a
  // suite that still exists and no longer runs). The excuse is a PREDICATE, not prose.
  const runElsewhere = new Map([
    ["scripts/tests/test-lint-skill-sizes.ps1", "workflow"],
    ["scripts/tests/test-llm-instructions-lint.ps1", "workflow"],
    ["scripts/tests/test-sync-issue-template-versions.ps1", "workflow"]
  ]);
  const owners = new Map([
    ["workflow", (file) => filesInvoking(".github/workflows", file).length > 0]
  ]);

  const reachable = new Set([
    ...scriptPathsIn(registryLeafCommands(CHECKS)),
    ...scriptPathsIn(registryLeafCommands(repoLintChecks)),
    ...scriptPathsIn(expandNpmScript("validate:tests:hook-regressions")),
    ...scriptPathsIn(expandNpmScript("validate:local")),
    ...scriptPathsIn(expandNpmScript("typecheck:unity"))
  ]);

  const suites = fs
    .readdirSync(path.join(repoRoot, "scripts", "tests"))
    .filter((name) => /^test-.*\.(?:ps1|sh|js|mjs)$/.test(name))
    .map((name) => `scripts/tests/${name}`)
    .sort();

  const orphans = suites.filter((file) => !reachable.has(file) && !runElsewhere.has(file));
  assert.deepStrictEqual(
    orphans,
    [],
    `these contract tests exist but nothing runs them -- add them to CHECKS in ` +
      `scripts/run-contract-tests.js, or to runElsewhere here with the kind of owner that does: ` +
      `${orphans.join(", ")}`
  );

  const stale = [...runElsewhere.keys()].filter(
    (file) => !fs.existsSync(path.join(repoRoot, file))
  );
  assert.deepStrictEqual(
    stale,
    [],
    `allowlist names files that no longer exist: ${stale.join(", ")}`
  );

  const unfounded = [...runElsewhere.entries()]
    .filter(([file, kind]) => {
      const owner = owners.get(kind);
      return !owner || !owner(file);
    })
    .map(([file, kind]) => `${file} (claimed: ${kind})`);
  assert.deepStrictEqual(
    unfounded,
    [],
    `these contract tests are excused by an owner that does not run them: ${unfounded.join(", ")}`
  );
});

if (require.main === module) {
  console.log("Testing scripts/run-contract-tests.js...\n");

  runQueuedTests().then(() => {
    console.log(`\n${passed} passed, ${failed} failed`);
    if (failed > 0) {
      console.log(`Failed: ${failedTests.join(", ")}`);
      process.exit(1);
    }
    process.exit(0);
  });
}
