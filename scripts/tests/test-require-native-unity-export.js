"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { requireNativeExport } = require("../release-tools/require-native-unity-export");

const repository = "Ambiguous-Interactive/unity-helpers";
const sourceSha = "a".repeat(40);
const run = {
  id: 100,
  run_attempt: 2,
  head_sha: sourceSha,
  repository: { full_name: repository },
  head_repository: { full_name: repository },
  path: ".github/workflows/unity-tests.yml"
};
const stepNames = [
  "Acquire organization Unity lock",
  "Export Unity package smoke artifact",
  "Return Unity license",
  "Classify Unity cleanup evidence",
  "Release organization Unity lock",
  "Require confirmed Unity cleanup"
];
const job = {
  id: 200,
  name: "Unity package export smoke",
  run_id: run.id,
  run_attempt: run.run_attempt,
  status: "completed",
  conclusion: "success",
  steps: stepNames.map((name, index) => ({
    name,
    number: index + 1,
    status: "completed",
    conclusion: "success"
  }))
};

function copy(value) {
  return JSON.parse(JSON.stringify(value));
}

async function probe(runs = [copy(run)], jobs = [copy(job)]) {
  return requireNativeExport(repository, sourceSha, async (endpoint) => {
    if (endpoint.includes("/workflows/")) {
      assert.ok(endpoint.includes(`head_sha=${sourceSha}`));
      return { workflow_runs: runs };
    }
    assert.match(endpoint, /\/actions\/runs\/100\/jobs\?filter=latest/);
    return { jobs };
  });
}

async function main() {
  assert.deepEqual(await probe(), { runId: 100, jobId: 200, sourceSha });
  await assert.rejects(probe([]), /No trusted Unity Tests run/);
  await assert.rejects(probe([copy(run)], []), /Exactly one native/);
  await assert.rejects(probe([copy(run)], [copy(job), copy(job)]), /Exactly one native/);

  for (const mutate of [
    (value) => (value.head_sha = "b".repeat(40)),
    (value) => (value.repository.full_name = "outside/repository"),
    (value) => (value.head_repository.full_name = "outside/fork"),
    (value) => (value.path = ".github/workflows/unrelated.yml"),
    (value) => (value.id = 0),
    (value) => (value.run_attempt = 0)
  ]) {
    const invalid = copy(run);
    mutate(invalid);
    await assert.rejects(probe([invalid]), /No trusted Unity Tests run/);
  }

  for (const mutate of [
    (value) => (value.id = 0),
    (value) => (value.run_id = 99),
    (value) => (value.run_attempt = 1),
    (value) => (value.status = "in_progress"),
    (value) => (value.conclusion = "failure"),
    (value) => (value.conclusion = "cancelled"),
    (value) => (value.conclusion = "skipped"),
    (value) => (value.steps = null)
  ]) {
    const invalid = copy(job);
    mutate(invalid);
    await assert.rejects(probe([copy(run)], [invalid]), /has not completed successfully/);
  }

  for (let index = 0; index < stepNames.length; ++index) {
    for (const conclusion of ["failure", "skipped", "cancelled", null]) {
      const invalid = copy(job);
      invalid.steps[index].conclusion = conclusion;
      await assert.rejects(probe([copy(run)], [invalid]), /successful ordered/);
    }
    const missing = copy(job);
    missing.steps.splice(index, 1);
    await assert.rejects(probe([copy(run)], [missing]), /successful ordered/);
    const duplicate = copy(job);
    duplicate.steps.push(copy(duplicate.steps[index]));
    await assert.rejects(probe([copy(run)], [duplicate]), /successful ordered/);
  }
  const unordered = copy(job);
  unordered.steps[2].number = unordered.steps[1].number;
  await assert.rejects(probe([copy(run)], [unordered]), /successful ordered/);

  await assert.rejects(
    requireNativeExport(repository, sourceSha, async () => ({})),
    /no workflow_runs array/
  );
  await assert.rejects(
    requireNativeExport(repository, sourceSha, async () => ({ workflow_runs: [] })),
    /No trusted/
  );
  for (const invalidSha of ["", "short", "A".repeat(40), `${sourceSha}\n`]) {
    await assert.rejects(requireNativeExport(repository, invalidSha, null), /source commit SHA/);
  }
  await assert.rejects(requireNativeExport("outside\n/repository", sourceSha, null), /owner\/name/);

  const older = { ...copy(run), id: 99 };
  await assert.deepEqual(await probe([older, copy(run)]), { runId: 100, jobId: 200, sourceSha });
  const latestFailure = { ...copy(job), conclusion: "failure" };
  await assert.rejects(
    probe([older, copy(run)], [latestFailure]),
    /has not completed successfully/
  );

  let calls = 0;
  await requireNativeExport(repository, sourceSha, async (endpoint) => {
    ++calls;
    const query = new URL(endpoint, "https://api.github.com").searchParams;
    if (endpoint.includes("/workflows/")) {
      if (query.get("page") === "1") {
        return { workflow_runs: Array.from({ length: 100 }, () => copy(older)) };
      }
      return { workflow_runs: [copy(run)] };
    }
    return { jobs: [copy(job)] };
  });
  assert.equal(
    calls,
    3,
    "The gate must inspect the second page instead of accepting stale evidence."
  );

  const workflow = fs.readFileSync(
    path.join(__dirname, "../../.github/workflows/release.yml"),
    "utf8"
  );
  const nativeGate = workflow.match(/^  native-validation:\n([\s\S]*?)(?=^  [a-z][a-z-]+:)/m);
  assert.ok(nativeGate, "Release publishing requires an independent native validation gate.");
  assert.match(nativeGate[1], /if: \$\{\{ !inputs\.export_only \}\}/);
  assert.match(nativeGate[1], /actions: read/);
  assert.match(nativeGate[1], /ref: \$\{\{ github\.workflow_sha \}\}/);
  assert.doesNotMatch(nativeGate[1], /ref: \$\{\{ needs\.verify-tag\.outputs\.source-sha \}\}/);
  assert.match(nativeGate[1], /SOURCE_SHA: \$\{\{ needs\.verify-tag\.outputs\.source-sha \}\}/);
  assert.match(nativeGate[1], /node scripts\/release-tools\/require-native-unity-export\.js/);
  for (const name of ["prepare-tag", "publish"]) {
    const block = workflow.match(
      new RegExp(`^  ${name}:\\n([\\s\\S]*?)(?=^  [a-z][a-z-]+:|(?![\\s\\S]))`, "m")
    );
    assert.ok(block);
    assert.match(block[1], /      - native-validation\n/);
    assert.match(block[1], /needs\.native-validation\.result == 'success'/);
  }
  console.log(
    "Native release validation controls passed, including empty, stale, failed, and incomplete evidence."
  );
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
