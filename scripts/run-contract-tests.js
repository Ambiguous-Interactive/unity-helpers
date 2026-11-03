"use strict";

const { runChecks, invokeSync, runRegistryCli } = require("./check-runner");
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
  process.exitCode = runRegistryCli("contracts", process.argv.slice(2));
}
