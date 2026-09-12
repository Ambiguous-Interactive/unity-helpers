"use strict";

const assert = require("node:assert/strict");
const childProcess = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");
const yaml = require("yaml");

const root = path.resolve(__dirname, "../..");
const returnActionPath = path.join(root, ".github/actions/return-unity-license/action.yml");
assert.equal(
  fs.existsSync(returnActionPath),
  false,
  "the unused local return wrapper must remain removed; all licensed callers use the central executor"
);
const configuredPolicyRoot = process.env.BUILD_LOCK_POLICY_ROOT || "";

// This test needs a separate checkout of the central build-lock policy. CI supplies it; local
// lifecycle edits must set BUILD_LOCK_POLICY_ROOT to the exact pinned checkout. Hard-failing without it
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
      reason: "unity-account-limit-20111",
      licensingCodeMatched: "20111"
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
      reason: "unity-return-400006",
      licensingCodeMatched: "400006"
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
      reason: "unity-20113-unclassified",
      licensingCodeMatched: "20113"
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
  // The central classifier reviews its verdict shape and may add evidence
  // fields. Pin the reviewed fields per case without forbidding that
  // reviewed evolution, so a repin never breaks on additive central output.
  const verdict = classifyEvidence({
    exitCode: testCase.exitCode,
    returnLog: Buffer.from(testCase.returnLog),
    supplemental: (testCase.supplemental || []).map((value) => Buffer.from(value)),
    commandCompleted: testCase.commandCompleted,
    captureComplete: testCase.captureComplete
  });
  for (const [field, expected] of Object.entries(testCase.expected)) {
    assert.equal(verdict[field], expected, `${testCase.name}: ${field}`);
  }
}

// The reviewed attribution vocabulary travels with every verdict.
assert.equal(
  classifyEvidence({
    exitCode: 0,
    returnLog: Buffer.from(positive),
    supplemental: [],
    commandCompleted: true,
    captureComplete: true
  }).licensingCodesChecked,
  "20111,20113,400006"
);

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

const centralClassifierUse = `Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/classify-unity-cleanup-evidence@${policyCommit}`;
for (const deprecatedPolicyFile of [
  ".github/actions/classify-unity-cleanup-evidence/action.yml",
  ".github/actions/classify-unity-cleanup-evidence/classify.js",
  ".github/actions/return-unity-license/Classify-UnityLicenseReturn.ps1"
]) {
  assert.equal(
    fs.existsSync(path.join(root, deprecatedPolicyFile)),
    false,
    `${deprecatedPolicyFile} must not duplicate central policy`
  );
}

// Discovered, not named. Listing workflow files here
// stated where the requirement currently lives rather than what it is, and the quiet failure that
// invites is a FOURTH workflow that returns a Unity license and is checked by nothing (#445). The
// requirement is that every workflow returning a license passes through the central cleanup gate.
//
// Comments are stripped before matching, because a workflow that explains in prose where its Unity
// job went would otherwise be discovered as one that has a Unity job.
const workflowDir = path.join(root, ".github/workflows");
const withoutComments = (content) =>
  content
    .split("\n")
    .filter((line) => !/^\s*#/.test(line))
    .join("\n");

const licenseReturningWorkflows = fs
  .readdirSync(workflowDir)
  .filter((name) => /\.ya?ml$/.test(name))
  .map((name) => {
    const body = withoutComments(fs.readFileSync(path.join(workflowDir, name), "utf8"));
    assert.doesNotMatch(
      body,
      /uses:\s*["']?\.\/\.github\/actions\/return-unity-license(?:["'\s]|$)/u,
      `${name} must not call the removed local return wrapper`
    );
    return { name, body };
  })
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
const centralReturnUse = `Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/return-unity-license@${policyCommit}`;
const centralReturns = occurrences(workflow, `uses: ${centralReturnUse}`);
for (const entry of licenseReturningWorkflows) {
  for (const [jobName, job] of Object.entries(yaml.parse(entry.body).jobs)) {
    for (const step of job.steps || []) {
      if (step.uses === centralGateUse) {
        assert.equal(
          step["continue-on-error"],
          undefined,
          `${entry.name}:${jobName}:${step.name} must not mask a central cleanup verdict`
        );
      }
    }
  }
}
const cleanupGates = licenseReturns;
assert.ok(centralReturns > 0, "no Windows caller uses the central return executor");
assert.equal(centralReturns, licenseReturns, "every return must use the pinned central executor");
assert.equal(
  occurrences(workflow, `uses: ${centralGateUse}`),
  cleanupGates,
  "every lifecycle needs a terminal gate"
);
assert.equal(
  occurrences(workflow, "id: release_unity_lock"),
  licenseReturns,
  "every licensed lifecycle must release its lock"
);
assert.equal(
  occurrences(workflow, `uses: ${centralClassifierUse}`),
  centralReturns,
  "every central return must feed one digest-bound central classifier"
);
assert.equal(
  occurrences(
    workflow,
    "resource-cleanup-status: ${{ steps.cleanup_classification.outputs.resource-cleanup-status }}"
  ),
  centralReturns,
  "every central classifier must forward its typed cleanup status to release"
);
assert.equal(
  occurrences(
    workflow,
    "resource-cleanup-status: ${{ steps.return_unity_license.outputs.resource-cleanup-status }}"
  ),
  0,
  "release must consume classifier evidence, never return executor status"
);
assert.equal(
  occurrences(
    workflow,
    "classification-complete: ${{ steps.cleanup_classification.outputs.classification-complete }}"
  ),
  cleanupGates,
  "every central classifier must feed the final gate"
);
assert.equal(
  occurrences(
    workflow,
    "classification-complete: ${{ steps.return_unity_license.outputs.classification-complete }}"
  ),
  0,
  "the final gate must consume classifier completion, never return executor status"
);
assert.equal(
  occurrences(workflow, "release-outcome: ${{ steps.release_unity_lock.outcome }}"),
  cleanupGates,
  "every final gate must consume the release outcome"
);
assert.equal(
  occurrences(workflow, "- name: Delete private Unity cleanup evidence"),
  0,
  "only the central classifier may delete private cleanup evidence"
);

process.stdout.write(
  `Central Unity cleanup policy parity passed (${classificationCases.length} classifier cases, ${gateCases.length} gate cases).\n`
);
