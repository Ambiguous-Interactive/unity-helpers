"use strict";
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const result = spawnSync(
  "uv",
  [
    "run",
    "--quiet",
    "--project",
    path.join(__dirname, "../python~"),
    "--locked",
    "pytest",
    "-q",
    path.join(__dirname, "test_release_automation.py"),
    ...process.argv.slice(2)
  ],
  { cwd: path.resolve(__dirname, "../.."), stdio: "inherit" }
);
if (result.error) console.error(result.error.message);
process.exitCode = result.status === null ? 1 : result.status;
