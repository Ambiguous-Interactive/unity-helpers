"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const yaml = require("yaml");

const workflow = yaml.parse(
  fs.readFileSync(path.join(__dirname, "../../.github/workflows/format-on-demand.yml"), "utf8")
);
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;

function evaluate(expression, github, outputs = {}) {
  const body = expression.replace(/^\s*\$\{\{\s*|\s*\}\}\s*$/g, "");
  return new Function("github", "steps", "contains", `return (${body});`)(
    github,
    { meta: { outputs }, changes: { outputs: {} } },
    (value, search) => value.toLowerCase().includes(search.toLowerCase())
  );
}

function event(
  body,
  state = "open",
  pullRequest = true,
  eventName = "issue_comment",
  action = "created"
) {
  return {
    event_name: eventName,
    event: {
      action,
      issue: { state, pull_request: pullRequest },
      comment: { body: body.toLowerCase() }
    }
  };
}

async function metadata(job, state, author, association, prInput = "895", sameRepo = true) {
  const outputs = {};
  const failures = [];
  let reads = 0;
  const pr = { state, user: { login: "author" } };
  if (state === "open") {
    pr.base = { ref: "main" };
    pr.head = {
      ref: "feature",
      repo: { full_name: sameRepo ? "wallstop/unity-helpers" : "author/unity-helpers" }
    };
  } else {
    Object.defineProperty(pr, "head", {
      get() {
        throw new Error("A closed PR must never resolve its deleted head");
      }
    });
  }
  const github = {
    rest: {
      pulls: {
        async get(args) {
          reads++;
          assert.equal(args.pull_number, 895);
          return { data: pr };
        }
      }
    }
  };
  const context = {
    repo: { owner: "wallstop", repo: "unity-helpers" },
    payload: {
      issue: { number: 895, state: "open" },
      comment: { user: { login: author }, author_association: association },
      inputs: { pr_number: prInput }
    }
  };
  const core = {
    setOutput(name, value) {
      outputs[name] = value;
    },
    setFailed(message) {
      failures.push(message);
    },
    info() {}
  };
  await new AsyncFunction("github", "context", "core", job.steps[0].with.script)(
    github,
    context,
    core
  );
  return { outputs, failures, reads };
}

async function main() {
  const commentJob = workflow.jobs.by_comment;
  const dispatchJob = workflow.jobs.by_dispatch;
  const negatives = [
    "manual/format/failure-notification skips",
    "Please run /format",
    "`/format`",
    "/format extra",
    "/autofix later",
    "/lint-fix\nmore text",
    " /format",
    "/format\n",
    ""
  ];
  for (const body of negatives) {
    assert.equal(evaluate(commentJob.if, event(body)), false, `Incidental text triggered: ${body}`);
  }
  for (const body of ["/format", "/autofix", "/lint-fix", "/FORMAT", "/AutoFix", "/LINT-FIX"]) {
    assert.equal(
      evaluate(commentJob.if, event(body)),
      true,
      `Standalone command rejected: ${body}`
    );
    assert.equal(evaluate(commentJob.if, event(body, "closed")), false);
    assert.equal(evaluate(commentJob.if, event(body, "open", false)), false);
    assert.equal(evaluate(commentJob.if, event(body, "open", true, "workflow_dispatch")), false);
    assert.equal(
      evaluate(commentJob.if, event(body, "open", true, "issue_comment", "edited")),
      false
    );
  }
  const oldTrigger =
    "${{ github.event_name == 'issue_comment' && github.event.action == 'created' && github.event.issue.pull_request && (contains(github.event.comment.body, '/format') || contains(github.event.comment.body, '/autofix') || contains(github.event.comment.body, '/lint-fix')) }}";
  assert.equal(
    evaluate(oldTrigger, event(negatives[0], "closed")),
    true,
    "Original incident must trigger the old predicate"
  );
  assert.throws(() => assert.equal(evaluate(oldTrigger, event(negatives[0], "closed")), false));

  for (const job of [commentJob, dispatchJob]) {
    const closed = await metadata(job, "closed", "author", "OWNER");
    assert.equal(closed.reads, 1);
    assert.deepEqual(closed.failures, []);
    assert.equal(closed.outputs.ready, "false");
    assert.equal(closed.outputs.head_ref, undefined);
    for (const step of job.steps.slice(1)) {
      assert.ok(step.if, `Missing closed-PR guard: ${step.name}`);
      assert.equal(evaluate(step.if, {}, closed.outputs), false, `Closed PR runs ${step.name}`);
    }
    for (const sameRepo of [true, false]) {
      const open = await metadata(job, "open", "author", "CONTRIBUTOR", "895", sameRepo);
      assert.equal(open.outputs.ready, "true");
      assert.equal(open.outputs.same_repo, String(sameRepo));
      assert.equal(open.outputs.head_ref, "feature");
      const checkouts = job.steps.filter((step) => step.uses?.startsWith("actions/checkout@"));
      assert.equal(checkouts.filter((step) => evaluate(step.if, {}, open.outputs)).length, 1);
      for (const step of job.steps.slice(1)) {
        if (step.name === "Exit if not authorized") continue;
        assert.match(
          step.if,
          /steps\.meta\.outputs\.ready == 'true'/,
          `Readiness guard required: ${step.name}`
        );
      }
    }
  }
  for (const association of ["OWNER", "MEMBER", "COLLABORATOR"]) {
    const maintainer = await metadata(commentJob, "open", "maintainer", association);
    assert.equal(maintainer.outputs.ready, "true");
  }
  const outsider = await metadata(commentJob, "open", "outsider", "CONTRIBUTOR");
  assert.equal(outsider.outputs.allowed, "false");
  assert.equal(outsider.outputs.ready, "false");
  const unauthorizedExit = commentJob.steps.find((step) => step.name === "Exit if not authorized");
  assert.equal(evaluate(unauthorizedExit.if, {}, outsider.outputs), true);
  for (const step of commentJob.steps.slice(1).filter((step) => step !== unauthorizedExit)) {
    assert.equal(evaluate(step.if, {}, outsider.outputs), false);
  }
  for (const input of [
    "",
    "0",
    "-1",
    "abc",
    "1.5",
    "9007199254740992",
    "895'); throw new Error('injected"
  ]) {
    const invalid = await metadata(dispatchJob, "open", "author", "OWNER", input);
    assert.equal(invalid.reads, 0);
    assert.equal(invalid.failures.length, 1);
    assert.equal(invalid.outputs.ready, "false");
  }
  console.log(
    "Formatting workflow contracts passed; original incident rejected and old-trigger control detected."
  );
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
