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
    for (const match of command.matchAll(/scripts\/[\w./-]+\.(?:ps1|sh|js|mjs)/g)) {
      found.add(match[0]);
    }
  }
  return found;
}

/**
 * Leaf commands a registry ultimately executes, npm indirection resolved.
 *
 * @param {{id: string, name: string, run: string}[]} checks Registry entries.
 * @returns {string[]} The shell commands they bottom out in.
 */
function leafCommands(checks) {
  return checks.flatMap((check) => {
    const npmScript = check.run.match(/^npm run ([\w:.-]+)$/);
    return npmScript ? expandNpmScript(npmScript[1]) : [check.run];
  });
}

/**
 * A file's text with everything that MENTIONS a script but does not RUN it removed.
 *
 * Every removal here is a shape that produced a false "this owner runs it":
 * - Comments, whole-line and trailing. `llm-instructions-lint.yml` names
 *   `scripts/lint-llm-instructions.ps1` twice in `paths:` filters before it ever runs it.
 * - `paths:` / `paths-ignore:` / `files:` keys, including a flow sequence written on the same line
 *   (`paths: ["scripts/x.ps1"]`), and the bare list entries under them.
 * - Documentation keys (`name:`, `description:`, `title:`), which are prose about the step.
 *
 * @param {string} relativePath Repo-relative file.
 * @returns {string} Text with mentions dropped and invocations kept.
 */
function invocationText(relativePath) {
  const full = path.join(repoRoot, relativePath);
  if (!fs.existsSync(full) || !fs.statSync(full).isFile()) {
    return "";
  }
  return fs
    .readFileSync(full, "utf8")
    .split(/\r?\n/)
    .map((line) => line.replace(/\s+#.*$/, ""))
    .filter((line) => !/^\s*#/.test(line))
    .filter((line) => !/^\s*-?\s*(?:paths|paths-ignore|files|exclude)\s*:/.test(line))
    .filter((line) => !/^\s*-?\s*(?:name|description|title|summary)\s*:/.test(line))
    .filter((line) => !/^\s*-\s*["']?!?[^:\s"']+["']?\s*$/.test(line))
    .join("\n");
}

/**
 * @param {string} file Repo-relative script path.
 * @returns {RegExp} Matches the path as a token, not as a suffix of a longer one.
 */
function invocationPattern(file) {
  const escaped = file.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  // The leading class excludes `/`, so `tools/scripts/lint-foo.ps1` does not answer for
  // `scripts/lint-foo.ps1`; the trailing lookahead excludes a longer file name.
  return new RegExp(`(?:^|[^\\w/.-])(?:\\./)?${escaped}(?![\\w.-])`, "m");
}

/**
 * @param {string} containerRelativePath The file that might run it.
 * @param {string} file Repo-relative script path.
 * @returns {boolean} True when the container invokes it, directly or via an npm script.
 */
function fileInvokes(containerRelativePath, file) {
  const text = invocationText(containerRelativePath);
  if (!text) {
    return false;
  }
  if (invocationPattern(file).test(text)) {
    return true;
  }
  for (const match of text.matchAll(/npm run ([\w:.-]+)/g)) {
    if (scriptPathsIn(expandNpmScript(match[1])).has(file)) {
      return true;
    }
  }
  return false;
}

/**
 * @param {string} dir Repo-relative directory to scan, recursively.
 * @param {string} file Repo-relative script path.
 * @returns {string[]} The files under `dir` that invoke it.
 */
function filesInvoking(dir, file) {
  const full = path.join(repoRoot, dir);
  if (!fs.existsSync(full)) {
    return [];
  }
  const found = [];
  for (const entry of fs.readdirSync(full, { withFileTypes: true })) {
    const candidate = `${dir}/${entry.name}`;
    if (entry.isDirectory()) {
      found.push(...filesInvoking(candidate, file));
    } else if (fileInvokes(candidate, file)) {
      found.push(candidate);
    }
  }
  return found;
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
  // Linters that legitimately do not belong in Repo Lint. The value is the KIND of owner that runs
  // it, and every kind is a PREDICATE evaluated below.
  //
  // It used to be free text naming a workflow, and nothing ever read it (#445). Three of the seven
  // claims were false when that was checked: one named a workflow that runs CSharpier directly and
  // never touches the linter, one named pre-push for a script only an npm aggregate runs, and two
  // said "git hooks" when the hook that runs them was the pre-commit FRAMEWORK's config rather than
  // anything in .githooks/. Those last two are gone with the config that was their only caller
  // (#453). An allowlist whose entries are prose excuses whatever is put in it -- the quiet half of
  // #445, one level up: the thing named still exists, so nothing goes red.
  const runElsewhere = new Map([
    ["scripts/lint-llm-instructions.ps1", "workflow"],
    ["scripts/lint-skill-sizes.ps1", "workflow"],
    ["scripts/lint-unity-test-modules.ps1", "workflow"],
    ["scripts/lint-csharp-format.ps1", "preflight"],
    ["scripts/validate-git-push-config.ps1", "prepush"]
  ]);

  // Only the kinds something actually claims. A registered predicate nothing uses is a dead branch
  // inside the one test whose whole point is that a claim has to be executable.
  const owners = new Map([
    ["workflow", (file) => filesInvoking(".github/workflows", file).length > 0],
    ["preflight", (file) => fileInvokes("scripts/agent-preflight.ps1", file)],
    ["prepush", (file) => scriptPathsIn(expandNpmScript("validate:prepush")).has(file)]
  ]);

  const reachable = new Set([
    ...scriptPathsIn(leafCommands(CHECKS)),
    // Local Gates runs these two aggregates; anything they already cover need not be repeated.
    // `validate:tests` reaches its fast half through a sibling REGISTRY rather than a chain of npm
    // scripts (#505), so expanding the npm script alone stops at `node scripts/run-contract-tests.js`
    // and every check behind it reads as an orphan.
    ...scriptPathsIn(
      leafCommands(require(path.join(repoRoot, "scripts", "run-contract-tests.js")).CHECKS)
    ),
    ...scriptPathsIn(expandNpmScript("validate:tests")),
    ...scriptPathsIn(expandNpmScript("typecheck:unity"))
  ]);

  const linters = fs
    .readdirSync(path.join(repoRoot, "scripts"))
    .filter((name) => /^(?:lint|validate)-.*\.(?:ps1|sh|js|mjs)$/.test(name))
    .map((name) => `scripts/${name}`)
    .sort();

  const orphans = linters.filter((file) => !reachable.has(file) && !runElsewhere.has(file));
  assert.deepStrictEqual(
    orphans,
    [],
    `these linters exist but nothing runs them -- add them to CHECKS in scripts/run-repo-lint.js, ` +
      `or to runElsewhere here with the kind of owner that does: ${orphans.join(", ")}`
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

  // And every excuse must be true. This is the assertion the free-text version could not make.
  const unfounded = [...runElsewhere.entries()]
    .filter(([file, kind]) => {
      const owner = owners.get(kind);
      return !owner || !owner(file);
    })
    .map(([file, kind]) => `${file} (claimed: ${kind})`);
  assert.deepStrictEqual(
    unfounded,
    [],
    `these linters are excused from Repo Lint by an owner that does not run them -- either wire ` +
      `them up, correct the kind, or delete the linter: ${unfounded.join(", ")}`
  );
});

runTest("no linter in scripts/ has been left unfalsifiable", () => {
  // One level up from the assertion above. That one asks whether a linter RUNS; this one asks
  // whether it can FAIL. For a scanner over a corpus that is clean by construction those are
  // different questions, and only the first was being asked: session 221 hit three gates that
  // reported success while checking nothing, all three by accident, because "found nothing" and
  // "looked at nothing" print the same thing (#556).
  //
  // The property checked is that a linter has a self-test which INVOKES it and which some
  // registered suite runs. A stronger-sounding "the self-test names a red half" was tried and
  // dropped: every one of the 32 self-tests that exist already matches that vocabulary, so it
  // discriminates nothing -- adding it would have been one more check that cannot go red, inside
  // the test whose whole subject is checks that cannot go red.
  //
  // Debt is carried here rather than hidden: each entry names the issue tracking the missing red
  // half, so the allowlist is a work list rather than an excuse.
  const missingRedHalf = new Map([
    ["scripts/lint-bundled-assemblies.ps1", "#562"],
    ["scripts/lint-doc-counts.ps1", "#562"],
    ["scripts/lint-unity-test-modules.ps1", "#562"],
    ["scripts/validate-devcontainer-config.ps1", "#562"],
    ["scripts/validate-github-pages-css.sh", "#562"],
    ["scripts/validate-hook-permissions.sh", "#562"],
    ["scripts/validate-hook-sync-calls.ps1", "#562"]
  ]);

  // This file and its sibling are REGISTRIES: they name linters in allowlists rather than run
  // them, so scanning them for a mention counts an excuse as coverage. The first draft did, and
  // every entry in the list above came back "settled" purely because the list itself names it --
  // and `lint-unity-test-modules.ps1` read as covered because the runElsewhere map above mentions
  // it. That is #556's own shape, inside the check written for #556; it was caught only because
  // the settled assertion below is a red half for the allowlist.
  const registries = new Set(["test-run-repo-lint.js", "test-run-contract-tests.js"]);

  const testsDirectory = path.join(repoRoot, "scripts", "tests");
  const selfTests = fs
    .readdirSync(testsDirectory)
    .filter((name) => /\.(?:ps1|sh|js|mjs)$/.test(name) && !registries.has(name));
  const selfTestSource = new Map(
    selfTests.map((name) => [
      `scripts/tests/${name}`,
      fs.readFileSync(path.join(testsDirectory, name), "utf8")
    ])
  );

  // A self-test nothing runs is worth exactly as much as one that cannot fail, so reachability is
  // part of the property rather than a separate assertion.
  const reachableTests = new Set([
    ...scriptPathsIn(
      leafCommands(require(path.join(repoRoot, "scripts", "run-contract-tests.js")).CHECKS)
    ),
    ...scriptPathsIn(leafCommands(CHECKS)),
    ...scriptPathsIn(expandNpmScript("validate:tests"))
  ]);

  const linters = fs
    .readdirSync(path.join(repoRoot, "scripts"))
    .filter((name) => /^(?:lint|validate)-.*\.(?:ps1|sh|js|mjs)$/.test(name))
    .sort();

  const unfalsifiable = [];
  for (const linter of linters) {
    const relative = `scripts/${linter}`;
    if (missingRedHalf.has(relative)) {
      continue;
    }
    const covering = [...selfTestSource.entries()]
      .filter(([, source]) => source.includes(linter))
      .map(([file]) => file);
    if (covering.length === 0) {
      unfalsifiable.push(`${relative} (no self-test invokes it)`);
      continue;
    }
    if (!covering.some((file) => reachableTests.has(file))) {
      unfalsifiable.push(
        `${relative} (self-test exists but no suite runs it: ${covering.join(", ")})`
      );
    }
  }

  assert.deepStrictEqual(
    unfalsifiable,
    [],
    `these linters have no reachable self-test that can make them report, so a green run of them ` +
      `is not evidence they still fire -- add one under scripts/tests/ and register it in ` +
      `scripts/run-contract-tests.js: ${unfalsifiable.join(", ")}`
  );

  // Same rule as the allowlist above: an entry that outlives its file excuses nothing.
  const staleDebt = [...missingRedHalf.keys()].filter(
    (file) => !fs.existsSync(path.join(repoRoot, file))
  );
  assert.deepStrictEqual(
    staleDebt,
    [],
    `the missing-red-half list names files that no longer exist: ${staleDebt.join(", ")}`
  );

  // And an entry that has since GROWN a self-test must leave, or the list quietly re-excuses a
  // linter that is already covered and the next reader believes the debt is larger than it is.
  const settled = [...missingRedHalf.keys()].filter((file) => {
    const linter = path.basename(file);
    return [...selfTestSource.entries()].some(
      ([test, source]) => source.includes(linter) && reachableTests.has(test)
    );
  });
  assert.deepStrictEqual(
    settled,
    [],
    `these linters now have a reachable self-test and must come off the missing-red-half list: ` +
      `${settled.join(", ")}`
  );

  // Every excuse points at a filed issue, so the debt is trackable rather than folklore.
  const untracked = [...missingRedHalf.entries()]
    .filter(([, issue]) => !/^#[0-9]+$/.test(issue))
    .map(([file, issue]) => `${file} (claimed: ${issue})`);
  assert.deepStrictEqual(
    untracked,
    [],
    `every missing-red-half entry must name the issue tracking it: ${untracked.join(", ")}`
  );
});

// Exported so scripts/tests/test-run-contract-tests.js can make the same "is this claim true"
// assertions against the sibling registry without a second copy of the mention-stripping rules --
// every one of which exists because a shape produced a false "this owner runs it".
module.exports = { expandNpmScript, scriptPathsIn, leafCommands, fileInvokes, filesInvoking };

if (require.main === module) {
  console.log("Testing scripts/run-repo-lint.js...\n");

  runQueuedTests().then(() => {
    console.log(`\n${passed} passed, ${failed} failed`);
    if (failed > 0) {
      console.log(`Failed: ${failedTests.join(", ")}`);
      process.exit(1);
    }
    process.exit(0);
  });
}
