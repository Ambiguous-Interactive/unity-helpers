"use strict";

const { spawnSync } = require("node:child_process");
const path = require("node:path");
const { runChecks, invokeSync } = require("./check-runner");
const CHECKS = require("./lint-checks.json");

module.exports = { CHECKS, runChecks };

if (require.main === module) {
  const result = spawnSync(
    "uv",
    [
      "run",
      "--quiet",
      "--project",
      path.join(__dirname, "python~"),
      "--locked",
      "--no-dev",
      "python",
      path.join(__dirname, "check_registries.py"),
      "lint",
      ...process.argv.slice(2)
    ],
    { stdio: "inherit" }
  );
  if (result.error) console.error(result.error.message);
  process.exitCode = result.status === null ? 1 : result.status;
}
