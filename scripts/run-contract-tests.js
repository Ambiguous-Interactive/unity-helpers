"use strict";

const { spawnSync } = require("node:child_process");
const path = require("node:path");
const { runChecks, invokeSync } = require("./check-runner");
const CHECKS = require("./contracts-checks.json");
const HOOK_CHECKS = require("./hooks-checks.json");

function checksFor(includeHookRegressions) {
  return includeHookRegressions ? [...HOOK_CHECKS, ...CHECKS] : CHECKS;
}

function directChecksFor(includeHookRegressions) {
  return invokeSync({
    operation: "registryChecks",
    registry: "contracts",
    includeHooks: Boolean(includeHookRegressions),
    direct: true
  });
}

module.exports = { CHECKS, HOOK_CHECKS, checksFor, directChecksFor, runChecks };

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
      "contracts",
      ...process.argv.slice(2)
    ],
    { stdio: "inherit" }
  );
  if (result.error) console.error(result.error.message);
  process.exitCode = result.status === null ? 1 : result.status;
}
