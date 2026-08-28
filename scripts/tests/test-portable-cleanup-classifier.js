"use strict";

const assert = require("node:assert/strict");
const childProcess = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "../..");
const configuredPolicyRoot = process.env.BUILD_LOCK_POLICY_ROOT || "";

// This test runs against a separate checkout of the central build-lock policy, which only CI
// provides (see the "Test central Unity cleanup policy parity" step). Hard-failing without it
// made the former `npm run validate:prepush` aggregate impossible to pass on a
// developer machine. Skip when the checkout is absent, but never when running in Actions: there
// its absence means the CI wiring broke, and silently skipping would drop the contract.
if (!configuredPolicyRoot) {
  if (process.env.GITHUB_ACTIONS === "true") {
    console.error(
      "BUILD_LOCK_POLICY_ROOT is unset under GitHub Actions. The central policy checkout step " +
        "must run before this test; skipping here would drop the parity contract silently."
    );
    process.exit(1);
  }

  console.log(
    "[test-portable-cleanup-classifier] SKIPPED: set BUILD_LOCK_POLICY_ROOT to a checkout of " +
      "ambiguous-organization-build-lock to run the central cleanup policy parity contract."
  );
  process.exit(0);
}

const buildLockRoot = path.resolve(configuredPolicyRoot);
// Derived, never restated: a hand-copied SHA here would keep matching the previous policy after a
// Dependabot bump, so this parity contract would pass while validating a version we no longer use.
const { resolveBuildLockPin } = require("../resolve-build-lock-pin");
const policyCommit = resolveBuildLockPin("require-confirmed-unity-cleanup", root);
const classifierPath = path.join(buildLockRoot, ".github/dist/classify-unity-cleanup-evidence.js");
const gatePath = path.join(buildLockRoot, ".github/dist/require-confirmed-unity-cleanup.js");

assert.notEqual(
  buildLockRoot,
  path.parse(buildLockRoot).root,
  "BUILD_LOCK_POLICY_ROOT must identify the exact checked-out central policy commit"
);
assert.ok(fs.existsSync(classifierPath), `missing central classifier runtime: ${classifierPath}`);
assert.ok(fs.existsSync(gatePath), `missing central gate runtime: ${gatePath}`);
assert.equal(
  childProcess
    .execFileSync("git", ["-C", buildLockRoot, "rev-parse", "HEAD"], {
      encoding: "utf8"
    })
    .trim(),
  policyCommit,
  "central policy checkout must be the exact immutable commit pinned by the consumer"
);

const { classifyEvidence } = require(classifierPath);
const { evaluateCleanupGate } = require(gatePath);

const positive = [
  "[Licensing::Module] Successfully returned the entitlement license",
  "[Licensing::Client] Successfully returned ULF license with serial number : <redacted>"
].join("\n");
const skipped =
  "[Licensing::Module] Error: Serial number unavailable for ULF return; skipping operation";

const classificationCases = [
  {
    name: "confirmed cleanup",
    returnLog: positive,
    commandCompleted: true,
    exitCode: 0,
    captureComplete: true,
    expected: {
      resourceSafe: true,
      cleanupStatus: "confirmed",
      health: "healthy",
      reason: "cleanup-confirmed"
    }
  },
  {
    name: "account limit in supplemental evidence",
    returnLog: positive,
    supplemental: ["Licensing failed with error 20111"],
    commandCompleted: true,
    exitCode: 0,
    captureComplete: true,
    expected: {
      resourceSafe: false,
      cleanupStatus: "unknown",
      health: "blocked",
      reason: "unity-account-limit-20111"
    }
  },
  {
    name: "incomplete evidence capture",
    returnLog: positive,
    commandCompleted: true,
    exitCode: 0,
    captureComplete: false,
    expected: {
      resourceSafe: false,
      cleanupStatus: "unknown",
      health: "healthy",
      reason: "return-log-truncated"
    }
  },
  {
    name: "shared entitlement return collision",
    returnLog: "Error: Code 400006 while processing request",
    commandCompleted: true,
    exitCode: 1,
    captureComplete: true,
    expected: {
      resourceSafe: false,
      cleanupStatus: "unknown",
      health: "healthy",
      reason: "unity-return-400006"
    }
  },
  {
    name: "terminated return",
    returnLog: positive,
    commandCompleted: true,
    exitCode: 143,
    captureComplete: true,
    expected: {
      resourceSafe: false,
      cleanupStatus: "unknown",
      health: "healthy",
      reason: "return-terminated"
    }
  },
  {
    name: "timed out return",
    returnLog: "",
    commandCompleted: true,
    exitCode: 124,
    captureComplete: true,
    expected: {
      resourceSafe: false,
      cleanupStatus: "unknown",
      health: "healthy",
      reason: "return-timeout"
    }
  },
  {
    name: "unclassified Unity error",
    returnLog: "Licensing error 20113",
    commandCompleted: true,
    exitCode: 1,
    captureComplete: true,
    expected: {
      resourceSafe: false,
      cleanupStatus: "unknown",
      health: "healthy",
      reason: "unity-20113-unclassified"
    }
  },
  {
    name: "ULF returned before another group was skipped",
    returnLog: `${positive}\n${skipped}`,
    commandCompleted: true,
    exitCode: 0,
    captureComplete: true,
    expected: {
      resourceSafe: false,
      cleanupStatus: "unknown",
      health: "healthy",
      reason: "return-ulf-skipped"
    }
  },
  {
    name: "ULF group skipped before another group returned",
    returnLog: `${skipped}\n${positive}`,
    commandCompleted: true,
    exitCode: 0,
    captureComplete: true,
    expected: {
      resourceSafe: false,
      cleanupStatus: "unknown",
      health: "healthy",
      reason: "return-ulf-skipped"
    }
  },
  {
    name: "positive supplemental evidence cannot prove cleanup",
    returnLog: "",
    supplemental: [positive],
    commandCompleted: true,
    exitCode: 0,
    captureComplete: true,
    expected: {
      resourceSafe: false,
      cleanupStatus: "unknown",
      health: "healthy",
      reason: "return-missing-positive-evidence"
    }
  },
  {
    name: "missing positive evidence",
    returnLog: "License return succeeded",
    commandCompleted: true,
    exitCode: 0,
    captureComplete: true,
    expected: {
      resourceSafe: false,
      cleanupStatus: "unknown",
      health: "healthy",
      reason: "return-missing-positive-evidence"
    }
  }
];

for (const testCase of classificationCases) {
  assert.deepEqual(
    classifyEvidence({
      exitCode: testCase.exitCode,
      returnLog: Buffer.from(testCase.returnLog),
      supplemental: (testCase.supplemental || []).map((value) => Buffer.from(value)),
      commandCompleted: testCase.commandCompleted,
      captureComplete: testCase.captureComplete
    }),
    testCase.expected,
    testCase.name
  );
}

const safeGate = {
  acquired: "true",
  classificationComplete: "true",
  cleanupStatus: "confirmed",
  cleanupHealth: "healthy",
  cleanupReason: "cleanup-confirmed",
  releaseOutcome: "success",
  cleanupResult: "cooldown-started",
  released: "true",
  releaseHealth: "healthy",
  releaseReason: "cleanup-confirmed",
  reservationState: "cooldown",
  reservationId: "reservation-1",
  incidentId: ""
};
const gateCases = [
  ["coherent cooldown", {}, true],
  [
    "coherent direct release",
    { cleanupResult: "released", reservationState: "", reservationId: "" },
    true
  ],
  ["classification did not complete", { classificationComplete: "false" }, false],
  ["cleanup quarantined", { cleanupResult: "quarantined" }, false],
  ["release failed", { releaseOutcome: "failure" }, false],
  ["holder removal not confirmed", { released: "false" }, false],
  ["account incident remains", { incidentId: "incident-1" }, false],
  ["cooldown reservation is missing", { reservationId: "" }, false],
  [
    "direct release contradicts reservation",
    { cleanupResult: "released", reservationState: "cooldown" },
    false
  ]
];
for (const [name, overrides, expected] of gateCases) {
  assert.equal(evaluateCleanupGate({ ...safeGate, ...overrides }).safe, expected, name);
}

// The local return-unity-license composite was migrated to the central node action (#411), so the
// delegation contract moved into the workflows themselves: every licensed leg must classify its
// return evidence through the central classifier pinned in this repository, forwarding the return
// step's evidence outputs. Asserted against the joined workflow bodies below.
const workflowDir = path.join(root, ".github/workflows");
const withoutComments = (content) =>
  content
    .split("\n")
    .filter((line) => !/^\s*#/.test(line))
    .join("\n");

const licenseReturningWorkflows = fs
  .readdirSync(workflowDir)
  .filter((name) => /\.ya?ml$/.test(name))
  .map((name) => ({
    name,
    body: withoutComments(fs.readFileSync(path.join(workflowDir, name), "utf8"))
  }))
  .filter((entry) => entry.body.includes("id: return_unity_license"));

assert.ok(
  licenseReturningWorkflows.length > 0,
  "no workflow returns a Unity license; this contract has nothing to protect and has silently " +
    "stopped checking anything"
);

const workflow = licenseReturningWorkflows.map((entry) => entry.body).join("\n");
// The expected count is the number of license returns that exist, not a number typed here: a leg
// added to or removed from the matrix must not have to remember to update this file.
const occurrences = (haystack, needle) => haystack.split(needle).length - 1;
const licenseReturns = occurrences(workflow, "id: return_unity_license");
const centralGateUse = `Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/require-confirmed-unity-cleanup@${policyCommit}`;
// Derived from its own `uses:` lines for the same reason the gate pin is: the classifier may move
// to a different central commit than the gate, and a hand-copied SHA would keep validating the
// version the workflows stopped using.
const classifierCommit = resolveBuildLockPin("classify-unity-cleanup-evidence", root);
const centralClassifierUse = `Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/classify-unity-cleanup-evidence@${classifierCommit}`;

for (const [description, needle] of [
  ["the pinned central cleanup gate", `uses: ${centralGateUse}`],
  ["the pinned central classifier", `uses: ${centralClassifierUse}`],
  ["a lock release", "id: release_unity_lock"],
  [
    "a forwarded resource-cleanup-status",
    "resource-cleanup-status: ${{ steps.cleanup_classification.outputs.resource-cleanup-status }}"
  ],
  [
    "a forwarded classification-complete",
    "classification-complete: ${{ steps.cleanup_classification.outputs.classification-complete }}"
  ],
  ["a forwarded release outcome", "release-outcome: ${{ steps.release_unity_lock.outcome }}"],
  ["an evidence deletion step", "- name: Delete private Unity cleanup evidence"]
]) {
  assert.equal(
    occurrences(workflow, needle),
    licenseReturns,
    `${licenseReturns} license return(s) across ` +
      `${licenseReturningWorkflows.map((entry) => entry.name).join(", ")} but ` +
      `${occurrences(workflow, needle)} instance(s) of ${description}`
  );
}

// Each classification must consume the return step's evidence, not fabricated constants: the
// classifier fails closed on incomplete evidence, and rewriting the forwarded values would
// convert that fail-closed contract into an unconditionally green one.
for (const [description, needle] of [
  [
    "a forwarded return-log-path",
    "return-log-path: ${{ steps.return_unity_license.outputs.return-log-path }}"
  ],
  [
    "a forwarded return-command-completed",
    "return-command-completed: ${{ steps.return_unity_license.outputs.return-command-completed }}"
  ],
  [
    "a forwarded return-exit-code",
    "return-exit-code: ${{ steps.return_unity_license.outputs.return-exit-code }}"
  ],
  [
    "a forwarded evidence-capture-complete",
    "evidence-capture-complete: ${{ steps.return_unity_license.outputs.evidence-capture-complete }}"
  ]
]) {
  assert.equal(
    occurrences(workflow, needle),
    licenseReturns,
    `${licenseReturns} license return(s) but ${occurrences(workflow, needle)} instance(s) of ${description}`
  );
}

process.stdout.write(
  `Central Unity cleanup policy parity passed (${classificationCases.length} classifier cases, ${gateCases.length} gate cases).\n`
);
