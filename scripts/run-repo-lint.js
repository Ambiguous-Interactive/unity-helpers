"use strict";

const { runChecks, runRegistryCli } = require("./check-runner");
const CHECKS = require("./lint-checks.json");

module.exports = { CHECKS, runChecks };

if (require.main === module) {
  process.exitCode = runRegistryCli("lint", process.argv.slice(2));
}
