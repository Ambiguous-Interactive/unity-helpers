"use strict";

const path = require("node:path");
const { spawnSync } = require("node:child_process");

const root = path.resolve(__dirname, "../..");
const TEST_MODES = Object.freeze(["editmode", "playmode", "standalone"]);

function invoke(args, input) {
  return spawnSync(
    "uv",
    [
      "run",
      "--quiet",
      "--project",
      path.join(root, "scripts/python~"),
      "--locked",
      "--no-dev",
      "python",
      path.join(__dirname, "resolve_test_matrix.py"),
      ...args
    ],
    { cwd: root, input, encoding: "utf8", windowsHide: true }
  );
}

function bridge(method, ...args) {
  const result = invoke(["--bridge"], JSON.stringify([method, ...args]));
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(result.stderr.trim() || "Unity matrix resolution failed.");
  }
  return JSON.parse(result.stdout);
}

function resolveTestMatrix(
  versions,
  requestedVersion = "",
  requestedMode = "all",
  requestedAcceptance = "none"
) {
  return bridge("resolve", versions, requestedVersion, requestedMode, requestedAcceptance);
}

function latestVersion(versions) {
  return bridge("latest", versions);
}

function resolveCompatibilityMatrix(versions) {
  return bridge("compatibility", versions);
}

function resolvePullRequestMatrix(versions, files) {
  return bridge("pull-request", versions, files);
}

if (require.main === module) {
  const result = invoke([]);
  if (result.stdout) process.stdout.write(result.stdout);
  if (result.stderr) process.stderr.write(result.stderr);
  if (result.error) console.error(result.error.message);
  process.exitCode = result.status ?? 1;
}

module.exports = {
  TEST_MODES,
  resolveTestMatrix,
  resolvePullRequestMatrix,
  latestVersion,
  resolveCompatibilityMatrix
};
