"use strict";
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const root = path.resolve(__dirname, "../..");
const result = spawnSync(
  "uv",
  [
    "run",
    "--quiet",
    "--project",
    path.join(root, "scripts/python~"),
    "--locked",
    "pytest",
    path.join(root, "scripts/tests/test_agent_check.py"),
    "-q",
    ...process.argv.slice(2)
  ],
  { cwd: root, stdio: "inherit" }
);
if (result.error) console.error(result.error.message);
process.exitCode = result.status === null ? 1 : result.status;
