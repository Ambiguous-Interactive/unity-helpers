#!/usr/bin/env node
"use strict";
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const root = path.resolve(__dirname, "..");
const forwardedArguments = process.argv
  .slice(2)
  .map((argument) => (argument === "-v" ? "--verbose" : argument));
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
    path.join(root, "scripts/readme_mirror.py"),
    "--check",
    ...forwardedArguments
  ],
  { cwd: root, stdio: "inherit", windowsHide: true }
);
if (result.error) {
  console.error(`Python tooling could not start: ${result.error.message}`);
}
process.exit(result.status === null ? 1 : result.status);
