"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const {
  autoPublish,
  createApi,
  independentWorkflows,
  marker,
  releaseCandidate,
  requiredWorkflows,
  runTitle,
  validationState
} = require("../release-tools/auto-publish-release");

const repository = "owner/package";
const sha = "a".repeat(40);
const version = "1.2.3";
const event = {
  repository: { full_name: repository, default_branch: "main", fork: false },
  workflow_run: {
    event: "push",
    head_sha: sha,
    head_branch: "main",
    conclusion: "success",
    head_repository: { full_name: repository }
  }
};
const pull = {
  number: 42,
  merged_at: "2026-10-12",
  merge_commit_sha: sha,
  title: `release: ${version}`,
  body: `Prepared release\n\n${marker}\n`,
  base: { ref: "main", repo: { full_name: repository } },
  head: { ref: `release/${version}`, repo: { full_name: repository } }
};
const runs = requiredWorkflows.map((file, index) => ({
  id: index + 1,
  path: `.github/workflows/${file}`,
  event: "push",
  head_sha: sha,
  head_branch: "main",
  repository: { full_name: repository },
  status: "completed",
  conclusion: "success"
}));
const candidate = releaseCandidate(event, [pull], version, repository);
assert.deepEqual(candidate, { sha, version, branch: "main", pull: 42 });
const skippedEvent = structuredClone(event);
skippedEvent.workflow_run.conclusion = "skipped";
assert.deepEqual(releaseCandidate(skippedEvent, [pull], version, repository), candidate);
for (const mutate of [
  (e) => (e.repository.fork = true),
  (e) => (e.repository.full_name = "outside/package"),
  (e) => (e.workflow_run.event = "pull_request"),
  (e) => (e.workflow_run.head_branch = "feature"),
  (e) => (e.workflow_run.head_repository.full_name = "outside/package"),
  (e) => (e.workflow_run.head_sha = "--bad"),
  (e) => (e.workflow_run.conclusion = "failure")
]) {
  const changed = structuredClone(event);
  mutate(changed);
  assert.equal(releaseCandidate(changed, [pull], version, repository), null);
}
for (const mutate of [
  (p) => (p.merged_at = null),
  (p) => (p.merge_commit_sha = "b".repeat(40)),
  (p) => (p.title = "release: 1.2.4"),
  (p) => (p.body = marker + " extra"),
  (p) => (p.base.ref = "feature"),
  (p) => (p.base.repo.full_name = "outside/package"),
  (p) => (p.head.ref = "feature"),
  (p) => (p.head.repo.full_name = "outside/package")
]) {
  const changed = structuredClone(pull);
  mutate(changed);
  assert.equal(releaseCandidate(event, [changed], version, repository), null);
}
for (const invalid of [undefined, "", "v1.2.3", "01.2.3", "1.2", "1.2.3\n", "1.2.3-beta"])
  assert.equal(releaseCandidate(event, [pull], invalid, repository), null);
assert.equal(releaseCandidate(event, [pull, pull], version, repository), null);
assert.equal(validationState(runs, sha, "main", repository), "passed");
assert.equal(validationState([], sha, "main", repository), "waiting");
for (let index = 0; index < runs.length; ++index) {
  assert.equal(
    validationState(
      runs.filter((_, i) => i !== index),
      sha,
      "main",
      repository
    ),
    "waiting"
  );
  for (const conclusion of ["failure", "cancelled", "timed_out", "skipped", "neutral", null]) {
    const changed = structuredClone(runs);
    changed[index].conclusion = conclusion;
    assert.equal(validationState(changed, sha, "main", repository), "failed");
  }
  const changed = structuredClone(runs);
  changed[index].status = "queued";
  changed[index].conclusion = null;
  assert.equal(validationState(changed, sha, "main", repository), "waiting");
  changed[index].head_sha = "b".repeat(40);
  assert.equal(validationState(changed, sha, "main", repository), "waiting");
}
const extra = { ...runs[0], id: 100, path: ".github/workflows/validate-docs.yml" };
assert.equal(
  validationState([...runs, { ...extra, status: "in_progress" }], sha, "main", repository),
  "waiting"
);
assert.equal(
  validationState([...runs, { ...extra, conclusion: "failure" }], sha, "main", repository),
  "failed"
);
assert.equal(
  validationState([...runs, { ...extra, conclusion: "skipped" }], sha, "main", repository),
  "passed"
);
for (const file of independentWorkflows) {
  for (const [status, conclusion] of [
    ["completed", "cancelled"],
    ["completed", "failure"],
    ["in_progress", null]
  ]) {
    assert.equal(
      validationState(
        [...runs, { ...extra, path: `.github/workflows/${file}`, status, conclusion }],
        sha,
        "main",
        repository
      ),
      "passed"
    );
  }
}
assert.equal(
  validationState(
    [...runs, { ...extra, path: ".github/workflows/new-validation.yml", conclusion: "failure" }],
    sha,
    "main",
    repository
  ),
  "failed"
);
assert.equal(
  validationState(
    [...runs, { ...runs[0], id: 100, conclusion: "cancelled" }],
    sha,
    "main",
    repository
  ),
  "failed"
);
assert.equal(
  validationState(
    [...runs, { ...extra, event: "schedule", conclusion: "failure" }],
    sha,
    "main",
    repository
  ),
  "passed"
);

async function main() {
  function fixture({
    states = [runs],
    releases = [],
    existing = [],
    error,
    claimError,
    claimedSha = sha,
    invalidClaim = false
  } = {}) {
    const posts = [];
    let reads = 0;
    let sleeps = 0;
    return {
      posts,
      get sleeps() {
        return sleeps;
      },
      api: {
        async get(endpoint) {
          if (endpoint.startsWith("git/ref/"))
            return invalidClaim
              ? {}
              : { ref: `refs/tags/release-publish-claims/${version}`, object: { sha: claimedSha } };
          assert.equal(endpoint, `contents/package.json?ref=${sha}`);
          return {
            encoding: "base64",
            content: Buffer.from(JSON.stringify({ version })).toString("base64")
          };
        },
        async pages(endpoint) {
          if (endpoint === `commits/${sha}/pulls`) return [pull];
          if (endpoint.startsWith("actions/runs?"))
            return states[Math.min(reads++, states.length - 1)];
          if (endpoint === "releases") return releases;
          if (endpoint.startsWith("actions/workflows/release.yml/runs?")) return existing;
          throw new Error(`Unexpected endpoint ${endpoint}`);
        },
        async post(endpoint, body) {
          posts.push({ endpoint, body });
          if (endpoint === "git/refs") {
            if (claimError) throw claimError;
            return { ref: body.ref, object: { sha: body.sha } };
          }
          if (error) throw error;
        }
      },
      sleep: async (ms) => {
        assert.equal(ms, 15000);
        ++sleeps;
      }
    };
  }
  const accepted = fixture({ states: [[], runs] });
  assert.equal((await autoPublish({ event, repository, ...accepted })).status, "dispatched");
  assert.equal(accepted.sleeps, 1);
  const skippedCompletion = fixture();
  assert.equal(
    (await autoPublish({ event: skippedEvent, repository, ...skippedCompletion })).status,
    "dispatched"
  );
  assert.deepEqual(accepted.posts, [
    {
      endpoint: "git/refs",
      body: { ref: `refs/tags/release-publish-claims/${version}`, sha }
    },
    {
      endpoint: "actions/workflows/release.yml/dispatches",
      body: {
        ref: "main",
        inputs: { version, source_ref: sha, export_only: "false", allow_tag_recovery: "false" }
      }
    }
  ]);
  for (const [options, status] of [
    [{ releases: [{ tag_name: version }] }, "already-published"],
    [
      { existing: [{ id: 77, display_title: runTitle(candidate), conclusion: "failure" }] },
      "already-dispatched"
    ],
    [{ states: [[...runs, { ...extra, conclusion: "failure" }]] }, "validation-failed"]
  ]) {
    const control = fixture(options);
    assert.equal((await autoPublish({ event, repository, ...control })).status, status);
    assert.equal(control.posts.length, 0);
  }
  const exhausted = fixture({ states: [[]] });
  assert.equal(
    (await autoPublish({ event, repository, ...exhausted, polls: 2 })).status,
    "waiting-for-validation"
  );
  assert.equal(exhausted.posts.length, 0);
  const ambiguous = fixture({ error: new Error("dispatch response lost") });
  await assert.rejects(autoPublish({ event, repository, ...ambiguous }), /response lost/);
  assert.equal(ambiguous.posts.length, 2, "An uncertain mutation must never be retried.");
  for (const status of [409, 422]) {
    const claimed = fixture({ claimError: Object.assign(new Error("claim exists"), { status }) });
    assert.equal((await autoPublish({ event, repository, ...claimed })).status, "already-claimed");
    assert.equal(claimed.posts.length, 1);
  }
  const otherSource = fixture({
    claimError: Object.assign(new Error("claim exists"), { status: 422 }),
    claimedSha: "b".repeat(40)
  });
  assert.equal(
    (await autoPublish({ event, repository, ...otherSource })).claimedSha,
    "b".repeat(40)
  );
  assert.equal(otherSource.posts.length, 1);
  const invalidClaim = fixture({
    claimError: Object.assign(new Error("claim exists"), { status: 422 }),
    invalidClaim: true
  });
  await assert.rejects(autoPublish({ event, repository, ...invalidClaim }), /Invalid existing/);
  assert.equal(invalidClaim.posts.length, 1);
  for (const claimError of [
    new Error("claim response lost"),
    Object.assign(new Error("claim forbidden"), { status: 403 })
  ]) {
    const uncertain = fixture({ claimError });
    await assert.rejects(autoPublish({ event, repository, ...uncertain }), /claim/);
    assert.equal(uncertain.posts.length, 1);
  }
  const racing = fixture();
  let reserved = false;
  const originalPost = racing.api.post;
  racing.api.post = async (endpoint, body) => {
    if (endpoint === "git/refs") {
      if (reserved) throw Object.assign(new Error("claim exists"), { status: 422 });
      reserved = true;
    }
    return originalPost(endpoint, body);
  };
  const raceResults = await Promise.all([
    autoPublish({ event, repository, ...racing }),
    autoPublish({ event, repository, ...racing })
  ]);
  assert.deepEqual(raceResults.map((result) => result.status).sort(), [
    "already-claimed",
    "dispatched"
  ]);
  assert.equal(
    racing.posts.filter((post) => post.endpoint.endsWith("dispatches")).length,
    1,
    "Run listing lag must not permit duplicate dispatches."
  );
  const forbidden = structuredClone(event);
  forbidden.workflow_run.event = "pull_request";
  assert.equal(
    (
      await autoPublish({
        event: forbidden,
        repository,
        api: {
          pages() {
            throw new Error("must not read");
          }
        }
      })
    ).status,
    "ineligible"
  );
  let calls = 0;
  const api = createApi(repository, "fixture-token", async (url, options) => {
    assert.equal(options.redirect, "error");
    assert.equal(options.headers.Authorization, "Bearer fixture-token");
    ++calls;
    assert.ok(url.endsWith(`per_page=100&page=${calls}`));
    return {
      ok: true,
      status: 200,
      json: async () => ({ workflow_runs: calls === 1 ? Array(100).fill(runs[0]) : [runs[1]] })
    };
  });
  assert.equal((await api.pages("actions/runs?event=push", "workflow_runs")).length, 101);
  assert.equal(calls, 2);
  await assert.rejects(
    createApi(repository, "fixture-token", async () => ({ ok: false, status: 403 })).get(
      "releases"
    ),
    /HTTP 403/
  );
  await assert.rejects(
    createApi(repository, "fixture-token", async () => ({
      ok: true,
      status: 200,
      json: async () => ({})
    })).pages("releases"),
    /Invalid paginated/
  );
  assert.throws(() => createApi("outside/../../repo", "token"), /required/);
  assert.throws(() => createApi(repository, ""), /required/);
  const root = path.resolve(__dirname, "../..");
  const yaml = require("yaml");
  const workflow = yaml.parse(
    fs.readFileSync(path.join(root, ".github/workflows/release-auto-publish.yml"), "utf8")
  );
  assert.deepEqual(workflow.on.workflow_run.types, ["completed"]);
  const pushNames = fs
    .readdirSync(path.join(root, ".github/workflows"))
    .filter((file) => file.endsWith(".yml"))
    .map((file) => yaml.parse(fs.readFileSync(path.join(root, ".github/workflows", file), "utf8")))
    .filter((entry) => entry.on?.push)
    .map((entry) => entry.name);
  assert.deepEqual(
    [...workflow.on.workflow_run.workflows].sort(),
    pushNames.sort(),
    "Every push workflow completion must wake publication."
  );
  assert.equal(workflow.jobs.publish.permissions.contents, "write");
  for (const file of fs
    .readdirSync(path.join(root, ".github/workflows"))
    .filter((file) => file.endsWith(".yml") && !independentWorkflows.includes(file))) {
    const validation = yaml.parse(
      fs.readFileSync(path.join(root, ".github/workflows", file), "utf8")
    );
    if (!validation.on?.push) continue;
    assert.ok(validation.concurrency.group.includes("github.event_name == 'push' && github.sha"));
    assert.equal(
      validation.concurrency["cancel-in-progress"],
      file === "unity-tests.yml" ? true : "${{ github.event_name == 'pull_request' }}"
    );
    if (file === "unity-tests.yml") {
      const access = validation.jobs["runner-preflight"].concurrency;
      assert.ok(access.group.includes("github.event_name == 'push' && github.sha"));
      assert.equal(access["cancel-in-progress"], true);
    }
  }
  assert.equal(workflow.concurrency["cancel-in-progress"], false);
  assert.equal(
    workflow.concurrency.group,
    "release-auto-publish-${{ github.event.workflow_run.head_sha }}"
  );
  const checkout = workflow.jobs.publish.steps.find((step) =>
    step.uses?.startsWith("actions/checkout@")
  );
  assert.equal(checkout.with.ref, "${{ github.event.repository.default_branch }}");
  assert.equal(checkout.with["persist-credentials"], false);
  const publishWorkflow = yaml.parse(
    fs.readFileSync(path.join(root, ".github/workflows/release.yml"), "utf8")
  );
  assert.ok(publishWorkflow["run-name"].includes("inputs.export_only && 'Export' || 'Release'"));
  const prepare = yaml.parse(
    fs.readFileSync(path.join(root, ".github/workflows/release-prepare.yml"), "utf8")
  );
  assert.equal(prepare.on.workflow_dispatch.inputs.auto_publish.default, true);
  assert.ok(
    prepare.jobs.prepare.steps.some(
      (step) =>
        step.run?.includes(marker) && step.env?.AUTO_PUBLISH === "${{ inputs.auto_publish }}"
    )
  );
  console.log(
    "Automatic release publication: admission, exact-head validation, failure, idempotency, pagination, and workflow controls passed."
  );
}
main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
