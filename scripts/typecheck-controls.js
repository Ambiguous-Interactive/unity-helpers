#!/usr/bin/env node
"use strict";
if (require.main === module) {
  const path = require("node:path");
  const { spawnSync } = require("node:child_process");
  const root = path.resolve(__dirname, "..");
  const result = spawnSync(
    "uv",
    [
      "run",
      "--quiet",
      "--project",
      path.join(root, "scripts/python~"),
      "--locked",
      "--no-dev",
      "python",
      path.join(root, "scripts/typecheck_controls.py"),
      ...process.argv.slice(2)
    ],
    { cwd: root, stdio: "inherit", windowsHide: true }
  );
  if (result.error) {
    console.error(`Python tooling could not start: ${result.error.message}`);
  }
  process.exitCode = result.status === null ? 1 : result.status;
}
