#!/usr/bin/env node
"use strict";
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const root = path.resolve(__dirname, "..");
const result = spawnSync(
  "uv",
  [
    "run",
    "--project",
    path.join(root, "scripts/python~"),
    "--locked",
    "--no-dev",
    "python",
    path.join(root, "scripts/lint_doc_identifiers.py"),
    ...process.argv.slice(2)
  ],
  { cwd: root, stdio: "inherit" }
);
if (result.error) {
  console.error(`Python tooling could not start: ${result.error.message}`);
}
process.exit(result.status === null ? 1 : result.status);
