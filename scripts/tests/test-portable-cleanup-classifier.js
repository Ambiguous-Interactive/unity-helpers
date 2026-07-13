"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "../..");
const actionDir = path.join(root, ".github/actions/classify-unity-cleanup-evidence");
const { classifyCleanupEvidence } = require(path.join(actionDir, "classify.js"));

const positive = [
  "exit_return_rc=0",
  "[Licensing::Module] Successfully returned the entitlement license",
  "[Licensing::Client] Successfully returned ULF license with serial number : <redacted>",
  ""
].join("\n");

const cases = [
  ["exact markers", true, positive, true],
  ["command incomplete", false, positive, false],
  ["exit status missing", true, positive.replace("exit_return_rc=0\n", ""), false],
  ["entitlement only", true, "Successfully returned the entitlement license\n", false],
  ["case changed", true, positive.replace("Successfully", "successfully"), false],
  ["terminated", true, positive.replace("exit_return_rc=0", "exit_return_rc=143"), false],
  [
    "unsigned Windows termination",
    true,
    positive.replace("exit_return_rc=0", "exit_return_rc=3221225786"),
    false
  ],
  [
    "nontermination nonzero with exact evidence",
    true,
    positive.replace("exit_return_rc=0", "exit_return_rc=1"),
    true
  ]
];

for (const [name, commandCompleted, logText, expected] of cases) {
  assert.equal(classifyCleanupEvidence({ commandCompleted, logText }), expected, name);
}

const action = fs.readFileSync(path.join(actionDir, "action.yml"), "utf8");
assert.match(action, /shell:\s*node \{0\}/u);
assert.doesNotMatch(action, /shell:\s*pwsh/u);
assert.match(action, /resource-cleanup-status/u);

const workflow = fs.readFileSync(path.join(root, ".github/workflows/unity-tests.yml"), "utf8");
for (const job of [
  "runner-preflight",
  "unity-tests",
  "unity-tests-standalone",
  "unity-tests-single-threaded",
  "unitypackage-smoke"
]) {
  const start = workflow.indexOf(`  ${job}:`);
  assert.notEqual(start, -1, `missing job ${job}`);
  const next = workflow.slice(start + 2).search(/^  [a-z0-9-]+:/mu);
  const block = workflow.slice(start, next === -1 ? undefined : start + 2 + next);
  assert.match(
    block,
    /github\.event_name != 'pull_request'/u,
    `${job} must not run licensed PR code`
  );
}
assert.match(
  workflow,
  /- name: Check for required licensed workflow secrets[\s\S]*?if: \$\{\{ github\.event_name != 'pull_request' \}\}/u
);

process.stdout.write("Portable Unity cleanup classifier tests passed.\n");
