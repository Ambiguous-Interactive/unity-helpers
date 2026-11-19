"use strict";

const { spawnSync } = require("node:child_process");
const path = require("node:path");

const root = path.resolve(__dirname, "../..");
const result = spawnSync(
  "uv",
  [
    "run",
    "--quiet",
    "--project",
    "scripts/python~",
    "--locked",
    "--no-dev",
    "python",
    "scripts/tests/check_analyzer_placement.py"
  ],
  { cwd: root, stdio: "inherit", windowsHide: true }
);
if (result.error) {
  console.error(`Analyzer placement check could not start: ${result.error.message}`);
}
process.exit(result.status ?? 1);
