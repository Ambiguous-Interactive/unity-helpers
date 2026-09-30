"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const yaml = require("yaml");

const repoRoot = path.resolve(__dirname, "../..");
const source = fs.readFileSync(
  path.join(repoRoot, ".github/workflows/schedule-unity-benchmarks.yml"),
  "utf8"
);
const workflow = yaml.parse(source);
const benchmark = yaml.parse(
  fs.readFileSync(path.join(repoRoot, ".github/workflows/unity-benchmarks.yml"), "utf8")
);
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;

assert.deepEqual(Object.keys(workflow.on), ["schedule"]);
assert.deepEqual(workflow.on.schedule, [{ cron: "29 10 * * 3" }]);
assert.deepEqual(Object.keys(benchmark.on), ["workflow_dispatch"]);
assert.deepEqual(workflow.permissions, {});
assert.deepEqual(Object.keys(workflow.jobs), ["dispatch"]);
assert.equal(workflow.concurrency["cancel-in-progress"], true);

const job = workflow.jobs.dispatch;
assert.equal(job["runs-on"], "ubuntu-latest");
assert.equal(job["timeout-minutes"], 5);
assert.deepEqual(job.permissions, { actions: "write" });
assert.equal(job.steps.length, 1);
const step = job.steps[0];
assert.equal(step.uses, "actions/github-script@3a2844b7e9c422d3c10d287c895573f7108da1b3");
assert.equal(step["continue-on-error"], undefined);
assert.equal(step.with["github-token"], undefined);
assert.doesNotMatch(source, /secrets\.|secrets\[|actions\/checkout@|self-hosted|\bUNITY_SERIAL\b/);
assert.doesNotMatch(step.with.script, /\$\{\{/);

const evaluate = new Function(
  "github",
  `return Boolean(${job.if.replace(/^\s*\$\{\{\s*|\s*\}\}\s*$/g, "")});`
);
function event(overrides = {}) {
  return {
    event_name: "schedule",
    repository: "Ambiguous-Interactive/unity-helpers",
    repository_id: "737391131",
    ref: "refs/heads/main",
    ref_protected: true,
    event: { schedule: "29 10 * * 3" },
    ...overrides
  };
}

async function runScheduledDispatch(githubEvent, dispatch) {
  if (!evaluate(githubEvent)) {
    return;
  }
  const [owner, repo] = githubEvent.repository.split("/");
  await new AsyncFunction("github", "context", step.with.script)(
    { rest: { actions: { createWorkflowDispatch: dispatch } } },
    { repo: { owner, repo } }
  );
}

async function main() {
  const calls = [];
  const dispatch = async (args) => {
    calls.push(args);
  };
  await runScheduledDispatch(event(), dispatch);
  assert.deepEqual(
    calls,
    [
      {
        owner: "Ambiguous-Interactive",
        repo: "unity-helpers",
        workflow_id: "unity-benchmarks.yml",
        ref: "main"
      }
    ],
    "The scheduled canonical protected branch must dispatch once"
  );
  await runScheduledDispatch(event({ repository: "new-owner/renamed-package" }), dispatch);
  assert.deepEqual(
    calls[1],
    {
      owner: "new-owner",
      repo: "renamed-package",
      workflow_id: "unity-benchmarks.yml",
      ref: "main"
    },
    "The same repository ID after rename or transfer must dispatch to its current context"
  );
  assert.equal(calls.length, 2);
  await runScheduledDispatch(event({ repository_id: 737391131 }), dispatch);
  assert.deepEqual(
    calls[2],
    calls[0],
    "Numeric repository ID coercion must retain the canonical dispatch"
  );
  assert.equal(calls.length, 3);

  const denied = [
    ["push", event({ event_name: "push" })],
    ["pull request", event({ event_name: "pull_request", ref: "refs/pull/42/merge" })],
    ["manual", event({ event_name: "workflow_dispatch" })],
    ["missing event name", event({ event_name: undefined })],
    [
      "other repository ID",
      event({ repository: "Ambiguous-Interactive/another-project", repository_id: "123" })
    ],
    ["wrong numeric repository ID", event({ repository_id: 123 })],
    ["missing repository ID", event({ repository_id: undefined })],
    ["outside fork", event({ repository: "outsider/unity-helpers", repository_id: "456" })],
    ["changed default branch", event({ ref: "refs/heads/release" })],
    ["feature branch", event({ ref: "refs/heads/feature" })],
    ["tag", event({ ref: "refs/tags/main" })],
    ["missing ref", event({ ref: undefined })],
    ["unprotected branch", event({ ref_protected: false })],
    ["missing protection evidence", event({ ref_protected: undefined })]
  ];
  for (const [name, input] of denied) {
    assert.equal(evaluate(input), false, `${name} must be denied`);
    await runScheduledDispatch(input, dispatch);
    assert.equal(calls.length, 3, `${name} must never call the API`);
  }

  const deniedError = new Error("Dispatch access denied");
  await assert.rejects(
    runScheduledDispatch(event(), async () => {
      throw deniedError;
    }),
    (error) => error === deniedError,
    "A rejected dispatch must fail the scheduler"
  );
  console.log(
    `Scheduled benchmark workflow: minimal schedule payload, string/numeric IDs, renamed dispatch and ${denied.length} denied inputs passed.`
  );
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
