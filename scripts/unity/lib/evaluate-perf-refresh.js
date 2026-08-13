"use strict";

/*
 * Decides whether a downloaded Unity benchmark matrix is safe to publish as the
 * canonical rolling result. The policy is deliberately pure and self-tested:
 * every exact expected file must exist, every file must contain metrics, and the
 * aggregate matrix job must have succeeded. Failed or partial runs remain useful
 * diagnostics, but can never advance the canonical report or baseline.
 */

const fs = require("fs");
const path = require("path");
const { extractFromFiles } = require("./extract-perf-metrics.js");

function evaluateRefresh({
  benchmarkResult,
  allowBaselineRefresh,
  expectedFiles,
  actualFiles,
  metricCounts
}) {
  const expected = [...expectedFiles].sort();
  const actual = [...actualFiles].sort();
  const identitiesMatch =
    expected.length === actual.length && expected.every((file, index) => file === actual[index]);
  const filesWithMetrics = expected.filter((file) => (metricCounts[file] ?? 0) > 0);
  const metricsComplete = filesWithMetrics.length === expected.length;
  const missingFiles = expected.filter((file) => !actual.includes(file));
  const unexpectedFiles = actual.filter((file) => !expected.includes(file));
  const zeroMetricFiles = expected.filter(
    (file) => actual.includes(file) && (metricCounts[file] ?? 0) === 0
  );
  const reasons = [];

  if (!allowBaselineRefresh) {
    reasons.push(
      "this pinned dispatch is diagnostic-only and cannot replace the canonical baseline"
    );
  }
  if (benchmarkResult !== "success") {
    reasons.push(`benchmark aggregate result is ${benchmarkResult}`);
  }
  if (!identitiesMatch) {
    reasons.push(
      `result identities differ (missing: ${missingFiles.join(", ") || "none"}; ` +
        `unexpected: ${unexpectedFiles.join(", ") || "none"})`
    );
  }
  if (!metricsComplete) {
    reasons.push(`results with no metrics: ${zeroMetricFiles.join(", ") || "none"}`);
  }

  const invalidSuccessfulMatrix =
    allowBaselineRefresh && benchmarkResult === "success" && (!identitiesMatch || !metricsComplete);
  return {
    complete: allowBaselineRefresh && reasons.length === 0,
    invalidSuccessfulMatrix,
    reasons,
    expectedFiles: expected,
    actualFiles: actual,
    expectedCount: expected.length,
    actualCount: actual.length,
    filesWithMetrics: filesWithMetrics.length,
    missingFiles,
    unexpectedFiles,
    zeroMetricFiles
  };
}

function parseArgs(argv) {
  const options = {
    benchmarkResult: null,
    allowBaselineRefresh: null,
    expectedFiles: null,
    candidateDir: null,
    output: null,
    selfTest: false
  };
  for (let index = 2; index < argv.length; index++) {
    const arg = argv[index];
    if (arg === "--self-test") {
      options.selfTest = true;
      continue;
    }
    const value = argv[++index];
    if (value === undefined) {
      throw new Error(`${arg} requires a value.`);
    }
    switch (arg) {
      case "--benchmark-result":
        options.benchmarkResult = value;
        break;
      case "--allow-baseline-refresh":
        options.allowBaselineRefresh = value === "true";
        break;
      case "--expected-files":
        options.expectedFiles = JSON.parse(value);
        break;
      case "--candidate-dir":
        options.candidateDir = value;
        break;
      case "--output":
        options.output = value;
        break;
      default:
        throw new Error(`Unknown argument: ${arg}`);
    }
  }
  return options;
}

function runCli(options) {
  if (
    !options.benchmarkResult ||
    options.allowBaselineRefresh === null ||
    !Array.isArray(options.expectedFiles) ||
    !options.candidateDir ||
    !options.output
  ) {
    throw new Error(
      "--benchmark-result, --allow-baseline-refresh, --expected-files, --candidate-dir, and --output are required."
    );
  }

  const actualFiles = fs.existsSync(options.candidateDir)
    ? fs
        .readdirSync(options.candidateDir, { withFileTypes: true })
        .filter((entry) => entry.isFile() && /^results-.*\.xml$/i.test(entry.name))
        .map((entry) => entry.name)
    : [];
  const metricCounts = {};
  for (const file of actualFiles) {
    const metrics = extractFromFiles([path.join(options.candidateDir, file)], {
      unityVersion: null,
      testMode: null
    });
    metricCounts[file] = metrics.length;
  }

  const decision = evaluateRefresh({
    benchmarkResult: options.benchmarkResult,
    allowBaselineRefresh: options.allowBaselineRefresh,
    expectedFiles: options.expectedFiles,
    actualFiles,
    metricCounts
  });
  fs.writeFileSync(options.output, `${JSON.stringify(decision, null, 2)}\n`, "utf8");
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(`Self-test failed: ${message}`);
  }
}

function runSelfTest() {
  const expectedFiles = ["results-2022-editmode.xml", "results-2022-playmode.xml"];
  const populated = {
    "results-2022-editmode.xml": 3,
    "results-2022-playmode.xml": 4
  };
  const complete = evaluateRefresh({
    benchmarkResult: "success",
    allowBaselineRefresh: true,
    expectedFiles,
    actualFiles: [...expectedFiles].reverse(),
    metricCounts: populated
  });
  assert(complete.complete, "a successful exact populated matrix must publish");

  const failed = evaluateRefresh({
    benchmarkResult: "failure",
    allowBaselineRefresh: true,
    expectedFiles,
    actualFiles: expectedFiles,
    metricCounts: populated
  });
  assert(!failed.complete, "a failed aggregate must not publish even with every XML file");

  const missing = evaluateRefresh({
    benchmarkResult: "success",
    allowBaselineRefresh: true,
    expectedFiles,
    actualFiles: [expectedFiles[0]],
    metricCounts: populated
  });
  assert(!missing.complete, "a missing expected result identity must not publish");

  const unexpected = evaluateRefresh({
    benchmarkResult: "success",
    allowBaselineRefresh: true,
    expectedFiles,
    actualFiles: [...expectedFiles, "results-unexpected-editmode.xml"],
    metricCounts: { ...populated, "results-unexpected-editmode.xml": 1 }
  });
  assert(!unexpected.complete, "an unexpected result identity must not publish");

  const empty = evaluateRefresh({
    benchmarkResult: "success",
    allowBaselineRefresh: true,
    expectedFiles,
    actualFiles: expectedFiles,
    metricCounts: { ...populated, "results-2022-playmode.xml": 0 }
  });
  assert(!empty.complete, "an expected result with zero metrics must not publish");

  const pinned = evaluateRefresh({
    benchmarkResult: "success",
    allowBaselineRefresh: false,
    expectedFiles,
    actualFiles: expectedFiles,
    metricCounts: populated
  });
  assert(!pinned.complete, "a pinned diagnostic dispatch must not publish the canonical baseline");
  assert(
    !pinned.invalidSuccessfulMatrix,
    "a valid pinned dispatch is diagnostic, not a CI failure"
  );

  process.stdout.write("evaluate-perf-refresh self-test passed (6 matrix-policy cases).\n");
}

if (require.main === module) {
  try {
    const options = parseArgs(process.argv);
    if (options.selfTest) {
      runSelfTest();
    } else {
      runCli(options);
    }
  } catch (error) {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  }
}

module.exports = { evaluateRefresh };
