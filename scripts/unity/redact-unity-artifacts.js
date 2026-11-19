"use strict";

const { spawnSync } = require("node:child_process");
const path = require("node:path");

const result = spawnSync(
  "uv",
  [
    "run",
    "--quiet",
    "--project",
    path.join(__dirname, "../python~"),
    "--locked",
    "--no-dev",
    "python",
    path.join(__dirname, "redact_unity_artifacts.py"),
    ...process.argv.slice(2)
  ],
  {
    env: { ...process.env, UH_REDACTION_LEGACY_USAGE: "1" },
    stdio: "inherit",
    windowsHide: true
  }
);
if (result.error) console.error(result.error.message);
process.exitCode = result.status ?? 1;
