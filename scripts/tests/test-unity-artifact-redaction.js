#!/usr/bin/env node
// MIT License — Copyright (c) wallstop studios
//
// Structural guard for the Unity credential leak.
//
// Unity writes its license identity into unity.log and configure.log while it activates. GitHub
// masks a registered secret in the rendered job log, but it never rewrites the bytes of an uploaded
// artifact, so uploading a Unity output directory publishes the serial to anyone who can download
// the artifact.
//
// Scrubbing the seven known uploads does not close the failure mode, because the next job that
// uploads a Unity directory reopens it. This asserts the invariant instead: every upload of a
// Unity-log-bearing path is preceded, in the same job, by a redaction step that covers that path.
//
// The workflows are read with a small line scanner rather than a YAML library. This repository
// installs no YAML parser, and scripts/validate-build-lock-action-inputs.js already reads workflow
// and action files the same way. The scanner is held honest by the parser self-tests below: it must
// find the known jobs, steps and uploads, so a scanner that silently stops matching fails loudly
// instead of reporting a clean repository.

"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const repoRoot = path.resolve(__dirname, "..", "..");
const workflowDirectory = path.join(repoRoot, ".github", "workflows");
const redactionAction = "./.github/actions/redact-unity-artifacts";
const uploadAction = "actions/upload-artifact";

/**
 * Directory prefixes that can hold Unity editor output.
 *
 * `.artifacts/unity` is where every Unity invocation in this repository writes, logs included.
 * `perf-staging` is the benchmark job's copy of a results file taken out of that tree, so it is
 * derived Unity output and is covered for the same reason.
 *
 * Two paths are deliberately absent. `.artifacts/unitypackage` holds only the exported
 * .unitypackage, which Unity writes with no log beside it, so matching it would demand a scrub of a
 * binary release asset that carries no credential material. `perf-results` holds files the commit
 * job DOWNLOADS from the benchmark job, which scrubbed them before it uploaded them.
 */
const unityOutputPrefixes = [".artifacts/unity", "perf-staging"];

let passed = 0;
let failed = 0;
const failedTests = [];

function runTest(name, fn) {
  try {
    fn();
    console.log(`  [PASS] ${name}`);
    passed++;
  } catch (err) {
    console.log(`  [FAIL] ${name}`);
    console.log(`         ${err.message}`);
    failed++;
    failedTests.push(name);
  }
}

function isBlankOrComment(line) {
  const trimmed = line.trim();
  return trimmed === "" || trimmed.startsWith("#");
}

/** Indentation of a line's own content, counting a sequence dash as two columns of indent. */
function keyColumn(line) {
  const indent = line.length - line.trimStart().length;
  return /^-\s/.test(line.trim()) ? indent + 2 : indent;
}

/**
 * Index one past the last line of the block that starts at `startIndex` and whose keys sit at
 * `column`. A shallower key ends the block, and so does the next sequence item at the same column,
 * which is what separates two steps. Deeper lines -- a nested mapping, a block scalar's body -- are
 * inside the block and are walked through.
 */
function blockEnd(lines, startIndex, column) {
  for (let index = startIndex + 1; index < lines.length; index += 1) {
    if (isBlankOrComment(lines[index])) {
      continue;
    }
    if (keyColumn(lines[index]) <= column) {
      return index;
    }
  }
  return lines.length;
}

/**
 * Index one past the last line of the step that starts at `startIndex`.
 *
 * A step is a sequence item, so its own keys share the sequence item's key column and `blockEnd`
 * would stop on the first of them. Only a shallower line or the next sequence item ends a step.
 */
function stepEnd(lines, startIndex) {
  for (let index = startIndex + 1; index < lines.length; index += 1) {
    if (isBlankOrComment(lines[index])) {
      continue;
    }
    if (keyColumn(lines[index]) < 8 || /^ {6}- /.test(lines[index])) {
      return index;
    }
  }
  return lines.length;
}

/**
 * Value of `key` in the mapping that spans `[start, end)` and whose keys sit at `column`.
 *
 * Returns the lines of the value -- one entry for an inline scalar, one per line for a block scalar
 * -- or null when the mapping has no such key. Only lines at exactly `column` count as keys, so a
 * nested mapping and a block scalar's body cannot be mistaken for one.
 */
function mappingValue(lines, start, end, column, key) {
  const pattern = new RegExp(`^${key}:(.*)$`);
  for (let index = start; index < end; index += 1) {
    const line = lines[index];
    if (isBlankOrComment(line) || keyColumn(line) !== column) {
      continue;
    }
    const match = pattern.exec(line.trim().replace(/^-\s+/, ""));
    if (!match) {
      continue;
    }
    const inline = match[1].trim();
    if (inline !== "" && !/^[|>][+-]?\d?$/.test(inline)) {
      return [inline];
    }
    const body = [];
    for (let inner = index + 1; inner < Math.min(end, blockEnd(lines, index, column)); inner += 1) {
      if (lines[inner].trim() !== "") {
        body.push(lines[inner].trim());
      }
    }
    return body;
  }
  return null;
}

/** Line indices at which each step of each job begins, in file order. */
function jobsWithSteps(lines) {
  const jobs = [];
  let inJobs = false;
  let current = null;
  let inSteps = false;
  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index];
    if (isBlankOrComment(line)) {
      continue;
    }
    if (/^jobs:\s*$/.test(line)) {
      inJobs = true;
      continue;
    }
    if (!inJobs) {
      continue;
    }
    if (keyColumn(line) === 0) {
      break;
    }
    const jobHeader = /^ {2}([A-Za-z0-9_-]+):\s*$/.exec(line);
    if (jobHeader) {
      current = { id: jobHeader[1], stepIndices: [] };
      jobs.push(current);
      inSteps = false;
      continue;
    }
    if (!current) {
      continue;
    }
    if (/^ {4}steps:\s*$/.test(line)) {
      inSteps = true;
      continue;
    }
    if (/^ {4}[A-Za-z0-9_-]+:/.test(line)) {
      inSteps = false;
      continue;
    }
    if (inSteps && /^ {6}- /.test(line)) {
      current.stepIndices.push(index);
    }
  }
  return jobs;
}

/** Normalize a workflow expression to plain text so a prefix comparison is meaningful. */
function normalizePath(value) {
  return String(value)
    .replace(/\$\{\{[^}]*\}\}/g, "")
    .split("\\")
    .join("/")
    .trim();
}

function toPathList(values) {
  return (values ?? []).map((entry) => normalizePath(entry)).filter((entry) => entry.length > 0);
}

function isUnityOutput(candidate) {
  return unityOutputPrefixes.some(
    (prefix) => candidate === prefix || candidate.startsWith(`${prefix}/`)
  );
}

/** A redaction step covers an upload when one of its paths is a prefix of the uploaded path. */
function covers(redactionPaths, uploadedPath) {
  return redactionPaths.some(
    (declared) => uploadedPath === declared || uploadedPath.startsWith(`${declared}/`)
  );
}

function readWorkflows() {
  return fs
    .readdirSync(workflowDirectory)
    .filter((name) => name.endsWith(".yml") || name.endsWith(".yaml"))
    .sort()
    .map((name) => ({
      name,
      lines: fs.readFileSync(path.join(workflowDirectory, name), "utf8").split(/\r?\n/)
    }));
}

/** Every step of every job in every workflow, with the fields this guard reads. */
function allSteps() {
  const steps = [];
  for (const { name, lines } of readWorkflows()) {
    for (const job of jobsWithSteps(lines)) {
      for (const index of job.stepIndices) {
        const end = stepEnd(lines, index);
        const withIndex = lines
          .slice(index, end)
          .findIndex((line) => keyColumn(line) === 8 && /^with:\s*$/.test(line.trim()));
        const withStart = withIndex < 0 ? index : index + withIndex + 1;
        const withEnd = withIndex < 0 ? index : blockEnd(lines, index + withIndex, 8);
        steps.push({
          workflow: name,
          jobId: job.id,
          index,
          stepName: (mappingValue(lines, index, end, 8, "name") ?? [])[0] ?? "",
          stepId: (mappingValue(lines, index, end, 8, "id") ?? [])[0] ?? "",
          condition: (mappingValue(lines, index, end, 8, "if") ?? []).join(" "),
          uses: (mappingValue(lines, index, end, 8, "uses") ?? [])[0] ?? "",
          withPath: toPathList(mappingValue(lines, withStart, withEnd, 10, "path")),
          withPaths: toPathList(mappingValue(lines, withStart, withEnd, 10, "paths"))
        });
      }
    }
  }
  return steps;
}

/** Every Unity-log-bearing upload, with the redaction paths declared earlier in the same job. */
function unityUploads() {
  const uploads = [];
  const redactedByJob = new Map();
  const redactionIdsByJob = new Map();
  for (const step of allSteps()) {
    const jobKey = `${step.workflow}#${step.jobId}`;
    if (step.uses.startsWith(redactionAction)) {
      redactedByJob.set(jobKey, [...(redactedByJob.get(jobKey) ?? []), ...step.withPaths]);
      redactionIdsByJob.set(jobKey, [...(redactionIdsByJob.get(jobKey) ?? []), step.stepId]);
      continue;
    }
    if (!step.uses.startsWith(uploadAction)) {
      continue;
    }
    for (const uploadedPath of step.withPath) {
      if (!isUnityOutput(uploadedPath)) {
        continue;
      }
      uploads.push({
        ...step,
        uploadedPath,
        redactedSoFar: [...(redactedByJob.get(jobKey) ?? [])],
        redactionIds: [...(redactionIdsByJob.get(jobKey) ?? [])]
      });
    }
  }
  return uploads;
}

console.log("Testing Unity artifact redaction coverage...\n");

runTest("the workflow scanner still finds the Unity jobs, steps and uploads", () => {
  // Every assertion below is vacuous if the scanner stops matching, so pin what it must see.
  const steps = allSteps();
  const jobIds = new Set(
    steps.filter((step) => step.workflow === "unity-tests.yml").map((step) => step.jobId)
  );
  for (const expected of [
    "unity-tests",
    "unity-tests-standalone",
    "unity-tests-single-threaded",
    "unitypackage-smoke"
  ]) {
    assert.ok(jobIds.has(expected), `scanner: unity-tests.yml job ${expected} was not found`);
  }
  const uploadSteps = steps.filter((step) => step.uses.startsWith(uploadAction));
  assert.ok(
    uploadSteps.length >= 14,
    `scanner: expected the repository's upload steps to be found, saw ${uploadSteps.length}`
  );
  assert.ok(
    uploadSteps.every((step) => step.withPath.length > 0),
    "scanner: every upload step declares a path, so one that parsed to none is a scanner fault"
  );
  assert.ok(
    unityUploads().length >= 13,
    `scanner: expected the Unity uploads to be found, saw ${unityUploads().length}`
  );
});

runTest("every Unity artifact upload is preceded by redaction in the same job", () => {
  const unprotected = unityUploads().filter(
    (upload) => !covers(upload.redactedSoFar, upload.uploadedPath)
  );
  assert.deepEqual(
    unprotected.map(
      (upload) => `${upload.workflow} ${upload.jobId} "${upload.stepName}" (${upload.uploadedPath})`
    ),
    [],
    `these steps upload Unity output that can still hold the license serial; add a ` +
      `"${redactionAction}" step earlier in the same job whose paths cover the uploaded path`
  );
});

runTest("every redaction step can actually run node", () => {
  // The self-hosted Unity runners have no Node on PATH. A redaction step without a Node toolchain
  // in front of it fails the job it was added to protect.
  const byJob = new Map();
  for (const step of allSteps()) {
    const jobKey = `${step.workflow}#${step.jobId}`;
    if (!byJob.has(jobKey)) {
      byJob.set(jobKey, []);
    }
    byJob.get(jobKey).push(step);
  }
  const missing = [];
  for (const [jobKey, steps] of byJob) {
    const redactionIndex = steps.findIndex((step) => step.uses.startsWith(redactionAction));
    if (redactionIndex < 0) {
      continue;
    }
    const hasNode = steps
      .slice(0, redactionIndex)
      .some((step) => step.uses.startsWith("actions/setup-node"));
    if (!hasNode) {
      missing.push(jobKey);
    }
  }
  assert.deepEqual(
    missing,
    [],
    "these jobs redact before setting up Node, so the redactor cannot start: " + missing.join(", ")
  );
});

runTest("the redaction action exists and runs the shared redactor", () => {
  const actionPath = path.join(
    repoRoot,
    ".github",
    "actions",
    "redact-unity-artifacts",
    "action.yml"
  );
  const body = fs.readFileSync(actionPath, "utf8");
  assert.match(body, /using: composite/, "the redaction step must be a composite action");
  assert.match(body, /shell: pwsh/, "the composite step must declare its shell");
  assert.match(
    body,
    /paths:\s*\n\s+description:[\s\S]*?required: true/,
    "paths must be required so a call cannot scrub nothing"
  );
  assert.match(
    body,
    /node scripts\/unity\/redact-unity-artifacts\.js/,
    "the action must call the tested redactor rather than restating the patterns inline"
  );
  assert.match(
    body,
    /throw/,
    "the action must fail the job when redaction fails; a silent pass recreates the leak"
  );
});

runTest("every Unity artifact upload is gated on redaction succeeding", () => {
  // Redaction failing closed only protects the artifact if the upload then does not run. An
  // `if: always()` upload publishes the unredacted tree the failing step was there to prevent, and
  // a `failure()` diagnostic upload is worse: the redaction failure is itself what triggers it.
  const ungated = [];
  for (const upload of unityUploads()) {
    const gated = upload.redactionIds.some(
      (id) => id.length > 0 && upload.condition.includes(`steps.${id}.outcome == 'success'`)
    );
    if (!gated) {
      ungated.push(
        `${upload.workflow} ${upload.jobId} "${upload.stepName}" (${upload.uploadedPath})`
      );
    }
  }
  assert.deepEqual(
    ungated,
    [],
    "these steps upload Unity output without requiring that redaction succeeded; give the " +
      "redaction step an id and add `steps.<id>.outcome == 'success'` to the upload's if"
  );
});

console.log("");
console.log(`Tests passed: ${passed}`);
console.log(`Tests failed: ${failed}`);
if (failedTests.length > 0) {
  console.log("Failed tests:");
  for (const name of failedTests) {
    console.log(`  - ${name}`);
  }
}

process.exit(failed === 0 ? 0 : 1);
