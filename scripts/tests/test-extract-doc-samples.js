#!/usr/bin/env node
"use strict";
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const root = path.resolve(__dirname, "../..");
const control = process.argv.includes("--editor-compiler-control");
const args = process.argv.slice(2).filter((argument) => argument !== "--editor-compiler-control");
const result = spawnSync(
  "uv",
  [
    "run",
    "--project",
    path.join(root, "scripts/python~"),
    "--locked",
    "pytest",
    path.join(root, "scripts/tests/test_doc_samples.py"),
    "-q",
    ...(control ? ["-k", "editor_compiler_control", "-s"] : []),
    ...args
  ],
  {
    cwd: root,
    stdio: "inherit",
    env: { ...process.env, ...(control ? { DOC_SAMPLES_EDITOR_COMPILER_CONTROL: "1" } : {}) }
  }
);
if (result.error) {
  console.error(`Python tooling could not start: ${result.error.message}`);
}
process.exit(result.status === null ? 1 : result.status);
