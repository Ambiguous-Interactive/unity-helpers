#!/usr/bin/env node
"use strict";
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const root = path.resolve(__dirname, "../..");
const result = spawnSync(
  "uv",
  [
    "run",
    "--project",
    path.join(root, "scripts/python~"),
    "--locked",
    "pytest",
    path.join(root, "scripts/tests/test_cspell_tools.py"),
    "-q",
    ...process.argv.slice(2)
  ],
  { cwd: root, stdio: "inherit" }
);
if (result.error) {
  console.error(`Python tooling could not start: ${result.error.message}`);
}
process.exit(result.status === null ? 1 : result.status);
