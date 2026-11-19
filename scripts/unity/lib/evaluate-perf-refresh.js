"use strict";

const path = require("node:path");
const { spawnSync } = require("node:child_process");

const result = spawnSync(
  "uv",
  [
    "run",
    "--quiet",
    "--project",
    path.join(__dirname, "../../python~"),
    "--locked",
    "--no-dev",
    "python",
    path.join(__dirname, "../evaluate_perf_refresh.py"),
    ...process.argv.slice(2)
  ],
  { stdio: "inherit" }
);
if (result.error) console.error(result.error.message);
process.exitCode = result.status === null ? 1 : result.status;
