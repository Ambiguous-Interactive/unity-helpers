#!/usr/bin/env node
// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
"use strict";

const path = require("path");
const { spawnSync } = require("child_process");
const root = path.resolve(__dirname, "..");
const argumentsPrefix = [
  "run",
  "--quiet",
  "--project",
  path.join(__dirname, "python~"),
  "--locked",
  "--no-dev",
  "python",
  path.join(__dirname, "lint_production_test_hooks.py")
];

function api(action, source, generated = false) {
  const result = spawnSync("uv", [...argumentsPrefix, "--api"], {
    cwd: root,
    input: JSON.stringify({ action, source, generated }),
    encoding: "utf8",
    maxBuffer: 64 * 1024 * 1024
  });
  if (result.error || result.status !== 0) {
    throw result.error || new Error(result.stderr || `Python tooling exited ${result.status}`);
  }
  return JSON.parse(result.stdout);
}

function main(repository = root) {
  const result = spawnSync("uv", argumentsPrefix, {
    cwd: root,
    stdio: "inherit",
    env: { ...process.env, PRODUCTION_TEST_HOOKS_ROOT: path.resolve(repository) }
  });
  if (result.error) console.error(`Python tooling could not start: ${result.error.message}`);
  return result.status === null ? 1 : result.status;
}

module.exports = {
  analyzeFile: (source, generated = false) => api("analyzeFile", source, Boolean(generated)),
  isProductionPath: (source) => api("isProductionPath", source),
  tokenize: (source) => api("tokenize", source),
  main
};
if (require.main === module)
  process.exitCode = main(process.env.PRODUCTION_TEST_HOOKS_ROOT || root);
