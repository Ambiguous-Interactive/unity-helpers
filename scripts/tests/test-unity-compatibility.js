"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const yaml = require("yaml");
const { generatedWorkflow, target } = require("../unity/generate-compatibility-workflow");
const { resolveCompatibilityMatrix, latestVersion } = require("../unity/resolve-test-matrix");
const { requireCompatibility } = require("../release-tools/require-native-unity-export");
const root = path.resolve(__dirname, "../..");
const versions = require("../../.github/unity-versions.json").all;
const sha = "a".repeat(40);
const repository = "Ambiguous-Interactive/unity-helpers";

test("compatibility is generated from the canonical licensed jobs and has separate reporting", async () => {
  const generated = await generatedWorkflow(root);
  assert.equal(fs.readFileSync(path.join(root, target), "utf8"), generated);
  const compatibility = yaml.parse(generated);
  const canonical = yaml.parse(
    fs.readFileSync(path.join(root, ".github/workflows/unity-tests.yml"), "utf8")
  );
  assert.deepEqual(Object.keys(compatibility.on), ["push", "workflow_dispatch"]);
  assert.deepEqual(compatibility.on.push.branches, ["main"]);
  assert.equal(compatibility.concurrency.group, "unity-compatibility-${{ github.sha }}");

  assert.equal(compatibility.jobs["unity-tests"].strategy["max-parallel"], 1);
  assert.equal(compatibility.jobs["unity-tests-aggregate"].name, "Unity Compatibility Success");
  assert.equal(compatibility.jobs["unity-tests-aggregate"].if, "${{ always() }}");
  assert.deepEqual(compatibility.jobs["unity-tests-aggregate"].needs, [
    "runner-preflight",
    "unity-tests"
  ]);
  assert.equal(compatibility.jobs["runner-preflight"].if, canonical.jobs["runner-preflight"].if);
  assert.equal(
    compatibility.jobs["runner-preflight"].concurrency.group,
    "${{ github.workflow }}-runner-preflight-${{ github.sha }}"
  );
  assert.deepEqual(compatibility.jobs["unity-tests"]["runs-on"], [
    "self-hosted",
    "Windows",
    "RAM-64GB",
    "DAD-MACHINE"
  ]);
  const normal = canonical.jobs["unity-tests"];
  const older = compatibility.jobs["unity-tests"];
  for (const name of [
    "Acquire organization Unity lock",
    "Return Unity license",
    "Classify Unity cleanup evidence",
    "Release organization Unity lock",
    "Require confirmed Unity cleanup",
    "Require every selected Unity mode"
  ]) {
    assert.deepEqual(
      older.steps.find((step) => step.name === name),
      normal.steps.find((step) => step.name === name)
    );
  }
  for (const id of ["matrix-config", "unity-tests"]) {
    assert.match(compatibility.jobs[id].if, /github.ref_protected/);
    assert.match(compatibility.jobs[id].if, /github.ref == 'refs\/heads\/main'/);
  }
  assert.ok(!Object.values(compatibility.jobs).some((job) => job.name === "Unity CI Success"));
});

test("the fast/compatibility split covers each supported editor once", () => {
  const latest = latestVersion(versions);
  const older = resolveCompatibilityMatrix(versions);
  assert.deepEqual(older["unity-versions"], versions.slice(0, -1));
  assert.deepEqual(older["unity-exclusions"], [{ "unity-version": latest }]);
  assert.deepEqual(older["test-modes"], ["editmode", "playmode", "standalone"]);
  assert.throws(() => latestVersion(["2022.3.45f1"]), /6.x/);
  assert.throws(() => resolveCompatibilityMatrix([latest]), /older editor/);
});

test("push selects latest and manual retains full qualification", () => {
  const os = require("node:os");
  const scratch = fs.mkdtempSync(path.join(os.tmpdir(), "unity-compatibility-"));
  try {
    for (const [eventName, coverage, expected] of [
      ["push", "", [versions.at(-1)]],
      ["workflow_dispatch", "", versions],
      ["workflow_dispatch", "compatibility", versions.slice(0, -1)]
    ]) {
      const output = path.join(scratch, `${eventName}-${coverage}`);
      const env = {
        ...process.env,
        GITHUB_EVENT_NAME: eventName,
        INPUT_UNITY_COVERAGE: coverage,
        INPUT_UNITY_VERSION: "",
        INPUT_TEST_MODE: "all",
        INPUT_ACCEPTANCE: "none",
        INPUT_EXPECTED_SOURCE_SHA: "",
        GITHUB_OUTPUT: output,
        GITHUB_STEP_SUMMARY: ""
      };
      const result = spawnSync(
        process.execPath,
        [path.join(root, "scripts/unity/resolve-test-matrix.js")],
        { env, encoding: "utf8" }
      );
      assert.equal(result.status, 0, result.stderr);
      assert.match(
        fs.readFileSync(output, "utf8"),
        new RegExp(
          `unity-versions=${JSON.stringify(expected).replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}`
        )
      );
    }
  } finally {
    fs.rmSync(scratch, { recursive: true, force: true });
  }
});

test("release requires every older version and the aggregate in the latest exact-source attempt", async () => {
  const run = {
    id: 3,
    run_attempt: 2,
    head_sha: sha,
    repository: { full_name: repository },
    head_repository: { full_name: repository },
    path: ".github/workflows/unity-compatibility.yml",
    event: "workflow_dispatch",
    head_branch: "main",
    status: "completed",
    conclusion: "success"
  };
  const jobs = [
    ...versions.slice(0, -1).map((version) => `Compatibility Unity ${version} (selected modes)`),
    "Unity Compatibility Success"
  ].map((name) => ({
    name,
    run_id: 3,
    run_attempt: 2,
    status: "completed",
    conclusion: "success"
  }));
  const probe = (runs, currentJobs) =>
    requireCompatibility(repository, sha, async (endpoint) =>
      endpoint.includes("/workflows/") ? { workflow_runs: runs } : { jobs: currentJobs }
    );
  assert.equal(await probe([run], jobs), 3);
  for (const conclusion of ["failure", "skipped", "cancelled", null]) {
    await assert.rejects(probe([{ ...run, conclusion }], jobs), /not completed successfully/);
    for (let index = 0; index < jobs.length; ++index)
      await assert.rejects(
        probe(
          [run],
          jobs.map((job, i) => (i === index ? { ...job, conclusion } : job))
        ),
        /one successful/
      );
  }
  for (let index = 0; index < jobs.length; ++index) {
    await assert.rejects(
      probe(
        [run],
        jobs.filter((_, i) => i !== index)
      ),
      /one successful/
    );
    await assert.rejects(probe([run], [...jobs, jobs[index]]), /one successful/);
    await assert.rejects(
      probe(
        [run],
        jobs.map((job, i) => (i === index ? { ...job, run_attempt: 1 } : job))
      ),
      /one successful/
    );
  }
  for (const changes of [
    { head_sha: "b".repeat(40) },
    { head_branch: "feature" },
    { event: "pull_request" },
    { head_repository: { full_name: "outside/repo" } },
    { status: "in_progress" }
  ])
    await assert.rejects(probe([{ ...run, ...changes }], jobs), /not completed successfully/);
  await assert.rejects(
    probe([run, { ...run, id: 4, conclusion: "failure" }], jobs),
    /not completed successfully/
  );
});

test("ordinary manual qualification uses concurrent modes while diagnostic and acceptance captures remain sequential", () => {
  const canonical = yaml.parse(
    fs.readFileSync(path.join(root, ".github/workflows/unity-tests.yml"), "utf8")
  );
  for (const id of ["unity-tests", "unity-tests-single-threaded"]) {
    const step = canonical.jobs[id].steps.find((candidate) => candidate.id === "run_modes");
    const body = step.if
      .replace(/^\$\{\{\s*|\s*\}\}$/g, "")
      .replace(/\.([A-Za-z_][A-Za-z0-9_]*-[A-Za-z0-9_-]+)/g, '["$1"]');
    const evaluate = new Function("github", "inputs", "steps", "cancelled", `return (${body});`);
    const inputs = {
      acceptance: "none",
      "fresh-conversion": false,
      "native-diagnostics": false,
      "native-loader-snaps-comparison": false
    };
    const steps = { unity_lock: { outputs: { acquired: "true" } } };
    assert.equal(
      evaluate({ event_name: "workflow_dispatch" }, inputs, steps, () => false),
      true
    );
    for (const changes of [
      { acceptance: "all" },
      { acceptance: "wproto" },
      { "fresh-conversion": true },
      { "native-diagnostics": true },
      { "native-loader-snaps-comparison": true }
    ])
      assert.equal(
        evaluate(
          { event_name: "workflow_dispatch" },
          { ...inputs, ...changes },
          steps,
          () => false
        ),
        false
      );
    assert.equal(
      evaluate({ event_name: "pull_request" }, {}, steps, () => false),
      true
    );
    assert.equal(
      evaluate({ event_name: "push" }, {}, steps, () => false),
      true
    );
    assert.equal(
      evaluate({ event_name: "push" }, {}, steps, () => true),
      false
    );
    assert.equal(
      evaluate(
        { event_name: "push" },
        {},
        { unity_lock: { outputs: { acquired: "false" } } },
        () => false
      ),
      false
    );
  }
});
