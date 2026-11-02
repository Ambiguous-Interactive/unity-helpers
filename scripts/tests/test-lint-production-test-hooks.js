#!/usr/bin/env node
// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
"use strict";

const path = require("path");
const { spawnSync } = require("child_process");
const root = path.resolve(__dirname, "..", "..");
const result = spawnSync(
  "uv",
  [
    "run",
    "--quiet",
    "--project",
    path.join(root, "scripts/python~"),
    "--locked",
    "pytest",
    path.join(__dirname, "test_production_test_hooks.py"),
    "-q"
  ],
  { cwd: root, stdio: "inherit" }
);
if (result.error) console.error(`Python tooling could not start: ${result.error.message}`);
process.exit(result.status === null ? 1 : result.status);
