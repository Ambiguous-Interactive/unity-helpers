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
  ["entitlement only", true, "Successfully returned the entitlement license\n", false],
  ["case changed", true, positive.replace("Successfully", "successfully"), false],
  ["terminated", true, positive.replace("exit_return_rc=0", "exit_return_rc=143"), false]
];

for (const [name, commandCompleted, logText, expected] of cases) {
  assert.equal(classifyCleanupEvidence({ commandCompleted, logText }), expected, name);
}

const action = fs.readFileSync(path.join(actionDir, "action.yml"), "utf8");
assert.match(action, /shell:\s*node \{0\}/u);
assert.doesNotMatch(action, /shell:\s*pwsh/u);
assert.match(action, /resource-cleanup-status/u);

process.stdout.write("Portable Unity cleanup classifier tests passed.\n");
